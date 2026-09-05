# Unattend Validation — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** the "Unattend (beyond what exists)" backlog item. The module
already reads, edits, exports and installs unattend.xml
(`Get/Set/Export/Install/New-UnattendXMLConfiguration` over
`UnattendXMLService` + `UnattendXMLConfiguration`); the missing half is
**validation** — telling an operator whether an unattend.xml is actually usable
by Windows Setup, and if not, exactly where and why.

## Problem

The existing unattend surface is load/edit-centric. The only check available
today is `UnattendXMLConfiguration.Validate()`, which returns a list of raw
strings for exactly two conditions (no root element, missing/invalid `xmlns`).
There is no way to answer:

- Is the file even well-formed XML, and does it have the right root/namespace?
- Do the `settings` elements carry valid `pass` attributes (the five real
  configuration passes)?
- Are component names sane, unique within their pass, and properly attributed?
- Are `RunSynchronous`/`RunAsynchronous` command `Order` values present,
  numeric, positive and unique (Setup behavior for duplicate orders is
  undefined)?
- Are `settings` entries free of children Setup cannot process (anything other
  than `component`), and is the root free of stray elements?
- Are the classic mistakes present — `CopyProfile` placed outside the
  `specialize` pass (silently ignored), deprecated
  `SkipMachineOOBE`/`SkipUserOOBE` settings, empty `settings` sections?

`Get-UnattendXMLConfiguration -Validate` surfaces only the two-string check, so
a broken file that *loads* still installs cleanly into
`Windows\Panther\unattend.xml` via `Install-UnattendXMLConfiguration` and fails
later (or silently does nothing) during setup. A typed, machine-readable
validation report closes that gap.

## Goals

1. Add `Test-UnattendXMLConfiguration` — a read-only cmdlet that validates an
   unattend.xml file and returns a structured report
   (`UnattendValidationReport`) with per-issue details (severity, pass, element
   path, message) plus an overall `IsValid`.
2. Implement the documented rule set below: well-formedness, root/namespace
   checks, pass validation, component name/duplicate/architecture checks,
   run-command ordering, settings child rules and the curated common-mistake
   table.
3. Keep every rule pure and unit-testable: `internal static` methods over an
   in-memory `System.Xml.XmlDocument` — no DISM, no image mounting, no network,
   no `RunSynchronous` execution of any kind.
4. Support a `-Severity` filter (`Error` = errors only; `Warning` = errors +
   warnings) while keeping `IsValid` computed over the *complete* issue set.
5. Mirror the family's input conventions exactly: the unattend family takes the
   file as a mandatory pipeline-by-value `FileInfo` (see
   `Get-UnattendXMLConfiguration`), so `Test-UnattendXMLConfiguration -Path`
   accepts `FileInfo` the same way.

## Non-goals

- **XSD schema validation.** Windows ships no maintained public unattend.xsd
  that covers every component/setting across OS versions, and the rule set
  below catches the mistakes that matter without one. Full schema validation
  stays out.
- **A component catalog.** "Unknown component" is decided by a documented
  heuristic (known-name prefix + structure), not an exhaustive
  every-Windows-version component database. The Capability repository phase is
  owned by another work stream; this design must not depend on it.
- **Writes / repair.** Detection only. No file modification, no
  `SupportsShouldProcess`, no auto-fix, no re-serialization.
- **DISM / image operations.** The validation runs against an XML file only.
  Real-image behavior is never exercised locally (the local DISM
  `OpenOfflineSession` servicing limitation stands); installing a validated
  file remains `Install-UnattendXMLConfiguration`'s job.
- **Changing the existing unattend surface.** `UnattendXMLService`,
  `UnattendXMLConfiguration` and the existing unattend cmdlets are read-only
  references. Everything here is new files.

## Design

All additions follow the existing service + model + cmdlet split. One new
cmdlet, one new service, one new model file, one help page, one test class. No
manifest change is made by this phase (the orchestrator adds the export), no
new NuGet packages (`System.Xml` ships with the platform), LangVersion 8.0 /
netstandard2.0 / nullable-enabled throughout, no C# 9+ syntax (no `is not`, no
`ArgumentList`).

### New files

**`src/Models/UnattendValidationModels.cs`**

- `enum UnattendValidationSeverity { Warning, Error }` — ordered by magnitude
  (`Warning = 0 < Error = 1`) so the `-Severity` minimum-threshold filter is a
  single comparison (`Severity >= minimumSeverity`): Warning (default) reports
  everything, Error reports errors only.
