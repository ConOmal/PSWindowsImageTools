# Scheduled Tasks (TaskCache inventory) — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** phase-1 backlog item "Scheduled Tasks/Services config". The Services
half is complete (`Get-WindowsImageService` / `Set-WindowsImageService` reading/writing
`ControlSet001\Services`); this design covers the **scheduled tasks** half.

## Problem

Golden-image validation needs to know which scheduled tasks are registered in an image:
a staged persistence mechanism (a task that re-runs an agent at boot, logon, or on a
schedule) is exactly the kind of drift that survives a package/driver/registry-Run diff
and that operators care about before sealing an image.

Online tooling (`schtasks`, `Get-ScheduledTask`) cannot run against a mounted offline
image, and DISM exposes no scheduled-task provider. The machine-wide registration state
lives in the image's offline **SOFTWARE hive** under
`HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache`:

- `TaskCache\Tree` mirrors the task-path hierarchy (`\Microsoft\Windows\...`). Every
  subkey is a folder **or** a registered task; a task leaf carries an `Id` value
  (REG_SZ GUID) linking it to the per-task cache.
- `TaskCache\Tasks\<GUID>` holds one cache entry per task: a `State` REG_DWORD (modern
  Windows), a `Hash` REG_BINARY validation hash, a `Uri` REG_SZ (where present), and the
  **task-definition blob** (`Triggers`, `Actions`, `Principal`, `RegistrationInfo`,
  `DataInfo`, `DynamicInfo`, ...) in an *undocumented, version-dependent binary format*.

The module already reads offline hives fully in memory (`RegistryHiveReader` via the
`Registry` package — no hive mounting, no persistent file handles, no elevation), so
this feature needs no DISM at all. That matters operationally: the local DISM
`OpenOfflineSession` servicing limitation documented in `docs/OpenCode-EngLog.md` makes
any DISM-based path untestable locally, while a registry-only feature verifies fully
with build + unit tests.

## Scope decision

**This phase is READ-ONLY reporting.** The `Tasks\<GUID>` binary blob format is
undocumented and fragile: it is not a serialization we can safely decode, and mis-parsing
it would silently report wrong triggers/actions. Therefore:

1. **No parsing of the task-definition blob.** Triggers, actions, principals, run times
   and the task XML are *not* extracted. The blob limitation is a hard non-goal (below).
2. **No mutating cmdlet.** No `Set-`/`New-`/`Unregister-` scheduled-task support; no
   `SupportsShouldProcess`. Creating/altering TaskCache entries would require writing the
   undocumented blob (and regenerating `Hash`), which is out of the question.
3. **Report what is reliably readable:** the task path (composed from the `Tree`
   hierarchy), the associated GUID (`Id`), the `State` DWORD (friendly + raw) where
   present, the `Uri` value where present, whether the GUID has a matching `Tasks` entry,
   and — under `-Detailed` — the raw decoded values of the cache entry (same convention
   as `-Detailed` on `Get-WindowsImageService`).

If Tree/State data alone seems thin for a report, that is accepted: path + GUID + state
is exactly the honest inventory the registry supports without decoding the blob. Real
trigger/action data belongs to the live OS (`Get-ScheduledTask`) or a future
spec-diving effort into the blob format.

## Goals

1. A new cmdlet `Get-WindowsImageScheduledTask` reports every registered task found in
   the `TaskCache\Tree` of each mounted image's offline SOFTWARE hive: task path, GUID,
   state (friendly enum + raw DWORD, `-1` when absent), `Uri` when present, and whether
   the matching `Tasks` cache entry exists.
2. A `-Path` filter narrows the report: exact (case-insensitive) match first, otherwise
   the value is treated as an anchored, case-insensitive regex — the same semantics
   `-Name` uses on `Get-WindowsImageService`.
3. All decision logic (tree-path composition, path filtering, state mapping, projection,
   value collection) is pure, `internal static`, and unit-tested without hive files,
   DISM sessions or real images. The hive read path stays thin.
4. Reuse the in-memory hive-reading pattern exactly (`IRegistryHiveReader.OpenHive` /
   `GetKey`, `RegistryHiveReader` — no mounting, no persistent handles), mirroring the
   completed Services half (`WindowsImageServicesService.GetServices`) as the freshest
   in-repo reference.
5. Follow the module's cmdlet conventions: `MountedWindowsImage[]` pipeline accumulator,
   `LoggingService` verbose/warning/error + `LogOperationStartWithTimestamp` /
   `LogOperationCompleteWithTimestamp`, `ProgressService.CreateProgressCallback`,
   `ModuleCallbacks.FromCmdlet`, `-ContinueOnError` per-image error handling.

## Non-goals

