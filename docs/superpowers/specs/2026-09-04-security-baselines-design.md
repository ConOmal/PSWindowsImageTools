# Security Baselines — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** the OOBE/security-baseline backlog phase that adds offline
registry-based configuration cmdlets to PSWindowsImageTools (parallel to the OOBE
configuration and services configuration phases). Registry drift detection already
proved that the in-memory offline-hive read path works; this phase adds a *curated,
documented security baseline* with compliance reporting and remediation.

## Problem

Golden-image engineers need to prove — and enforce — that a captured image satisfies
a defined set of security-relevant registry settings (UAC, LSA/NTLM hardening, SMB
signing, RDP/Remote Assistance, AutoRun, logon UX). Today the module can see parts of
this state (`Get-WindowsImageSnapshot` captures `Policies` keys as raw drift
signatures) but has no notion of an *expected value*:

- Drift detection diffs image-vs-image; it cannot say "EnableLUA must be 1".
- There is no command to report per-entry compliance (Compliant / NonCompliant /
  NotPresent) of a mounted image against a curated baseline, and no command to
  remediate an image to the baseline.
- The local DISM API servicing is broken (`OpenOfflineSession` fails —
  `docs/OpenCode-EngLog.md`), so any baseline feature must be pure registry:
  reads via the in-memory `RegistryHiveReader`, writes via the existing
  hive-mounted `NativeRegistryService.ApplyRegistryOperations` path. Neither
  surface is currently exposed for a security baseline.

## Goals

1. Define a curated, documented security baseline of ~22 registry entries spanning
   the offline `SOFTWARE` hive, the offline `SYSTEM` hive, and the image's
   default-user hive (`Users\Default\NTUSER.DAT` — the module's `HKU`/default-user
   convention, see "Default-user hive, not config\DEFAULT" below). Every entry
   carries a rationale and is committed in code as a single source of truth.
2. `Get-WindowsImageSecurityBaseline` reports, per mounted image, each entry's
   current value vs the expected value with state Compliant / NonCompliant /
   NotPresent, plus per-image counts and an overall verdict.
3. `Set-WindowsImageSecurityBaseline` applies the baseline to a mounted image:
   entries that are already compliant are skipped (reported as `AlreadyApplied`),
   the rest are written via the proven hive-mounted write path with
   `SupportsShouldProcess` / `-WhatIf` / `-Confirm` honored, and per-entry
   applied/failed/skipped results are returned.
4. Keep every piece of decision logic (baseline table, hive-path resolution, value
   normalization, compliance comparison, operation building, result building) pure
   `internal static` and unit-testable without hive files, DISM sessions or real
   images. The hive read and hive-mounted write paths stay thin.
5. Follow the repo's cmdlet conventions exactly so the orchestrator can export the
   two new verbs with zero manifest surgery: `MountedWindowsImage[]` pipeline
   accumulator, per-image failure handling, `SupportsShouldProcess` on the Set
   verb, PlatyPS help files, no psd1 edits, no new dependencies.

## Non-goals

- **A user-configurable baseline file.** The baseline is code-defined and
  documented in this spec; accepting arbitrary `-Baseline <file>` input would turn
  the cmdlets into a generic registry writer (already covered by
  `Write-RegistryOperationList` / recipes). Extending the curated set is a code
  change by design.
- **DISM / SECEDIT / LGPO integration.** Registry values only; no policy-INF
  processing, no SECURITY/SAM hive policy blobs (the `Audit` subcategory policy is
  stored in binary policy blobs inside the SECURITY hive, which neither the read
  path nor the write path touches).
- **Live-machine checks.** Offline mounted images only, matching every other
  phase in this backlog.
- **Removing "extra" values.** Remediation is additive (write expected values);
  entries that are present but wrong are overwritten, but stray values not in the
  baseline are never deleted.
- **Audit-policy blobs.** Only registry values that are plain
  DWORD/REG_SZ-backed security settings; `AuditBaseObjects`-class pointers that
  are already defaulted on supported images and advanced-audit subcategory blobs
  are out of scope.
- **psd1 / MAML changes.** The orchestrator adds
  `Get-WindowsImageSecurityBaseline` / `Set-WindowsImageSecurityBaseline` to
  `CmdletsToExport` and regenerates MAML.

