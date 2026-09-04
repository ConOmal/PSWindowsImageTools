# Phase 1 Extensions: Component Store, Drivers, Inventory/SBOM, Validation — Design

**Date:** 2026-09-03
**Status:** Approved for planning

## Problem

An external capability wishlist proposed extending PSWindowsImageTools with
~90 new cmdlets across ~25 subsystems, modeled as a script-based module
(`Public/Private/Common/Subsystems` folders of loose `.ps1` files, a `psm1`
autoloader, `PSImage`-prefixed cmdlet names like `Analyze-PSWindowsImage`).
That structure does not match this repository: PSWindowsImageTools is a
compiled C# binary module (`src/Cmdlets/*.cs`, `src/Services/*.cs`,
`src/Models/*.cs` → `PSWindowsImageTools.dll`), with cmdlets named
`Verb-WindowsImage<Noun>` (e.g. `Get-WindowsImageList`, `Compare-WindowsImage`,
`Reset-WindowsImageBase`).

The full wishlist is too large for one spec and duplicates existing
functionality in several places (an inventory/snapshot/compare pipeline
already exists via `Get-WindowsImageSnapshot` / `Compare-WindowsImage`).

This spec scopes **Phase 1** — the four subsystems that build directly on
services already in the codebase — re-expressed as C# cmdlets/services/models
matching this repo's conventions. Later phases (Registry drift, WinRE
intelligence, Security baselines, WinGet provisioning, etc.) are out of scope
here and will get their own spec/plan cycles.

## Goals

1. Report on WinSxS component-store size and package health, and perform
   real component cleanup (`/StartComponentCleanup`, optional `/ResetBase`)
   against a mounted image.
2. Enumerate, remove, compare, and export drivers already present inside a
   mounted (offline) image — distinct from the existing `INFDriverService`,
   which only scans loose `.inf` files on disk before injection.
3. Extend the existing inventory snapshot format with driver data, and add an
   SBOM export built from real captured snapshot data.
4. Provide a composite offline-image health check (corruption, missing
   hives, orphaned/superseded packages, driver issues) as a single report.

## Non-goals

- Any subsystem beyond these four (Registry drift, Security baselines,
  WinRE intelligence beyond what exists, Unattend, WinGet, Boot image,
  OOBE, Scheduled Tasks/Services config, Dynamic Update, Edition servicing,
  Reserved Storage, Recovery/checkpoint, Compliance manifests, Capability
  repository). These remain a backlog for future phases.
- A generic `Export-WindowsImageInventory`/`InventoryReport` cmdlet —
  `Get-WindowsImageSnapshot -ExportPath` already exports the same data as
  JSON.
- A generic `Test-WindowsImage` pass/fail cmdlet — `Invoke-WindowsImageHealthCheck`
  already exposes `OverallHealth` for this purpose.
- Separate `Analyze/Optimize/Test-WindowsImageDriverStore` cmdlets — the
  signal they'd provide (unsigned drivers, duplicate OEM entries) is exposed
  as fields on `Get-WindowsImageDriver`/`Compare-WindowsImageDriver` output
  instead.
- A standalone `Repair-WindowsImage` cmdlet — "repair" with no specific
  target operation isn't a real DISM action; corruption repair is covered by
  `Invoke-WindowsImageHealthCheck`'s use of `DismApi.RestoreImageHealth`
  reporting, not a separate mutating cmdlet in this phase.
- Literal JSON Schema files / a hand-rolled schema validator script (as the
  wishlist proposed) — this module's convention is typed C# model objects
  returned via `WriteObject`; JSON is whatever the caller gets from piping to
  `ConvertTo-Json`, not something a cmdlet constructs directly.

## Architecture

