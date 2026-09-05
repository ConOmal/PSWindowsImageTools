# Compliance Manifest — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Export-WindowsImageComplianceManifest` to PSWindowsImageTools — one audit artifact (JSON) that combines an `ImageSnapshot`, an optional `WindowsImageSecurityBaselineReport` and an optional `ServicingChainReport` into a provenance-carrying, policy-evaluation manifest. This is NOT the excluded generic-inventory export: the manifest carries aggregate counts + evaluation verdicts + provenance, never item lists.

**Architecture:** New files only: `Models/ComplianceManifestModels.cs` (document shape), `Services/ComplianceManifestService.cs` (pure builders + Save/Load mirroring `ImageComparisonService.SaveSnapshot`/`LoadSnapshot`), `Cmdlets/ComplianceManifestCmdlet.cs` (mirrors `Export-WindowsImageSBOM`: `PSCmdlet`, no `SupportsShouldProcess`, file-write only, `-Force` gate like `Export-UnattendXMLConfiguration`), plus a PlatyPS help source and xUnit tests. Every piece of logic a unit test can drive is `internal static` and pure.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), Newtonsoft.Json 13 (existing dependency, incl. `StringEnumConverter`), xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-compliance-manifest-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions / nullable annotations exactly as the existing services do; netstandard2.0 ref assemblies lack `[NotNullWhen]`, so null-narrowing uses `!` / explicit null checks.
- No new NuGet dependencies (`StringEnumConverter` ships in Newtonsoft.Json).
- Do NOT modify consumed files (read-only references): `src/Models/ImageComparisonModels.cs`, `src/Models/SecurityBaselineModels.cs`, `src/Models/ServicingChainModels.cs`, `src/Services/ImageComparisonService.cs`, `src/Services/SecurityBaselineService.cs`, `src/Services/ServicingChainService.cs`.
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (orchestrator adds the export), `Module/PSWindowsImageTools/bin/*`, `Module/PSWindowsImageTools/en-US/*`, or `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (orchestrator owns all of them).
- Do NOT touch other agents' files (`UnattendValidation*`, `DynamicUpdate*`, `CapabilityRepository*`) or the protected services list (`FormatUtilityService.cs`, `NativeRegistryService.cs`, `RegistryDriftService.cs`, etc.).
- No DISM anywhere: the cmdlet is a pure file writer; all tests run offline with synthetic objects.
- Do NOT commit. Do not run the full unit suite (concurrent builders); verify with a filtered `dotnet test`. If MSBuild `.obj`/file-lock errors appear, wait ~30s and retry.
- Use the temp-directory fixture pattern from `tests/PSWindowsImageTools.Tests/ImageComparisonServiceTests.cs` (`Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"))`) only where disk access is required (JSON round-trip); all other new tests are pure in-memory.
- Test class: `ComplianceManifestServiceTests` (new, filtered run only).

---

### Task 1: Compliance manifest models

**Files:**
- Create: `src/Models/ComplianceManifestModels.cs`

**Interfaces:**
- `enum WindowsImageComplianceStatus { Unknown, Compliant, NonCompliant }` with `[JsonConverter(typeof(StringEnumConverter))]`.
- `WindowsImageComplianceManifest { ManifestVersion, GeneratedAt, ToolName, ToolVersion, Image, Inventory, OverallStatus, SecurityBaseline?, ServicingChain?, HasSecurityBaseline, HasServicingChain }`.
- `ComplianceManifestImageIdentity { ImageName, ImageIndex, ImagePath, MountPath, CapturedAt }`.
- `ComplianceManifestInventorySummary { Packages, Features, Capabilities, AppxPackages, Software, Drivers, Registry, TotalItems }`.
- `ComplianceManifestBaselineSection { ImageName, MountPath, IsCompliant, TotalEntries, CompliantCount, NonCompliantCount, NotPresentCount, Entries }`.
- `ComplianceManifestBaselineEntry { Hive, KeyPath, ValueName, ExpectedValue, ValueType, Rationale, State, ObservedValue, ObservedValueType }` (all string).
- `ComplianceManifestServicingSection { ImageName, ImagePath, GeneratedAt, PackageCount, ServicingStackUpdate?, CumulativeUpdate?, OrderingValid, Issues }`.

