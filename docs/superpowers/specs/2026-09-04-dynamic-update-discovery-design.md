# Dynamic Update Discovery — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** the "Dynamic Update (beyond what exists)" backlog item. The module
already APPLIES Dynamic Updates (`Invoke-MediaDynamicUpdate` boot.wim/install.wim sequence);
this phase adds the missing discovery half so the workflow "discover → download → apply"
becomes one-liner-able.

## Problem

`Invoke-MediaDynamicUpdate` applies Dynamic Updates found in a local `-UpdatesPath`
directory (Servicing Stack Update → SafeOS DU → Cumulative Update → Setup DU), but nothing
in the module helps an operator find *which* packages exist for a given Windows
build. Discovery today means hand-crafting Microsoft Update Catalog searches
(`Search-WindowsUpdateCatalog` takes free-form queries), knowing the per-DU-type
title patterns, filtering the noise (`.NET Framework` cumulative updates, tool
entries, wrong architectures, superseded duplicates), and picking the latest
package per type — four catalog searches and a lot of eyeballing per build.

`Get-WindowsUpdateDownloadUrl` and `Save-WindowsUpdateCatalogResult` already solve
the download half and `Invoke-MediaDynamicUpdate` solves the apply half; only
discovery is missing.

## Goals

1. New read-only cmdlet `Get-WindowsDynamicUpdate` that, given a Windows build
   (e.g. `26100`, `26100.1234`, or `10.0.26100.1234`), discovers the currently
   available Dynamic Updates from the Microsoft Update Catalog: ServicingStack,
   SafeOS, Cumulative, Setup — the exact four types the apply side consumes.
2. One result object per discovered update carrying the DU type, KB article,
   title, catalog metadata (classification, last-modified, size, version,
   architecture, catalog product label) and a resolved `DownloadUrl` where the
   existing catalog plumbing can fetch one cheaply.
3. One result **per DU type** (latest wins) by default, so piping straight into a
   download step yields the minimal apply set — plus catalog-only filtering noise
   (`.NET Framework` updates, Malicious Software Removal Tool, driver metadata)
   never leaks into the output.
4. Keep every decision (query-string construction per DU type, build→OS-label
   mapping, title-based classification into DU types, dedup/latest selection)
   as pure `internal static` logic unit-tested against synthetic catalog result
   objects; the HTTP/catalog path stays a thin reuse of
   `WindowsUpdateCatalogService` (no network in unit tests).
5. Follow the established plumbing exactly: `ModuleCallbacks` for host
   communication, `LoggingService` operation logging, progress reporting during
   multi-query discovery, and netstandard2.0 / C# 8 / nullable rules.

## Non-goals

- **Applying updates.** `Invoke-MediaDynamicUpdate` already exists and is the
  apply surface; discovery is read-only (no `SupportsShouldProcess`).
- **Downloading.** Output carries a `DownloadUrl`; downloading stays with
  `Save-WindowsUpdateCatalogResult` (or plain `Invoke-WebRequest`).
- **Servicing-chain intelligence.** `Get-WindowsImageServicingChain` classifies
  packages *inside an image*; this phase classifies catalog *titles* only. No
  DISM, no mounted images, no WIM reading.
- **Catalog pagination.** The underlying `WindowsUpdateCatalogService` reads one
  results page per query (25 rows, sorted by LastUpdated descending). Discovery
  only needs the freshest few entries per DU type; deep pagination is not wired.
- **Build-range math.** No computing of "next" builds, UBR targeting
  (`26100.1234` is accepted but only the major build `26100` is used for
  discovery), or release-health scraping. The build number plus a documented
  build→OS-label table is the discovery key.

## Design

### Catalog query strategy per DU type

The catalog's full-text search matches update titles, so each DU type gets the
title fragment it appears in. With an OS label resolved from the build (see
below), the queries are:

| DU type | Catalog query | Rationale |
| --- | --- | --- |
| ServicingStack | `"{label} Servicing Stack Update"` | SSU titles: "2026-09 Servicing Stack Update for Windows 11 Version 24H2 for x64-based Systems (KB…)" |
| Cumulative | `"{label} Cumulative Update"` | LCU titles: "2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB…)" — the `.NET Framework` cumulative updates that share this pattern are filtered out by classification |
| SafeOS | `"{label} Dynamic Update"` | SafeOS DU titles: "2026-09 Dynamic Update for Windows 11 Version 24H2 for x64-based Systems (KB…)" |
| Setup | `"{label} Dynamic Update"` | Setup DU titles: "2026-09 Dynamic Update for Windows 11 Setup for x64-based Systems (KB…)"; the same query feeds both — classification splits SafeOS vs Setup from the titles |