All additions follow the existing service + cmdlet + model split (a
`Services/*Service.cs` doing the work, a thin `Cmdlets/*Cmdlet.cs` wrapping it
in PowerShell semantics via `PSCmdlet`, `Models/*.cs` for new output types),
using `LoggingService` for verbose/progress/error output and
`ModuleCallbacks`/`WindowsImageService.ForCmdlet(this)` for DISM session
lifecycle, matching `ResetWindowsImageBaseCmdlet` and the
`Get-WindowsImageSnapshot`/`Compare-WindowsImage` pair.

Confirmed available APIs this design relies on (verified via
`Microsoft.Dism.dll` reflection against the module's own binary):
`DismApi.GetDrivers`, `DismApi.RemoveDriver`, `DismApi.CheckImageHealth`,
`DismApi.RestoreImageHealth`. `DismDriverPackage` exposes `PublishedName`,
`OriginalFileName`, `ProviderName`, `ClassName`, `ClassDescription`, `Date`,
`Version`, `BootCritical`, `InBox`, `DriverSignature`, `CatalogFile`.

### 1. Component Store Intelligence

**New:** `src/Models/ComponentStoreModels.cs`, `src/Services/ComponentStoreService.cs`

`ComponentStoreReport`:
- `ImageName`, `ImagePath`, `MountPath`, `GeneratedAt`
- `WinSxSSizeMB` (double) — recursive size of `<mount>\Windows\WinSxS`
- `TotalPackages`, `InstalledPackages`, `SupersededPackages`, `PendingPackages`
  (int) — classified from `DismPackage.PackageState` returned by the existing
  `IWindowsImageService.GetPackages`
- `SupersededPackageNames: List<string>`
- `Issues: List<string>`

`ComponentStoreService`:
- `Analyze(MountedWindowsImage, IWindowsImageService) -> ComponentStoreReport`
  — read-only; no new DISM surface needed.
- `Cleanup(MountedWindowsImage, resetBase: bool, PSCmdlet) -> ComponentStoreCleanupResult`
  (`Before`, `After`: `ComponentStoreReport`; `ExitCode`; `Duration`) — shells
  to `dism.exe /Image:"<mount>" /Cleanup-Image /StartComponentCleanup [/ResetBase]`
  via the existing `ProcessMonitoringService.ExecuteProcessWithMonitoring`,
  the same pattern `OptionalComponentService` already uses for DISM verbs the
  managed `Microsoft.Dism` wrapper doesn't cover.

**Cmdlets:**
- `Get-WindowsImageComponentStore` (`VerbsCommon.Get`) — pipeline of
  `MountedWindowsImage[]`, outputs `ComponentStoreReport[]`.
- `Optimize-WindowsImageComponentStore` (`VerbsCommon.Optimize`,
  `SupportsShouldProcess`) — `-ResetBase` switch, `-ContinueOnError` switch
  (matching `Reset-WindowsImageBase`'s existing parameter), outputs
  `ComponentStoreCleanupResult[]`.

### 2. Driver Lifecycle Management

**New:** `src/Models/DriverModels.cs`, `src/Services/WindowsImageDriverService.cs`

Extends `IWindowsImageService` (and its `WindowsImageService` implementation)
with two members, wrapping the confirmed `DismApi` calls the same way
`AddDriversFromDirectory` already wraps `DismApi.AddDriversEx`:
```csharp
List<Microsoft.Dism.DismDriverPackage> GetDrivers(string mountPath, bool allDrivers = false);
void RemoveDriver(string mountPath, string publishedName, Action<int, string>? progressCallback = null);
```

`WindowsImageDriverInfo` model: `PublishedName`, `OriginalFileName`,
`ProviderName`, `ClassName`, `ClassDescription`, `Date`, `Version`,
`BootCritical`, `InBox`, `ImageName`, `MountPath`.

`DriverComparisonResult` model (mirrors `CategoryDifference` from
`ImageComparisonModels.cs`): `ReferenceName`, `CurrentName`, `Added`,
`Removed`, `Superseded` (same `ProviderName`+`OriginalFileName`, higher
`Version` in current), `DuplicateOem` (same `ProviderName`+`OriginalFileName`,
different `PublishedName`) — each `List<WindowsImageDriverInfo>`.

`WindowsImageDriverService`:
- `GetDrivers(MountedWindowsImage, IWindowsImageService, bool all) -> List<WindowsImageDriverInfo>`
- `Compare(List<WindowsImageDriverInfo> reference, List<WindowsImageDriverInfo> current) -> DriverComparisonResult`
  — pure in-memory diff, unit-testable without DISM (same shape as
  `ImageComparisonService.CompareCategory`).
- `Export(WindowsImageDriverInfo, DirectoryInfo destination)` — copies the
  driver's `Windows\System32\DriverStore\FileRepository\<folder>` tree.

**Cmdlets:**
- `Get-WindowsImageDriver` (`VerbsCommon.Get`) — `-All` switch to include
  inbox drivers (default: third-party only).
- `Remove-WindowsImageDriver` (`VerbsCommon.Remove`, `SupportsShouldProcess`)
  — accepts `WindowsImageDriverInfo[]` from the pipeline or `-PublishedName`.
- `Compare-WindowsImageDriver` (`VerbsData.Compare`) — two `MountedWindowsImage`
  (reference/current), outputs `DriverComparisonResult`.
- `Export-WindowsImageDriver` (`VerbsData.Export`) — pipeline of
  `WindowsImageDriverInfo[]`, `-DestinationPath`.

### 3. Inventory & SBOM

**Change:** `ImageSnapshot` (`src/Models/ImageComparisonModels.cs`) gains
`Drivers: List<SnapshotItem>`, populated in
`ImageComparisonService.CaptureSnapshot` via the new
`WindowsImageDriverService.GetDrivers` (mirrors how `Packages`/`Features`
are already populated there). `TotalItems` and the existing
`Compare`/`CompareCategory` logic pick this up automatically since they
already iterate all `SnapshotItem` lists generically per category name.

**New:** `src/Models/SbomModels.cs` (`SbomReport`: `WindowsVersion`,
`ImageName`, `ImagePath`, `GeneratedAt`, `Packages`, `Drivers`, `Features`,
`Capabilities`, `Applications` — each `List<SnapshotItem>`, reusing the
existing `Software`→`Applications` naming from `ImageSnapshot`).

**Cmdlet:** `Export-WindowsImageSBOM` (`VerbsData.Export`) — accepts
`ImageSnapshot[]` from the pipeline (from `Get-WindowsImageSnapshot`) or
`-SnapshotPath` (loaded via the existing
`ImageComparisonService.LoadSnapshot`), `-DestinationPath`, writes one
`SbomReport` JSON file per snapshot via `JsonConvert.SerializeObject`
(matching `ImageComparisonService.SaveSnapshot`'s existing pattern) and also
returns the `SbomReport[]` objects.

### 4. Validation

**New:** `src/Models/HealthCheckModels.cs`, `src/Services/WindowsImageHealthCheckService.cs`

`HealthCheckReport`: `ImageName`, `ImagePath`, `MountPath`, `GeneratedAt`,
`OverallHealth` (`enum HealthStatus { Healthy, Warning, Unhealthy }`),
`Findings: List<HealthFinding>` (`Category`, `Severity`, `Message`) where
`Category` is one of: `Corruption`, `MissingRegistryHive`,
`OrphanedOrSupersededPackage`, `DriverIssue`, `PendingOperation`.

`WindowsImageHealthCheckService.Run(MountedWindowsImage, IWindowsImageService, PSCmdlet) -> HealthCheckReport`
composes:
- `DismApi.CheckImageHealth` (confirmed available) for corruption →
  `Corruption` findings; `-RestoreHealth` switch on the cmdlet triggers
  `DismApi.RestoreImageHealth` and records the outcome as a finding rather
  than throwing.
- `RegistryHiveReader` hive-file presence checks (SOFTWARE/SYSTEM under
  `Windows\System32\config`) → `MissingRegistryHive` findings.
- `ComponentStoreService.Analyze` superseded-package count →
  `OrphanedOrSupersededPackage` findings.
- `WindowsImageDriverService.GetDrivers` findings where
  `DriverSignature == DismDriverSignature.Unsigned`, plus duplicate-OEM
  count → `DriverIssue` findings.

`OverallHealth` is `Unhealthy` if any `Corruption` finding exists,
`Warning` if any other category has findings, else `Healthy`.

**Cmdlet:** `Invoke-WindowsImageHealthCheck` (`VerbsLifecycle.Invoke`) —
pipeline of `MountedWindowsImage[]`, `-RestoreHealth` switch,
`-ContinueOnError` switch, outputs `HealthCheckReport[]`.

## Data Flow

```
Mount-WindowsImageList
        │
        ├─► Get-WindowsImageComponentStore ──► ComponentStoreReport
        │         │
        │         └─► Optimize-WindowsImageComponentStore ──► ComponentStoreCleanupResult
        │
        ├─► Get-WindowsImageDriver ──► WindowsImageDriverInfo[]
        │         │                        │
        │         │                        ├─► Remove-WindowsImageDriver
        │         │                        └─► Export-WindowsImageDriver
        │         └─► Compare-WindowsImageDriver ──► DriverComparisonResult
        │
        ├─► Get-WindowsImageSnapshot (extended with Drivers) ──► ImageSnapshot
        │         │
        │         └─► Export-WindowsImageSBOM ──► SbomReport
        │
        └─► Invoke-WindowsImageHealthCheck ──► HealthCheckReport
                  (internally calls ComponentStoreService + WindowsImageDriverService)
```

## Error Handling

Matches existing convention throughout:
- Multi-image cmdlets accept `-ContinueOnError`; per-image failures are
  caught, logged via `LoggingService.WriteError`, and recorded on the result
  object rather than aborting the batch, unless `-ContinueOnError` is absent
  (mirrors `Reset-WindowsImageBase`).
- Setup failures (null `MountPath`, no images supplied) use
  `ThrowTerminatingError` / `LoggingService.WriteWarning` + early return,
  matching `CompareWindowsImageCmdlet` and `GetWindowsImageSnapshotCmdlet`.
- Mutating cmdlets (`Optimize-WindowsImageComponentStore`,
  `Remove-WindowsImageDriver`) implement `SupportsShouldProcess` and call
  `ShouldProcess` before making changes.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/`)**: pure-logic pieces
  that don't need a real mounted image or DISM session — package
  classification math in `ComponentStoreService.Analyze`, the driver diff
  logic in `WindowsImageDriverService.Compare` (same style as
  `ImageComparisonServiceTests.cs`), `SbomReport` shape mapping from a
  hand-built `ImageSnapshot`, and `HealthStatus` roll-up logic given
  synthetic `HealthFinding` lists.
- **Integration (Pester, `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`)**:
  end-to-end cases against a real mounted image for
  `Get-WindowsImageComponentStore`, `Optimize-WindowsImageComponentStore`,
  `Get-WindowsImageDriver`/`Compare-WindowsImageDriver`,
  `Export-WindowsImageSBOM`, and `Invoke-WindowsImageHealthCheck`.

## Risks

- `Optimize-WindowsImageComponentStore -ResetBase` shells out to `dism.exe`
  rather than a managed API (none exists in `Microsoft.Dism`) — same
  reliability tradeoff the module already accepts for `OptionalComponentService`.
  `/StartComponentCleanup /ResetBase` can take a long time on a large image;
  `ProcessMonitoringService`'s existing timeout parameter must be set
  generously (recommend 60+ minutes, configurable).
  Component cleanup makes prior updates non-removable; document this in
  cmdlet help.
- `Remove-WindowsImageDriver` is destructive and image-specific; no rollback
  beyond re-adding via `Add-INFDriverList`-style injection if the caller kept
  the original files.