## Architecture

New service + models + cmdlets file trio; no existing file is modified.
`NativeRegistryService.cs`, `RegistryHiveReader.cs`, `IRegistryHiveReader.cs`,
`RegistryOperation.cs` and the drift/services files are read-only references.

### New files

**`src/Models/SecurityBaselineModels.cs`**

- `enum WindowsImageBaselineComplianceState { Compliant, NonCompliant, NotPresent }`
  — per-entry verdict of a Get report.
- `enum WindowsImageBaselineApplyState { Applied, AlreadyApplied, Failed, Skipped }`
  — per-entry verdict of a Set result.
- `WindowsImageSecurityBaselineEntry` — one baseline definition: `Hive`
  (`HKLM\SOFTWARE` / `HKLM\SYSTEM` / `HKU\DefaultUser`), `KeyPath` (relative to the
  hive root), `ValueName`, `ExpectedValue` (normalized string), `ValueType`
  (`RegistryValueKind` — the baseline only uses `DWord` and `String`), and
  `Rationale`. `ToString()` → `Hive\KeyPath\ValueName = ExpectedValue (Kind)`.
- `WindowsImageSecurityBaselineObservation` — one Get report row: everything from
  the entry, plus `ImageName`, `MountPath`, `State`, `ObservedValue` (string, empty
  when not present), `ObservedValueType` (friendly `Reg*` string as returned by the
  hive parser, empty when not present).
- `WindowsImageSecurityBaselineReport` — per-image Get result: `ImageName`,
  `MountPath`, `Entries: List<WindowsImageSecurityBaselineObservation>`; computed
  `TotalEntries`, `CompliantCount`, `NonCompliantCount`, `NotPresentCount`,
  `IsCompliant` (`TotalEntries > 0 && NonCompliantCount == 0 && NotPresentCount == 0`).
- `WindowsImageSecurityBaselineApplyEntry` — one Set result row: `ImageName`,
  `Hive`, `KeyPath`, `ValueName`, `ExpectedValue`, `State`
  (`WindowsImageBaselineApplyState`), `Detail` (human reason, e.g. the shared error
  message of a failed batch or "Already compliant" / "Hive file not found").
- `WindowsImageSecurityBaselineApplyResult` — per-image Set result: `ImageName`,
  `MountPath`, `Results: List<WindowsImageSecurityBaselineApplyEntry>`, `Success`,
  `ErrorMessage`; computed `TotalCount`, `AppliedCount`, `AlreadyAppliedCount`,
  `FailedCount`, `SkippedCount`.

**`src/Services/SecurityBaselineService.cs`** (`_callbacks`, `ModuleCallbacks`-aware,
mirroring `WindowsImageServicesService`)