`-Type All` (default) deduplicates the query strings, so discovery runs **three
searches per OS label** (SSU, CU, DU) instead of four. Each search is sorted by
LastUpdated descending via the service's existing sort plumbing, architecture is
filtered by the service's existing `criteria.Architecture` filter, and results
from all searches are merged before classification.

### Build → OS label resolution

Catalog titles spell out the OS ("Windows 11 Version 24H2", "Windows Server
2025"), not the build. A documented mapping table converts the parsed build
number into ordered candidate labels — client first, then server for shared
builds — so one `-Build` covers both product lines without user knowledge:

| Build | Labels (in order) |
| --- | --- |
| 10240 / 10586 | Windows 10 Version 1507 / 1511 |
| 14393 | Windows 10 Version 1607, Windows Server 2016 |
| 15063 / 16299 / 17134 | Windows 10 Version 1703 / 1709 / 1803 |
| 17763 | Windows 10 Version 1809, Windows Server 2019 |
| 18362 / 18363 | Windows 10 Version 1903 / 1909 |
| 19041 / 19042 / 19043 / 19044 / 19045 | Windows 10 Version 2004 / 20H2 / 21H1 / 21H2 / 22H2 |
| 20348 | Windows Server 2022 |
| 22000 / 22621 / 22631 | Windows 11 Version 21H2 / 22H2 / 23H2 |
| 26100 | Windows 11 Version 24H2, Windows Server 2025 |
| any other build ≥ 22000 | Windows 11 |
| any other build < 22000 | Windows 10 |

`-OSLabel` overrides the table with an explicit catalog title fragment (useful
for unknown/future builds and for pinning a specific label out of an ambiguous
pair). Unknown builds default to the generic label, which still queries the
catalog; results are then narrowed by classification only (documented fallback).

### Title-based classification into DU types

Applied per catalog result (ordinal-ignore-case, in priority order):

1. Title contains `.NET Framework` → **not a Dynamic Update** (the catalog's
   .NET cumulative updates match the Cumulative query but are not media DUs).
2. Contains `servicing stack` → **ServicingStack**.
3. Contains `safe os` or `safeos` → **SafeOS**.
4. Contains `dynamic update` **and** `setup` → **Setup**.
5. Contains `dynamic update` → **SafeOS** (per Microsoft's Dynamic Update docs,
   the SafeOS DU entry is titled "Dynamic Update for \<OS\>"; Setup DU titles
   additionally say "Setup", caught by rule 4).
6. Contains `cumulative` → **Cumulative**.
7. Anything else (e.g. "Windows Malicious Software Removal Tool") → **not a
   Dynamic Update**.

### Latest-per-type selection

- Deduplicate by `UpdateId` (first occurrence wins).
- Classify; drop results with no DU type.
- Group by DU type; within a group pick the entry with the greatest
  `LastModified`, tie-broken by greater `Size` (larger of two same-date
  re-releases is the corrected package).
- Emit in apply order — ServicingStack, SafeOS, Cumulative, Setup — matching
  `Invoke-MediaDynamicUpdate`'s `ApplyDynamicUpdatesSequence`, so piping the
  discovery result into a download step yields the packages in the order the
  apply side expects.

### Download URLs

Only the selected latest-per-type results (≤ 4 for `-Type All`) are resolved, so
URL resolution adds at most one request per result regardless of how noisy the
searches were. `WindowsUpdateCatalogService.GetDownloadUrls(updateId)` supplies
them; the first URL lands on `WindowsDynamicUpdate.DownloadUrl`. Per-result
failures become warnings and leave `DownloadUrl` null.

### New files

**`src/Models/DynamicUpdateModels.cs`**

- `enum DynamicUpdateType { ServicingStack, SafeOS, Cumulative, Setup }` — the
  four media DU types, ordered by the apply sequence. (`All` exists only as a
  cmdlet parameter value, not as an enum member.)
- `class WindowsDynamicUpdate` — `UpdateType` (enum), `Build` (int),
  `OSLabel` (catalog label used), `KBNumber`, `Title`, `UpdateId`,
  `Architecture` (normalized, e.g. `x64`), `Version`, `Classification`,
  `LastModified` (DateTime), `Size` (long) + `SizeFormatted` (computed, mirroring
  `WindowsUpdateCatalogResult`), `DownloadUrl` (`Uri?`), and a `ToString()`
  override in the established style.

**`src/Services/DynamicUpdateDiscoveryService.cs`** (`ModuleCallbacks`-aware,
thin HTTP, pure logic below)

- `private const string ServiceName = "DynamicUpdateDiscoveryService"`.
- `public const int MaxResultsPerQuery = 50` — per-search cap passed to the
  catalog criteria (page 1 holds 25 rows; the cap matches
  `Search-WindowsUpdateCatalog`'s default).
- `public DynamicUpdateDiscoveryService(ModuleCallbacks? callbacks = null)` —
  `ModuleCallbacks.Silent` default.
- `internal DynamicUpdateDiscoveryService(ModuleCallbacks callbacks,
  WindowsUpdateCatalogService catalogService)` — test seam; injects a
  catalog service constructed with a stubbed `HttpClient` (same pattern as
  `WindowsUpdateCatalogServiceTests`).
- `public List<WindowsDynamicUpdate> Discover(int build, IList<string> osLabels,
  string architecture, ISet<DynamicUpdateType> requestedTypes, bool debugMode)`
  — the thin orchestration: normalize architecture, build the deduplicated
  query set (label × type → `BuildCatalogQuery`), run one
  `WindowsUpdateCatalogService.SearchUpdates` per query with LastUpdated/Descending
  sorting, map rows to `WindowsUpdateCatalogResult`, aggregate, then hand
  everything to the pure selection below; finally resolve download URLs for the
  selected results. Per-query failure → warning + continue.
- `internal static int? ParseBuildNumber(string? build)` — accepts `26100`,
  `26100.1234`, `10.0.26100`, `10.0.26100.1234`; takes the third component of a
  dotted version, else the whole token; null when unparseable.
- `internal static string NormalizeArchitecture(string? architecture)` —
  `amd64`→`x64`, `arm64`→`ARM64` (ordinal-ignore-case), `x64`/`x86` pass
  through, anything else falls back to `x64`.
- `internal static List<string> ResolveOSLabels(int build)` — the documented
  table above; client label first, server label second for shared builds.
- `internal static string BuildCatalogQuery(DynamicUpdateType type, string osLabel)`
  — per-DU-type query construction; empty label degrades to the bare type query.
- `internal static DynamicUpdateType? ClassifyCatalogResult(WindowsUpdateCatalogResult result)`
  — the title rules above.
- `internal static List<WindowsDynamicUpdate> SelectLatestPerType(
  IEnumerable<WindowsUpdateCatalogResult> results, ISet<DynamicUpdateType> requestedTypes,
  int build, string osLabel, string architecture)` — dedup → classify → filter to
  requested types → latest per type → map to `WindowsDynamicUpdate` → order by
  apply sequence.

**`src/Cmdlets/GetWindowsDynamicUpdateCmdlet.cs`**

- `[Cmdlet(VerbsCommon.Get, "WindowsDynamicUpdate")]`,
  `[OutputType(typeof(WindowsDynamicUpdate[]))]` — read-only, no
  `SupportsShouldProcess`.
- `-Build <string>` (Mandatory, Position 0, `ValueFromPipelineByPropertyName`,
  `[ValidateNotNullOrEmpty]`).
- `-Architecture <string>` — `[ValidateSet("amd64", "x64", "x86", "arm64")]`,
  default `amd64`, normalized to the catalog's `x64`/`x86`/`ARM64`.
- `-Type <string>` — `[ValidateSet("ServicingStack", "Cumulative", "SafeOS",
  "Setup", "All")]`, default `All`.
- `-OSLabel <string>` — optional explicit catalog title fragment override.
- `-DebugMode` — passthrough to the catalog service debug logging.
- Flow mirrors `SearchWindowsUpdateCatalogCmdlet`: `LoggingService`
  operation start/complete timestamps, `ModuleCallbacks` (Verbose/Warning/Error
  wired via the established lambda pattern, Progress wired to
  `LoggingService.WriteProgress` for per-query progress),
  `LoggingService.CompleteProgress`, one `WriteObject` per result, terminating
  error on invalid build (ErrorCategory.InvalidArgument) or failed discovery.

### Modified files

None. `WindowsUpdateCatalogService`, `SearchWindowsUpdateCatalogCmdlet`,
`GetWindowsUpdateDownloadUrlCmdlet`, `WindowsReleaseInfoService`-equivalent
(`WindowsReleaseHistoryService`) and the module manifest are read-only
references. Manifest export addition is the orchestrator's job.

## Data Flow

```
Get-WindowsDynamicUpdate -Build 26100 -Type All
   └─► DynamicUpdateDiscoveryService.Discover
         ├─► ParseBuildNumber / ResolveOSLabels (pure)
         ├─► for each label × unique query (BuildCatalogQuery, pure):
         │     └─► WindowsUpdateCatalogService.SearchUpdates(criteria)   [existing]
         │           └─► rows mapped to WindowsUpdateCatalogResult
         ├─► SelectLatestPerType (pure: dedup → classify → latest → order)
         └─► WindowsUpdateCatalogService.GetDownloadUrls(updateId)       [existing]
               └─► WindowsDynamicUpdate.DownloadUrl

   discover → download → apply
   Get-WindowsDynamicUpdate | foreach { Save to UpdatesPath } | Invoke-MediaDynamicUpdate
```

## Error Handling

- Invalid build string → terminating `ErrorRecord`
  (`GetWindowsDynamicUpdateFailed`, `ErrorCategory.InvalidArgument`).
- Catalog search failure for one query → warning + continue (other queries still
  run); total discovery failure only when every query fails, surfaced as a
  terminating error by the cmdlet.
- `SearchUpdates`/`GetDownloadUrls` already catch internally and report through
  `ModuleCallbacks.Error`; discovery treats an unsuccessful or empty
  `WindowsUpdateSearchResult` as "no candidates for that query" (verbose note).
- Empty overall result (catalog unreachable / no DU for the build) → the cmdlet
  writes a warning and emits nothing (read-only cmdlet, non-fatal).

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/DynamicUpdateDiscoveryServiceTests.cs`,
  plain `[Fact]`/`[Theory]`, no mocking framework):**
  - `ParseBuildNumber` — all four accepted shapes, garbage → null.
  - `ResolveOSLabels` — ambiguous builds return client-first label pairs;
    unambiguous builds return a single label; unknown builds fall back to the
    generic label.
  - `NormalizeArchitecture` — amd64/x64/arm64/x86 mapping and fallback.
  - `BuildCatalogQuery` — per-type fragment with and without an OS label;
    SafeOS and Setup share the Dynamic Update query.
  - `ClassifyCatalogResult` — synthetic `WindowsUpdateCatalogResult` objects:
    SSU, LCU, SafeOS DU, Setup DU titles → expected types; `.NET Framework`
    cumulative update and unrelated tool titles → null; empty title → null.
  - `SelectLatestPerType` — synthetic result set (older + newer CU, SSU, SafeOS,
    Setup, .NET noise, duplicate UpdateId): one result per type, latest CU wins
    by LastModified (Size tie-break), output ordered ServicingStack → SafeOS →
    Cumulative → Setup, field mapping (KB, title, metadata, null DownloadUrl),
    subset filtering by requested types, dedup by UpdateId.
  - `Discover` end-to-end against a stubbed `HttpMessageHandler` (no live
    network — the established `WindowsUpdateCatalogServiceTests` pattern):
    searches run per label, one result per type is returned with the
    `DownloadUrl` resolved from the stubbed download dialog, and a no-results
    catalog yields an empty list.
- **Integration:** none. Discovery is a live-catalog operation; real runs are
  manual/CI-only. Local DISM servicing is broken and out of scope here anyway —
  discovery never touches DISM.

## Risks

- **Catalog title drift.** Classification keys off title wording; a Microsoft
  rewording could drop a type out of the results. Mitigation: the rules are
  centralized in one method, match the apply side's filename patterns
  (`ssu/servicing`, `safeos`, `cumulative/lcu`, `setup`), and are exhaustively
  unit-tested with real-world title shapes.
- **Ambiguous builds.** Shared client/server builds (17763, 20348-adjacent,
  26100) double the query count; dedup + UpdateId-keyed selection keeps the
  merged output correct.
- **Page-1 dependency.** If a type's latest entry falls outside the first
  results page for its query (25 rows), it will be missed. Sorted descending by
  LastUpdated this is only reachable with heavy noise; acceptable and documented.
- **No manifest/help sync.** The psd1 export and shipped MAML are regenerated by
  the orchestrator; until then the cmdlet is source-complete but not exported.
