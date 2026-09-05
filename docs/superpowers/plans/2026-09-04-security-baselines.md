# Security Baselines — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageSecurityBaseline` and `Set-WindowsImageSecurityBaseline` to PSWindowsImageTools — a curated, documented 22-entry security baseline (UAC, LSA/NTLM hardening, SMB signing, RDP/Remote Assistance, AutoRun, logon UX, default-profile screen-saver lock) spanning the offline SOFTWARE hive, the offline SYSTEM hive, and the default-user hive (`Users\Default\NTUSER.DAT`). Get reports per-entry compliance (Compliant / NonCompliant / NotPresent) against the expected values via the in-memory `IRegistryHiveReader`; Set remediates an image via the existing hive-mounted `NativeRegistryService.ApplyRegistryOperations` path with `SupportsShouldProcess`. No existing file is modified.

**Architecture:** Mirror the services-configuration convention exactly: new `src/Models/SecurityBaselineModels.cs` (compliance/apply enums, baseline entry, observation, report, apply rows/result), new `src/Services/SecurityBaselineService.cs` (curated baseline table + thin `IRegistryHiveReader` read + pure operation/result builders), new `src/Cmdlets/SecurityBaselineCmdlets.cs` (`MountedWindowsImage[]` pipeline accumulator + `EndProcessing` per-image handling + `SupportsShouldProcess` on Set), PlatyPS help files under `docs/help/`, and a pure unit-test class (no hive files, no DISM, no mocks) matching the `WindowsImageServicesServiceTests` pattern. Registry-based only: local DISM `OpenOfflineSession` servicing is broken and never touched.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), `Registry.dll` (existing), `Microsoft.Win32` (existing), xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-security-baselines-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions, `??=`, and nullable annotations exactly as `WindowsImageServicesService.cs` / `RegistryDriftService.cs` do. Remember netstandard2.0 ref assemblies lack `[NotNullWhen]` — null-narrowing uses `!` / `?? string.Empty` patterns.
- Do NOT touch the user's in-flight files: `src/Services/WimExportService.cs`, `src/Cmdlets/ExportWindowsImageCmdlet.cs`, `src/Cmdlets/MountWindowsImageListCmdlet.cs` (mid-edit; 33 CS1022 at last check), `.claude/`, and the user's docs (`docs/superpowers/*boot-image*`, `*app-provisioning*`, `*image-checkpoint*`).
- Do NOT touch the completed sibling phase's files: `src/Services/WindowsImageServicesService.cs`, `src/Models/WindowsImageServiceModels.cs`, `src/Cmdlets/WindowsImageServicesCmdlets.cs`, `tests/PSWindowsImageTools.Tests/WindowsImageServicesServiceTests.cs`, `docs/help/Get-WindowsImageService.md`, `docs/help/Set-WindowsImageService.md` (read-only references).
- Do NOT touch: `src/Services/FormatUtilityService.cs`, `src/Services/NativeRegistryService.cs` (read-only reference), `src/Services/RegistryDriftService.cs`, `src/Models/RegistryDriftModels.cs`, `src/Services/ImageComparisonService.cs`, `src/Models/ImageComparisonModels.cs`, `src/Services/ComponentStoreService.cs`, `src/Services/WindowsImageDriverService.cs`, `src/Services/WindowsImageHealthCheckService.cs`, `src/Services/WinREImageService.cs`, `src/Services/WinREIntelligenceService.cs`, `src/Services/ReservedStorageService.cs`, `src/Services/WindowsImageEditionService.cs`, `src/Services/ServicingChainService.cs`, `src/Models/RegistryOperation.cs`, `src/Models/MountedWindowsImage.cs`, `src/Services/RegistryHiveReader.cs`, `src/Services/Abstractions/IRegistryHiveReader.cs`, or any file another agent creates (OOBE configuration, Scheduled Tasks).
- Do NOT change `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (`CmdletsToExport` is the orchestrator's job — report the two exact cmdlet names). Do NOT regenerate the shipped MAML. Do NOT sync DLLs into `Module/PSWindowsImageTools/bin/`.
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Do not run the full unit suite or the Pester integration suite locally (concurrent builders — on MSBuild `.obj`/file-lock errors wait ~30s and retry). If `dotnet build src/PSWindowsImageTools.csproj` fails on the three user-owned files above, verify via the scratch-tree fallback: copy the repo to `C:\Users\ConOmal\AppData\Local\Temp\opencode\pswit-verify-baseline`, `git checkout HEAD -- src/Services/WimExportService.cs src/Cmdlets/ExportWindowsImageCmdlet.cs src/Cmdlets/MountWindowsImageListCmdlet.cs` there, and build/test in the scratch copy.
- Verification: build (0 errors on non-user files), then `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~SecurityBaselineService"`, then `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"` (checks 1–3 pass; check 4 = shipped MAML is the expected orchestrator-only failure — report it, don't fix it).
- Real-image reads and hive-mounted writes are manual/CI-only per repo policy (elevation required for `RegLoadKey`/`RegUnLoadKey`); the local DISM limitation in `docs/OpenCode-EngLog.md` is irrelevant to both paths but no Pester changes are made.
- Test class: `SecurityBaselineServiceTests` (new file). Use the temp-directory fixture pattern (`Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"))`) only where disk paths are needed (hive-path resolution); all other tests are pure in-memory.

