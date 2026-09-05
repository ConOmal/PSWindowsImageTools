# Dynamic Update Discovery — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add the discovery half of the Dynamic Update workflow — `Get-WindowsDynamicUpdate` — which resolves a Windows build to catalog title labels, runs the per-DU-type Microsoft Update Catalog queries, classifies/dedups/selects the latest package per DU type, and returns `WindowsDynamicUpdate` objects with resolved download URLs, all reusing the existing `WindowsUpdateCatalogService` plumbing.

**Architecture:** Mirror the existing convention: `Models/DynamicUpdateModels.cs` for the new types, `Services/DynamicUpdateDiscoveryService.cs` for the work, `Cmdlets/GetWindowsDynamicUpdateCmdlet.cs` as the thin PowerShell surface, and pure `internal static` methods for every decision a unit test can drive without the catalog (build parsing, build→OS-label mapping, query construction, title classification, dedup/latest selection, architecture normalization). The HTTP path reuses `WindowsUpdateCatalogService.SearchUpdates`/`GetDownloadUrls` unchanged via an injected-instance test seam.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enable), xUnit (`tests/PSWindowsImageTools.Tests/`, plain `[Fact]`/`[Theory]`, no mock framework), existing `HtmlAgilityPack`-based catalog service.

**Spec:** `docs/superpowers/specs/2026-09-04-dynamic-update-discovery-design.md`

## Global Constraints

- C# 8 only: no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Nullable-enable means null-narrowing uses `!` / `?? string.Empty` patterns (netstandard2.0 ref assemblies lack `[NotNullWhen]`).
- Reuse `WindowsUpdateCatalogService` (ModuleCallbacks refactor) — do NOT modify it, `SearchWindowsUpdateCatalogCmdlet`, `GetWindowsUpdateDownloadUrlCmdlet`, `UpdateWindowsImageOnlineCmdlet`, or `WindowsReleaseHistoryService`.
- No new NuGet packages; `InternalsVisibleTo("PSWindowsImageTools.Tests")` already exists in `src/Properties/AssemblyInfo.cs`.
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (orchestrator adds the export), `Module/*/bin/*`, `Module/*/en-US/*`, or `tests/integration/*` (orchestrator owns both). Do not commit; leave changes in the working tree.
- Do not run the full unit suite (concurrent builders). Verification is `dotnet build src/PSWindowsImageTools.csproj` (0 errors/warnings; retry after ~30s on MSBuild file-lock errors) and `dotnet test --filter "FullyQualifiedName~DynamicUpdateDiscoveryService"`.
- No live catalog HTTP calls in tests: the `Discover` end-to-end tests use a stubbed `HttpMessageHandler` (the established `WindowsUpdateCatalogServiceTests` pattern). Local DISM is broken — no integration runs.
- Respect other agents' file ownership: nothing under `UnattendValidation*`, `ComplianceManifest*`, `CapabilityRepository*`, or the protected services list.

---

### Task 1: Models

**Files:**
- Create: `src/Models/DynamicUpdateModels.cs`

**Interfaces:**
- `enum DynamicUpdateType { ServicingStack, SafeOS, Cumulative, Setup }` — ordered by the apply sequence.
- `class WindowsDynamicUpdate { UpdateType, Build, OSLabel, KBNumber, Title, UpdateId, Architecture, Version, Classification, LastModified, Size, SizeFormatted (computed), DownloadUrl (Uri?), ToString() }`.

- [x] **Step 1: Create `src/Models/DynamicUpdateModels.cs`** with the enum and POCO (`= string.Empty` initializers, XML doc comments, `SizeFormatted` mirroring `WindowsUpdateCatalogResult.SizeFormatted`, `ToString()` like `{KBNumber} - {Title} ({UpdateType})`).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: DynamicUpdateDiscoveryService — pure logic + thin catalog path

**Files:**
- Create: `src/Services/DynamicUpdateDiscoveryService.cs`