- `UnattendValidationIssue` — one problem: `Severity`
  (`UnattendValidationSeverity`), `Pass` (configuration pass the element lives
  in, empty when not applicable), `ElementPath` (stable readable path, e.g.
  `/unattend/settings[@pass='specialize']/component[@name='Microsoft-Windows-Shell-Setup']/CopyProfile`),
  `Message`, `RuleId` (stable machine-readable rule identifier, see rule table),
  and `ToString()` override mirroring `HealthFinding`.
- `UnattendValidationReport` — `FilePath`, `Issues`
  (`List<UnattendValidationIssue>`), `IsValid` (no `Error`-severity issues over
  the complete, unfiltered set), `ErrorCount` / `WarningCount` (computed on the
  reported, post-filter issues), `ValidatedAt` (`DateTime.UtcNow`),
  `ToString()` override.

**`src/Services/UnattendXMLValidationService.cs`** (`ModuleCallbacks`-aware,
mirroring `ReservedStorageService`)

- `private const string ServiceName = "UnattendXMLValidationService"`.
- `public const string UnattendNamespace = "urn:schemas-microsoft-com:unattend"`.
- `public static readonly string[] ValidPasses` — `windowsPE`,
  `offlineServicing`, `generalize`, `specialize`, `oobeSystem`.
- `public UnattendXMLValidationService(ModuleCallbacks? callbacks = null)` —
  public ctor, `ModuleCallbacks.Silent` default.
- `public UnattendValidationReport ValidateFile(string filePath,
  UnattendValidationSeverity minimumSeverity = UnattendValidationSeverity.Warning)`
  — thin orchestrator: verbose-log, load + parse (parse failure → single
  `XML-NotWellFormed` error issue, `IsValid = false`), run
  `AnalyzeDocument`, filter, build report. Missing file →
  `InvalidOperationException` (the cmdlet pre-checks existence).
- `public UnattendValidationReport ValidateDocument(UnattendXMLConfiguration
  config, UnattendValidationSeverity minimumSeverity = ...)` — thin; validates
  `config.XmlDocument` (so an in-memory pipeline object can be tested without a
  file) and reports under `config.SourceFilePath`.
- `internal static List<UnattendValidationIssue> AnalyzeDocument(XmlDocument
  document)` — pure; aggregates all rules in the documented order.
- Pure rule methods, one per rule family (all `internal static`, all returning
  `List<UnattendValidationIssue>`):
  - `ValidateRootStructure(XmlDocument)` — R1–R3.
  - `ValidateSettings(XmlDocument)` — R4–R8.
  - `ValidateComponents(XmlDocument)` — R9–R13.
  - `ValidateRunCommands(XmlDocument)` — R14–R19.
  - `ValidateKnownSettings(XmlDocument)` — R20–R21.
- `internal static List<UnattendValidationIssue> FilterIssues(List<
  UnattendValidationIssue> issues, UnattendValidationSeverity minimumSeverity)`
  — pure; the `-Severity` threshold (`Severity >= minimumSeverity`).
- `internal static UnattendValidationReport BuildReport(string filePath,
  List<UnattendValidationIssue> issues, UnattendValidationSeverity
  minimumSeverity)` — pure; sets `IsValid` from the unfiltered set, stores the
  filtered list, stamps `ValidatedAt`.
- `internal static string BuildElementPath(XmlNode node)` — pure; the
  `ElementPath` format: root as `/unattend`; `settings` annotated with its
  `pass` (`settings[@pass='specialize']`, or `settings` when the attribute is
  absent); `component` annotated with its `name`; every other element as
  `name[n]` where `n` is the 1-based index among same-local-name element
  siblings.

### Validation rules (exact, machine-checked set)

Severity values: **E** = `Error` (makes `IsValid` false), **W** = `Warning`.

