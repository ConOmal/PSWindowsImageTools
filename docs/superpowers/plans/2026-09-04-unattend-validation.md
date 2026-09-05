# Unattend Validation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Test-UnattendXMLConfiguration` — a read-only validation cmdlet that parses an unattend.xml file and returns a typed `UnattendValidationReport` (per-issue severity/pass/element path/message + overall `IsValid`) covering well-formedness, root/namespace, pass attributes, component sanity, run-command ordering, settings structure and curated common mistakes.

**Architecture:** Mirror the existing convention: `Models/UnattendValidationModels.cs` for the new types (`UnattendValidationSeverity`, `UnattendValidationIssue`, `UnattendValidationReport`), `Services/UnattendXMLValidationService.cs` for the work (`ModuleCallbacks`-aware like `ReservedStorageService`), and pure `internal static` methods for every piece of logic a unit test can drive without DISM or images (rule evaluation, path building, filtering, report building). The only non-pure surface is `ValidateFile`'s file load, which routes an `XmlException` into a synthetic `XML-NotWellFormed` issue. `ValidateDocument` reuses the pure core for in-memory `UnattendXMLConfiguration` objects.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), `System.Xml` (platform), xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-unattend-validation-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Netstandard2.0 ref assemblies lack `[NotNullWhen]` — null-narrowing uses `!`/`?? string.Empty` patterns as the existing services do.
- No new NuGet packages. `System.Xml` ships with the platform.
- Read-only cmdlet: no `SupportsShouldProcess`, no file writes, no DISM, no image mounting, no network.
- Do NOT modify the existing unattend files (`src/Services/UnattendXMLService.cs`, `src/Models/UnattendXMLConfiguration.cs`, existing unattend cmdlets) — read-only references; all new code goes in NEW files.
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (orchestrator adds the export), `Module/PSWindowsImageTools/bin/*`, `Module/PSWindowsImageTools/en-US/*`, or `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (orchestrator owns them).
- Do NOT touch files owned by other agents (`DynamicUpdate*`, `ComplianceManifest*`, `CapabilityRepository*`) or the protected services list.
- Do not run the full unit suite (concurrent builders). Verification is: `dotnet build src/PSWindowsImageTools.csproj` (0 errors, 0 warnings; if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry), then `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~UnattendXMLValidation"`.
- Temp-file fixtures use the pattern from `tests/PSWindowsImageTools.Tests/ImageComparisonServiceTests.cs` (`Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"))`), cleaned up in `IDisposable` fixture classes.
- Test class: `UnattendXMLValidationServiceTests` (new).

---

### Task 1: Unattend validation models

**Files:**
- Create: `src/Models/UnattendValidationModels.cs`

**Interfaces:**
- `enum UnattendValidationSeverity { Warning = 0, Error = 1 }` — ordered by magnitude so `-Severity` is a minimum-threshold comparison (Warning reports everything, Error errors only).
- `UnattendValidationIssue { Severity, Pass, ElementPath, Message, RuleId }` with `ToString()` override.
- `UnattendValidationReport { FilePath, Issues, IsValid, ErrorCount, WarningCount, ValidatedAt }` with `ToString()` override.

- [x] **Step 1: Create `src/Models/UnattendValidationModels.cs`** with the enum and two types (plain POCOs, `= string.Empty` / `new List<...>()` initializers, XML doc comments, `ToString()` overrides mirroring `HealthCheckModels.cs` style).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: UnattendXMLValidationService — pure rule engine + thin file load

**Files:**
- Create: `src/Services/UnattendXMLValidationService.cs`

