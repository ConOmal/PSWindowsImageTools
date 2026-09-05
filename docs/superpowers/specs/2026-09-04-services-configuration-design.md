# Windows Image Services Configuration — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** the OOBE/security-baseline backlog phase that adds offline
registry-based configuration cmdlets to PSWindowsImageTools (parallel to the
OOBE configuration and security-baseline phases). Registry drift detection
already reads the `SYSTEM` hive's service set (`ControlSet001\Services`) as a
signature; this phase adds first-class query and mutation of that service set.

## Problem

Golden-image engineers routinely need to inspect and control the service
configuration baked into an offline Windows image — which services start at
boot, which are disabled for attack-surface reduction, which drivers run early.
Today the module offers no command to do this:

- The registry drift phase captures `ControlSet001\Services` **subkey names**
  as a signature, but never reads per-service property values (`Start`,
  `DisplayName`, `ImagePath`, `DelayedAutoStart`) and never writes them.
- DISM exposes no commands for pre-baked service start-type configuration, and
  the local DISM `OpenOfflineSession` servicing limitation documented in
  `docs/OpenCode-EngLog.md` blocks any DISM-session approach locally.
- The module already reads offline hives in memory (`RegistryHiveReader` via
  `Registry.dll`, no hive mounting, no persistent handles) and already writes
  offline hives through the hive-mounted native API path
  (`NativeRegistryService.ApplyRegistryOperations`: `RegLoadKey` → apply →
  `RegUnLoadKey` in `finally`). Neither surface is exposed for services.

So an image's service configuration is effectively invisible and unmodifiable
within the module.

## Goals

1. Enumerate and filter the services configured in a mounted image's offline
   `SYSTEM` hive (`ControlSet001\Services`), reporting each service's name,
   display name, image path, description, start type and delayed-auto-start
   flag — via a new `Get-WindowsImageService` cmdlet.
2. Change a service's start type (and optionally enable `DelayedAutoStart`) in
   the offline `SYSTEM` hive — via a new `Set-WindowsImageService` cmdlet
   honoring `ShouldProcess`/`-WhatIf`/`-Confirm`.
3. Read via the existing in-memory path only (no hive mounting for reads);
   write via the proven hive-mounted native API path only (no new mounting
   logic).
4. Keep every piece of decision logic (start-type mapping, name filtering,
   projection, validation, operation building, result building) pure
   `internal static` and unit-testable without hive files, DISM sessions or
   real images.
5. Follow the repo's cmdlet conventions exactly so the orchestrator can export
   the two new verbs with zero manifest surgery: `MountedWindowsImage[]`
   pipeline accumulator, per-image failure handling, `SupportsShouldProcess`
   on the Set verb, PlatyPS help files, no psd1 edits, no new dependencies.

## Non-goals

- **Starting/stopping/delaying real services.** Offline image configuration
  only; no interaction with the online Service Control Manager.
- **Creating or deleting services.** `Set-WindowsImageService` changes the
  `Start` / `DelayedAutoStart` values of an existing service key; it does not
  create service keys or remove them.
- **Service dependency / account / recovery editing.** `DependOnService`,
  `ObjectName`, `FailureActions`, `RequiredPrivileges`, etc. are read-only
  surface (surfaced under `-Detailed` as raw values), not settable this phase.
- **Wildcard `-Name`.** `-Name` is an exact (case-insensitive) service name or
  a regular-expression pattern — no `*`/`?` globbing (an invalid pattern
  warns/does not match; documented).
- **Real-image automation in CI.** Local verification is pure unit tests +
  build. Exercising real `SYSTEM` hive files (read and hive-mounted write)
  stays manual/CI on a mounted image (see Testing).
- **psd1 / MAML changes.** The orchestrator adds `Get-WindowsImageService` /
  `Set-WindowsImageService` to `CmdletsToExport` and regenerates MAML.

## Architecture

Follows the existing service + model + cmdlet split. No modifications to any
existing file (`NativeRegistryService.cs`, `RegistryHiveReader.cs`,
`ComponentStoreService.cs`, the drift files, etc. are untouched). Everything is
additive in four files plus help docs and tests.