**Interfaces:**
- Ctors: `DynamicUpdateDiscoveryService(ModuleCallbacks? callbacks = null)`; `internal DynamicUpdateDiscoveryService(ModuleCallbacks callbacks, WindowsUpdateCatalogService catalogService)` (test seam; the injected catalog service is not disposed).
- `public const int MaxResultsPerQuery = 50`.
- `public List<WindowsDynamicUpdate> Discover(int build, IList<string> osLabels, string architecture, ISet<DynamicUpdateType> requestedTypes, bool debugMode)`.
- `internal static int? ParseBuildNumber(string? build)`.
- `internal static string NormalizeArchitecture(string? architecture)`.
- `internal static List<string> ResolveOSLabels(int build)`.
- `internal static string BuildCatalogQuery(DynamicUpdateType type, string osLabel)`.
- `internal static DynamicUpdateType? ClassifyCatalogResult(WindowsUpdateCatalogResult result)`.
- `internal static List<WindowsDynamicUpdate> SelectLatestPerType(IEnumerable<WindowsUpdateCatalogResult> results, ISet<DynamicUpdateType> requestedTypes, int build, string osLabel, string architecture)`.
- `internal static WindowsUpdateCatalogResult ToCatalogResult(WindowsUpdate update)` — the row→catalog-result mapper (same shape as `SearchWindowsUpdateCatalogCmdlet.ConvertToNewModel`, which stays untouched).

- [x] **Step 1: Write `DynamicUpdateDiscoveryService.cs`**:
  - `ParseBuildNumber`: trim; empty → null; split on `.` — 1 part → parse part[0]; ≥ 3 parts → parse part[2]; 2 parts → parse part[0]; result must be ≥ 10240 (first Windows 10 build) else null.
  - `NormalizeArchitecture`: ordinal-ignore-case `amd64`→`x64`, `arm64`→`ARM64`; `x64`/`x86` pass; null/empty/other → `x64`.
  - `ResolveOSLabels`: documented table (10240…26100); unknown ≥ 22000 → `["Windows 11"]`; unknown < 22000 → `["Windows 10"]`.
  - `BuildCatalogQuery`: ServicingStack → `"{label} Servicing Stack Update"`; Cumulative → `"{label} Cumulative Update"`; SafeOS/Setup → `"{label} Dynamic Update"`; empty label → bare fragment.
  - `ClassifyCatalogResult`: `.net framework` → null; `servicing stack` → ServicingStack; `safe os`/`safeos` → SafeOS; `dynamic update` + `setup` → Setup; `dynamic update` → SafeOS; `cumulative` → Cumulative; else null.
  - `SelectLatestPerType`: dedup by UpdateId (first wins); classify; drop null types; filter to requested types; group by type → pick max by `LastModified` then `Size`; map via private `ToDynamicUpdate`; order ServicingStack → SafeOS → Cumulative → Setup.
  - `Discover`: normalize architecture; for each label × unique query build criteria (`Query`, `Architecture = normalized`, `MaxResultsPerQuery`, `Page = 1`, `SortBy = "LastUpdated"`, `SortDirection = "Descending"`, `IncludeSuperseded = false`); run `SearchUpdates(criteria, false, debugMode)`; map rows via `ToCatalogResult`; aggregate; progress via `_callbacks.Progress` per query; per-query try/catch → warning + continue; then `SelectLatestPerType` and resolve `DownloadUrl` per selected result via `GetDownloadUrls(updateId)` (first URL; failure → warning + null).
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Cmdlet