| # | RuleId | Sev | Rule |
| --- | --- | --- | --- |
| R1 | `XML-NotWellFormed` | E | File fails XML parsing (`XmlException` at load). Single issue; element path `/`. |
| R2 | `XML-RootNotUnattend` | E | Parsed, but root element `LocalName != "unattend"` (includes no-root/empty document). |
| R3 | `XML-WrongNamespace` | E | Root is `unattend` but `NamespaceURI != "urn:schemas-microsoft-com:unattend"`. Windows Setup requires the namespace. |
| R4 | `Pass-Missing` | E | A `settings` element has no `pass` attribute (or an empty one). |
| R5 | `Pass-Unknown` | E | A `settings` element's `pass` is not one of `windowsPE`, `offlineServicing`, `generalize`, `specialize`, `oobeSystem` (compared OrdinalIgnoreCase). Nuance: `auditSystem`/`auditUser` are real audit-mode passes but out of scope for this tool's deployment flows — they get `Pass-Unknown` as a **Warning** instead of an Error; any other value is an Error. |
| R6 | `Settings-InvalidChild` | E | A direct child of `settings` is not a `component` element (Setup processes only components under settings). |
| R7 | `Root-InvalidChild` | E | A direct child of the root element is not a `settings` element (e.g. a component parked directly under `unattend`). |
| R8 | `Settings-Empty` | W | A `settings` element has no children (useless but harmless). |
| R9 | `Component-MissingName` | E | A `component` element has no `name` attribute (or an empty one). |
| R10 | `Component-Duplicate` | E | The same component `name` + `processorArchitecture` appears more than once under the *same* `settings` element. Same name in different passes (or with different architectures) is valid and not flagged. |
| R11 | `Component-UnknownName` | W | Component `name` does not start with `Microsoft-Windows-` (OrdinalIgnoreCase) or contains whitespace — the documented "unknown component" heuristic, not a catalog. |
| R12 | `Component-MissingArchitecture` | W | Component has no `processorArchitecture` attribute (Setup usually requires it). |
| R13 | `Component-UnknownArchitecture` | W | `processorArchitecture` is not one of `x86`, `amd64`, `ia64`, `arm`, `arm64` (OrdinalIgnoreCase). |
| R14 | `Run-MissingOrder` | E | A `RunSynchronousCommand`/`RunAsynchronousCommand` has no `Order` child element, or it is empty. |
| R15 | `Run-DuplicateOrder` | E | Two commands in the *same* `RunSynchronous`/`RunAsynchronous` section share the same `Order`. Execution order for duplicates is undefined. |
| R16 | `Run-InvalidOrder` | E | An `Order` value is not a positive integer (non-numeric, `<= 0`). |
| R17 | `Run-MissingCommand` | E | A run command has no `Command` child element, or it is empty. |
| R18 | `Run-UnknownCommandElement` | W | A child of `RunSynchronous`/`RunAsynchronous` is not a `RunSynchronousCommand`/`RunAsynchronousCommand` element. |
| R19 | `Run-InvalidPass` | W | A `RunAsynchronous` section appears inside a component in the `windowsPE` pass (asynchronous commands are not supported in windowsPE). |
| R20 | `Setting-WrongPass` | E | `CopyProfile` appears inside a `Microsoft-Windows-Shell-Setup` component whose pass is not `specialize` — it is only honored in specialize and silently ignored elsewhere. Skipped when the owning pass is empty (that case is already flagged by R4/R7). |
| R21 | `Setting-Deprecated` | W | `SkipMachineOOBE` or `SkipUserOOBE` appears inside a `Microsoft-Windows-Shell-Setup` component — both are deprecated and ignored by modern Setup. |

Rule scoping notes:

- Elements are matched by `LocalName` and accepted in either the unattend
  namespace or no namespace, mirroring how the existing
  `UnattendXMLConfiguration.Components` selector already treats namespaced and
  legacy non-namespaced files.
- The pass used for R20/R21 and `UnattendValidationIssue.Pass` is resolved
  bottom-up from the owning `settings[@pass]` ancestor; empty when the element
  is not under a settings element (that case is separately flagged by the R7
  structural check where applicable).
- R10 duplicate detection keys on `(name, processorArchitecture)` with
  OrdinalIgnoreCase, scoped per `settings` parent — first occurrence wins,
  subsequent ones each raise an issue.
- The run-command checks (R14–R18) apply to `RunSynchronous` and
  `RunAsynchronous` containers wherever they appear (inside components in any
  pass); order uniqueness is scoped per container, matching the documented
  sibling-command semantics.

### Cmdlet

**`src/Cmdlets/TestUnattendXMLConfigurationCmdlet.cs`**

- `[Cmdlet(VerbsDiagnostic.Test, "UnattendXMLConfiguration")]`,
  `[OutputType(typeof(UnattendValidationReport))]` — no
  `SupportsShouldProcess` (read-only).
