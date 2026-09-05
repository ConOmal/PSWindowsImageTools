# Windows Image Services Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageService` and `Set-WindowsImageService` to PSWindowsImageTools — registry-based query and mutation of a mounted image's offline `SYSTEM` hive service set (`ControlSet001\Services`). Reads use the existing in-memory `IRegistryHiveReader` path; writes delegate to the existing hive-mounted `NativeRegistryService.ApplyRegistryOperations` path. No existing file is modified.

**Architecture:** Mirror the established convention: new `src/Models/WindowsImageServiceModels.cs` (start-type enum, info POCO, set result), new `src/Services/WindowsImageServicesService.cs` (thin `IRegistryHiveReader` enumeration + `NativeRegistryService` delegation, with all decision logic as pure `internal static` methods), new `src/Cmdlets/WindowsImageServicesCmdlets.cs` (`MountedWindowsImage[]` pipeline accumulator + `EndProcessing` per-image handling + `SupportsShouldProcess` on Set), new PlatyPS help files under `docs/help/`, and a pure unit-test class (no hive files, no DISM, no mocks) matching the `RegistryDriftServiceTests` / `ReservedStorageServiceTests` pattern.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), `Registry.dll` (existing), `Microsoft.Win32` (existing), xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-services-configuration-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions, `??=`, and nullable annotations exactly as `RegistryDriftService.cs` / `WindowsImageEditionService.cs`.
- Do NOT touch `src/Cmdlets/ExportWindowsImageCmdlet.cs` — it carries another agent's/uncommitted in-progress `SplitSize` edit that currently breaks the build (`CS0103 CompressionType`); not ours to fix. Re-check the build at the end; if it still fails, report it in the final summary instead of editing that file.
- Do NOT touch `src/Services/NativeRegistryService.cs` or `src/Services/RegistryDriftService.cs` (read-only references). Do NOT touch `src/Models/RegistryOperation.cs`, `RegistryDriftModels.cs`, `RegistryHiveReader.cs`, `IRegistryHiveReader.cs`, `MountedWindowsImage.cs`, or any component-store/driver/health/OOBE/security/WinRE/servicing file.
- Do NOT change `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (`CmdletsToExport` is the orchestrator's job — report the two exact cmdlet names). Do NOT regenerate the shipped MAML.
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Do not run the full unit suite or the Pester integration suite locally. Verification is: `dotnet build src/PSWindowsImageTools.csproj` (0 errors; on MSBuild `.obj`/file-lock errors from a concurrent build wait ~30s and retry), then `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsImageServicesService"`, then `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"`. verify-help check 4 (shipped MAML) is an expected failure — report it, don't fix it. New cmdlets are not yet exported, so checks 1–3 must pass.
- Reads that touch real hive content (enumeration through `Registry.dll`) and the hive-mounted write are manual/CI-only on a real mounted image, consistent with repo policy and the broken local DISM `OpenOfflineSession` servicing path (documented in `docs/OpenCode-EngLog.md`). Everything testable locally is pure logic.
- Test class: `WindowsImageServicesServiceTests` (new file). Use the temp-directory fixture pattern (`Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"))`) only where disk access is required (hive-path resolution); all other tests are pure in-memory.

---

### Task 1: Windows Image Service models

**Files:**
- Create: `src/Models/WindowsImageServiceModels.cs`

**Interfaces:**
- `enum WindowsImageServiceStartType { Boot, System, Automatic, Manual, Disabled, Unknown }`.
- `WindowsImageServiceInfo { ImageName, MountPath, Name, DisplayName, ImagePath, Description, StartType, StartValue, DelayedAutoStart, RegistryValues }` (`RegistryValues` is `Dictionary<string, object>?`, null unless detailed).
- `WindowsImageServiceOperationResult { ImageName, ServiceName, Operation, RequestedStartType, SetDelayedAutoStart, Success, ErrorMessage }`.

- [ ] **Step 1: Create `src/Models/WindowsImageServiceModels.cs`** with the three types above (plain POCOs, `= string.Empty` / `new Dictionary<...>()` initializers, XML doc comments, `ToString()` overrides mirroring `RegistryDriftModels.cs` / `ReservedStorageModels.cs`).
- [ ] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm the project compiles (regardless of the pre-existing `ExportWindowsImageCmdlet.cs` break — Step 2 is informational; the baseline may still be broken).

### Task 2: WindowsImageServicesService — pure logic + thin read/write