**Files:**
- Create: `src/Cmdlets/GetWindowsDynamicUpdateCmdlet.cs`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Get, "WindowsDynamicUpdate")]`, `[OutputType(typeof(WindowsDynamicUpdate[]))]`, no `SupportsShouldProcess`.
- `-Build <string>` Mandatory/Position 0/`ValueFromPipelineByPropertyName`; `-Architecture` ValidateSet(amd64, x64, x86, arm64) default amd64; `-Type` ValidateSet(ServicingStack, Cumulative, SafeOS, Setup, All) default All; `-OSLabel <string?>`; `-DebugMode` switch.
- ModuleCallbacks wired with Verbose/Warning/Error plus Progress → `LoggingService.WriteProgress`; `LoggingService.LogOperationStartWithTimestamp`/`LogOperationCompleteWithTimestamp` + `CompleteProgress`.

- [x] **Step 1: Write the cmdlet** — ProcessRecord flow: parse build (`ParseBuildNumber` → null = terminating InvalidArgument error `GetWindowsDynamicUpdateFailed`); resolve labels (`-OSLabel` → single label, else `ResolveOSLabels`); parse `-Type` into the requested set; construct `DynamicUpdateDiscoveryService` with ModuleCallbacks; `Discover(...)`; warn + return on empty; `WriteObject` per result; terminating error on exception; `finally` → `CompleteProgress`.
- [x] **Step 2: Build** to confirm everything compiles.

### Task 4: Help

**Files:**
- Create: `docs/help/Get-WindowsDynamicUpdate.md`

- [x] **Step 1: Write the PlatyPS markdown** copied from `docs/help/Search-WindowsUpdateCatalog.md` as the template — front matter (`external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`), SYNOPSIS/SYNTAX/DESCRIPTION/EXAMPLES, a PARAMETERS entry for every declared parameter (`-Build`, `-Architecture`, `-Type`, `-OSLabel`, `-DebugMode`; ProgressAction is a common parameter and intentionally not documented, matching the guardrail's common-params list), INPUTS/OUTPUTS/NOTES/RELATED LINKS. SYNOPSIS/DESCRIPTION must match the help template style (one-line SYNOPSIS, DESCRIPTION describing the DU types and the discover → download → apply flow).
- [x] **Step 2: Sanity-check** the markdown structure (front matter, section order, every parameter documented).

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/DynamicUpdateDiscoveryServiceTests.cs`

- [x] **Step 1: Write the tests** (pure logic + stubbed-HTTP Discover, no live catalog):
  - `ParseBuildNumber` — `"26100"`, `"26100.1234"`, `"10.0.26100"`, `"10.0.26100.1234"` → 26100; `"abc"`, `""`, null → null.
  - `NormalizeArchitecture` — amd64→x64, arm64→ARM64, x64/x86 pass-through, case-insensitive, null→x64.
  - `ResolveOSLabels` — 26100 → client-first pair; 17763 → pair; 22631 → single; unknown build → generic fallback.
  - `BuildCatalogQuery` — per-type fragments with and without label; SafeOS/Setup share the query.
  - `ClassifyCatalogResult` — [Theory] over real-world title shapes: SSU→ServicingStack, LCU→Cumulative, SafeOS DU→SafeOS, Setup DU→Setup, .NET Framework CU→null, MSRT→null, empty→null.
  - `SelectLatestPerType` — synthetic results (older + newer CU, SSU, SafeOS, Setup, .NET noise, duplicate UpdateId): latest per type by LastModified (Size tie-break), ordered ServicingStack → SafeOS → Cumulative → Setup, KB/title/metadata mapping, DownloadUrl null, requested-type subset filtering, dedup.
  - `Discover` end-to-end via `internal` ctor + `StubHttpMessageHandler` (returns catalog rows HTML for search/sort POSTs, download-dialog HTML for DownloadDialog.aspx POSTs): one result per type with resolved DownloadUrl; no-results HTML → empty list; failed query → warning and remaining results still returned.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~DynamicUpdateDiscoveryService"`) and confirm they pass.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` — 0 errors, 0 warnings.
- [x] **Step 2: Run filtered unit tests** (same filter as Task 5 / Step 2) — all pass.
- [x] **Step 3: Final report** — spec + plan paths, cmdlet name, query strategy per DU type, test counts, deviations. Working tree left uncommitted; psd1 export and shipped MAML noted as orchestrator-owned follow-ups.