- [x] **Step 1: Create `src/Models/ComplianceManifestModels.cs`** with the types above (plain POCOs, `= string.Empty` / `new List<...>()` initializers, XML doc comments, `ToString()` overrides mirroring existing Models style; computed `Has*` flags as get-only properties like `ImageSnapshot.TotalItems`).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: ComplianceManifestService — pure builders + Save/Load

**Files:**
- Create: `src/Services/ComplianceManifestService.cs`

**Interfaces:**
- `ComplianceManifestService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default.
- `public const string CurrentManifestVersion = "1.0"`.
- `public WindowsImageComplianceManifest BuildManifest(ImageSnapshot snapshot, WindowsImageSecurityBaselineReport? baselineReport = null, ServicingChainReport? servicingChainReport = null)`.
- `internal static ComplianceManifestImageIdentity BuildImageIdentity(ImageSnapshot snapshot)`.
- `internal static ComplianceManifestInventorySummary BuildInventorySummary(ImageSnapshot snapshot)`.
- `internal static ComplianceManifestBaselineSection BuildBaselineSection(WindowsImageSecurityBaselineReport report)`.
- `internal static ComplianceManifestBaselineEntry AppendBaselineEntry(WindowsImageSecurityBaselineObservation observation)`.
- `internal static ComplianceManifestServicingSection BuildServicingSection(ServicingChainReport report)`.
- `internal static WindowsImageComplianceStatus ResolveOverallStatus(WindowsImageSecurityBaselineReport? baselineReport)`.
- `internal static string ResolveToolVersion()`.
- `public static void SaveManifest(WindowsImageComplianceManifest manifest, string manifestPath)`.
- `public static WindowsImageComplianceManifest LoadManifest(string manifestPath)`.

- [x] **Step 1: Write `ComplianceManifestService.cs`** with all members above:
  - `BuildManifest`: null snapshot → `ArgumentNullException`; assemble identity/summary; sections via the builders (null when the report is null); `OverallStatus` via `ResolveOverallStatus`; provenance (`GeneratedAt` = now UTC, `ToolName`, `CurrentManifestVersion`, `ResolveToolVersion`); image-name mismatch between snapshot and a supplied report → `_callbacks.Warning` (section still included).
  - `BuildBaselineSection`: counts computed from `Entries` states (`CompliantCount` etc.), `IsCompliant` from the report, entries projected in order via `AppendBaselineEntry` (enums → `ToString()`).
  - `BuildServicingSection`: `PackageCount` = `Packages.Count`; SSU/LCU via `?.ToString()` (null when unclassified); `OrderingValid` + `Issues` copied.
  - `ResolveOverallStatus`: null report → `Unknown`; else `IsCompliant ? Compliant : NonCompliant`.
  - `ResolveToolVersion`: `typeof(ComplianceManifestService).Assembly.GetName().Version` → `ToString()`, `"unknown"` when null.
  - `SaveManifest`/`LoadManifest`: mirror `ImageComparisonService.SaveSnapshot`/`LoadSnapshot` (`Formatting.Indented`, `File.WriteAllText`; `FileNotFoundException` / null → `InvalidOperationException`).
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Export-WindowsImageComplianceManifest cmdlet

**Files:**
- Create: `src/Cmdlets/ComplianceManifestCmdlet.cs`

**Interfaces:**
- `ExportWindowsImageComplianceManifestCmdlet : PSCmdlet`, `[Cmdlet(VerbsData.Export, "WindowsImageComplianceManifest")]`, `[OutputType(typeof(WindowsImageComplianceManifest))]`, no `SupportsShouldProcess`.
- `-Snapshot <ImageSnapshot>` (Mandatory, Position 0, pipeline by value, `[ValidateNotNull]`).
- `-BaselineReport <WindowsImageSecurityBaselineReport>` (optional, `[ValidateNotNull]`).
- `-ServicingChainReport <ServicingChainReport>` (optional, `[ValidateNotNull]`).
- `-DestinationPath <string>` (Mandatory, Position 1, `[ValidateNotNullOrEmpty]`, PSPath-resolved).
- `-Force` (file exists without it → non-terminating `FileExists`/`ResourceExists` error, mirroring `Export-UnattendXMLConfiguration`).

- [x] **Step 1: Write `ComplianceManifestCmdlet.cs`** — `ProcessRecord`: resolve path via `GetUnresolvedProviderPathFromPSPath`; enforce exists/`-Force`; create parent directory when missing; `LoggingService.WriteVerbose(this, ComponentName, ...)` around build + save via `ComplianceManifestService` with `ModuleCallbacks.FromCmdlet(this)`; `WriteObject(manifest)`; catch-all → `WriteError(new ErrorRecord(ex, "ExportComplianceManifestFailed", ErrorCategory.NotSpecified, Snapshot))`.
- [x] **Step 2: Build** to confirm everything compiles.

### Task 4: Help markdown

**Files:**
- Create: `docs/help/Export-WindowsImageComplianceManifest.md`

- [x] **Step 1: Write the PlatyPS markdown** (template: `docs/help/Export-WindowsImageSBOM.md`; front matter `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`): SYNOPSIS/DESCRIPTION, one syntax block, EXAMPLES (pipeline from `Get-WindowsImageSnapshot` + full audit combination), PARAMETERS documenting `-Snapshot`, `-BaselineReport`, `-ServicingChainReport`, `-DestinationPath`, `-Force`, `-ProgressAction`, CommonParameters, INPUTS (`PSWindowsImageTools.Models.ImageSnapshot`), OUTPUTS (`PSWindowsImageTools.Models.WindowsImageComplianceManifest`), NOTES (baseline provenance = assembly version; `OverallStatus` semantics), RELATED LINKS.

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/ComplianceManifestServiceTests.cs`