- `private const string ServiceName = "SecurityBaselineService"`.
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`,
  `public const string SystemHiveName = "HKLM\\SYSTEM"`,
  `public const string DefaultUserHiveName = "HKU\\DefaultUser"`.
- `public static List<WindowsImageSecurityBaselineEntry> GetBaselineEntries()` —
  the curated 22-entry table (see below), in stable order (SOFTWARE → SYSTEM →
  default user, key order documented in the spec).
- Thin read surface (only method touching `IRegistryHiveReader`):
  - `public WindowsImageSecurityBaselineReport GetBaselineCompliance(
    IRegistryHiveReader reader, string imageName, string mountPath,
    IReadOnlyList<WindowsImageSecurityBaselineEntry>? entries = null)` — group the
    entries by hive; per hive resolve the file path (`ResolveHivePath`); missing
    hive file → `_callbacks.Verbose` + entries become `NotPresent` observations
    (no throw); otherwise `reader.OpenHive` once per hive and per entry
    `reader.GetKey(hive, entry.KeyPath)` (null → `NotPresent`), find the value by
    case-insensitive name, then build the observation with the pure
    `BuildObservation`. Per-entry exceptions → `_callbacks.Warning` + the entry is
    reported `NotPresent` (matching `CollectSoftwareEntries`' never-drop-the-batch
    behavior).
- Pure `internal static` logic (all unit-tested):
  - `internal static string ResolveHivePath(string mountPath, string hive)` —
    `HKLM\SOFTWARE` → `<mount>\Windows\System32\config\SOFTWARE`; `HKLM\SYSTEM` →
    `...config\SYSTEM`; `HKU\DefaultUser` → `<mount>\Users\Default\NTUSER.DAT`;
    any other name → `...config\<hive.Replace('\\','_')>` (keeps the helper total;
    the curated set only uses the three real names). Comparison is
    ordinal-ignore-case.
  - `internal static string NormalizeValueData(string? data)` — null/blank →
    empty; collapse `\r\n`/`\r` → `\n`; trim (same rules as
    `RegistryDriftService.NormalizeValueData`).
  - `internal static string ToExpectedTypeString(RegistryValueKind kind)` — maps
    the baseline's `RegistryValueKind` to the hive parser's friendly type string
    (`DWord` → `RegDword`, `String` → `RegSz`, `ExpandString` → `RegExpandSz`,
    `QWord` → `RegQword`) for reporting (verified against the live parser).
  - `internal static bool ValuesEquivalent(string? expected, string? observed)` —
    trims both; numeric-aware comparison: when both sides parse as `long`
    (invariant), compare numerically (so `1` == `1`); otherwise
    ordinal-ignore-case string equality. `null` is only equal to `null`.
  - `internal static WindowsImageBaselineComplianceState CompareEntry(
    WindowsImageSecurityBaselineEntry entry, string? observedValue)` —
    `null`/absent → `NotPresent`; `ValuesEquivalent(expected, observed)` →
    `Compliant`; else `NonCompliant`.
  - `internal static WindowsImageSecurityBaselineObservation BuildObservation(
    string imageName, string mountPath, WindowsImageSecurityBaselineEntry entry,
    string? observedValue, string observedValueType)` — pure projection used by
    the thin read.
  - `internal static string MapOperationHive(string hive)` — `HKLM\*` → `HKLM`,
    `HKU\DefaultUser` → `HKU`; unknown → `ArgumentException` (the curated set
    never triggers it, tests cover it).
  - `internal static string MapOperationKey(string hive, string keyPath)` —
    `HKLM\SOFTWARE` → `"SOFTWARE\" + keyPath` (the write path strips that prefix
    after mounting the SOFTWARE hive); `HKLM\SYSTEM` → keyPath unchanged
    (`ControlSet001\...` is already relative to the SYSTEM hive root);
    `HKU\DefaultUser` → keyPath unchanged (relative to the default-user hive
    root); unknown → `ArgumentException`.
  - `internal static object ToWriteValue(WindowsImageSecurityBaselineEntry entry)`
    — `DWord` → `Convert.ToUInt32(ExpectedValue, invariant)`, `QWord` → ulong,
    `String`/`ExpandString` → the trimmed string; other kinds →
    `ArgumentOutOfRangeException` (the curated set never triggers it).
  - `internal static List<RegistryOperation> BuildApplyOperations(
    IReadOnlyList<WindowsImageSecurityBaselineEntry> entries)` — one `Modify`
    `RegistryOperation` per entry: `Hive = MapOperationHive(...)`, `Key =
    MapOperationKey(...)`, `ValueName = entry.ValueName`, `Value =
    ToWriteValue(entry)`, `ValueType = entry.ValueType`.
  - `internal static string DescribeApplyAction(int pendingCount, int
    alreadyCount, string imageName)` — human action string for `ShouldProcess`
    (e.g. `Apply 6 security baseline entries (6 to write, 0 already compliant) to <image>`).
  - `internal static List<WindowsImageSecurityBaselineApplyEntry> BuildApplyRows(
    string imageName, IReadOnlyList<WindowsImageSecurityBaselineEntry> primary,
    WindowsImageBaselineApplyState primaryState, string? primaryDetail,
    IReadOnlyList<WindowsImageSecurityBaselineEntry> secondary,
    WindowsImageBaselineApplyState secondaryState, string? secondaryDetail)` —
    one pure projection used for all row groups (written/failed, already
    compliant, skipped), so every Set result row is built by tested code.
  - `internal static WindowsImageSecurityBaselineApplyResult BuildApplyResult(
    string imageName, string mountPath, List<WindowsImageSecurityBaselineApplyEntry> rows,
    bool success, string? errorMessage)` — pure wrapper.

**`src/Cmdlets/SecurityBaselineCmdlets.cs`**