---

### Task 1: Security baseline models

**Files:**
- Create: `src/Models/SecurityBaselineModels.cs`

**Interfaces:**
- `enum WindowsImageBaselineComplianceState { Compliant, NonCompliant, NotPresent }`.
- `enum WindowsImageBaselineApplyState { Applied, AlreadyApplied, Failed, Skipped }`.
- `WindowsImageSecurityBaselineEntry { Hive, KeyPath, ValueName, ExpectedValue, ValueType (RegistryValueKind), Rationale }`.
- `WindowsImageSecurityBaselineObservation { ImageName, MountPath, Hive, KeyPath, ValueName, ExpectedValue, ValueType, Rationale, State, ObservedValue, ObservedValueType }`.
- `WindowsImageSecurityBaselineReport { ImageName, MountPath, Entries, TotalEntries, CompliantCount, NonCompliantCount, NotPresentCount, IsCompliant }`.
- `WindowsImageSecurityBaselineApplyEntry { ImageName, Hive, KeyPath, ValueName, ExpectedValue, State, Detail }`.
- `WindowsImageSecurityBaselineApplyResult { ImageName, MountPath, Results, Success, ErrorMessage, TotalCount, AppliedCount, AlreadyAppliedCount, FailedCount, SkippedCount }`.

- [x] **Step 1: Create `src/Models/SecurityBaselineModels.cs`** with the seven types above (plain POCOs, `= string.Empty` / `new List<...>()` initializers, XML doc comments, `ToString()` overrides mirroring `RegistryDriftModels.cs` / `WindowsImageServiceModels.cs`; computed count properties via LINQ `Count`).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm the project compiles (expected to fail on the three user-owned in-flight files — that failure is out of scope; confirm no errors point at the new file, using the scratch tree if needed).

### Task 2: SecurityBaselineService — curated table + pure logic + thin read

**Files:**
- Create: `src/Services/SecurityBaselineService.cs`

**Interfaces:**
- `SecurityBaselineService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default (mirror `RegistryHiveReader`).
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`, `public const string SystemHiveName = "HKLM\\SYSTEM"`, `public const string DefaultUserHiveName = "HKU\\DefaultUser"`.
- `public static List<WindowsImageSecurityBaselineEntry> GetBaselineEntries()` — the 22-entry curated table from the spec (order: SOFTWARE 1–9, SYSTEM 10–20, default-user 21–22).
- `public WindowsImageSecurityBaselineReport GetBaselineCompliance(IRegistryHiveReader reader, string imageName, string mountPath, IReadOnlyList<WindowsImageSecurityBaselineEntry>? entries = null)` — thin; the only method that touches `IRegistryHiveReader`.
- Pure `internal static` (unit-testable): `ResolveHivePath`, `NormalizeValueData`, `ToExpectedTypeString`, `ValuesEquivalent`, `CompareEntry`, `BuildObservation`, `MapOperationHive`, `MapOperationKey`, `ToWriteValue`, `BuildApplyOperations`, `DescribeApplyAction`, `BuildApplyRows`, `BuildApplyResult` — signatures per spec.

