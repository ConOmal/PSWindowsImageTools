# OOBE Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageOOBE` and `Set-WindowsImageOOBE` to PSWindowsImageTools — query and modify Out-of-Box-Experience settings (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE`) in one or more mounted images' offline SOFTWARE hives, via the existing in-memory hive reader and the hive-mounted native write path.

**Architecture:** Mirror the completed Services phase exactly: `Models/WindowsImageOobeModels.cs` for the new types, `Services/WindowsImageOobeService.cs` for the work (a documented 7-entry OOBE setting catalog plus pure mapping/validation/operation/result builders and one thin hive-read method), and `Cmdlets/WindowsImageOobeCmdlets.cs` with the `MountedWindowsImage[]` pipeline-accumulator convention. The Set cmdlet honors SupportsShouldProcess and delegates every write to `NativeRegistryService.ApplyRegistryOperations` (`Hive = "HKLM"`, `Key = "SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE"` so the SOFTWARE hive is mounted and mapped).

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enable per existing `.csproj`), xUnit (`tests/PSWindowsImageTools.Tests/`, plain `[Fact]`/`[Theory]`, no mocking framework).

**Spec:** `docs/superpowers/specs/2026-09-04-oobe-configuration-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions, `??=`, and nullable annotations exactly as `RegistryOperation.cs` / the existing services do. netstandard2.0 ref assemblies lack `[NotNullWhen]` — use `!` / `?? string.Empty` patterns for null-narrowing.
- No new NuGet/assembly dependencies.
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (the orchestrator adds the two `CmdletsToExport` entries after review).
- Do NOT touch the user's in-flight files (`src/Services/WimExportService.cs`, `src/Cmdlets/ExportWindowsImageCmdlet.cs`, `src/Cmdlets/MountWindowsImageListCmdlet.cs`), the completed Services-phase files, or any other agent's files (`NativeRegistryService.cs`, `FormatUtilityService.cs`, `RegistryDriftService.cs` and friends are read-only references).
- No DISM calls anywhere. The phase is registry-based; real-image verification is manual/CI-only (local DISM `OpenOfflineSession` limitation per `docs/OpenCode-EngLog.md`).
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Do not run the full unit suite or any integration suite (concurrent builders). Verification is: `dotnet build src/PSWindowsImageTools.csproj` (0 errors; if the build fails **on the user's in-flight files**, copy the repo to `C:\Users\ConOmal\AppData\Local\Temp\opencode\pswit-verify-oobe`, `git checkout HEAD -- src/Services/WimExportService.cs src/Cmdlets/ExportWindowsImageCmdlet.cs src/Cmdlets/MountWindowsImageListCmdlet.cs` in the scratch copy, and build/test there; if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry), then a filtered `dotnet test --filter "FullyQualifiedName~WindowsImageOobe"`.
- New test class: `WindowsImageOobeServiceTests`. New cmdlet names (exact): `Get-WindowsImageOOBE`, `Set-WindowsImageOOBE`.

---

### Task 1: OOBE models

**Files:**
- Create: `src/Models/WindowsImageOobeModels.cs`

**Interfaces:**
- `enum WindowsImageOobeProtectYourPc { Recommended = 1, ImportantOnly = 2, NotInProgram = 3 }`.
- `WindowsImageOobeSettingDefinition { SettingName, ValueName, Description }`.
- `WindowsImageOobeSetting { ImageName, MountPath, SettingName, ValueName, Description, IsSet, Value (int?), State }` + `ToString()`.
- `WindowsImageOobeChange { ValueName, Value (int?) }` — null Value = remove.
- `WindowsImageOobeOperationResult { ImageName, Operation, Success, ErrorMessage }` + `ToString()`.

- [x] **Step 1: Create `src/Models/WindowsImageOobeModels.cs`** with the types above (plain POCOs, `= string.Empty` initializers, XML doc comments, `ToString()` overrides mirroring `WindowsImageServiceModels.cs`).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles. (Real tree currently fails on the user's in-flight `WimExportService.cs`; verified via the scratch-tree fallback.)

### Task 2: WindowsImageOobeService — catalog + pure logic + thin hive read

**Files:**
- Create: `src/Services/WindowsImageOobeService.cs`

**Interfaces:**
- `WindowsImageOobeService(ModuleCallbacks? callbacks = null)`.
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`.
- `internal const string OobeKeyPath = @"Microsoft\Windows\CurrentVersion\OOBE"` (read path, relative to the SOFTWARE hive root).
- `internal const string OobeOperationKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE"` (write path — `RegistryOperation.Key` must carry the `SOFTWARE\` prefix for `NativeRegistryService` hive mounting/mapping).
- `public static List<WindowsImageOobeSettingDefinition> GetDefaultSettings()` — the 7-entry catalog: SkipMachineOOBE, SkipUserOOBE, SkipPrivacyExperience, ProtectYourPC, BypassNRO, HideOnlineAccountScreens, HideWirelessSetupInOOBE.
- `public List<WindowsImageOobeSetting> GetSettings(IRegistryHiveReader reader, string imageName, string mountPath)` — thin; the only method that touches `IRegistryHiveReader`.
- `internal static string ResolveSoftwareHivePath(string mountPath)`.
- `internal static int? GetDwordValue(IEnumerable<(string Name, object? Data)> values, string valueName)`.
- `internal static WindowsImageOobeSetting ProjectSetting(string imageName, string mountPath, WindowsImageOobeSettingDefinition definition, int? value)`.
- `internal static bool IsValidValueName(string valueName)`.
- `internal static int ToProtectYourPcValue(WindowsImageOobeProtectYourPc mode)`.
- `internal static void ValidateChanges(List<WindowsImageOobeChange> changes)`.
- `internal static List<RegistryOperation> BuildSetOperations(List<WindowsImageOobeChange> changes)`.
- `internal static string DescribeSetChange(List<WindowsImageOobeChange> changes)`.
- `internal static WindowsImageOobeOperationResult BuildSetResult(string imageName, string operation, bool success, string? errorMessage)`.

- [x] **Step 1: Write `WindowsImageOobeService.cs`** with all members above:
  - `GetDefaultSettings`: fixed catalog order (machine OOBE, user OOBE, privacy experience, ProtectYourPC, BypassNRO, HideOnlineAccountScreens, HideWirelessSetupInOOBE); `SettingName == ValueName` for every entry; descriptions state semantics and legacy/build caveats.
  - `GetSettings`: resolve hive path; `!File.Exists` → verbose + empty (never throw); `reader.OpenHive` once; `reader.GetKey(hive, OobeKeyPath)`; missing key → every catalog entry `ProjectSetting(..., null)`; project in catalog order using `GetDwordValue`; per-entry try/catch → warning + continue.
  - `GetDwordValue`: ordinal-ignore-case; `Convert.ToInt32` with invariant culture in try/catch; absent/non-numeric → null.
  - `ProjectSetting`: `IsSet = value.HasValue`; `Value = value`; `State = value.HasValue ? $"Set: {value.Value}" : "Not set"`.
  - `ValidateChanges`: null/empty → `ArgumentException("Specify at least one OOBE change...")`; unknown `ValueName` → `ArgumentException` naming the value; a name both written (non-null Value) and removed (null Value) → `ArgumentException`.
  - `BuildSetOperations`: writes (`Modify`, `Hive = "HKLM"`, `Key = OobeOperationKeyPath`, `Value = (uint)change.Value.Value`, `ValueType = RegistryValueKind.DWord`) in catalog order first, then removals (`Remove`, same key, `Value = null`, `ValueType = RegistryValueKind.Unknown`).
  - `DescribeSetChange`: catalog-ordered `Write <Name>=<v>` parts then `Remove <Name>` parts, joined with `", "`.
  - `BuildSetResult`: mirror `WindowsImageServicesService.BuildSetResult` shape (without service-specific fields).
- [x] **Step 2: Build** to confirm it compiles. (Scratch tree.)

### Task 3: Get-WindowsImageOOBE + Set-WindowsImageOOBE cmdlets

**Files:**
- Create: `src/Cmdlets/WindowsImageOobeCmdlets.cs`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Get, "WindowsImageOOBE")]` `GetWindowsImageOobeCmdlet`:
  - `-MountedImages <MountedWindowsImage[]>` (Mandatory, Position 0, ValueFromPipeline), `-ContinueOnError`.
  - `ProcessRecord` accumulates; `EndProcessing` guards empty input, resolves `MountPath?.FullName ?? string.Empty` (missing mount path → error + `ImageNotMounted` terminating error unless `ContinueOnError`), reads via `RegistryHiveReader`, writes `WindowsImageOobeSetting[]` once.
- `[Cmdlet(VerbsCommon.Set, "WindowsImageOOBE", SupportsShouldProcess = true)]` `SetWindowsImageOobeCmdlet`:
  - `-MountedImages <MountedWindowsImage[]>`, tri-state switches `-SkipMachineOOBE`, `-SkipUserOOBE`, `-SkipPrivacyExperience`, `-BypassNRO`, `-HideOnlineAccountScreens`, `-HideWirelessSetupInOOBE` (present → write 1; `-X:$false` → write 0; absent → untouched), `-ProtectYourPC <WindowsImageOobeProtectYourPc?>`, `-Remove <string[]>`, `-ContinueOnError`.
  - `EndProcessing`: build `List<WindowsImageOobeChange>` (switch writes via `IsPresent`/`SwitchValue`, ProtectYourPC write via `ToProtectYourPcValue`, removals) → `ValidateChanges` (terminating `InvalidOobeConfiguration` on failure) → per image: `ShouldProcess($"OOBE settings on {mountPath}", operationName)` → `LogOperationStartWithTimestamp` → `new NativeRegistryService().ApplyRegistryOperations(mountPath, operations.ToArray(), this)` → `LogOperationCompleteWithTimestamp` → `WriteObject(BuildSetResult(...))` → failure handling with `ContinueOnError` (warning) or throw, mirroring `Set-WindowsImageService` exactly.
- `[OutputType]` attributes on both.

- [x] **Step 1: Write `WindowsImageOobeCmdlets.cs`** with both cmdlets (structurally clone `WindowsImageServicesCmdlets.cs`; keep `ComponentName` constants `Get-WindowsImageOOBE` / `Set-WindowsImageOOBE`).
- [x] **Step 2: Build** to confirm everything compiles. (Scratch tree; fixed a `SwitchParameter.SwitchValue` → `ToBool()` slip against PowerShellStandard 5.1.)

### Task 4: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/WindowsImageOobeServiceTests.cs`

- [x] **Step 1: Create `WindowsImageOobeServiceTests.cs`** — pure xUnit tests (no hive files, no DISM):
  - `GetDefaultSettings`: 7 entries; unique value names; `SettingName == ValueName` for all; all descriptions non-empty; SkipMachineOOBE/SkipUserOOBE/SkipPrivacyExperience/ProtectYourPC present.
  - `GetDwordValue`: case-insensitive hit; absent → null; non-numeric → null; null data → null.
  - `ProjectSetting`: set=1 → `IsSet` + `State = "Set: 1"`; set=0 → `State = "Set: 0"`; unset → `IsSet = false`, `State = "Not set"`, copies ImageName/MountPath/ValueName/Description.
  - `ResolveSoftwareHivePath`: temp mount path → `Windows\System32\config\SOFTWARE`.
  - `IsValidValueName`: catalog name case-insensitive → true; unknown → false; blank/null → false.
  - `ToProtectYourPcValue`: Recommended→1, ImportantOnly→2, NotInProgram→3.
  - `ValidateChanges`: null/empty → throws; unknown name → throws; name written and removed → throws; valid mixed list passes.
  - `BuildSetOperations`: write 1 → `Modify`/DWord/uint 1 with `SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE` key + `HKLM` hive; write 0 → uint 0; removal → `Remove` op; writes before removals; catalog order preserved.
  - `DescribeSetChange`: single write; ProtectYourPC write; write + remove combined.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~WindowsImageOobe"`) and confirm they pass. (33/33 in the scratch tree; fixed the write-before-remove ordering in `BuildSetOperations`/`DescribeSetChange` found by the ordering test.)

### Task 5: Help files

**Files:**
- Create: `docs/help/Get-WindowsImageOOBE.md`
- Create: `docs/help/Set-WindowsImageOOBE.md`

- [x] **Step 1: Create `docs/help/Get-WindowsImageOOBE.md`** — PlatyPS front matter (`external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`, `online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md`, `schema: 2.0.0`), SYNOPSIS/SYNTAX/DESCRIPTION/EXAMPLES/PARAMETERS (document every parameter incl. `-ProgressAction`)/INPUTS/OUTPUTS, mirroring `docs/help/Get-WindowsImageService.md` (parameters alphabetized).
- [x] **Step 2: Create `docs/help/Set-WindowsImageOOBE.md`** — same structure, mirroring `docs/help/Set-WindowsImageService.md`; document the tri-state switch semantics (`-X` writes 1, `-X:$false` writes 0, absent leaves untouched), the `ProtectYourPC` enum values, and `-Remove`.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` — 0 errors. (Real tree first failed on the user's in-flight `WimExportService.cs` (33 errors — their file, untouched); used the scratch-tree fallback at `C:\Users\ConOmal\AppData\Local\Temp\opencode\pswit-verify-oobe` (their three files restored to HEAD) for Tasks 1–4. By final verification the user's session had fixed their file, so Tasks 5–6 and the final build + tests were re-verified in the real tree: 0 errors, 0 warnings.)
- [x] **Step 2: Run filtered unit tests** (`dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsImageOobe"`). 33/33 pass in the real tree.
- [x] **Step 3: Integration note** — real-image OOBE read/write is manual/CI-only; nothing in this phase calls DISM. No Pester changes made. `Scripts/verify-help.ps1` passes (checks 1, 2, 4: 0 problems; check 3 skipped — platyPS not installed locally).
- [x] **Step 4: Final report** — spec + plan paths, exact cmdlet names (`Get-WindowsImageOOBE`, `Set-WindowsImageOOBE`), how the write path is invoked, test counts, real-tree vs scratch-tree verification, deviations. Leave working tree uncommitted.