**Files:**
- Create: `src/Services/WindowsImageServicesService.cs`

**Interfaces:**
- `WindowsImageServicesService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default (mirror `RegistryHiveReader`).
- `public const string SystemHiveName = "HKLM\\SYSTEM"`; `internal const string ServicesKeyPath = @"ControlSet001\Services"`.
- `public List<WindowsImageServiceInfo> GetServices(IRegistryHiveReader reader, string imageName, string mountPath, string? nameFilter = null, bool detailed = false)` — thin; the only enumeration path.
- `public bool ServiceExists(IRegistryHiveReader reader, string mountPath, string serviceName)` — thin pre-flight for Set.
- Pure `internal static` (unit-testable): `ResolveSystemHivePath`, `IsValidServiceName`, `ParseStartType`, `ToStartValue`, `GetDwordValue`, `GetStringValue`, `GetDelayedAutoStart`, `ProjectServiceInfo`, `CollectValues`, `MatchesNameFilter`, `ValidateSetParameters`, `BuildSetOperations`, `DescribeSetChange`, `BuildSetResult` — signatures per spec.

- [ ] **Step 1: Write `WindowsImageServicesService.cs`**:
  - `GetServices`: resolve `ResolveSystemHivePath`; `!File.Exists(hivePath)` → `_callbacks.Verbose` + empty (never throw); `var hive = reader.OpenHive(hivePath)`; `var servicesKey = reader.GetKey(hive, ServicesKeyPath)` (null → verbose + empty); for each non-empty subkey name, skip when `!MatchesNameFilter(name, nameFilter)`, then `reader.GetKey(hive, $"{ServicesKeyPath}\\{name}")` (null → continue), project `key.Values` → `(ValueName, ValueData)` tuples into `ProjectServiceInfo`; attach `CollectValues` when `detailed`; per-service `try/catch` → `_callbacks.Warning` + continue.
  - Pure helpers behave exactly as specced (case-insensitive value lookup by name, sorted `CollectValues`, `^(?i:<filter>)$` regex fallback with 1s timeout returning false on invalid/timeout, `StartTypeValue` throwing for `Unknown`, operations with `Hive = "HKLM"`, `Key = $"{ServicesKeyPath}\\{name}"`, DWord values, `Operation = RegistryOperationType.Modify`).
  - `ValidateSetParameters`: throw `ArgumentException` when `!startType.HasValue && !setDelayedAutoStart`, and when `setDelayedAutoStart && startType.HasValue && startType != Automatic`.
- [ ] **Step 2: Build** to confirm it compiles.

### Task 3: Cmdlets

**Files:**
- Create: `src/Cmdlets/WindowsImageServicesCmdlets.cs`

**Interfaces:**
- `GetWindowsImageServiceCmdlet` — Get verb; `MountedWindowsImage[] MountedImages` (Mandatory, Position 0, ValueFromPipeline, `ValidateNotNull`), `string Name` (Position 1), `SwitchParameter Detailed`, `SwitchParameter ContinueOnError`; `[OutputType(typeof(WindowsImageServiceInfo[]))]`.
- `SetWindowsImageServiceCmdlet` — Set verb, `SupportsShouldProcess = true`; `MountedWindowsImage[] MountedImages` (Mandatory, Position 0, ValueFromPipeline), `string Name` (Mandatory, Position 1, `ValidateNotNullOrEmpty`), `WindowsImageServiceStartType? StartType`, `SwitchParameter DelayedAutoStart`, `SwitchParameter ContinueOnError`; `[OutputType(typeof(WindowsImageServiceOperationResult[]))]`.

- [ ] **Step 1: Write `GetWindowsImageServiceCmdlet`** — accumulate in `ProcessRecord`; in `EndProcessing` warn+return on no images; `using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this))` + `new WindowsImageServicesService(ModuleCallbacks.FromCmdlet(this))`; per image resolve `MountPath?.FullName` (null → `WriteError` + throw unless `ContinueOnError`); `GetServices(reader, image.ImageName, mountPath, NameFilter, Detailed)`; `WriteObject(results.ToArray())`; mirror `GetWindowsImageComponentStoreCmdlet` failure handling.
- [ ] **Step 2: Write `SetWindowsImageServiceCmdlet`** — accumulate in `ProcessRecord`; in `EndProcessing` warn+return on no images; validate `WindowsImageServicesService.ValidateSetParameters(StartType, DelayedAutoStart.IsPresent)` and `IsValidServiceName(Name)` (terminating `ArgumentException`); per image resolve `MountPath` (null → error + throw unless `ContinueOnError`); `ServiceExists` pre-flight (not found → `InvalidOperationException` error, throw unless `ContinueOnError`); `BuildSetOperations`; `ShouldProcess($"{Name} on {mountPath}", DescribeSetChange(...))` (skips image when false = `-WhatIf`); write path `new NativeRegistryService().ApplyRegistryOperations(mountPath, ops.ToArray(), this)`; `WriteObject(BuildSetResult(...))` with success/error mapped; wrap each image in `LoggingService.LogOperationStartWithTimestamp` / `LogOperationCompleteWithTimestamp`. Do NOT add new native/hive logic.
- [ ] **Step 3: Build** to confirm both compile.

### Task 4: Help files

**Files:**
- Create: `docs/help/Get-WindowsImageService.md`
- Create: `docs/help/Set-WindowsImageService.md`

- [ ] **Step 1: Write both PlatyPS files** (front matter `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`, online version URL, `schema: 2.0.0`; SYNTAX with `-MountedImages` pipeline Position 0 + `-ProgressAction`; PARAMETERS documenting every live parameter, `Accept pipeline input: True (ByValue)` on `MountedImages`; INPUTS/OUTPUTS full type names; common-parameters paragraph) — model on `Get-WindowsImageComponentStore.md` / `Set-WindowsImageReservedStorage.md`. Include EXAMPLE sections (Get with `-Name`, Get with `-Detailed`, Set with `-WhatIf`, Set `-StartType Disabled`, Set `-DelayedAutoStart`).

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/WindowsImageServicesServiceTests.cs`