- [x] **Step 1: Write `SecurityBaselineService.cs`**:
  - `GetBaselineEntries`: static list built once (per call copy) from the spec table; DWord entries carry `RegistryValueKind.DWord`, string entries `RegistryValueKind.String`.
  - `GetBaselineCompliance`: group entries by hive (ordinal-ignore-case); `ResolveHivePath`; `!File.Exists(hivePath)` → `_callbacks.Verbose` + `NotPresent` observations for that group (never throw); else `reader.OpenHive(hivePath)` once per group; per entry `reader.GetKey(hive, entry.KeyPath)` (null → `NotPresent`); find the value by case-insensitive name (empty name → default value); project `NormalizeValueData(v.ValueData)` + the parser's `ValueType` string; funnel into the pure `BuildObservation`; per-entry `try/catch` → `_callbacks.Warning` + `NotPresent` observation.
  - `ResolveHivePath`: `HKLM\SOFTWARE` → `Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE")`; `HKLM\SYSTEM` → same with `SYSTEM`; `HKU\DefaultUser` → `Path.Combine(mountPath, "Users", "Default", "NTUSER.DAT")`; unknown → `...config\<hive.Replace('\\','_')>`; ordinal-ignore-case matching.
  - `ValuesEquivalent`: trim both; both parse as `long` invariant → numeric equality; else ordinal-ignore-case equality; null only equals null.
  - `MapOperationKey`: `HKLM\SOFTWARE` → `"SOFTWARE\" + keyPath`; `HKLM\SYSTEM` and `HKU\DefaultUser` → keyPath; unknown → `ArgumentException`.
  - `MapOperationHive`: `HKLM\*` → `"HKLM"`; `HKU\DefaultUser` → `"HKU"`; unknown → `ArgumentException` (must match `NativeRegistryService.MountRequiredHives`'s exact strings so the right hives mount).
  - `ToWriteValue`: DWord → `Convert.ToUInt32(entry.ExpectedValue, invariant)`; QWord → ulong; String/ExpandString → trimmed string; else `ArgumentOutOfRangeException`.
  - `BuildApplyOperations`: one `RegistryOperation { Operation = Modify, Hive = MapOperationHive, Key = MapOperationKey, ValueName, Value = ToWriteValue, ValueType }` per entry.
  - `DescribeApplyAction(pendingCount, alreadyCount, imageName)`; `BuildApplyRows(...)`; `BuildApplyResult(...)` per spec.
- [x] **Step 2: Build** to confirm the new code compiles (user-owned files may still break the tree — scratch-tree fallback if so).

### Task 3: Cmdlets

**Files:**
- Create: `src/Cmdlets/SecurityBaselineCmdlets.cs`

**Interfaces:**
- `GetWindowsImageSecurityBaselineCmdlet` — Get verb; `MountedWindowsImage[] MountedImages` (Mandatory, Position 0, ValueFromPipeline, `ValidateNotNull`), `SwitchParameter ContinueOnError`; `[OutputType(typeof(WindowsImageSecurityBaselineReport[]))]`.
- `SetWindowsImageSecurityBaselineCmdlet` — Set verb, `SupportsShouldProcess = true`; `MountedWindowsImage[] MountedImages` (Mandatory, Position 0, ValueFromPipeline), `SwitchParameter ContinueOnError`; `[OutputType(typeof(WindowsImageSecurityBaselineApplyResult[]))]`.

- [x] **Step 1: Write `GetWindowsImageSecurityBaselineCmdlet`** — accumulate in `ProcessRecord`; in `EndProcessing` warn+return on no images; per image resolve `MountPath?.FullName` (null → `LoggingService.WriteError` + throw unless `ContinueOnError`); `using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this))`; `GetBaselineCompliance(reader, image.ImageName, mountPath)`; `WriteObject(reports.ToArray())`; mirror `GetWindowsImageServiceCmdlet` failure handling (`ImageNotMounted` error record, try/catch around the per-image body).
- [x] **Step 2: Write `SetWindowsImageSecurityBaselineCmdlet`** — accumulate in `ProcessRecord`; in `EndProcessing` warn+return on no images; per image: resolve mount path (same failure handling); read compliance in memory (`GetBaselineCompliance` via a `using` reader); partition entries (pure helpers only: compliant → `AlreadyApplied` rows; missing-hive → `Skipped` rows with detail; rest = pending); when pending is empty emit the result (Success = true) with a verbose note; otherwise `ShouldProcess(target, DescribeApplyAction(...))` — false → `continue` (nothing written, no rows emitted for this image); wrap the write in `LoggingService.LogOperationStartWithTimestamp` / `LogOperationCompleteWithTimestamp`; write via `new NativeRegistryService().ApplyRegistryOperations(mountPath, BuildApplyOperations(pending).ToArray(), this)`; `true` → pending rows `Applied` + `Success = true`; `false`/exception → pending rows `Failed` + `Success = false` + `ErrorMessage`, error logged and rethrown unless `ContinueOnError`. Do NOT add new native/hive logic.
- [x] **Step 3: Build** to confirm both compile (scratch-tree fallback if the user-owned files still break the tree).

### Task 4: Help files

**Files:**
- Create: `docs/help/Get-WindowsImageSecurityBaseline.md`
- Create: `docs/help/Set-WindowsImageSecurityBaseline.md`

- [x] **Step 1: Write both PlatyPS files** (front matter `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`, online version URL, `schema: 2.0.0`; SYNTAX with `-MountedImages` pipeline Position 0 + `-ProgressAction` (+ `-WhatIf`/`-Confirm` on Set); PARAMETERS documenting every live parameter (`MountedImages`, `ContinueOnError`, `ProgressAction`, plus `WhatIf`/`Confirm` on Set), `Accept pipeline input: True (ByValue)` on `MountedImages`; INPUTS/OUTPUTS full type names; common-parameters paragraph) — model on `docs/help/Get-WindowsImageService.md`. Include EXAMPLE sections (Get on one image, Get piped from a mount list, Set with `-WhatIf`, Set for real, Set piped with `-ContinueOnError`). The Set DESCRIPTION must mention the hive-mounted write (elevation required), already-compliant skipping, and the `-WhatIf` support; the Get DESCRIPTION must mention Compliant/NonCompliant/NotPresent semantics.

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/SecurityBaselineServiceTests.cs`

- [x] **Step 1: Create `SecurityBaselineServiceTests.cs`** — pure xUnit `[Fact]`/`[Theory]`, no mock framework, no hive files:
  - `GetBaselineEntries_IsCuratedAndWellFormed` — 22 entries; hives only the three known names; non-blank `KeyPath`/`ValueName`/`ExpectedValue`/`Rationale`; kinds only `DWord`/`String`; DWord expected values parse as integers; unique `Hive\KeyPath\ValueName`; two calls return equal tables (stable order).
  - `NormalizeValueData_CollapsesAndTrims` — CRLF/CR → LF, trim, null/blank → empty.
  - `ValuesEquivalent_Theory` — `"1"`/`"1"` equal; `"255"`/`"255"` equal; `" 900 "`/`"900"` equal; `"ScreenSaver"`/`"screensaver"` equal (case-insensitive string); `"1"`/`"2"` unequal; `null`/`"0"` unequal; `null`/`null` equal.
  - `CompareEntry_MapsStates` — null → `NotPresent`; equal → `Compliant`; different → `NonCompliant`.
  - `ResolveHivePath_MapsKnownHives` — temp mount path → `config\SOFTWARE` / `config\SYSTEM` / `Users\Default\NTUSER.DAT`, case-insensitive, unknown → config fallback.
  - `MapOperationKey_And_MapOperationHive_MapsHivesForWritePath` — SOFTWARE prefix / SYSTEM relative / HKU relative; unknown hive throws `ArgumentException`.
  - `ToWriteValue_ConvertsKinds` — DWord `"1"` → `1u`, DWord `"255"` → `255u`, String `" 900 "` → `"900"`, QWord → ulong, invalid DWord throws.
  - `BuildApplyOperations_ProducesWritePathOperations` — one `Modify` op per entry; `Hive`/`Key` per the three hive mappings; `ValueName`/`Value`/`ValueType` from the entry.
  - `DescribeApplyAction_MentionsCounts` — contains pending count, already count, image name.
  - `BuildApplyRows_And_BuildApplyResult_MapStates` — written rows get `Applied`/`Failed` + detail, secondary rows get `AlreadyApplied`/`Skipped`; counts and `Success`/`ErrorMessage` on the result.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~SecurityBaselineService"`) and confirm they pass (scratch tree if the real tree cannot build).

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors on non-user-owned files; if the user's three in-flight files still break the tree, verify in the scratch tree `C:\Users\ConOmal\AppData\Local\Temp\opencode\pswit-verify-baseline` and report that).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 5 / Step 2).
- [x] **Step 3: Run help guardrail** `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"`; confirm checks 1–3 pass (new cmdlets not yet exported) and report check 4 (shipped MAML) as the expected orchestrator-only failure.
- [x] **Step 4: Integration note** — real-image compliance reporting and hive-mounted remediation are verified manually/CI on a mounted image (elevation required for `RegLoadKey`/`RegUnLoadKey`); no local DISM `OpenOfflineSession` dependency for either path. No Pester changes made.
- [x] **Step 5: Final report** — spec + plan paths, exact cmdlet names (`Get-WindowsImageSecurityBaseline`, `Set-WindowsImageSecurityBaseline`), baseline composition (22 entries: 9 SOFTWARE, 11 SYSTEM, 2 default-user NTUSER.DAT; `config\DEFAULT` excluded with rationale), write-path invocation (`NativeRegistryService.ApplyRegistryOperations` directly, one batch per image, hive mapping explanation), verification tree (real vs scratch + why), test counts, verify-help outcome, deviations. Leave working tree uncommitted.
