# Compliance Manifest — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** "Compliance manifests" backlog item, first listed as a future
phase in the Phase 1 spec's Non-goals
(`docs/superpowers/specs/2026-09-03-phase1-component-store-drivers-inventory-validation-design.md`).

## Problem

The module already produces the raw inputs of an audit story, but nothing combines
them into one signed-off artifact:

- `Get-WindowsImageSnapshot` captures point-in-time inventory (and exports it as JSON).
- `Get-WindowsImageSecurityBaseline` evaluates a mounted image against the curated
  security baseline and returns a per-entry compliance verdict.
- `Get-WindowsImageServicingChain` classifies the image's SSU/LCU servicing state.

An auditor or release manager has to run three cmdlets against the same image and
manually stitch the results together with their own provenance (which tool, which
version, captured when, from which image). There is no single document that answers
"what is this image, was it policy-compliant when evaluated, and was its servicing
state consistent — as recorded by tool X at time T".

## Not the excluded generic-inventory non-goal

The Phase 1 spec explicitly excluded **"a generic
`Export-WindowsImageInventory`/`InventoryReport` cmdlet"** because
`Get-WindowsImageSnapshot -ExportPath` already dumps the full item inventory as
JSON, and `Export-WindowsImageSBOM` already serializes the component/package lists
for inventory tracking. That non-goal still stands.

The compliance manifest is a **different document** for a **different consumer**:

- It does **not** carry item lists. Package names, feature lists, software
  inventory, driver details and registry values stay in the snapshot JSON and the
  SBOM. The manifest carries only **aggregate counts** per category
  (`Inventory` section) plus pointers back to the image identity.
- What it *adds* is the **policy evaluation** layer (security-baseline verdicts,
  per entry and rolled up) and the **servicing-chain verdict** (SSU/LCU ordering),
  plus **provenance** (tool name/version, manifest generation timestamp, snapshot
  capture timestamp, image identity).

Rule of thumb: the snapshot/SBOM answers "what is in the image"; the manifest
answers "was that image compliant and consistent when we checked, and who/when/what
checked it".

## Goals

1. One cmdlet, `Export-WindowsImageComplianceManifest`, that takes an
   `ImageSnapshot` (from `Get-WindowsImageSnapshot`), optionally a
   `WindowsImageSecurityBaselineReport` (from `Get-WindowsImageSecurityBaseline`)
   and/or a `ServicingChainReport` (from `Get-WindowsImageServicingChain`), and
   writes a single JSON manifest file to `-DestinationPath`.
2. Documented, versioned JSON shape (`ManifestVersion`) so downstream audit
   tooling can consume it; round-trips through `JsonConvert` exactly like
   `ImageComparisonService.SaveSnapshot`/`LoadSnapshot`.
3. Provenance by construction: tool name + assembly version, manifest
   `GeneratedAt` (UTC), snapshot `CapturedAt` (UTC), image identity (name, index,
   source path, mount path).
4. A deterministic roll-up status (`OverallStatus`: `Unknown` when no baseline was
   supplied, `Compliant`/`NonCompliant` from the baseline report otherwise) so a
   pipeline can gate on one field.
5. All assembly/roll-up logic pure (`internal static`) and unit-testable with
   synthetic snapshot/report objects — no DISM, no hive files, no network. The
   cmdlet itself performs no image operations at all; it is a file writer like
   `Export-WindowsImageSBOM`.
6. Image-name mismatches between the snapshot and a supplied report are **not**
   silently dropped: the section is still included (the report is evidence) and a
   warning is emitted naming both images.

## Non-goals

- **Not a generic inventory export.** See the section above — the excluded
  Phase 1 non-goal remains excluded. No item lists in the manifest.
- **Signature/sealing.** The manifest is plain JSON, not cryptographically signed
  or hashed against the snapshot file. A later phase can add signing; recording a
  hash of mutable in-memory data would be misleading.
- **New evaluation logic.** The manifest assembles existing outputs; it does not
  re-evaluate the baseline, re-classify servicing packages or re-count inventory.
  Garbage in the reports is garbage in the manifest, faithfully.
- **Single-source-only enforcement.** The cmdlet does not require that the
  baseline/servicing reports came from the same image as the snapshot (only a
  warning on name mismatch) — audits legitimately combine evidence collected in
  separate passes.
- **No DISM, no mounting, no hive reads.** Read-only regarding images; the only
  I/O is writing the destination JSON file (and creating its parent directory).