- [ ] **Step 1: Create `WindowsImageServicesServiceTests.cs`** — pure xUnit `[Fact]`/`[Theory]`, hand-built `(Name, Data)` tuples, no mock framework, no hive files:
  - `ParseStartType` maps 0–4 and 5/-1 → `Unknown`.
  - `ToStartValue` round-trips 0–4; throws for `Unknown`.
  - `GetDwordValue`/`GetStringValue`/`GetDelayedAutoStart` case-insensitive lookup + absent/0/non-numeric handling.
  - `ProjectServiceInfo` projects a tuple set (with and without `Start`).
  - `CollectValues` name-sorted.
  - `MatchesNameFilter` (null/blank/exact/regex/invalid regex/empty name/timeout).
  - `ResolveSystemHivePath` temp-dir mount path → `...\config\SYSTEM`.
  - `IsValidServiceName` (blank, `\`, `/`, valid).
  - `ValidateSetParameters` (nothing requested; `-DelayedAutoStart` + `Manual` throws; valid combos pass).
  - `BuildSetOperations` (Start op DWord value/type/Key; `DelayedAutoStart` op = 1 DWord; single op per change).
  - `DescribeSetChange` / `BuildSetResult` mapping.
- [ ] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~WindowsImageServicesService"`) and confirm they pass.

### Task 6: Final verification

Files: none.

- [ ] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors target; re-check whether the pre-existing `ExportWindowsImageCmdlet.cs` break is still present and report it untouched if so).
- [ ] **Step 2: Run filtered unit tests** (same filter as Task 5 / Step 2).
- [ ] **Step 3: Run help guardrail** `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"`; confirm checks 1–3 pass (new cmdlets not yet exported) and report check 4 (shipped MAML) as the expected orchestrator-only failure.
- [ ] **Step 4: Integration note** — real-image enumeration and hive-mounted writes are verified manually/CI on a mounted image (elevation required for `RegLoadKey`/`RegUnLoadKey`); no local DISM `OpenOfflineSession` dependency for either path. No Pester changes made.
- [ ] **Step 5: Final report** — spec + plan paths, exact cmdlet names (`Get-WindowsImageService`, `Set-WindowsImageService`), convention choice (`MountedWindowsImage[]` pipeline accumulator + `EndProcessing`, mirroring `Get-WindowsImageComponentStore`) + why, write-path invocation (`NativeRegistryService.ApplyRegistryOperations` directly, not `RegistryApplicationService`) + why, `-Name` filter semantics, `ControlSet001\Services` choice, test counts, verify-help outcome, deviations, and the untouched pre-existing `ExportWindowsImageCmdlet.cs` build break. Leave working tree uncommitted.