- **Task-definition blob parsing.** The undocumented binary values (`Triggers`,
  `Actions`, `Principal`, `RegistrationInfo`, `DataInfo`, `DynamicInfo`) inside
  `Tasks\<GUID>` are **not** decoded into triggers/actions/users/times. `-Detailed`
  exposes the registry package's raw decoded values (which for binary values is an
  opaque string form) — useful for diffing/inspection, not for reporting semantics.
- **Writes.** No `Set-WindowsImageScheduledTask` / task creation / deletion / state
  change. Read-only output type only.
- **Per-user task caches.** Only the machine-wide `HKLM\SOFTWARE` TaskCache is read.
  Per-user tasks (inside each profile's `NTUSER.DAT`, `...\Schedule\TaskCache`) are out
  of scope for this phase.
- **Legacy Task Scheduler 1.0 (`.job`) folders.** Pre-Vista tasks stored as
  `%SystemRoot%\Tasks\*.job` files are not enumerated; the registry TaskCache covers
  Task Scheduler 2.0 registrations (Vista+), which is what modern golden images use.
- **Online machines.** Offline mounted-image hives only, matching `RegistryHiveReader`'s
  existing scope.
- **State interpretation beyond the friendly mapping.** We surface the raw DWORD and the
  friendly name; deciding whether a state is "wrong" for a golden image is the
  operator's (or a future policy feature's) call.

## Architecture

All additions follow the existing service + model + cmdlet split. No existing files are
modified; no new NuGet/assembly dependencies (`Registry` package already referenced).
The read path mirrors `WindowsImageServicesService.GetServices` (resolve hive path →
`File.Exists` guard → `OpenHive` → walk keys → project), and the state/filter logic
mirrors its pure helpers.

### TaskCache layout being read

```
HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache
├── Tree                      (hierarchy of folders and task leaves)
│   ├── Microsoft             (folder — no Id)
│   │   └── Windows           (folder — no Id)
│   │       └── ...           (task leaves carry Id = REG_SZ GUID)
│   └── <CustomFolder>        (folder and/or task leaves)
└── Tasks                     (one subkey per GUID)
    └── <GUID>
        ├── State   (REG_DWORD — modern Windows only)
        ├── Hash    (REG_BINARY — validation hash, not parsed)
        ├── Uri     (REG_SZ — task path, where present)
        └── <blob>  (undocumented binary task definition, not parsed)
```

Only Tree leaves whose `Id` value is a non-empty string are treated as registered tasks;
folder nodes are traversed. Task paths are composed as `\Folder\...\Leaf` (leading
backslash, like `schtasks` output). The GUID is reported as found (no brace/ordinal
normalization). A leaf whose GUID has no `Tasks\<GUID>` subkey is still reported with
`HasTasksEntry = false` and `State = Unknown` — an orphaned/legacy entry is worth
surfacing, not hiding.

### New files

**`src/Models/WindowsImageScheduledTaskModels.cs`**

