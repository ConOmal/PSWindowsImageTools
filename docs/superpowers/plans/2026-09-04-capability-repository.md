# Capability Repository — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsCapabilityRepository` — a read-only cmdlet that indexes a Features on Demand (FoD) payload source directory and reports the capability packages it offers (parsed from cab file names), with name/architecture/language regex filters and an optional `-GroupByName` summary.

**Architecture:** New files only, following the repo convention: `Models/CapabilityRepositoryModels.cs` for the two output types, `Services/CapabilityRepositoryService.cs` for the work, `Cmdlets/GetWindowsCapabilityRepositoryCmdlet.cs` for the PowerShell surface. Every piece of logic a unit test can drive without a filesystem (filename parsing, filter matching, regex validation, grouping) is pure `internal static`; the only thin, non-pure surface is the `DirectoryInfo.GetFiles("*.cab")` enumeration + `FileInfo.Length` in `IndexRepository`, exercised against temp directories holding synthetic empty `.cab` files with convention-conforming names. No DISM, no image, no network.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), xUnit (`tests/PSWindowsImageTools.Tests/`), PlatyPS-style markdown help under `docs/help/`.

**Spec:** `docs/superpowers/specs/2026-09-04-capability-repository-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Nullable annotations exactly as existing services use. netstandard2.0 ref assemblies lack `[NotNullWhen]` — use `?? string.Empty` / `!` null-narrowing patterns where needed.
- Filename convention parsed (strict, documented in the spec): `Microsoft-Windows-<CapabilityName>~<token>~<arch>~<language>~<version>.cab`, exactly 5 `~`-separated segments, `Microsoft-Windows-` prefix required; non-conforming files are skipped with a verbose note and counted, never errors.
- No new NuGet/assembly dependencies; no DISM calls anywhere in this phase; cmdlet is read-only (no `SupportsShouldProcess`).
- Do NOT modify existing capability cmdlets (`src/Cmdlets/WindowsImagePackageFeatureCmdlets.cs`), any existing service, `Module/PSWindowsImageTools/*` (psd1, bin, en-US), or `tests/integration/*` — orchestrator owns those. Do NOT touch files owned by other concurrent agents (UnattendValidation*, DynamicUpdate*, ComplianceManifest*) or the protected service list.
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Do not run the full unit suite (concurrent builders). Verification is `dotnet build src/PSWindowsImageTools.csproj` (0 errors; if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry), then `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~CapabilityRepositoryServiceTests"`.
- Test class: `CapabilityRepositoryServiceTests` (new). Temp-dir fixture pattern: `Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"))`, `IDisposable` cleanup.

---

### Task 1: Capability repository models

**Files:**
- Create: `src/Models/CapabilityRepositoryModels.cs`

**Interfaces:**
- `CapabilityRepositoryEntry { FileName, FilePath, CapabilityName, Token, Architecture, Language, Version, FileSize }` — one indexed cab.
- `CapabilityRepositoryGroup { CapabilityName, PackageCount, Architectures, Languages, Versions, TotalSize }` — `-GroupByName` summary; list properties initialize to `new List<string>()`.

- [x] **Step 1: Create `src/Models/CapabilityRepositoryModels.cs`** with both types (plain POCOs, `= string.Empty` initializers, XML doc comments, `ToString()` overrides mirroring the existing `Models` style).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: CapabilityRepositoryService — pure logic + thin scan

**Files:**
- Create: `src/Services/CapabilityRepositoryService.cs`

**Interfaces:**
- `CapabilityRepositoryService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default.
- `internal const string CabFileNamePrefix = "Microsoft-Windows-"`; `public const string NeutralToken = "neutral"`.
- `public List<CapabilityRepositoryEntry> IndexRepository(DirectoryInfo sourceDirectory, string? nameFilter, string? architectureFilter, string? languageFilter, PSCmdlet cmdlet)` — delegates to the callbacks overload.
- `public List<CapabilityRepositoryEntry> IndexRepository(DirectoryInfo sourceDirectory, string? nameFilter, string? architectureFilter, string? languageFilter, ModuleCallbacks callbacks, Action<int, string>? progress = null)` — the only filesystem-touching method: `GetFiles("*.cab", TopDirectoryOnly)` sorted by name, parse + filter via pure helpers, return entries sorted by CapabilityName/Language/Architecture/Version; missing dir → warning + empty list.
- `internal static CapabilityRepositoryEntry? ParseCabFileName(string filePath)` — strict convention parse; null when non-conforming.
- `internal static string? ExtractCapabilityName(string firstSegment)` — prefix strip (ordinal-ignore-case), null when absent/empty.
- `internal static bool MatchesFilters(CapabilityRepositoryEntry entry, string? nameFilter, string? architectureFilter, string? languageFilter)` — case-insensitive culture-invariant regex per filter; null/empty = no constraint.
- `internal static bool IsValidRegexPattern(string? pattern)` — true for null/empty/whitespace or accepted regex.
- `internal static List<CapabilityRepositoryGroup> GroupEntries(IEnumerable<CapabilityRepositoryEntry> entries)` — group by CapabilityName (ordinal-ignore-case), sorted output, sorted distinct arch/lang/version lists, summed size.

- [x] **Step 1: Write `CapabilityRepositoryService.cs`** with all members above (XML doc comments; verbose notes for skipped files; summary verbose at the end: indexed / skipped counts; per-file `progress(percent, fileName)` callback).
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Get-WindowsCapabilityRepository cmdlet

**Files:**
- Create: `src/Cmdlets/GetWindowsCapabilityRepositoryCmdlet.cs`
- Create: `docs/help/Get-WindowsCapabilityRepository.md`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Get, "WindowsCapabilityRepository")]`, output types `CapabilityRepositoryEntry[]` and `CapabilityRepositoryGroup[]`; read-only (no `SupportsShouldProcess`).
- `-SourcePath <string>` mandatory, position 0 — resolved via `GetUnresolvedProviderPathFromPSPath`; missing directory → terminating `DirectoryNotFound`.
- `-Name`, `-Architecture`, `-Language` (optional, positions 1–3, regex filters; invalid regex → terminating `InvalidArgument` via `IsValidRegexPattern`).
- `-GroupByName` switch — output `CapabilityRepositoryGroup[]` instead of entries.
- Flow per `GetWindowsImageScheduledTaskCmdlet` / `GetINFDriverListCmdlet`: `LoggingService.LogOperationStartWithTimestamp`, service with `ModuleCallbacks.FromCmdlet(this)`, `ProgressService.CreateProgressCallback` per-file progress, single `WriteObject(...ToArray())`, `LogOperationCompleteWithTimestamp` summary; error path logs + rethrows.

- [x] **Step 1: Create the cmdlet** with the parameters and flow above (ComponentName = "Get-WindowsCapabilityRepository").
- [x] **Step 2: Create `docs/help/Get-WindowsCapabilityRepository.md`** in PlatyPS format copied from `docs/help/Get-INFDriverList.md` (front matter `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`; SYNOPSIS/SYNTAX/DESCRIPTION/EXAMPLES/PARAMETERS incl. `-ProgressAction`/CommonParameters/INPUTS/OUTPUTS/NOTES/RELATED LINKS; document the filename convention and its filename-derived limits in DESCRIPTION/NOTES).
- [x] **Step 3: Build** to confirm everything compiles.

### Task 4: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/CapabilityRepositoryServiceTests.cs`

- [x] **Step 1: Create `CapabilityRepositoryServiceTests.cs`** — plain xUnit `[Fact]`/`[Theory]`, no mocking framework:
  - `ParseCabFileName`: conforming name → all fields incl. neutral/empty handling; case-insensitive prefix/extension; non-conforming (no `~`, wrong prefix, >5 segments, empty name, wrong extension) → null.
  - `MatchesFilters`: null filters pass all; regex name match; arch/language case-insensitive; no match → false.
  - `IsValidRegexPattern`: null/empty/valid pattern true; `(` false.
  - `GroupEntries`: case-insensitive grouping, PackageCount, sorted distinct lists, TotalSize, groups sorted by name.
  - `IndexRepository` (temp dir with synthetic empty `.cab` files + a non-conforming cab + a non-cab file): only conforming entries returned with correct FilePath/FileSize/SortOrder; filters narrow results; grouping works end-to-end; missing directory → empty, no throw.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~CapabilityRepositoryServiceTests"`) and confirm they pass.

### Task 5: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors, 0 warnings).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 4 / Step 2).
- [x] **Step 3: Final report** — spec + plan paths, exact cmdlet name, filename convention + limits, test counts, deviations. Leave working tree uncommitted; no psd1/MAML/integration changes (orchestrator-owned).