- [x] **Step 1: Create `ComplianceManifestServiceTests.cs`** — plain `[Fact]`s, no mock framework, synthetic objects:
  - Snapshot-only manifest: identity/summary/provenance from the snapshot, sections null, `OverallStatus = Unknown`, tool version non-empty, `Has*` false.
  - Baseline section: counts per state, `IsCompliant`, per-entry projections (string `State`/`ValueType`, observed values), order preserved.
  - Servicing section: package count, SSU/LCU `ToString()` summaries (null when the report has none), `OrderingValid`, `Issues`.
  - `ResolveOverallStatus`: null → Unknown; compliant → Compliant; non-compliant → NonCompliant.
  - Image-name mismatch: section still built + warning recorded via a recording `ModuleCallbacks`.
  - JSON round-trip: `SaveManifest` → `LoadManifest` in a temp `PSWIT-Tests-` directory preserves `OverallStatus` (string), baseline entries, servicing issues, provenance; `LoadManifest` missing file → `FileNotFoundException`.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~ComplianceManifestServiceTests"`) and confirm they pass.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors, 0 warnings).
- [x] **Step 2: Re-run the filtered unit tests** (same filter as Task 5).
- [x] **Step 3: Help guardrail note** — `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` is intentionally untouched (orchestrator adds the export); once exported, checks 1–3 of `Scripts/verify-help.ps1` are satisfied by the new markdown, and check 4 (shipped MAML) is regenerated by the orchestrator. Do not run the full unit suite or any integration/Pester suite; this phase never touches DISM.
- [x] **Step 4: Final report** — spec + plan paths, cmdlet name, manifest JSON shape, test counts, deviations. Leave working tree uncommitted.
