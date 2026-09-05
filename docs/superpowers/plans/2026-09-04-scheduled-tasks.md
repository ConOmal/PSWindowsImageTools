# Scheduled Tasks (TaskCache inventory) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add a read-only scheduled-task inventory to PSWindowsImageTools — `Get-WindowsImageScheduledTask` reporting task path, GUID, state (friendly + raw), `Uri`, and `-Detailed` raw cache-entry values from each mounted image's offline SOFTWARE hive (`Schedule\TaskCache`) — with no blob parsing and no writes.

**Architecture:** Mirror the completed Services half exactly: `Models/*.cs` for the new types, `Services/ScheduledTasksService.cs` for the work, one `Cmdlets/*.cs` cmdlet, and pure `internal static` methods for every piece of logic a unit test can drive without hive files or DISM (tree-path composition, path filtering, state mapping, projection, value collection). The thin, non-unit-tested surface is the hive read itself (`IRegistryHiveReader.OpenHive`/`GetKey` + the `Registry` package `RegistryKey.Values`/`SubKeys` enumeration), which reuses the module's existing in-memory hive-reading pattern and gets real-hive coverage only via the manual/CI image step.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), `Registry` 1.5.0 (existing dependency), xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-scheduled-tasks-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Nullable annotations exactly as `WindowsImageServicesService.cs` uses; remember netstandard2.0 ref assemblies lack `[NotNullWhen]` — use `!`/`?? string.Empty` narrowing like the sibling phase.
- No new NuGet/assembly dependencies.
- Do NOT touch the user's in-flight files (`src/Services/WimExportService.cs`, `src/Cmdlets/ExportWindowsImageCmdlet.cs`, `src/Cmdlets/MountWindowsImageListCmdlet.cs`, `.claude/`, user docs under `docs/superpowers/*boot-image*` / `*app-provisioning*` / `*image-checkout*`) or the sibling phase's files (`WindowsImageServicesService.cs`, `WindowsImageServiceModels.cs`, `WindowsImageServicesCmdlets.cs`, `WindowsImageServicesServiceTests.cs`, `docs/help/Get-WindowsImageService.md`, `docs/help/Set-WindowsImageService.md`) — read-only references.
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (orchestrator adds the `CmdletsToExport` entry) and do NOT sync DLLs into `Module/PSWindowsImageTools/bin/`.
- Do NOT commit. Leave all changes in the working tree.
- The local DISM `OpenOfflineSession` servicing limitation (documented in `docs/OpenCode-EngLog.md`) makes real-image verification manual/CI-only. Being registry-based, everything locally testable is pure logic.
- Do not run the full unit suite or the Pester integration suite (concurrent builders). If MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry. If the real-tree build fails **only** in the user's three in-flight files, fall back to the scratch-tree verification: copy the repo to `C:\Users\ConOmal\AppData\Local\Temp\opencode\pswit-verify-tasks`, `git checkout HEAD -- src/Services/WimExportService.cs src/Cmdlets/ExportWindowsImageCmdlet.cs src/Cmdlets/MountWindowsImageListCmdlet.cs` there, build/test in the scratch copy.
- Test class: `ScheduledTasksServiceTests` (new), pure tests only.

---

### Task 1: Scheduled-task models

**Files:**
- Create: `src/Models/WindowsImageScheduledTaskModels.cs`

**Interfaces:**
- `enum WindowsImageScheduledTaskState { Unknown, Disabled, Queued, Ready, Running }` — 1–4 map to the documented names; 0/absent/out-of-range surface as `Unknown` (raw DWORD always available on the info object).
- `WindowsImageScheduledTaskInfo { ImageName, MountPath, TaskPath, TaskGuid, State, StateValue, Uri, HasTasksEntry, RegistryValues }` — `StateValue` = raw DWORD, `-1` when absent/non-numeric; `RegistryValues` null unless `-Detailed`.

- [x] **Step 1: Create `src/Models/WindowsImageScheduledTaskModels.cs`** with the enum + info class (plain POCOs, `= string.Empty` / `-1` initializers, XML doc comments, `ToString()` mirroring `WindowsImageServiceInfo`).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles (or note the user-file failures and continue).

### Task 2: ScheduledTasksService — pure logic + thin hive read

**Files:**
- Create: `src/Services/ScheduledTasksService.cs`

