# Capability Repository — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** capability (Features on Demand) lifecycle — the module can already
add/remove capabilities in mounted images (`Add-WindowsImageCapability` /
`Remove-WindowsImageCapability` accept a `-SourcePath` pointing at a FoD payload
directory), but nothing tells the operator what that source actually offers before
they run the add.

## Problem

A Features on Demand (FoD) payload source — a FoD disk/ISO, or a repo share that
mirrors one — is a flat directory of `.cab` files. When `Add-WindowsImageCapability
-SourcePath <dir>` fails or silently ignores a capability, the operator has no way
to answer the basic question **"what capabilities does this source actually
contain?"** without eyeballing hundreds of `~`-delimited file names.

There is no discovery cmdlet for the payload directory. The existing capability
cmdlets are write-oriented, require a mounted image, and go through DISM; the
source directory itself is never indexed.

## Goals

1. Provide `Get-WindowsCapabilityRepository` — a strictly read-only cmdlet that
   scans a FoD payload source directory and reports one entry per capability
   package found: capability name, architecture, language, version, file path and
   file size.
2. Support focused discovery: `-Name` (regular expression on capability name),
   `-Architecture` and `-Language` filters, case-insensitive.
3. Support summarization: `-GroupByName` collapses the many per-arch/per-language
   cabs of one capability into a single summary entry (package count, distinct
   architectures/languages/versions, total size).
4. Keep ALL decision logic (filename parsing, filtering, grouping, sorting) pure
   `internal static` methods, unit-tested against temp directories holding
   synthetic, convention-conforming `.cab` file names — empty files are enough.
   No network, no DISM, no cab-content parsing — fully verifiable locally.
5. Follow repo conventions exactly: new files under `src/Models/`,
   `src/Services/`, `src/Cmdlets/`, `docs/help/`; `LoggingService` /
   `ModuleCallbacks` / `ProgressService` as the existing read-only cmdlets
   (`Get-INFDriverList`, `Get-WindowsImageScheduledTask`) use them.

## Non-goals

- **No DISM, no image required.** Indexing is a filesystem scan of the payload
  directory. It does not open a DISM session, does not inspect a mounted image,
  and does not validate that a found cab would actually install.
- **No cab-internal metadata.** We never open the cab. Everything is derived from
  the file name (see the convention section for what that costs).
- **No capability add/remove/rename.** The write path
  (`Add-WindowsImageCapability`) is untouched; this cmdlet only reports.
- **No recursive scanning.** `-SourcePath` is indexed top-level only — point it at
  the directory that holds the cabs (FoD disk root, or
  `sources\LanguagesAndOptionalFeatures` on a Windows ISO). Recursion would add
  traversal surprises on huge media for little value; non-goals can be revisited
  if a real layout demands it.
- **No DISM capability-name reconstruction.** We do not synthesize
  `<Name>~~~~<version>`-style DISM capability strings from parsed parts. FoD
  naming drifts (e.g. cab `Microsoft-Windows-Notepad-System-FoD-Package~…` vs the
  DISM capability `Microsoft.Windows.Notepad.System~~~~…`), so a reconstructed
  string would over-promise. The parsed `CapabilityName` plus
  `Get-WindowsImageCapability` (DISM) on the target image remains the correct way
  to confirm the exact string before an add.
- **No new cmdlet exports in the manifest** — the orchestrator owns
  `PSWindowsImageTools.psd1`, help MAML regeneration and integration tests.

## Design

### Filename convention parsed (and its honest limits)

FoD payload cab file names follow:

```
Microsoft-Windows-<CapabilityName>~<token>~<arch>~<language>~<version>.cab
```

Examples:

```
Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab
Microsoft-Windows-LanguageFeatures-Basic-en-us~31bf3856ad364e35~amd64~en-us~.cab
```

Parsing (strict):

- extension must be `.cab` (case-insensitive) — `GetFiles("*.cab")` plus a
  re-check on the stem;
- the stem is split on `~` and must yield **exactly 5 segments**;
- segment 0 must start with the `Microsoft-Windows-` prefix (case-insensitive);
  the remainder is `CapabilityName` and must be non-empty;
- segment 1 is `Token` — the opaque publisher/build-revision token
  (e.g. `31bf3856ad364e35`); reported verbatim, never interpreted;
- segment 2 is `Architecture` (e.g. `amd64`, `x86`, `arm64`); an empty segment is
  reported as `neutral`;