- `enum WindowsImageScheduledTaskState` — friendly state of a TaskCache entry:
  `Unknown` (0 or absent/out-of-range), `Disabled` (1), `Queued` (2), `Ready` (3),
  `Running` (4). The 1–4 mapping is the community-documented TaskCache State encoding
  (consistent with Task Scheduler's own state enum); it is *not* contractually
  documented by Microsoft, so anything else degrades to `Unknown` and the raw DWORD is
  always available alongside.
- `class WindowsImageScheduledTaskInfo` — one reported task:
  `ImageName`, `MountPath`, `TaskPath` (`\Microsoft\Windows\...`), `TaskGuid` (the Tree
  leaf's `Id`), `State` (friendly), `StateValue` (raw DWORD; `-1` when absent or
  non-numeric), `Uri` (`Uri` value of the Tasks entry; empty when absent),
  `HasTasksEntry` (whether `Tasks\<GUID>` exists), `RegistryValues`
  (`Dictionary<string, object>?` — all raw values of the Tasks entry sorted by value
  name; null unless `-Detailed`), plus a `ToString()` in the existing style.

**`src/Services/ScheduledTasksService.cs`** (`_callbacks`, `ModuleCallbacks`-aware,
mirroring `WindowsImageServicesService`)

- `private const string ServiceName = "ScheduledTasksService"`.
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"`.
- `internal const string TaskCacheKeyPath = @"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache"`,
  `TreeSubKeyName = "Tree"`, `TasksSubKeyName = "Tasks"`, `TreeIdValueName = "Id"`,
  `StateValueName = "State"`, `UriValueName = "Uri"`.
- `public ScheduledTasksService(ModuleCallbacks? callbacks = null)` — public ctor,
  `ModuleCallbacks.Silent` default.
- `public List<WindowsImageScheduledTaskInfo> GetScheduledTasks(
  IRegistryHiveReader reader, string imageName, string mountPath,
  string? pathFilter = null, bool detailed = false,
  Action<int, string>? progress = null)` — thin hive-reading path. Resolves the
  SOFTWARE hive path, opens it via `reader.OpenHive`, reads `TaskCache` → `Tree` and
  walks the hierarchy depth-first collecting `(TaskPath, TaskGuid)` leaves (thin
  recursion), then funnels into the pure `FilterTreeTasks`; for each filtered task reads
  `Tasks\<guid>` (null → no entry), collects its raw `(Name, Data)` tuples and calls the
  pure `BuildTaskInfo`. Per-task try/catch → `_callbacks.Warning` + continue (matching
  the `CollectSoftwareEntries` pattern). `progress` is invoked during the read loop.
- `internal static string ResolveSoftwareHivePath(string mountPath)` —
  `Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE")`.
- `internal static string JoinTreePath(string parentPath, string nodeName)` — pure;
  composes task paths: root (`""`) + `"Microsoft"` → `"\Microsoft"`;
  `"\Microsoft"` + `"Windows"` → `"\Microsoft\Windows"`.
- `internal static List<(string TaskPath, string TaskGuid)> FilterTreeTasks(
  List<(string TaskPath, string TaskGuid)> tasks, string? pathFilter)` — pure; keeps
  tasks whose path matches `MatchesPathFilter`, sorted by `TaskPath`
  (ordinal-ignore-case) so output is deterministic.
- `internal static bool MatchesPathFilter(string? taskPath, string? filter)` — pure;
  blank filter matches everything; exact case-insensitive match wins; otherwise anchored
  case-invariant regex with a 1s timeout; invalid pattern/timeout matches nothing
  (same semantics as `WindowsImageServicesService.MatchesNameFilter`).
- `internal static WindowsImageScheduledTaskState ParseTaskState(int value)` — pure;
  1–4 → `Disabled`/`Queued`/`Ready`/`Running`, everything else (including 0) → `Unknown`.
- `internal static int? GetDwordValue(...)` / `internal static string GetStringValue(...)`
  — pure value readers by name (ordinal-ignore-case), mirroring the Services service.
- `internal static Dictionary<string, object>? CollectValues(
  IEnumerable<(string Name, object? Data)> values)` — pure; raw cache-entry values
  sorted ordinal by name, blank names skipped (mirror of the Services helper); used for
  `-Detailed`.
- `internal static WindowsImageScheduledTaskInfo BuildTaskInfo(string imageName,
  string mountPath, string taskPath, string taskGuid, bool hasTasksEntry,
  IEnumerable<(string Name, object? Data)>? values, bool detailed)` — pure projection:
  reads `State` (DWORD, `-1` when absent/non-numeric) → `ParseTaskState`, reads `Uri`
  (string, empty when absent), attaches `CollectValues` only when `detailed` and an
  entry exists.

**`src/Cmdlets/GetWindowsImageScheduledTaskCmdlet.cs`**

- `[Cmdlet(VerbsCommon.Get, "WindowsImageScheduledTask")]`,
  `[OutputType(typeof(WindowsImageScheduledTaskInfo[]))]` — **read-only**, no
  `SupportsShouldProcess`.
- `MountedImages` (`MountedWindowsImage[]`, mandatory, pipeline) with the
  `List<MountedWindowsImage>` accumulator + `ProcessRecord`/`EndProcessing` convention
  of `GetWindowsImageComponentStoreCmdlet` / `GetWindowsImageServiceCmdlet`.
- `-Path` (string, position 1) — task-path filter per the Goals above.
- `-Detailed`, `-ContinueOnError` switches, mirroring the Services cmdlet.
- Per image: mount-path guard (`ImageNotMounted` terminating error unless
  `-ContinueOnError`), `LoggingService.LogOperationStartWithTimestamp` /
  `LogOperationCompleteWithTimestamp`, `ProgressService.CreateProgressCallback`
  (one per image, `currentIndex`/`totalCount` across images), `ModuleCallbacks.FromCmdlet`,
  per-image try/catch with `ContinueOnError` semantics. Results written once as an
  array.

**`docs/help/Get-WindowsImageScheduledTask.md`** — PlatyPS markdown, modeled on
`Get-WindowsImageService.md` (front matter `external help file:
PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`; every parameter
documented).

### Modified files

None in `src/`. The orchestrator (outside this phase, per instructions) adds
`Get-WindowsImageScheduledTask` to `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`
`CmdletsToExport` and regenerates the shipped MAML — the DLL is not synced into
`Module/.../bin/` and nothing is committed by this work.

## Data Flow

```
Get-WindowsImageScheduledTask -MountedImages $imgs [-Path <regex|path>] [-Detailed]
   └─► per mounted image (accumulator; per-image timestamps + progress)
         └─► ScheduledTasksService.GetScheduledTasks
               ├─► IRegistryHiveReader.OpenHive(<mount>\Windows\System32\config\SOFTWARE)
               ├─► GetKey(TaskCache) → GetKey(TaskCache\Tree)
               ├─► thin Tree walk (JoinTreePath + Id values) → (TaskPath, TaskGuid) pairs
               ├─► pure FilterTreeTasks (path filter, sorted)
               ├─► per task: GetKey(TaskCache\Tasks\<guid>) → raw (Name, Data) tuples
               └─► pure BuildTaskInfo (State/Uri/HasTasksEntry/RegistryValues)
                     └─► WindowsImageScheduledTaskInfo[] (one array per cmdlet run)
```

## Error Handling

- Missing SOFTWARE hive → verbose note + empty result for that image (never throws) —
  matches `WindowsImageServicesService.GetServices`.
- `TaskCache` or `Tree` key absent (pre-Vista image, unusual build) → verbose note +
  empty result — the honest "no Task Scheduler 2.0 registrations found" answer.
- Per-task read failure (bad key, decode error) → `_callbacks.Warning` + skip the task;
  one bad entry never drops the whole inventory (mirrors `CollectSoftwareEntries`).
- Cmdlet-level: missing mount path → `ImageNotMounted` terminating error unless
  `-ContinueOnError`; per-image exception → logged error, rethrown unless
  `-ContinueOnError` (exact Services-cmdlet semantics).
- `-Path` with an invalid regex matches nothing by design (documented in help); it never
  throws at read time.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/ScheduledTasksServiceTests.cs`)** —
  all pure, plain `[Fact]`/`[Theory]`, no mocking framework, no hive files:
  - `ParseTaskState` maps 1–4 → `Disabled`/`Queued`/`Ready`/`Running`; 0, out-of-range →
    `Unknown`.
  - `JoinTreePath` composes root and nested paths with leading backslashes.
  - `FilterTreeTasks`: blank filter keeps everything and sorts by path; exact filter
    matches case-insensitively; regex filter anchors correctly; invalid regex matches
    nothing; GUID pairing preserved.
  - `MatchesPathFilter`: blank/exact/regex/invalid-pattern/timeout semantics.
  - `BuildTaskInfo`: full projection with a `State`/`Uri` entry (detailed + not), state
    absent → `Unknown`/`-1`, no Tasks entry → `HasTasksEntry = false` with no values,
    non-numeric `State` → `-1`/`Unknown`, detailed value collection skips blank names
    and sorts ordinal.
  - `GetDwordValue` / `GetStringValue` / `CollectValues` edge cases (absent, null,
    non-numeric, case-insensitive).
  - `ResolveSoftwareHivePath` maps to `Windows\System32\config\SOFTWARE` under a temp
    mount path (temp-dir fixture pattern).
- **Thin read path** (`Tree` walk, `Tasks` lookup): not unit-testable without faking the
  `Registry` package classes (no mocking framework allowed; the package classes are not
  interface-implementable from tests). Covered by compile + the manual/CI real-image
  step below — same trade-off as the Services half's `GetServices` read path.
- **Integration (manual/CI-only).** A real mounted image is required to exercise the
  actual SOFTWARE hive. Per `docs/OpenCode-EngLog.md` the local DISM
  `OpenOfflineSession` is broken, so real-image steps stay out of the local suite;
  being registry-only, this phase otherwise verifies fully via build + unit tests.
  Manual check on a real image: `Get-WindowsImageScheduledTask` returns the machine's
  registered tasks with states, and `-Path '\Microsoft\Windows\Defrag.*'` narrows it.

## Risks

- **State encoding may not cover every build.** The 1–4 mapping is community-documented
  (aligns with Task Scheduler's state enum), not contractually documented; legacy
  Windows 7-era entries often lack `State` entirely. Both degrade to `Unknown` with the
  raw value visible (`StateValue`), never a wrong claim.
- **Tree/Tasks key differences across builds.** Older or N-edition images may lack
  `TaskCache` entirely → empty result with a verbose note (by design).
- **`-Detailed` noise.** `Tasks` entries contain a binary `Hash` plus the opaque
  definition blob; the package's decoded strings can be large but are bounded (one entry
  per task), and `-Detailed` is opt-in — same trade-off as `-Detailed` on
  `Get-WindowsImageService`.
- **GUID normalization.** `Id` values are reported verbatim (no brace trimming); filter
  matching operates on task paths, so this does not affect `-Path`.
- **Guardrails.** Until the orchestrator adds the `CmdletsToExport` entry and regenerates
  MAML, `Scripts/verify-help.ps1` will not exercise the new cmdlet (it only checks
  *exported* cmdlets); the new markdown is written to be check-clean so the
  orchestrator's step passes on the first try.