**Interfaces:**
- `ScheduledTasksService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default (mirror `WindowsImageServicesService`).
- Constants: `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`, `internal const string TaskCacheKeyPath`, `TreeSubKeyName = "Tree"`, `TasksSubKeyName = "Tasks"`, `TreeIdValueName = "Id"`, `StateValueName = "State"`, `UriValueName = "Uri"`.
- `public List<WindowsImageScheduledTaskInfo> GetScheduledTasks(IRegistryHiveReader reader, string imageName, string mountPath, string? pathFilter = null, bool detailed = false, Action<int, string>? progress = null)` — thin: resolve hive path (`File.Exists` guard → verbose + empty), `OpenHive`, read `TaskCache` → `Tree` (absent → verbose + empty), walk the hierarchy depth-first collecting `(TaskPath, TaskGuid)` for leaves with a non-empty `Id` (skip blank subkey names; per-node try/catch → warning), `FilterTreeTasks`, then per task read `Tasks\<guid>` (null → no entry), collect `(Name, Data)` tuples, `BuildTaskInfo`; per-task try/catch → warning + continue; drive `progress` during the loop; final verbose summary.
- `internal static string ResolveSoftwareHivePath(string mountPath)`.
- `internal static string JoinTreePath(string parentPath, string nodeName)`.
- `internal static List<(string TaskPath, string TaskGuid)> FilterTreeTasks(List<(string TaskPath, string TaskGuid)> tasks, string? pathFilter)`.
- `internal static bool MatchesPathFilter(string? taskPath, string? filter)` — blank matches all; exact case-insensitive wins; else anchored `(?i:)` regex with 1s timeout; invalid/timeout matches nothing.
- `internal static WindowsImageScheduledTaskState ParseTaskState(int value)`.
- `internal static int? GetDwordValue(IEnumerable<(string Name, object? Data)> values, string valueName)`.
- `internal static string GetStringValue(IEnumerable<(string Name, object? Data)> values, string valueName)`.
- `internal static Dictionary<string, object>? CollectValues(IEnumerable<(string Name, object? Data)> values)` — ordinal-sorted, blank names skipped (mirror Services helper).
- `internal static WindowsImageScheduledTaskInfo BuildTaskInfo(string imageName, string mountPath, string taskPath, string taskGuid, bool hasTasksEntry, IEnumerable<(string Name, object? Data)>? values, bool detailed)`.

- [x] **Step 1: Write `ScheduledTasksService.cs`** with all members above (read path mirrors `WindowsImageServicesService.GetServices`; pure helpers unit-testable, tuples like `RegistryDriftService`).
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Get-WindowsImageScheduledTask cmdlet

**Files:**
- Create: `src/Cmdlets/GetWindowsImageScheduledTaskCmdlet.cs`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Get, "WindowsImageScheduledTask")]`, `[OutputType(typeof(WindowsImageScheduledTaskInfo[]))]`, read-only (no `SupportsShouldProcess`).
- `MountedImages` (`MountedWindowsImage[]`, Mandatory, Position 0, ValueFromPipeline) + `_allMountedImages` accumulator (`ProcessRecord`/`EndProcessing`).
- `-Path` (string, Position 1) — exact-match-then-anchored-regex task-path filter.
- `-Detailed`, `-ContinueOnError` switches.
- `EndProcessing`: no-images warning; per image — mount-path guard (`ImageNotMounted` unless `-ContinueOnError`), `LogOperationStartWithTimestamp`/`LogOperationCompleteWithTimestamp`, `ProgressService.CreateProgressCallback` (per image, indexed), `new ScheduledTasksService(ModuleCallbacks.FromCmdlet(this))` + `using var reader = new RegistryHiveReader(...)`, per-image try/catch honoring `-ContinueOnError`; `WriteObject(results.ToArray())`.

- [x] **Step 1: Write the cmdlet** mirroring `GetWindowsImageServiceCmdlet` plus `-Path`, timestamps, and the progress callback.
- [x] **Step 2: Build** to confirm everything compiles.

### Task 4: PlatyPS help file

**Files:**
- Create: `docs/help/Get-WindowsImageScheduledTask.md`

- [x] **Step 1: Write the help markdown** modeled on `docs/help/Get-WindowsImageService.md`: same front matter (`external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`, `online version`, `schema: 2.0.0`), SYNOPSIS/SYNTAX/DESCRIPTION/EXAMPLES/`## PARAMETERS` documenting `-MountedImages`, `-Path`, `-Detailed`, `-ContinueOnError` (+ `-ProgressAction` placeholder + CommonParameters), INPUTS/OUTPUTS (`PSWindowsImageTools.Models.WindowsImageScheduledTaskInfo[]`), DESCRIPTION noting the read-only registry-only behavior and the honest blob limitation.
- [x] **Step 2: Sanity-check** against `Scripts/verify-help.ps1` check 2 conventions (every non-common parameter documented under `## PARAMETERS`).

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/ScheduledTasksServiceTests.cs`

- [x] **Step 1: Write pure xUnit tests** (plain `[Fact]`/`[Theory]`, no mocks, no hive files):
  - `ParseTaskState` mapping (1–4 named; 0, 5, -1 → Unknown).
  - `JoinTreePath` root + nested composition.
  - `MatchesPathFilter`: blank matches all; exact case-insensitive; anchored regex; invalid regex → nothing; empty path → nothing.
  - `FilterTreeTasks`: sorting by path, filtering, GUID pairing preserved.
  - `BuildTaskInfo`: with State/Uri (detailed + non-detailed), state absent → Unknown/-1, no Tasks entry → `HasTasksEntry = false` + null values, non-numeric State → -1/Unknown.
  - `GetDwordValue` / `GetStringValue` / `CollectValues` edge cases (absent/null/non-numeric, case-insensitivity, blank names skipped, ordinal sort).
  - `ResolveSoftwareHivePath` temp-dir fixture.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~ScheduledTasksServiceTests"`) and confirm they pass.

### Task 6: Verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors for our files; scratch-tree fallback if the user's three in-flight files break the tree — record which tree was used).
- [x] **Step 2: Run filtered unit tests** (`dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~ScheduledTasksServiceTests"`).
- [x] **Step 3: Help guardrail sanity run** `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"` — the shipped module does not yet export the new cmdlet (orchestrator step), so no new drift should be reported; report outcome.
- [x] **Step 4: Integration note** — real-image TaskCache reads are verified manually/CI on a mounted image (local DISM limitation); no Pester changes made.
- [x] **Step 5: Final report** — spec + plan paths, exact cmdlet name (`Get-WindowsImageScheduledTask`), fields exposed, blob limitation, verification tree (real vs scratch), test counts, deviations. Leave working tree uncommitted.

### Orchestrator hand-off (not this work)

- Add `Get-WindowsImageScheduledTask` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`, sync the built DLL into `Module/PSWindowsImageTools/bin/`, and re-run `Scripts/build-help.ps1` + `New-ExternalHelp` to regenerate the shipped MAML.