- **No manifest "diff".** Comparing two manifests is out of scope; snapshots and
  `Compare-WindowsImage` already cover drift.

## Architecture

New files only — no changes to the consumed models/services
(`ImageComparisonModels.cs`, `SecurityBaselineModels.cs`, `ServicingChainModels.cs`,
`ImageComparisonService.cs`, `SecurityBaselineService.cs`, `ServicingChainService.cs`),
no manifest (psd1) or shipped-MAML changes by this phase (the orchestrator adds the
export and regenerates MAML). The cmdlet mirrors `ExportWindowsImageSBOMCmdlet`'s
conventions: `PSCmdlet` without `SupportsShouldProcess` (plain file write),
`GetUnresolvedProviderPathFromPSPath` for the destination, `LoggingService` +
`ModuleCallbacks.FromCmdlet` for output, `Newtonsoft.Json` for serialization.

### New files

**`src/Models/ComplianceManifestModels.cs`**

- `WindowsImageComplianceStatus` — enum: `Unknown`, `Compliant`, `NonCompliant`.
  Serialized as a string via `[JsonConverter(typeof(StringEnumConverter))]` so the
  JSON is audit-readable (no new dependency — it ships in Newtonsoft.Json).
- `WindowsImageComplianceManifest` — top-level document:
  - `ManifestVersion` (default `"1.0"`), `GeneratedAt` (UTC),
    `ToolName` (`"PSWindowsImageTools"`), `ToolVersion` (assembly version string).
  - `Image: ComplianceManifestImageIdentity`, `Inventory:
    ComplianceManifestInventorySummary`, `OverallStatus` (enum above).
  - `SecurityBaseline: ComplianceManifestBaselineSection?` — null when the report
    was not supplied.
  - `ServicingChain: ComplianceManifestServicingSection?` — null when the report
    was not supplied.
  - `HasSecurityBaseline` / `HasServicingChain` (computed convenience flags).
- `ComplianceManifestImageIdentity` — `ImageName`, `ImageIndex`, `ImagePath`,
  `MountPath`, `CapturedAt` (copied from the snapshot).
- `ComplianceManifestInventorySummary` — `Packages`, `Features`, `Capabilities`,
  `AppxPackages`, `Software`, `Drivers`, `Registry`, `TotalItems` (counts only).
- `ComplianceManifestBaselineSection` — `ImageName`, `MountPath`, `IsCompliant`,
  `TotalEntries`, `CompliantCount`, `NonCompliantCount`, `NotPresentCount`,
  `Entries: List<ComplianceManifestBaselineEntry>`.
- `ComplianceManifestBaselineEntry` — flattened, string-typed projection of one
  `WindowsImageSecurityBaselineObservation`: `Hive`, `KeyPath`, `ValueName`,
  `ExpectedValue`, `ValueType` (e.g. `DWord`), `Rationale`, `State` (e.g.
  `Compliant`), `ObservedValue`, `ObservedValueType`.
- `ComplianceManifestServicingSection` — `ImageName`, `ImagePath`, `GeneratedAt`,
  `PackageCount`, `ServicingStackUpdate` / `CumulativeUpdate` (the packages'
  `ToString()`, null when unclassified), `OrderingValid`, `Issues`.

**`src/Services/ComplianceManifestService.cs`** (`ModuleCallbacks`-aware, mirroring
`ImageComparisonService`)

- `private const string ServiceName = "ComplianceManifestService"`.
- `public const string CurrentManifestVersion = "1.0"`.
- `public WindowsImageComplianceManifest BuildManifest(ImageSnapshot snapshot,
  WindowsImageSecurityBaselineReport? baselineReport = null,
  ServicingChainReport? servicingChainReport = null)` — orchestrates the pure
  builders below; throws `ArgumentNullException` on null snapshot; warns (never
  throws) on image-name mismatch between the snapshot and a supplied report.
- `internal static ComplianceManifestImageIdentity BuildImageIdentity(ImageSnapshot
  snapshot)` — pure.
- `internal static ComplianceManifestInventorySummary
  BuildInventorySummary(ImageSnapshot snapshot)` — pure.
- `internal static ComplianceManifestBaselineSection
  BuildBaselineSection(WindowsImageSecurityBaselineReport report)` — pure; maps
  each observation through `AppendBaselineEntry`.
- `internal static ComplianceManifestBaselineEntry AppendBaselineEntry(
  WindowsImageSecurityBaselineObservation observation)` — pure projection
  (enums → `ToString()`).