- segment 3 is `Language` (e.g. `en-us`); an empty segment is reported as
  `neutral` (language-neutral package);
- segment 4 is `Version` (e.g. `10.0.26100.1`, `0.0.1.0`); an empty segment is
  reported as an empty string (the filename carries no version).

**Honest limits of filename-derived metadata** (documented in help and here):

- All fields except path/size come from the file name. Nothing is read from
  inside the cab; a renamed or malformed file yields wrong or no data. Files that
  do not match the convention are skipped with a verbose note and counted in the
  scan summary — never errors.
- `Token` is opaque; it identifies the publisher token but has no operator
  meaning.
- The parsed `CapabilityName` is the file-name name, not guaranteed to equal the
  DISM capability string reported by `Get-WindowsImageCapability` (see non-goals).
- Version/language casing and availability vary across FoD media generations;
  language-neutral cabs legitimately have empty language/version segments.
- Because grouping is by parsed name, one real capability split across multiple
  cab naming variants could appear as multiple groups.

### New files

**`src/Models/CapabilityRepositoryModels.cs`**

- `CapabilityRepositoryEntry` — one indexed cab: `FileName`, `FilePath` (full
  path), `CapabilityName`, `Token`, `Architecture`, `Language`, `Version`,
  `FileSize` (bytes); `ToString()` override in the existing model style.
- `CapabilityRepositoryGroup` — one `-GroupByName` summary: `CapabilityName`,
  `PackageCount`, `Architectures` (sorted distinct), `Languages` (sorted
  distinct), `Versions` (sorted distinct), `TotalSize` (bytes); `ToString()`
  override.

**`src/Services/CapabilityRepositoryService.cs`** (`ModuleCallbacks`-aware,
mirroring `INFDriverService` / `ScheduledTasksService`)

- `private const string ServiceName = "CapabilityRepositoryService"`.
- `internal const string CabFileNamePrefix = "Microsoft-Windows-"`.
- `public const string NeutralToken = "neutral"` — reported for empty
  architecture/language segments.
- `public CapabilityRepositoryService(ModuleCallbacks? callbacks = null)` —
  silent default, like every other service.
- `public List<CapabilityRepositoryEntry> IndexRepository(DirectoryInfo
  sourceDirectory, string? nameFilter, string? architectureFilter, string?
  languageFilter, PSCmdlet cmdlet)` + callbacks overload — the only method that
  touches the filesystem. Enumerates `*.cab` top-level only (sorted by file name
  for determinism), emits progress via an optional `Action<int, string>`
  callback, parses each file with the pure helpers, applies the pure filter, and
  returns entries sorted by `CapabilityName`, then `Language`, `Architecture`,
  `Version`. Missing directory → warning + empty list (defensive; the cmdlet
  validates first). Per-file parse failures are impossible (parser returns null),
  but a per-file `FileInfo.Length` guard keeps the loop warning-and-continue.
- `internal static CapabilityRepositoryEntry? ParseCabFileName(string filePath)`
  — pure; implements the convention above; returns null for non-conforming
  names.
- `internal static string? ExtractCapabilityName(string firstSegment)` — pure;
  strips the `Microsoft-Windows-` prefix (ordinal-ignore-case); null when absent
  or empty.
- `internal static bool MatchesFilters(CapabilityRepositoryEntry entry, string?
  nameFilter, string? architectureFilter, string? languageFilter)` — pure; each
  filter is a case-insensitive culture-invariant `Regex.IsMatch` (null/empty
  filter = no constraint).
- `internal static bool IsValidRegexPattern(string? pattern)` — pure; true for
  null/empty/whitespace and for patterns `Regex` accepts; lets the cmdlet fail
  fast with a terminating error on invalid user regex.
- `internal static List<CapabilityRepositoryGroup> GroupEntries(
  IEnumerable<CapabilityRepositoryEntry> entries)` — pure; groups by
  `CapabilityName` (ordinal-ignore-case), emits groups sorted by name with
  sorted distinct architectures/languages/versions and summed size.

**`src/Cmdlets/GetWindowsCapabilityRepositoryCmdlet.cs`**

- `[Cmdlet(VerbsCommon.Get, "WindowsCapabilityRepository")]`,
  `[OutputType(typeof(CapabilityRepositoryEntry[]))]` +
  `[OutputType(typeof(CapabilityRepositoryGroup[]))]`; strictly read-only — no
  `SupportsShouldProcess`.