**Interfaces:**
- `UnattendXMLValidationService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default (mirror `ReservedStorageService`).
- `public const string UnattendNamespace` = `urn:schemas-microsoft-com:unattend`; `public static readonly string[] ValidPasses` = windowsPE/offlineServicing/generalize/specialize/oobeSystem.
- `public UnattendValidationReport ValidateFile(string filePath, UnattendValidationSeverity minimumSeverity = Warning)` — thin; parse failure → single `XML-NotWellFormed` issue; missing file → `InvalidOperationException`.
- `public UnattendValidationReport ValidateDocument(UnattendXMLConfiguration config, UnattendValidationSeverity minimumSeverity = Warning)` — thin; validates `config.XmlDocument`, reports under `config.SourceFilePath`.
- `internal static List<UnattendValidationIssue> AnalyzeDocument(XmlDocument document)` — pure; aggregates rules in documented order.
- `internal static` rule methods: `ValidateRootStructure` (R1–R3), `ValidateSettings` (R4–R8), `ValidateComponents` (R9–R13), `ValidateRunCommands` (R14–R19), `ValidateKnownSettings` (R20–R21).
- `internal static List<UnattendValidationIssue> FilterIssues(issues, minimumSeverity)`; `internal static UnattendValidationReport BuildReport(filePath, issues, minimumSeverity)`; `internal static string BuildElementPath(XmlNode)`.

- [x] **Step 1: Write `UnattendXMLValidationService.cs`** with all members above:
  - Element matching by `LocalName`, accepting unattend-namespace and non-namespaced elements (mirrors the existing `Components` selector).
  - Rules exactly as the spec table (R1–R21); issue order = R1..R21 grouped; `Pass` resolved from the nearest `settings[@pass]` ancestor; duplicate components keyed on `(name, processorArchitecture)` per settings parent; run-order checks scoped per `RunSynchronous`/`RunAsynchronous` container; root children limited to `settings`.
  - `BuildElementPath`: root `/unattend`; `settings[@pass='x']` (or bare `settings`); `component[@name='X']`; others `name[n]` (1-based index among same-local-name element siblings).
  - `BuildReport`: `IsValid` from the unfiltered set; `Issues` filtered; counts from the filtered list; `ValidatedAt = DateTime.UtcNow`.
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Test-UnattendXMLConfiguration cmdlet

**Files:**
- Create: `src/Cmdlets/TestUnattendXMLConfigurationCmdlet.cs`

**Interfaces:**
- `[Cmdlet(VerbsDiagnostic.Test, "UnattendXMLConfiguration")]`, `[OutputType(typeof(UnattendValidationReport))]`, no `SupportsShouldProcess`.
- `-Path <FileInfo>` — Mandatory, Position 0, `ValueFromPipeline`, `ValueFromPipelineByPropertyName`, `[ValidateNotNull]` (mirrors `GetUnattendXMLConfigurationCmdlet.File`).
- `-Severity <UnattendValidationSeverity>` — default `Warning` (report everything); `Error` reports errors only.
- Flow: existence pre-check (FileNotFoundException → `WriteError` → return) → `LogOperationStartWithTimestamp(this, ComponentName, "Validate Unattend XML configuration")` → `new UnattendXMLValidationService(ModuleCallbacks.FromCmdlet(this)).ValidateFile(...)` → `LogOperationCompleteWithTimestamp` → `WriteObject(report)`; exceptions → `LoggingService.WriteError` + error record.

- [x] **Step 1: Write `TestUnattendXMLConfigurationCmdlet.cs`** (`ComponentName = "Test-UnattendXMLConfiguration"`; verbose start/complete logging; verbose summary of counts; no throw out of `ProcessRecord`).
- [x] **Step 2: Build** to confirm everything compiles.

### Task 4: Help page

**Files:**
- Create: `docs/help/Test-UnattendXMLConfiguration.md`

- [x] **Step 1: Create `docs/help/Test-UnattendXMLConfiguration.md`** in PlatyPS format using `docs/help/Get-UnattendXMLConfiguration.md` as the template (front matter: `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`, `schema: 2.0.0`); document SYNOPSIS/SYNTAX/DESCRIPTION/EXAMPLES, every parameter including `-ProgressAction` (aliases `proga`) and CommonParameters, INPUTS (`System.IO.FileInfo`), OUTPUTS (`PSWindowsImageTools.Models.UnattendValidationReport`).

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/UnattendXMLValidationServiceTests.cs`

- [x] **Step 1: Create `UnattendXMLValidationServiceTests.cs`** — plain `[Fact]`/`[Theory]`:
  - Temp-file fixtures (disposable temp dir per test class) through public `ValidateFile`: valid unattend → clean; malformed XML → single `XML-NotWellFormed` error; missing file → throws `InvalidOperationException`.
  - Pure `LoadXml` fixtures per rule family: R1–R3 root/namespace; R4–R5 pass missing/unknown + all five valid passes accepted; R6–R7 settings child + empty; R8–R12 component name/duplicate/architecture (same name+different arch in one pass is NOT flagged); R13–R17 run ordering; R18 RunAsynchronous in windowsPE; R19–R20 CopyProfile/deprecated.
  - `BuildElementPath` shape (`/unattend/settings[@pass='specialize']/component[@name='X']/Child`).
  - Report building (`IsValid`, counts, `ToString`) and severity filter (`Error` drops warnings; `IsValid` unchanged).
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~UnattendXMLValidation"`) and confirm they pass.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors, 0 warnings).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 5 / Step 2).
- [x] **Step 3: Integration note** — no Pester changes; this phase never touches DISM or mounted images (all logic pure + temp-file fixtures).
- [x] **Step 4: Final report** — spec + plan paths, cmdlet name, implemented rules, test counts, deviations. Leave working tree uncommitted.