- `internal static ComplianceManifestServicingSection
  BuildServicingSection(ServicingChainReport report)` — pure.
- `internal static WindowsImageComplianceStatus ResolveOverallStatus(
  WindowsImageSecurityBaselineReport? baselineReport)` — pure; null → `Unknown`,
  else `IsCompliant ? Compliant : NonCompliant`.
- `internal static string ResolveToolVersion()` — assembly informational version
  via reflection; `"unknown"` if unavailable (keeps manifest generation total).
- `public static void SaveManifest(WindowsImageComplianceManifest manifest,
  string manifestPath)` — `JsonConvert.SerializeObject(..., Formatting.Indented)`
  + `File.WriteAllText` (mirrors `ImageComparisonService.SaveSnapshot`).
- `public static WindowsImageComplianceManifest LoadManifest(string
  manifestPath)` — `FileNotFoundException` when missing, null-deserialize →
  `InvalidOperationException` (mirrors `ImageComparisonService.LoadSnapshot`).

**`src/Cmdlets/ComplianceManifestCmdlet.cs`**

- `ExportWindowsImageComplianceManifestCmdlet : PSCmdlet`,
  `[Cmdlet(VerbsData.Export, "WindowsImageComplianceManifest")]`,
  `[OutputType(typeof(WindowsImageComplianceManifest))]`. No
  `SupportsShouldProcess` (mirrors `Export-WindowsImageSBOM`).
- Parameters:
  - `-Snapshot <ImageSnapshot>` (Mandatory, Position 0, pipeline by value).
  - `-BaselineReport <WindowsImageSecurityBaselineReport>` (optional,
    `[ValidateNotNull]` so an explicit `$null` is rejected).
  - `-ServicingChainReport <ServicingChainReport>` (optional, same guard).
  - `-DestinationPath <string>` (Mandatory, Position 1, PSPath-resolved).
  - `-Force` (overwrite; file-exists without `-Force` → non-terminating
    `WriteError` with `FileExists` / `ResourceExists`, mirroring
    `Export-UnattendXMLConfiguration`).
- `ProcessRecord`: resolve path, enforce `-Force`, create the parent directory if
  missing, build + save, `WriteVerbose` the path, emit the manifest object.

**`docs/help/Export-WindowsImageComplianceManifest.md`** — PlatyPS source (copy of
the `Export-WindowsImageSBOM.md` template; front matter
`external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name:
PSWindowsImageTools`), documenting every parameter including `-ProgressAction`.

## Manifest JSON shape (`ManifestVersion = "1.0"`)

```json
{
  "ManifestVersion": "1.0",
  "GeneratedAt": "2026-09-04T12:00:00.0000000Z",
  "ToolName": "PSWindowsImageTools",
  "ToolVersion": "1.0.0.0",
  "Image": {
    "ImageName": "Windows 11 Pro",
    "ImageIndex": 1,
    "ImagePath": "C:\\media\\install.wim",
    "MountPath": "C:\\mount\\win11",
    "CapturedAt": "2026-09-04T11:30:00.0000000Z"
  },
  "Inventory": {
    "Packages": 4,
    "Features": 12,
    "Capabilities": 5,
    "AppxPackages": 24,
    "Software": 31,
    "Drivers": 9,
    "Registry": 1420,
    "TotalItems": 1505
  },
  "OverallStatus": "Compliant",
  "SecurityBaseline": {
    "ImageName": "Windows 11 Pro",
    "MountPath": "C:\\mount\\win11",
    "IsCompliant": true,
    "TotalEntries": 24,
    "CompliantCount": 24,
    "NonCompliantCount": 0,
    "NotPresentCount": 0,
    "Entries": [
      {
        "Hive": "HKLM\\SOFTWARE",
        "KeyPath": "Microsoft\\Windows\\CurrentVersion\\Policies\\System",
        "ValueName": "EnableLUA",
        "ExpectedValue": "1",
        "ValueType": "DWord",
        "Rationale": "UAC must stay enabled",
        "State": "Compliant",
        "ObservedValue": "1",
        "ObservedValueType": "RegDword"
      }
    ]
  },
  "ServicingChain": {
    "ImageName": "Windows 11 Pro",
    "ImagePath": "C:\\media\\install.wim",
    "GeneratedAt": "2026-09-04T11:45:00.0000000Z",
    "PackageCount": 3,
    "ServicingStackUpdate": "ServicingStackUpdate (Verified): SSU [22621.1000]",
    "CumulativeUpdate": "CumulativeUpdate (Verified): LCU [22621.3400]",
    "OrderingValid": true,
    "Issues": []
  },
  "HasSecurityBaseline": true,
  "HasServicingChain": true
}
```