- `-SourcePath <string>` (mandatory, position 0) — resolved via
  `GetUnresolvedProviderPathFromPSPath`; must exist as a directory, otherwise a
  terminating `DirectoryNotFound` error.
- `-Name`, `-Architecture`, `-Language` (positions 1–3, optional regex filters);
  invalid regex → terminating `InvalidArgument` error via
  `IsValidRegexPattern`.
- `-GroupByName` switch — swaps flat entries for `CapabilityRepositoryGroup`
  summaries.
- Flow mirrors `Get-WindowsImageScheduledTaskCmdlet`: validate inputs,
  `LoggingService.LogOperationStartWithTimestamp`, instantiate the service with
  `ModuleCallbacks.FromCmdlet(this)`, `ProgressService.CreateProgressCallback`
  for per-file progress, `WriteObject(result.ToArray())` once, then
  `LogOperationCompleteWithTimestamp` with a summary (indexed / skipped counts);
  failure path logs the error and rethrows. No DISM, no image parameters.

### Data Flow

```
Get-WindowsCapabilityRepository -SourcePath <FoD dir> [-Name rsat] [-Architecture amd64] [-Language en-us] [-GroupByName]
   └─► CapabilityRepositoryService.IndexRepository
         ├─► sourceDirectory.GetFiles("*.cab", TopDirectoryOnly)   (sorted by name)
         ├─► pure ParseCabFileName ──► CapabilityRepositoryEntry (or skipped + verbose)
         ├─► pure MatchesFilters
         └─► pure SortEntries
   └─► optional pure GroupEntries ──► CapabilityRepositoryGroup
   └─► WriteObject(entries | groups)
```

### Error Handling

- Non-existent `-SourcePath` → terminating error in the cmdlet (`DirectoryNotFound`).
- Invalid `-Name`/`-Architecture`/`-Language` regex → terminating error in the
  cmdlet (`InvalidArgument`), validated before any scan.
- Files not matching the convention → skipped silently at output, verbose note +
  counted in the summary; the scan never fails on odd files.
- Missing directory discovered late (race) or unreadable → service warns and
  returns what it indexed (same warning-and-continue shape as
  `INFDriverService.ScanSingleDirectory`).
- Output is empty (no cabs / nothing matched) → verbose summary, empty array —
  not an error.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/CapabilityRepositoryServiceTests.cs`,
  plain `[Fact]`/`[Theory]`, no mocking framework, temp-dir fixture pattern from
  `AppProvisioningServiceTests.cs`)**:
  - `ParseCabFileName`: fully-conforming name → every field; language-neutral
    (empty lang/version) → `neutral`/empty handling; case-insensitive prefix and
    extension; non-conforming inputs (no `~`, wrong prefix, too many segments,
    empty name) → null.
  - `MatchesFilters`: null filters pass everything; regex `-Name` matching;
    architecture/language case-insensitive matching; non-matching → false.
  - `IsValidRegexPattern`: null/empty valid, `(` invalid.
  - `GroupEntries`: case-insensitive grouping, counts, sorted distinct lists,
    summed size, groups sorted by name.
  - `IndexRepository` against a temp directory with synthetic empty `.cab` files
    (conforming names) plus a non-conforming cab and a non-cab file: returns only
    parsed entries, correct `FilePath`/`FileSize`/order; filters narrow the
    result; missing directory → empty list, no throw.
- **Integration:** none. This phase never touches DISM or a mounted image; the
  local DISM servicing limitation documented in `docs/OpenCode-EngLog.md` is
  irrelevant to it. Orchestrator owns the Pester suite.

## Risks

- **Filename drift across FoD media.** Different media generations vary naming;
  strict 5-segment parsing may skip legitimately-shaped files. Mitigation: skips
  are verbose + counted, so an operator sees "0 indexed, N skipped" instead of
  silent emptiness, and the convention is documented.
- **Rename → wrong data.** A hand-renamed cab yields plausible-but-wrong
  metadata. Accepted and documented: this is a discovery index, not a catalog;
  nothing installs based on the parsed fields alone.
- **Grouping approximation.** Grouping by parsed name can split one capability
  across naming variants (documented limit). Counting per-file entries remains
  exact regardless.
- **No new env/test surface.** New cmdlet means the orchestrator will add the
  `CmdletsToExport` entry and regenerate MAML; `verify-help.ps1` checks 1–3 stay
  green by construction (this phase adds the PlatyPS markdown help file).