- `GetWindowsImageSecurityBaselineCmdlet` — `[Cmdlet(VerbsCommon.Get,
  "WindowsImageSecurityBaseline")]`, `[OutputType(typeof(
  WindowsImageSecurityBaselineReport[]))]`. Parameters: `MountedWindowsImage[]
  MountedImages` (Mandatory, Position 0, ValueFromPipeline, `ValidateNotNull`),
  `SwitchParameter ContinueOnError`. Accumulates in `ProcessRecord`; in
  `EndProcessing` warns+returns on no images, then per image resolves
  `MountPath?.FullName` (null → `LoggingService.WriteError` + rethrow unless
  `-ContinueOnError`), opens one `RegistryHiveReader` per image
  (`ModuleCallbacks.FromCmdlet(this)`) and calls
  `GetBaselineCompliance`; `WriteObject(reports.ToArray())`. Mirrors
  `GetWindowsImageServiceCmdlet` exactly.
- `SetWindowsImageSecurityBaselineCmdlet` — `[Cmdlet(VerbsCommon.Set,
  "WindowsImageSecurityBaseline", SupportsShouldProcess = true)]`,
  `[OutputType(typeof(WindowsImageSecurityBaselineApplyResult[]))]`. Parameters:
  `MountedWindowsImage[] MountedImages` (Mandatory, Position 0, ValueFromPipeline),
  `SwitchParameter ContinueOnError`. Per image in `EndProcessing`:
  1. Resolve mount path (null → error + rethrow unless `-ContinueOnError`).
  2. Read current compliance in memory (the thin read — also classifies missing
     hive files) using `WindowsImageSecurityBaselineService.GetBaselineCompliance`.
  3. Partition rows (pure helpers): compliant entries → `AlreadyApplied`; entries
     in a missing hive → `Skipped` ("hive file not found"); the rest are the
     *pending* set.
  4. When nothing is pending, emit the result (Success = true) with a verbose
     note — no hive is ever mounted.
  5. `ShouldProcess(DescribeApplyTarget(image, mountPath),
     DescribeApplyAction(pending, already, image))` — `-WhatIf` stops here,
     nothing written.
  6. Write path: `new NativeRegistryService().ApplyRegistryOperations(mountPath,
     operations, this)` with `operations = BuildApplyOperations(pending)` — one
     call per image; that method enables privileges, mounts every hive the batch
     needs (SOFTWARE / SYSTEM / NTUSER), applies each operation, and unmounts in
     `finally`. `true` → pending rows `Applied`; `false`/exception → pending rows
     `Failed` with the shared error message (per-operation warnings are emitted by
     `ApplyRegistryOperations` itself).
  7. Wrap the write in
     `LoggingService.LogOperationStartWithTimestamp` /
     `LogOperationCompleteWithTimestamp`; on failure write the error and rethrow
     unless `-ContinueOnError`.

### The curated baseline (`GetBaselineEntries`)

22 entries: 9 in `HKLM\SOFTWARE`, 11 in `HKLM\SYSTEM`, 2 in the default-user hive.
All DWORD values are decimal; all string values are REG_SZ.