- `-Path <FileInfo>` — mandatory, `Position = 0`,
  `ValueFromPipeline = true`, `ValueFromPipelineByPropertyName = true`,
  `[ValidateNotNull]` — exactly the input pattern of
  `Get-UnattendXMLConfiguration` (`File` there, `-Path` here per the phase
  spec).
- `-Severity <UnattendValidationSeverity>` — default `Warning` (report
  everything); `Error` restricts the reported issues to errors. `IsValid` is
  always computed over the complete issue set regardless of the filter.
- Flow mirrors the family: existence pre-check with
  `FileNotFoundException` + `WriteError` + return; then
  `LoggingService.LogOperationStartWithTimestamp` → service call →
  `LogOperationCompleteWithTimestamp` (component name
  `Test-UnattendXMLConfiguration`) → `WriteObject(report)`. Exceptions →
  `LoggingService.WriteError` + error record, no throw out of `ProcessRecord`.

### Data Flow

```
Test-UnattendXMLConfiguration -Path <FileInfo> [-Severity <Error|Warning>]
   └─► UnattendXMLValidationService.ValidateFile(path, minimumSeverity)
         ├─► XmlDocument.Load(path) ─── parse failure ──► R1 issue
         └─► AnalyzeDocument (pure)
               ├─► ValidateRootStructure  (R1–R3)
               ├─► ValidateSettings       (R4–R8)
               ├─► ValidateComponents     (R9–R13)
               ├─► ValidateRunCommands    (R14–R19)
               └─► ValidateKnownSettings  (R20–R21)
         └─► FilterIssues ─► BuildReport ─► UnattendValidationReport
   └─► WriteObject(report)  ── IsValid drives pass/fail automation
```

## Error Handling

- Parse failures never throw out of the service: an `XmlException` becomes the
  single `XML-NotWellFormed` issue with `IsValid = false` (the report is still
  the output; downstream automation keys on `IsValid`).
- A missing file throws `InvalidOperationException` from `ValidateFile`
  (documented precondition); the cmdlet pre-checks `FileInfo.Exists` and
  writes a `FileNotFound` error record instead, mirroring
  `GetUnattendXMLConfigurationCmdlet`.
- Structural issues are additive: one broken document yields every applicable
  issue, not just the first. Rules are individually guarded so an unexpected
  DOM shape cannot abort the remaining rules.
- The severity filter never changes `IsValid`; it only narrows `Issues` (and
  with them `ErrorCount`/`WarningCount`).

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/UnattendXMLValidationServiceTests.cs`,
  plain `[Fact]`/`[Theory]`, no mocking framework, no DISM, no images):**
  - Temp-file fixtures (temp-dir pattern
    `Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid...)` from
    `ImageComparisonServiceTests.cs`) driving the public `ValidateFile` path:
    a deliberately valid unattend.xml validates clean; a malformed file yields
    the single `XML-NotWellFormed` error; a missing file throws.
  - Pure `LoadXml`-fixture tests per rule family: root/namespace (R1–R3), pass
    missing/unknown/valid set (R4–R5), settings child + empty (R6–R7),
    component name/duplicate/architecture (R8–R12), run ordering
    (R13–R17) + windowsPE RunAsynchronous (R18), CopyProfile pass and
    deprecated settings (R19–R20).
  - Report building: `IsValid`/counts/`ToString`; a multi-issue document
    reports issues in rule order with stable element paths.
  - Severity filter: `Error` keeps only errors; warnings are dropped but
    `IsValid` still reflects them.
- **Integration:** none required and none added — this phase is fully
  local-verifiable and never touches DISM or a mounted image (the
  `tests/integration/` Pester file is orchestrator-owned and untouched).

## Risks

- **Rule drift vs. Windows versions.** The curated table (valid passes, valid
  architectures, CopyProfile/deprecation notes) is high-confidence for
  currently shipping Windows; a future OS could change it. Mitigation: each
  rule is isolated behind one `RuleId`, so a rule can be adjusted without
  touching the others.
- **False positives from the name heuristic (R11).** A third-party component
  name legitimately not starting with `Microsoft-Windows-` would warn.
  Documented as a heuristic warning (never an error), so it cannot flip
  `IsValid`.
- **Ordering semantics.** Order uniqueness is enforced per
  `RunSynchronous`/`RunAsynchronous` container; cross-container ordering in
  the same pass is not validated because Setup resolves it per container.
  Documented scope keeps the rule testable and avoids speculative errors.