`SecurityBaseline` and `ServicingChain` are omitted (`null`) when the
corresponding report parameter is not supplied; `OverallStatus` is then `"Unknown"`.

## Data Flow

```
Get-WindowsImageSnapshot ──► ImageSnapshot ─────────────┐
Get-WindowsImageSecurityBaseline ─► BaselineReport? ────┤
Get-WindowsImageServicingChain ──► ServicingChainReport?┤
                                                        ▼
              Export-WindowsImageComplianceManifest
                └─► ComplianceManifestService.BuildManifest
                      ├─► BuildImageIdentity / BuildInventorySummary
                      ├─► BuildBaselineSection (per-observation AppendBaselineEntry)
                      ├─► BuildServicingSection
                      └─► ResolveOverallStatus / ResolveToolVersion
                └─► ComplianceManifestService.SaveManifest ──► <DestinationPath>.json
                └─► WindowsImageComplianceManifest (also emitted to the pipeline)
```

## Error Handling

- `BuildManifest` throws `ArgumentNullException` for a null snapshot (programming
  error) and never throws for null/missing reports (optional by design).
- Image-name mismatches (snapshot vs baseline/servicing report) produce a
  `_callbacks.Warning` and the section is still embedded — evidence is preserved
  and flagged, not dropped.
- The cmdlet: existing destination file without `-Force` → non-terminating
  `FileExists` error and no write; missing parent directory → created; any
  build/save exception → non-terminating `WriteError`
  (`ExportComplianceManifestFailed`), mirroring `Export-UnattendXMLConfiguration`'s
  handler shape.
- `SaveManifest`/`LoadManifest` mirror `SaveSnapshot`/`LoadSnapshot` semantics:
  `File.WriteAllText` overwrites silently (the cmdlet layer owns the
  exists-check), `LoadManifest` throws `FileNotFoundException` /
  `InvalidOperationException` on missing/empty files.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/ComplianceManifestServiceTests.cs`)**
  — all pure except the round-trip test; synthetic objects only, no DISM, no
  hives, no network:
  - Manifest with snapshot only: identity/summary/provenance populated from the
    snapshot, both sections null, `OverallStatus = Unknown`, tool version
    non-empty.
  - Baseline section mapping: counts (compliant/non-compliant/not-present),
    `IsCompliant`, per-entry string projections (`State`, `ValueType`,
    `ObservedValue*`), entry order preserved.
  - Servicing section mapping: package count, SSU/LCU summaries (null when the
    report has none), `OrderingValid`, `Issues` carried through.
  - `OverallStatus` rules: null → Unknown; compliant → Compliant;
    non-compliant/not-present entries → NonCompliant.
  - Image-name mismatch: sections still built + warning recorded (recording
    `ModuleCallbacks`).
  - JSON round-trip: `SaveManifest` → `LoadManifest` in a temp directory
    (`PSWIT-Tests-` fixture pattern from `ImageComparisonServiceTests.cs`)
    preserves the full document including `OverallStatus` as a string and both
    optional sections.
- **Help guardrail** (`Scripts/verify-help.ps1`): the new PlatyPS markdown keeps
  checks 1–3 green once the orchestrator exports the cmdlet; check 4 (shipped
  MAML) is regenerated by the orchestrator.
- **Integration**: none — this phase never touches DISM or mounted images. The
  existing DISM `OpenOfflineSession` servicing limitation is irrelevant here by
  construction.

## Risks

- **Report/snapshot skew.** The baseline and servicing reports can be captured at
  a different time (or from a different image) than the snapshot; the manifest
  records each report's own `GeneratedAt`/`MountPath` and warns on name mismatch,
  but cannot prove the evidence is contemporaneous. Documented, not solved.
- **Baseline report without a servicing chain (or vice versa)** produces a
  partially-populated manifest; `OverallStatus` is explicitly defined only over
  the baseline, so consumers never guess at semantics.
- **Assembly version is not the module version.** The psd1 `ModuleVersion` lives
  outside the DLL; the manifest records the assembly version. Acceptable for
  provenance (the psd1 is not readable from the assembly), documented in help.
- **No new cmdlet export by this phase.** Until the orchestrator adds the export
  to the psd1 (and regenerates the MAML), `verify-help.ps1` will not see the
  cmdlet; the markdown is written so checks 1–3 pass immediately after export.
