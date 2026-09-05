# OOBE Configuration — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** phase-1 spec backlog ("OOBE") — out-of-box-experience settings
for offline Windows images, registry-based (no DISM; the local DISM API servicing
path is broken per `docs/OpenCode-EngLog.md`).

## Problem

Windows golden images can be customized so that the Out-of-Box Experience (OOBE)
is quieter, faster and policy-compliant: skipping the machine/user OOBE phases,
skipping the privacy-experience screen, preselecting the express-settings choice
(`ProtectYourPC`), bypassing the network requirement (Win11 `BypassNRO`), and
hiding the Microsoft-account / wireless setup screens. These switches live in
the offline `SOFTWARE` hive under
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE` as DWORD values.

The module already has both halves of the machinery — in-memory offline hive
reads (`RegistryHiveReader`, no hive mounting, no persistent handles) and the
proven hive-mounted write path (`NativeRegistryService.ApplyRegistryOperations`:
EnablePrivileges → MountRequiredHives → apply → UnmountHives in `finally`) — and
the Services phase (`Get-WindowsImageService` / `Set-WindowsImageService`)
established the exact cmdlet + service pattern for a single registry key. But
there is no OOBE-specific query/modify surface: today an operator has to hand-edit
the hive or script `reg.exe` against mounted files to answer "did we skip the
privacy experience in this image?" or to flip those switches per image.

## Goals

1. `Get-WindowsImageOOBE` — report the current OOBE configuration of one or more
   mounted images from each image's offline SOFTWARE hive: every documented
   OOBE setting with its friendly name, current DWORD value, and whether it is
   set at all.
2. `Set-WindowsImageOOBE` — apply OOBE settings to one or more mounted images by
   writing DWORD values into the offline OOBE key (write 1/0 per boolean
   setting, write the `ProtectYourPC` mode, remove values), delegating to the
   existing `NativeRegistryService.ApplyRegistryOperations` write path.
3. Define a bounded, documented catalog of OOBE value names (7 entries) so both
   cmdlets share one source of truth for names, descriptions and semantics.
4. Keep OOBE ↔ registry mapping, tri-state switch resolution, request
   validation, operation building and result building pure and unit-testable
   without hive files, DISM sessions or real images; the hive read/write paths
   stay thin.
5. Follow the Services-phase conventions exactly: `MountedWindowsImage[]`
   pipeline-accumulator parameter, `LoggingService` timestamps,
   `SupportsShouldProcess` on the mutating cmdlet, `ModuleCallbacks`-aware
   service, results written as model objects at the end.

## Non-goals

- **Unattend XML.** `UnattendXMLService` already covers the `oobeSystem` pass
  (`HideWirelessSetupInOOBE`, `ProtectYourPC` elements, etc.). This phase is the
  registry-only surface for images that skip or supplement unattend; it does not
  read, write or validate unattend files.
- **DISM.** No `Microsoft.Dism` calls at all. The phase is registry-based, so it
  verifies fully via build + unit tests locally; real-image steps are
  manual/CI-only.
- **Online machine OOBE.** Offline mounted image hives only
  (`<mount>\Windows\System32\config\SOFTWARE`), matching the existing
  `RegistryHiveReader` scope.
- **Whole-key dump.** Only the documented catalog is reported. Arbitrary extra
  values an image may carry under the OOBE key are not enumerated (bounded,
  documented surface; the generic `Get-RegistryHiveOnDemand` remains the tool
  for open-ended hive triage).
- **Drift/compare integration.** OOBE settings can already flow into the drift
  fingerprint via the `HKLM\SOFTWARE` `Values`-mode key definitions if added
  there later; no `ImageSnapshot`/`ImageComparisonResult` changes in this phase.
- **User-hive OOBE.** The DEFAULT/NTUSER hives are untouched; the catalog covers
  machine-scope OOBE values only.

## Architecture

All additions follow the existing service + model + cmdlet split, mirroring the
completed Services phase (`WindowsImageServicesService` /
`WindowsImageServiceModels` / `WindowsImageServicesCmdlets`). No changes to the
manifest (`CmdletsToExport` is added by the orchestrator afterwards), no new
NuGet/assembly dependencies.

### New files

**`src/Models/WindowsImageOobeModels.cs`**

- `WindowsImageOobeProtectYourPc` — enum with explicit DWORD values:
  `Recommended = 1`, `ImportantOnly = 2`, `NotInProgram = 3` (1 = use recommended
  settings, 2 = recommended settings off — only important updates installed,
  3 = not in the recommended program).
- `WindowsImageOobeSettingDefinition` — one catalog entry: `SettingName`
  (friendly, parameter-shaped name, e.g. `SkipPrivacyExperience`), `ValueName`
  (registry value name under the OOBE key — identical to `SettingName` for this
  catalog), `Description` (human-readable meaning).
- `WindowsImageOobeSetting` — one reported setting from `Get-WindowsImageOOBE`:
  `ImageName`, `MountPath`, `SettingName`, `ValueName`, `Description`, `IsSet`
  (the value exists in the key), `Value` (raw `int?` DWORD; null when unset),
  and `State` (`"Set: 1"` / `"Set: 0"` / `"Not set"` display string) plus a
  `ToString()` override.
- `WindowsImageOobeChange` — one requested Set change: `ValueName` plus
  `Value` (`1` or `0` for a write; null = remove the value).
- `WindowsImageOobeOperationResult` — one result per image from
  `Set-WindowsImageOOBE`: `ImageName`, `Operation` (human-readable description,
  same text as ShouldProcess), `Success`, `ErrorMessage`, `ToString()` override.

**`src/Services/WindowsImageOobeService.cs`** (`_callbacks`, `ModuleCallbacks`-
aware, mirroring `WindowsImageServicesService`)

- `private const string ServiceName = "WindowsImageOobeService"`.
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`.
- `internal const string OobeKeyPath = @"Microsoft\Windows\CurrentVersion\OOBE"`
  (relative to the SOFTWARE hive root — the read path).