| # | Hive | KeyPath | ValueName | Type | Expected | Rationale |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | `EnableLUA` | DWord | `1` | UAC enabled. Windows default, but actively enforced so an image cannot ship with UAC silently off. |
| 2 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | `ConsentPromptBehaviorAdmin` | DWord | `2` | Elevation prompts for consent on the secure desktop (Windows default and the safest interactive UAC mode; CIS-aligned). |
| 3 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | `PromptOnSecureDesktop` | DWord | `1` | Elevation UI only on the secure desktop — defeats spoofing of the UAC prompt. |
| 4 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | `dontdisplaylastusername` | DWord | `1` | "Interactive logon: Don't display last signed-in" — avoids leaking account names on shared/console images (CIS L1). |
| 5 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | `DisableAutomaticRestartSignOn` | DWord | `1` | Disables ARSO ("Sign-in last interactive user automatically after a restart") so restarts never auto-logon — CIS L1. |
| 6 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\Explorer` | `NoDriveTypeAutoRun` | DWord | `255` | Autoplay disabled on all drive types (0xFF) — the classic Autorun-borne-malware hardening (CIS L1). |
| 7 | HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\Explorer` | `NoAutorun` | DWord | `1` | "Disallow Autoplay for non-volume devices" — companion to entry 6 (CIS L1). |
| 8 | HKLM\SOFTWARE | `Microsoft\Windows NT\CurrentVersion\Winlogon` | `AutoAdminLogon` | String | `0` | No cached autologon: an image must never boot unattended into a desktop. REG_SZ by design (that is its native type). |
| 9 | HKLM\SOFTWARE | `Policies\Microsoft\Windows NT\Terminal Services` | `fDenyTSConnections` | DWord | `1` | Remote Desktop disabled at the policy level — the GPO-honored switch; images enable it deliberately, never by default. |
| 10 | HKLM\SYSTEM | `ControlSet001\Control\Lsa` | `RunAsPPL` | DWord | `1` | LSA Protection (credential theft mitigation; default on Win11 22H2+, enforced explicitly for older bases). |
| 11 | HKLM\SYSTEM | `ControlSet001\Control\Lsa` | `LmCompatibilityLevel` | DWord | `5` | "Send NTLMv2 responses only; refuse LM & NTLM" — CIS L1 network-security setting. |
| 12 | HKLM\SYSTEM | `ControlSet001\Control\Lsa` | `NoLMHash` | DWord | `1` | Never store LM password hashes in the SAM. |
| 13 | HKLM\SYSTEM | `ControlSet001\Control\Lsa` | `RestrictAnonymous` | DWord | `1` | Restrict anonymous enumeration of SAM accounts (CIS L1). |
| 14 | HKLM\SYSTEM | `ControlSet001\Control\Lsa` | `RestrictAnonymousSam` | DWord | `1` | Restrict anonymous enumeration of SAM names (CIS L1). |
| 15 | HKLM\SYSTEM | `ControlSet001\Services\LanmanServer\Parameters` | `SMB1` | DWord | `0` | SMB1 server component off — deprecated, worm-exploited protocol (MS17-010 class). |
| 16 | HKLM\SYSTEM | `ControlSet001\Services\LanmanServer\Parameters` | `RequireSecuritySignature` | DWord | `1` | "Microsoft network server: Digitally sign communications (always)" — SMB signing enforced server-side (CIS L1). |
| 17 | HKLM\SYSTEM | `ControlSet001\Services\LanmanWorkstation\Parameters` | `RequireSecuritySignature` | DWord | `1` | "Microsoft network client: Digitally sign communications (always)" — SMB signing enforced client-side (CIS L1; Windows 11 24H2 default). |
| 18 | HKLM\SYSTEM | `ControlSet001\Control\Terminal Server` | `fDenyTSConnections` | DWord | `1` | RDP disabled at the system level (the value a `SystemPropertiesRemote` toggle flips); pairs with entry 9. |
| 19 | HKLM\SYSTEM | `ControlSet001\Control\Terminal Server\WinStations\RDP-Tcp` | `UserAuthentication` | DWord | `1` | Network Level Authentication required for RDP — defense in depth for images that later enable RDP. |
| 20 | HKLM\SYSTEM | `ControlSet001\Control\Remote Assistance` | `fAllowToGetHelp` | DWord | `0` | Remote Assistance solicited help disabled (CIS L1). |
| 21 | HKU\DefaultUser | `Software\Policies\Microsoft\Windows\Control Panel\Desktop` | `ScreenSaverIsSecure` | String | `1` | Password-protected screen saver for every new profile created from the default user hive (CIS L1). REG_SZ natively. |
| 22 | HKU\DefaultUser | `Software\Policies\Microsoft\Windows\Control Panel\Desktop` | `ScreenSaveTimeOut` | String | `900` | 15-minute inactivity lock for new profiles (CIS L1 upper bound). REG_SZ natively. |

Curation principles:

- **Windows-default-honoring.** Entries where the compliant value equals the
  Windows default (most of the table) are deliberate: a baseline exists to *keep*
  images compliant and to catch tooling that silently flips them, not only to
  diverge from defaults.
- **DWORD/REG_SZ only.** No `REG_BINARY`/`REG_MULTI_SZ`/`REG_EXPAND_SZ` expected
  values — those decode ambiguously across parsers; the curated set stays
  byte-stable. `AutoAdminLogon` and the screen-saver values are REG_SZ *by design*
  (that is their native type), which the parser and the write path preserve.