### New files

**`src/Models/WindowsImageServiceModels.cs`**

- `enum WindowsImageServiceStartType { Boot, System, Automatic, Manual,
  Disabled, Unknown }` — friendly start-type mapping for the offline `Start`
  DWORD (0=Boot, 1=System, 2=Automatic, 3=Manual, 4=Disabled; anything else →
  `Unknown`). `Unknown` is used for display only; it is never accepted back for
  writes.
- `WindowsImageServiceInfo` — one service: `ImageName`, `MountPath`
  (directory FullName), `Name` (service key name), `DisplayName`, `ImagePath`,
  `Description`, `StartType` (enum), `StartValue` (raw DWORD, -1 when absent),
  `DelayedAutoStart` (bool), and `RegistryValues` (`Dictionary<string, object>?`
  of the key's values; null by default, populated only with `-Detailed`).
- `WindowsImageServiceOperationResult` — Set result per image: `ImageName`,
  `ServiceName`, `Operation` (descriptive string), `RequestedStartType`
  (`WindowsImageServiceStartType?`), `SetDelayedAutoStart` (bool), `Success`,
  `ErrorMessage`.

**`src/Services/WindowsImageServicesService.cs`** (`_callbacks`,
`ModuleCallbacks`-aware, mirroring `RegistryDriftService`)

- `private const string ServiceName = "WindowsImageServicesService"`,
  `public const string SystemHiveName = "HKLM\\SYSTEM"`,
  `internal const string ServicesKeyPath = @"ControlSet001\Services"`.
- Thin read surface (only methods that touch `IRegistryHiveReader`):
  - `public List<WindowsImageServiceInfo> GetServices(IRegistryHiveReader
    reader, string imageName, string mountPath, string? nameFilter = null,
    bool detailed = false)` — enumerate `ServicesKeyPath` subkeys; missing
    hive → verbose + empty; missing services key → verbose + empty; per-service
    `try/catch` → `_callbacks.Warning` + continue; all projection via the pure
    `ProjectServiceInfo`; `MatchesNameFilter` applied to each key name.
  - `public bool ServiceExists(IRegistryHiveReader reader, string mountPath,
    string serviceName)` — `GetKey(ServicesKeyPath\<name>) != null` (used by
    Set as a cheap pre-flight before the hive-mounted write).
- Pure `internal static` logic (all unit-tested):
  - `internal static string ResolveSystemHivePath(string mountPath)` →
    `Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM")`.
  - `internal static bool IsValidServiceName(string name)` — non-blank, no
    `\` or `/` (guards the left-hand side of the registry key path).
  - `internal static WindowsImageServiceStartType ParseStartType(int value)`;
    `internal static int ToStartValue(WindowsImageServiceStartType type)`
    (throws for `Unknown`).
  - `internal static int? GetDwordValue(IEnumerable<(string Name, object?
    Data)> values, string valueName)`; `internal static string
    GetStringValue(IEnumerable<(string Name, object? Data)> values, string
    valueName)`; `internal static bool GetDelayedAutoStart(IEnumerable<
    (string Name, object? Data)> values)` (DWORD 1 → true).
  - `internal static WindowsImageServiceInfo ProjectServiceInfo(string
    imageName, string mountPath, string name, IEnumerable<(string Name,
    object? Data)> values)` — the only projection used by Get; `StartValue` =
    raw DWORD or -1, `StartType` via `ParseStartType`, standard values read by
    name (ordinal-ignore-case).
  - `internal static Dictionary<string, object> CollectValues(IEnumerable<
    (string Name, object? Data)> values)` — sorted by name (ordinal), used by
    `-Detailed`.
  - `internal static bool MatchesNameFilter(string? serviceName, string?
    filter)` — null/blank filter → true; exact ordinal-ignore-case equality →
    true; otherwise compiles `^(?i)<filter>$` as a regex (timeout 1s) and
    returns the match; invalid regex / timeout → false. Exhaustive singleton
    then regex, so plain names behave exactly.
  - `internal static void ValidateSetParameters(WindowsImageServiceStartType?
    startType, bool setDelayedAutoStart)` — throws `ArgumentException` when
    nothing is requested, or when `DelayedAutoStart` is combined with a
    non-`Automatic` start type.
  - `internal static List<RegistryOperation> BuildSetOperations(string
    serviceName, WindowsImageServiceStartType? startType, bool
    setDelayedAutoStart)` — assumes validated; `Modify` operations on
    `Hive = "HKLM"`, `Key = @"ControlSet001\Services\<name>"`, DWord values.
  - `internal static string DescribeSetChange(WindowsImageServiceStartType?
    startType, bool setDelayedAutoStart)` — human action string for
    `ShouldProcess` and the result's `Operation`.
  - `internal static WindowsImageServiceOperationResult BuildSetResult(
    string imageName, string serviceName, WindowsImageServiceStartType?
    startType, bool setDelayedAutoStart, bool success, string? errorMessage)`.

**`src/Cmdlets/WindowsImageServicesCmdlets.cs`**

- `GetWindowsImageServiceCmdlet` — `[Cmdlet(VerbsCommon.Get,
  "WindowsImageService", ConfirmImpact = Medium?No — none)]`, deliberately
  base. Parameters: `MountedWindowsImage[] MountedImages` (Mandatory, Position
  0, `ValueFromPipeline`, `ValidateNotNull`), `string Name` (Position 1),
  `SwitchParameter Detailed`, `SwitchParameter ContinueOnError`. Accumulates
  in `ProcessRecord`, enumerates in `EndProcessing` (mirror of
  `GetWindowsImageComponentStoreCmdlet`): per image, resolve `MountPath`
  (null → error + throw unless `ContinueOnError`), call
  `new WindowsImageServicesService(callbacks)` with a `using
  RegistryHiveReader`, `WriteObject(results.ToArray())`.
- `SetWindowsImageServiceCmdlet` — `[Cmdlet(VerbsCommon.Set,
  "WindowsImageService", SupportsShouldProcess = true)]`. Parameters:
  `MountedWindowsImage[] MountedImages` (Mandatory, Position 0,
  `ValueFromPipeline`), `string Name` (Mandatory, Position 1,
  `ValidateNotNullOrEmpty`), `WindowsImageServiceStartType? StartType`,
  `SwitchParameter DelayedAutoStart`, `SwitchParameter ContinueOnError`.
  `EndProcessing`: no images → warning + return; `ValidateSetParameters`
  (throw terminating on violation); `IsValidServiceName(Name)` (throw
  terminating); per image — resolve `MountPath`, `ServiceExists` pre-flight
  (not found → error + throw unless `ContinueOnError`), `BuildSetOperations`,
  `ShouldProcess($"{Name} on {mountPath}", DescribeSetChange(...))`, then the
  write path: `new NativeRegistryService().ApplyRegistryOperations(mountPath,
  operations, this)` (the proven hive-mounted path — enables privileges,
  mounts the SYSTEM hive, applies, unmounts in `finally`), then
  `WriteObject(BuildSetResult(...))`. `-WhatIf` is honored automatically via
  `ShouldProcess`.

### Committed conventions (why they were chosen)

- **`ControlSet001\Services`.** The active control set is `ControlSet001` on
  standard images, consistent with `NativeRegistryService`
  (`ReadServicesInfoWithNativeApi`) and `RegistryDriftService`
  (`GetDefaultDriftKeyDefinitions`), and unlike `CurrentControlSet` (a link in
  live hives whose resolution in the in-memory `Registry.dll` parser is not
  guaranteed). Same canonical set = same "source of truth" the drift phase
  already signs.
- **Read = in-memory `IRegistryHiveReader`.** Same object the module already
  uses (`RegistryHiveReader`), no mounting, no persistent handles, no DISM —
  sidesteps the broken `OpenOfflineSession` path entirely for reads.
- **Write = `NativeRegistryService.ApplyRegistryOperations` directly**, not via
  `RegistryApplicationService`. The task's guidance is to delegate the actual
  writes to `ApplyRegistryOperations`; calling it directly is the minimal
  correct path (`RegistryApplicationService.ApplyOperations` re-splits
  results op-by-op with an arbitrary half/half split and adds mount-ID caching
  we do not need). We mirror its `RegistryOperation` construction
  (`Hive = "HKLM"`, key relative to the hive root, DWord values) so
  `ApplyRegistryOperations` mounts the correct hive: a key path without
  `SOFTWARE` maps to the SYSTEM hive in that method. The Set cmdlet pre-flights
  via the in-memory read so typo'd service names fail cheaply before any hive
  is loaded/unloaded.
- **Start type surface.** Include `Boot`(0)/`System`(1) alongside
  `Automatic`(2)/`Manual`(3)/`Disabled`(4) because driver start type is a
  real golden-image concern (boot-start filter/driver adoption); the danger of
  `Boot`/`System` on non-driver services is documented in help and gated by
  `ShouldProcess`.
- **`-DelayedAutoStart` is additive-only** (DWORD 1). There is no "off" switch;
  setting `StartType` away from `Automatic` (or a future value removal) is the
  documented way to clear it. Validation rejects `-DelayedAutoStart` with any
  non-`Automatic` `-StartType`.

## Data Flow

```
Get-WindowsImageService -MountedImages $mounted
   └─► WindowsImageServicesService.GetServices(reader, image, mountPath, Name, Detailed)
         ├─► IRegistryHiveReader.OpenHive(<mount>\Windows\System32\config\SYSTEM)
         ├─► IRegistryHiveReader.GetKey(hive, ControlSet001\Services) → subkey names
         ├─► pure MatchesNameFilter / ProjectServiceInfo / CollectValues
         └─► WriteObject(WindowsImageServiceInfo[])

Set-WindowsImageService -MountedImages $mounted -Name spooler -StartType Disabled -WhatIf
   └─► ValidateSetParameters / IsValidServiceName (pure, throws)
   └─► per image: ServiceExists(reader, mountPath, name)  [in-memory pre-flight]
         └─► BuildSetOperations(name, StartType, DelayedAutoStart)  [pure → RegistryOperation[]]
         └─► ShouldProcess("spooler on <mount>", "Set start type to Disabled")
         └─► NativeRegistryService.ApplyRegistryOperations(<mount>, ops, cmdlet)
                ├─► EnablePrivileges() → MountRequiredHives() [RegLoadKey SYSTEM]
                ├─► apply each operation (RegSetValueEx)
                └─► finally UnmountHives() [RegUnLoadKey]
   └─► WriteObject(WindowsImageServiceOperationResult)
```

## Error Handling

- **Get is never destructive and never throws per-image:** missing hive /
  missing services key → verbose note + empty result for that image; per-service
  read failure → `_callbacks.Warning` + continue; image-level failure →
  `LoggingService.WriteError` + rethrow unless `-ContinueOnError` (mirrors
  `GetWindowsImageComponentStoreCmdlet`).
- **Set fails fast on bad input:** no requested change, or
  `-DelayedAutoStart` with a non-`Automatic` `-StartType`, is a terminating
  `ArgumentException`. A service that does not exist in the offline hive is a
  per-image error (throw unless `-ContinueOnError`). An empty `-Name` / a name
  containing path separators is rejected before any registry read.
- **The hive-mounted write path is owned by `NativeRegistryService`
  unchanged:** it logs per-operation failures as warnings, returns `false`
  when any operation fails, and always unmounts mounted hives in `finally`.
  The Set cmdlet maps `false` + exceptions into a
  `WindowsImageServiceOperationResult` with `Success = false` and an
  `ErrorMessage` (never throws out of the pipeline unless `-ContinueOnError`
  is absent on an unexpected exception).

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/
  WindowsImageServicesServiceTests.cs`)** — all pure, hand-built value tuples,
  no hive files, no DISM, no registry files:
  - `ParseStartType` 0–4 → the five enum members; 5/-1/absent → `Unknown`.
  - `ToStartValue` round-trips 0–4 and throws on `Unknown`.
  - `GetDwordValue`/`GetStringValue`/`GetDelayedAutoStart` extract by
    case-insensitive name; absent/`0`/non-numeric handled.
  - `ProjectServiceInfo` projects a known tuple set into a
    `WindowsImageServiceInfo` (display name, image path, description, start
    type + raw value, delayed flag).
  - `CollectValues` returns a name-sorted copy.
  - `MatchesNameFilter`: null/blank → true; exact case-insensitive; regex
    pattern (e.g. `spool.*`) matches; invalid pattern (e.g. `[`) and timeout →
    false; empty service name → false.
  - `ResolveSystemHivePath` maps to `...\config\SYSTEM` under a temp mount
    path (temp-dir fixture pattern from `RegistryDriftServiceTests`).
  - `IsValidServiceName` rejects blank/`\`/`/`.
  - `ValidateSetParameters` throws when nothing requested and when
    `-DelayedAutoStart` pairs with a non-`Automatic` `-StartType`; accepts
    each valid combination.
  - `BuildSetOperations` produces `Modify / HKLM /
    ControlSet001\Services\<name>` DWord ops for `Start` (and
    `DelayedAutoStart` = 1), correct `Value`/`ValueType`, one op per requested
    change.
  - `DescribeSetChange` / `BuildSetResult` map inputs → string / POCO.
  - Enumeration-through-`IRegistryHiveReader` (real `SYSTEM` hive) and the
    hive-mounted write are **manual/CI-only**; the `Registry.dll` read surface
    already has real-hive coverage via `RegistryHiveReaderTests.cs`, and the
    write path is the unchanged, already-shipped `NativeRegistryService`
    surface.
- **Integration (manual on a mounted image):** `Get-WindowsImageService |
  Where-Object Name -eq 'Spooler'` shows `Manual`; `Set-WindowsImageService
  -Name Spooler -StartType Disabled -WhatIf` then for real; re-`Get` confirms.
  The local DISM `OpenOfflineSession` limitation is irrelevant to both paths
  (reads are in-memory, writes are hive-mounted), but real-image coverage
  stays manual/CI per repo policy.
- **Guardrails:** new cmdlets are not yet exported (orchestrator adds them), so
  `verify-help.ps1` checks 1–3 pass by construction for this phase; the new
  PlatyPS files are added now so the orchestrator's later export keeps
  check 2 green. Check 4 (shipped MAML) is the orchestrator's job.

## Risks

- **`ControlSet001` assumption.** Same accepted risk as the drift phase: if an
  image's active control set is not `ControlSet001`, writes target the wrong
  (backup) control set. Documented as canonical; extremely low likelihood for
  standard installs.
- **Hive load/unload on write.** `ApplyRegistryOperations` uses
  `RegLoadKey`/`RegUnLoadKey`, which require elevation (the `EnablePrivileges`
  helper is intentionally a no-op stub in the existing code). Set operates on
  an unmounted hive file, so no DISM `OpenOfflineSession` is involved — but an
  elevated session (or a build with restore rights) is still required. Documented
  in help; `#Requires -RunAsAdministrator`-class guidance is out of scope for
  the binary.
- **`DelayedAutoStart` semantics.** The value only has meaning when `Start`
  is `Automatic`. We guard the combo; an image where the flag already exists
  with a non-Automatic `Start` is left alone rather than "fixed".
- **Regex `-Name` edge.** A bracket-heavy pattern silently matches nothing; the
  mental model is "exact name, or a regex if it looks like one." Documented in
  help.

## Out of scope follow-ups
- `-StartType` wildcard/array and `Set-WindowsImageService` for
  `DependOnService`/`ObjectName`/recovery actions.
- Direct `DelayedAutoStart` removal (`-DelayedAutoStart:$false` variant).
- A `Report`-style diff of service config between two images.