- `internal const string OobeOperationKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE"`
  (the `RegistryOperation.Key` form — the write path needs the `SOFTWARE\`
  prefix so `NativeRegistryService` mounts and maps the SOFTWARE hive).
- `public static List<WindowsImageOobeSettingDefinition> GetDefaultSettings()` —
  the documented 7-entry catalog (see below), in a fixed display order.
- `public List<WindowsImageOobeSetting> GetSettings(IRegistryHiveReader reader,
  string imageName, string mountPath)` — thin hive-reading path. Resolves the
  SOFTWARE hive file; missing hive → verbose note + empty result (never throws);
  opens the hive, reads the OOBE key (missing key → every catalog entry reported
  as not set); projects each catalog entry through `ProjectSetting`. Per-entry
  value parsing never throws (unknown/absent → not set).
- `internal static string ResolveSoftwareHivePath(string mountPath)`.
- `internal static int? GetDwordValue(IEnumerable<(string Name, object? Data)>
  values, string valueName)` — ordinal-ignore-case lookup; null when absent or
  non-numeric (same pattern as `WindowsImageServicesService.GetDwordValue`).
- `internal static WindowsImageOobeSetting ProjectSetting(string imageName,
  string mountPath, WindowsImageOobeSettingDefinition definition, int? value)` —
  pure projection (sets `IsSet`, `Value`, `State`).
- `internal static bool IsValidValueName(string valueName)` — non-blank and
  present in the catalog (ordinal-ignore-case). Guards `-Remove` and any
  programmatic change list against typos before any hive is mounted.
- `internal static int ToProtectYourPcValue(WindowsImageOobeProtectYourPc mode)`
  — enum → DWORD (throws `ArgumentOutOfRangeException` only if the enum grows an
  unmapped member; the three documented members always map).
- `internal static void ValidateChanges(List<WindowsImageOobeChange> changes)` —
  pure. Throws `ArgumentException` when the list is null/empty ("Specify at
  least one OOBE change"), when any `ValueName` is unknown to the catalog, or
  when a value name appears both as a write and as a removal.
- `internal static List<RegistryOperation> BuildSetOperations(List<WindowsImageOobeChange>
  changes)` — pure. `Modify` operations for writes (`Hive = "HKLM"`,
  `Key = OobeOperationKeyPath`, `ValueName`, `Value = (uint)change.Value`,
  `ValueType = DWord`), `Remove` operations for removals (same key, `Value =
  null`, `ValueType = Unknown`); writes first, then removals, each in catalog
  order.
- `internal static string DescribeSetChange(List<WindowsImageOobeChange> changes)`
  — pure; `"Write SkipPrivacyExperience=1, Write ProtectYourPC=2, Remove
  BypassNRO"` (used for ShouldProcess, the result `Operation`, and logging).
- `internal static WindowsImageOobeOperationResult BuildSetResult(string
  imageName, string operation, bool success, string? errorMessage)` — pure.

**`src/Cmdlets/WindowsImageOobeCmdlets.cs`**

- `Get-WindowsImageOOBE` (`GetWindowsImageOobeCmdlet`) — `MountedImages`
  (`MountedWindowsImage[]`, Mandatory, Position 0, ValueFromPipeline) +
  `ContinueOnError` switch, exactly like `Get-WindowsImageService`. `ProcessRecord`
  accumulates; `EndProcessing` loops images (`MountPath?.FullName ?? string.Empty`
  guard, per-image try/catch → error + `ContinueOnError` or rethrow), writes
  `WindowsImageOobeSetting[]` once at the end. Reads via
  `using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this))`.
- `Set-WindowsImageOOBE` (`SetWindowsImageOobeCmdlet`, `SupportsShouldProcess =
  true`) — same `MountedImages` parameter plus:
  - Tri-state switches `-SkipMachineOOBE`, `-SkipUserOOBE`,
    `-SkipPrivacyExperience`, `-BypassNRO`, `-HideOnlineAccountScreens`,
    `-HideWirelessSetupInOOBE`. Switch semantics: not specified → value
    untouched; specified → write DWORD 1; specified with `:$false` → write DWORD
    0 (`SwitchParameter.IsPresent` distinguishes both — documented in help).
  - `-ProtectYourPC <WindowsImageOobeProtectYourPc?>` — nullable enum
    (Recommended / ImportantOnly / NotInProgram); null → untouched.
  - `-Remove <string[]>` — value names to delete (each validated against the
    catalog; terminating error on unknown names).
  - `-ContinueOnError`.
  - `EndProcessing`: builds the change list (switches → writes, `ProtectYourPC`
    → write, `-Remove` → removals), validates via
    `WindowsImageOobeService.ValidateChanges` (terminating error on invalid
    requests before touching any image), then per image: `ShouldProcess(target,
    operationName)` → `LogOperationStartWithTimestamp` →
    `new NativeRegistryService().ApplyRegistryOperations(mountPath,
    operations.ToArray(), this)` → `LogOperationCompleteWithTimestamp` →
    `WriteObject(BuildSetResult(...))`; failure → warning (ContinueOnError) or
    throw, mirroring `Set-WindowsImageService` exactly.

### Default OOBE setting catalog (`GetDefaultSettings`)

All entries are DWORD values under
`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE` (write path:
`RegistryOperation { Hive = "HKLM", Key = "SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE" }`,
which maps to the SOFTWARE hive in `NativeRegistryService`):

| ValueName | Meaning |
| --- | --- |
| `SkipMachineOOBE` | 1 = skip the machine OOBE phase (legacy switch, honored by Windows 7-era setup and some tooling; informational on Windows 10/11 images) |
| `SkipUserOOBE` | 1 = skip the user OOBE phase (legacy switch, same caveat as above) |
| `SkipPrivacyExperience` | 1 = skip the privacy/express-settings experience screen (Windows 10 1709+ and Windows 11) |
| `ProtectYourPC` | 1 = use recommended settings, 2 = recommended settings off (only important updates), 3 = not in the recommended program |
| `BypassNRO` | 1 = allow completing OOBE without a network connection (Windows 11; removed in some newer builds — informational if ignored by the image) |
| `HideOnlineAccountScreens` | 1 = hide Microsoft-account online sign-up/sign-in screens during OOBE |
| `HideWirelessSetupInOOBE` | 1 = hide the wireless-network setup screen during OOBE |

> Real-image correctness of these values is verified manually/CI (the local DISM
> `OpenOfflineSession` limitation documented in `docs/OpenCode-EngLog.md` means
> nothing in this phase touches DISM anyway — reads use the in-memory hive
> parser, writes use hive mounting; both are covered by unit tests for the pure
> logic and by the existing `NativeRegistryService` write path used by the
> Services phase).

## Data Flow

```
Get-WindowsImageOOBE -MountedImages $img
   └─► GetWindowsImageOobeCmdlet.EndProcessing
         └─► WindowsImageOobeService.GetSettings
               ├─► IRegistryHiveReader.OpenHive(<mount>\Windows\System32\config\SOFTWARE)
               ├─► IRegistryHiveReader.GetKey(hive, "Microsoft\Windows\CurrentVersion\OOBE")
               └─► pure GetDwordValue / ProjectSetting
         └─► WriteObject(WindowsImageOobeSetting[])

Set-WindowsImageOOBE -MountedImages $img -SkipPrivacyExperience -ProtectYourPC ImportantOnly
   └─► SetWindowsImageOobeCmdlet.EndProcessing
         └─► pure ValidateChanges / BuildSetOperations / DescribeSetChange
         └─► ShouldProcess(target, operation)
         └─► NativeRegistryService.ApplyRegistryOperations(mountPath, operations, this)
               ├─► EnablePrivileges
               ├─► MountRequiredHives (SOFTWARE)
               ├─► CreateSubKey + SetValue (writes) / DeleteValue (removals)
               └─► UnmountHives (finally)
         └─► WriteObject(WindowsImageOobeOperationResult[])
```

## Error Handling

- `Get`: missing SOFTWARE hive → verbose note + empty result; missing OOBE key →
  all seven catalog settings reported as "Not set" (a stock image legitimately
  has no OOBE key); per-image exceptions → error + `ContinueOnError` or rethrow,
  matching `Get-WindowsImageService`.
- `Set`: invalid requests (no changes, unknown value name, name both written and
  removed) are rejected terminally before any hive is mounted — no partial
  requests; per-image `ApplyRegistryOperations` failure → `Success = false`
  result object with `ErrorMessage`, then warning (ContinueOnError) or
  terminating error; hives are always unmounted in the native service's
  `finally`.
- `Get` never mutates; `Set` never reads (validation is value-name based, and
  the native path creates the OOBE key when missing via `CreateSubKey`).

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/WindowsImageOobeServiceTests.cs`,
  plain `[Fact]`/`[Theory]`, no mocking, following
  `WindowsImageServicesServiceTests.cs` patterns):**
  - `GetDefaultSettings` — non-empty, unique value names, `SettingName ==
    ValueName`, every entry carries a description, expected 7 entries, and the
    two switch-facing names from the phase backlog are present.
  - `GetDwordValue` — case-insensitive hit, absent → null, non-numeric → null.
  - `ProjectSetting` — set (1/0) and unset projections, `State` strings.
  - `ResolveSoftwareHivePath` — maps a mount path to
    `Windows\System32\config\SOFTWARE` (temp-dir pattern from
    `ImageComparisonServiceTests.cs`).
  - `IsValidValueName` — catalog name (case-insensitive) true; unknown/blank
    false.
  - `ToProtectYourPcValue` — all three enum members map to 1/2/3.
  - `ValidateChanges` — empty list throws; unknown name throws; same name
    written and removed throws; valid mixed list passes.
  - `BuildSetOperations` — write 1 → `Modify` DWORD `(uint)1` with the
    `SOFTWARE\…\OOBE` key; write 0 → `(uint)0`; removal → `Remove` op with the
    same key; writes precede removals; empty validated list → empty operations.
  - `DescribeSetChange` — single write, mixed write+remove, and combined
    ProtectYourPC text.
- **Integration:** none locally. Real-image reads/writes are manual/CI-only;
  everything in this phase is registry-based, so build + unit tests cover the
  verifiable surface (the DISM servicing limitation in
  `docs/OpenCode-EngLog.md` is bypassed by design — this phase never calls DISM).

## Risks

- **Legacy semantics.** `SkipMachineOOBE`/`SkipUserOOBE` are informational on
  current Windows 10/11 images (setup ignores them in some builds). The cmdlets
  read/write the values faithfully and the catalog descriptions state the
  caveat; interpretation stays with the operator (same stance as the drift
  phase's "no drift meaning beyond compare").
- **`BypassNRO` build dependence.** Some newer Windows 11 builds no longer
  honor `BypassNRO`; the value is still written/read as data. Documented in the
  catalog and help.
- **`ProtectYourPC` dual home.** The value is primarily documented as an
  unattend `oobeSystem` setting; the registry value under the OOBE key is the
  deployment-community surface this phase exposes. Both are covered: unattend
  stays in `UnattendXMLService`, the registry value is written here.
- **Write-path mapping dependency.** `NativeRegistryService` maps operations to
  the mounted SOFTWARE hive only when `Key` starts with `SOFTWARE\`; the service
  uses the dedicated `OobeOperationKeyPath` constant so the mapping is always
  correct, and a unit test pins the key shape.
- **No new env/test surface.** The orchestrator adds the two `CmdletsToExport`
  entries after review; help files are created but the shipped MAML regeneration
  stays orchestrator-owned (same flow as the Services phase).