- **Policy keys first.** Where a policy override exists
  (`Policies\...\Terminal Services`) the baseline targets it, because policy wins
  over the preference at runtime.
- **`ControlSet001` is canonical** — same decision as the drift and services
  phases (`CurrentControlSet` is a live-system link whose resolution in the
  in-memory hive parser is not guaranteed).
- **Excluded on purpose:** Windows Update / telemetry posture
  (environment-specific by policy domain), `AuditBaseObjects` /
  `CrashOnAuditFail` / `SCENoApplyLegacyAuditPolicy` (already defaulted on
  supported images), Netlogon secure-channel values (domain-member only), and
  anything stored in the SECURITY/SAM hives (binary policy blobs — outside both
  the read and write paths).

### Default-user hive, not `config\DEFAULT`

"DEFAULT span" here means the image's **default-user profile hive**
(`<mount>\Users\Default\NTUSER.DAT`), addressed as `HKU\DefaultUser`:

- `RegistryOperation.GetMappedHive` and
  `NativeRegistryService.MountRequiredHives` map `HKU` to exactly that file
  (`Users\Default\NTUSER.DAT`), so both the read and the write path support it
  natively with zero new mounting logic.
- `Windows\System32\config\DEFAULT` (the logon-desktop profile) is **excluded**:
  the sanctioned write path cannot mount it (`MountRequiredHives` knows only
  SOFTWARE/SYSTEM/NTUSER), and its security surface is negligible — the profile
  new users actually inherit is NTUSER.DAT, which is where
  CIS-aligned per-user settings (screen-saver lock) must land.

## Data Flow

```
Get-WindowsImageSecurityBaseline -MountedImages $mounted
   └─► per image: SecurityBaselineService.GetBaselineCompliance(reader, image, mountPath)
         ├─► ResolveHivePath → config\SOFTWARE | config\SYSTEM | Users\Default\NTUSER.DAT
         ├─► IRegistryHiveReader.OpenHive(<file>) / GetKey(hive, entry.KeyPath)
         └─► pure ValuesEquivalent / CompareEntry / BuildObservation
   └─► WindowsImageSecurityBaselineReport[]  (per-entry state + counts + IsCompliant)

Set-WindowsImageSecurityBaseline -MountedImages $mounted -WhatIf
   └─► per image: GetBaselineCompliance (in-memory, step 2 above)
         └─► partition: AlreadyApplied | Skipped (hive missing) | pending
         └─► ShouldProcess(target, DescribeApplyAction(...))   [-WhatIf stops here]
         └─► BuildApplyOperations(pending)  [pure → RegistryOperation[]]
               Hive HKLM + Key SOFTWARE\...   → SOFTWARE hive (prefix stripped by write path)
               Hive HKLM + Key ControlSet001\... → SYSTEM hive
               Hive HKU  + Key Software\...   → Users\Default\NTUSER.DAT
         └─► NativeRegistryService.ApplyRegistryOperations(<mount>, ops, cmdlet)
               ├─► EnablePrivileges() → MountRequiredHives() [RegLoadKey × {SOFTWARE,SYSTEM,NTUSER}]
               ├─► apply each operation (RegSetValueEx, correct RegistryValueKind)
               └─► finally UnmountHives() [RegUnLoadKey]
         └─► WindowsImageSecurityBaselineApplyResult  (Applied / AlreadyApplied / Skipped / Failed rows)
```

## Error Handling

- **Get never throws per image:** missing hive → verbose note + those entries
  report `NotPresent`; missing key or missing value → `NotPresent`; per-entry
  read exception → `_callbacks.Warning` + `NotPresent` for that entry; image-level
  failure → `LoggingService.WriteError` + rethrow unless `-ContinueOnError`.
- **Set fails fast on nothing, writes atomically per image:** the pre-flight is a
  pure in-memory read (typo-free by construction — the baseline is code-defined);
  `-WhatIf`/declined `ShouldProcess` writes nothing; the hive-mounted batch is the
  unchanged `ApplyRegistryOperations` surface which always unmounts in `finally`
  and reports per-operation failures as warnings; a `false` return maps to
  `Failed` rows + `Success = false` (+ rethrow unless `-ContinueOnError`).
- **Type fidelity:** writes use the entry's `RegistryValueKind`
  (`DWord`/`String`), so a compliant check that reads back the image after Set
  observes both the same data and the same type.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/SecurityBaselineServiceTests.cs`)**
  — all pure, no hive files, no DISM, no mocks:
  - `GetBaselineEntries` — non-empty, 15–25 entries, every entry has a known hive,
    non-blank key/value/rationale, `DWord`/`String` kinds only, DWord expected
    values parse as integers, no duplicate full paths, order stable across calls.
  - `NormalizeValueData` — collapse/trim/null rules.
  - `ValuesEquivalent` — numeric equality (`"1"`/`"1"`, `"255"`/`"255"`), trimmed,
    case-insensitive string equality, inequality (`"1"` vs `"2"`, `null` vs `"0"`).
  - `CompareEntry` — null → `NotPresent`; equal → `Compliant`; different →
    `NonCompliant`.
  - `ResolveHivePath` — the three real hive names map to
    `config\SOFTWARE` / `config\SYSTEM` / `Users\Default\NTUSER.DAT` under a temp
    mount path (temp-dir fixture pattern), case-insensitive, unknown → config fallback.
  - `MapOperationHive` / `MapOperationKey` — the three mappings (SOFTWARE prefix,
    SYSTEM relative, HKU relative) and the unknown-hive throw.
  - `ToWriteValue` — DWord → uint, String → trimmed string, QWord → ulong,
    invalid numeric → throws.
  - `BuildApplyOperations` — one `Modify` op per entry with the exact
    Hive/Key/ValueName/Value/ValueType per hive mapping.
  - `DescribeApplyAction` — contains pending/already counts and the image name.
  - `BuildApplyRows` / `BuildApplyResult` — row states and counts
    (Applied/AlreadyApplied/Skipped/Failed) and `Success` mapping.
  - Enumeration through a real hive and the hive-mounted write are
    **manual/CI-only** (see below); the parser surface already has real-hive
    coverage via `RegistryHiveReaderTests.cs`, and the write path is the shipped,
    unchanged `NativeRegistryService`.
- **Integration (manual on a mounted image):** `Get-WindowsImageSecurityBaseline`
  shows the per-entry report; `Set-WindowsImageSecurityBaseline -WhatIf` prints
  the apply plan; a real run then re-`Get`s to confirm `IsCompliant`. The local
  DISM `OpenOfflineSession` limitation is irrelevant (reads are in-memory, writes
  are hive-mounted) but real-image coverage stays manual/CI per repo policy.
- **Guardrails:** new cmdlets are not yet exported (orchestrator adds them), so
  `verify-help.ps1` checks 1–3 pass by construction for this phase; the PlatyPS
  files (`docs/help/Get-WindowsImageSecurityBaseline.md`,
  `docs/help/Set-WindowsImageSecurityBaseline.md`) are added now so the
  orchestrator's later export keeps check 2 green. Check 4 (shipped MAML) is the
  orchestrator's job.

## Risks

- **Opinionated defaults.** Entries like `dontdisplaylastusername = 1` change
  interactive-logon UX; every entry's rationale is documented and the baseline is
  applied only when an operator runs the Set cmdlet (with `-WhatIf`/`-Confirm`
  available). Organizations wanting different values fork the curated table (a
  code change by design).
- **`ControlSet001` assumption.** Same accepted risk as the drift/services phases.
- **Hive load/unload on write.** `RegLoadKey`/`RegUnLoadKey` require elevation
  (the `EnablePrivileges` helper is a documented no-op stub). Documented in help.
- **SMB signing interop.** Enforcing client/server SMB signing (entries 16–17)
  can affect legacy SMB1 peers — SMB1 is simultaneously disabled by entry 15, and
  signing is the CIS L1 posture; documented in help so operators know the blast
  radius.
- **Parser type strings.** Compliance compares data (numeric-aware), and surfaces
  the parser's `Reg*` type string for visibility; type equality is implied by
  writing with the entry's `RegistryValueKind` and by the parser's stable decode
  for the DWORD/REG_SZ types the curated set uses (verified against the live
  parser: `RegDword` renders `ValueData` as a decimal string, `RegSz` as text).
