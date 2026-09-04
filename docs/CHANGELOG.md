# Changelog

All notable changes to PSWindowsImageTools will be documented in this file.

## [2026.09.04] - Phase 1: Drivers, Component Store, Health Check & SBOM

### Added
- **8 new cmdlets** (62 total exported):
  - Driver management: `Get-WindowsImageDriver`, `Add`/`Remove` (via `Remove-WindowsImageDriver`),
    `Compare-WindowsImageDriver`, `Export-WindowsImageDriver` (per-image and mounted-image driver
    inventory, INF parsing, add/remove with inbox-filter)
  - Component store: `Get-WindowsImageComponentStore` (WinSxS analysis: superseded packages, size)
    and `Optimize-WindowsImageComponentStore` (cleanup with progress)
  - `Invoke-WindowsImageHealthCheck` (mounted-image health report: drivers, packages, component
    store, with severity rollup)
  - `Export-WindowsImageSBOM` (software bill of materials JSON from a snapshot)
- **17-test integration suite** (`tests/integration/`): real WIM mount/unmount, query, export,
  recipe, driver, component-store and SBOM round-trips, pinned to Pester 5
- **PSGallery release pipeline** (`.github/workflows/release.yml`): tag (`v*`) or manual dispatch
  with `dry_run` → build + unit tests + manifest guard (≥50 exports) → `Publish-Module` using the
  `PSGALLERY_API_KEY` secret

### Fixed
- `Dismount-WindowsImageList`: `Save`/`Discard`/`Append` now bind correctly when images arrive via
  the pipeline (parameters were unreachable in their own parameter sets)
- `Dismount-WindowsImageList`: unmount now goes through the Microsoft.Dism managed API; the native
  path always failed with `0xC142010C` (`CWimImage::Save`) on this machine class

### Changed
- **Help-drift guardrail**: `Scripts/verify-help.ps1` (markdown-per-cmdlet, live-parameter coverage,
  `New-ExternalHelp` round-trip, shipped-MAML synopsis coverage) runs in CI; help authored for all
  62 cmdlets and MAML regenerated from source
- **v2025.09.04.1 published to PSGallery** (54-cmdlet state of main) — first public release; the
  module is discoverable, installable and loads cleanly (`Find-Module`/`Save-Module`/`Import-Module`)

## [2025.09.04] - Phase 2-4: Architecture Refactor & Feature Completion

### Added
- **Full cmdlet help**: PlatyPS-generated markdown (`docs/help/`) and compiled MAML help shipped in
  the module — `Get-Help` works for all exported cmdlets (synopsis, description, examples, parameters)
- **Image diffing**: `Get-WindowsImageSnapshot` (JSON export) + `Compare-WindowsImage`
  (added/removed/changed per category, from mounted images or snapshot files)
- **14 new cmdlets** (51 total exported):
  - Package/feature/capability management: `Get-WindowsImagePackageList`, `Get-WindowsImageFeatureList`,
    `Add-WindowsImagePackage`, `Enable-WindowsImageFeature`, `Disable-WindowsImageFeature`,
    `Add-WindowsImageCapability`, `Remove-WindowsImageCapability`
  - Recipe-driven builds: `New-WindowsImageRecipe`, `Test-WindowsImageRecipe`, `Invoke-WindowsImageRecipe`
  - `Export-WindowsImage` (native WIM API, index-by-name, boot flag, compression, rename)
  - `New-WindowsImageISO` (oscdimg via installed ADK)
  - `Get-MountedWindowsImage` (cross-session mount registry with -Prune)
  - `Update-WindowsImageOnline` (latest-KB discovery → catalog download → install)
  - Image diffing: `Get-WindowsImageSnapshot` (JSON export) + `Compare-WindowsImage` (added/removed/changed)
- **BuildRecipe executor**: declarative JSON recipes driving image builds — AppX removal, file copy,
  wallpapers, feature enablement, driver/update/FoD integration, offline registry writes
- ISO input support in `Get-WindowsImageList` (mounts ISO, locates install.wim/esd)
- `ModuleCallbacks` decoupling layer so services are unit-testable without PSCmdlet
- 93 unit tests (up from 0): parsers, .reg parsing, registry reads on real hives, recipe logic

### Changed
- **Unified DISM layer**: `DismService` + `NativeDismService` → single `WindowsImageService` with one
  API lifecycle; mount/unmount now throw with real DISM errors instead of returning bool
- **Registry services consolidated 6 → 2 + parser**: new `RegistryHiveReader` (typed access, no
  reflection); dead `RegistryPackageService`/`OfflineRegistryService` deleted
- Implemented the full DISM write API (the old "HONEST ASSESSMENT" stubs were wrong — ManagedDism
  3.3.12 has AddPackage/EnableFeature/Capabilities/AppX/Drivers)
- `WimExportService`: finished all TODOs (index-by-name, image count, boot flag, name/description)

### Fixed
- Mount/unmount failures now carry the real DISM HRESULT/message instead of generic strings
- `throw ex` stack-trace loss on force-unmount failures
- ISO input no longer throws NotImplementedException

## [2025.09.03] - Phase 1: Hygiene & Correctness

### Fixed
- **Critical manifest bug**: `Get-RegistryHiveOnDemand` was silently not exported (manifest listed the
  non-existent `Read-RegistryHiveOnDemand`); phantom `Install-WindowsUpdateFile` export removed
- Package restore failure (`System.Runtime.CompilerServices.Unsafe` downgrade) resolved
- Unmount flakiness: removed all `GC.Collect()`/`Thread.Sleep()` handle workarounds — verified that
  `RegistryHiveOnDemand` holds no file handle after use, making the hacks unnecessary
- Stack-trace preservation on force-unmount failure (`throw ex` → `throw`)

### Changed
- Solution renamed to `PSWindowsImageTools.sln` (was `PSWindowsUpdateTools.sln`)
- `ProjectUri` corrected to the PSWindowsImageTools repository
- README license statement corrected to GPL-3.0 (matches LICENSE and module manifest)
- Removed references to removed "Windows Image Database" cmdlets from README and all guides
- Removed unused `System.Text.Json` dependency (Newtonsoft.Json is the single JSON stack)
- Removed debug scaffolding (mount-content listings, verbose file enumeration)

### Added
- xUnit test project (`tests/PSWindowsImageTools.Tests`) with 73 tests: format parsers, `.reg`
  parsing, registry operation model, BuildRecipe JSON round-trip
- GitHub Actions CI workflow (build + test on windows-latest)

## [2.0.0] - 2025-01-03

### Added
- **Complete ADK Management Suite**
  - `Get-ADKInstallation` - Detect installed Windows ADK versions
  - `Install-ADK` - Download and install latest ADK with automatic patch detection
  - `Uninstall-ADK` - Remove ADK installations
  - `Get-WinPEOptionalComponent` - Discover available WinPE components
  - `Add-WinPEOptionalComponent` - Install components into boot images

- **Enhanced Windows Update Integration**
  - Dynamic parsing of Microsoft's ADK download pages
  - Automatic patch detection and installation (ZIP files with MSP files)
  - Enhanced process monitoring with command line display and timeouts
  - Robust error handling and fallback mechanisms

- **Advanced Image Customization**
  - `Get-INFDriverList` - Parse INF files and extract driver information
  - `Add-INFDriverList` - Install drivers into mounted images
  - `Remove-AppXProvisionedPackageList` - Remove AppX packages with regex filtering
  - `Get-RegistryOperationList` - Parse registry files
  - `Write-RegistryOperationList` - Apply registry operations
  - `Get-AutopilotConfiguration` - Load Autopilot JSON configuration
  - `Set-AutopilotConfiguration` - Modify Autopilot settings
  - `Export-AutopilotConfiguration` - Save Autopilot configuration
  - `Install-AutopilotConfiguration` - Apply to mounted images
  - `New-AutopilotConfiguration` - Create new configuration

- **Enterprise Features**
  - Windows release information and KB correlation
  - Patch Tuesday automation and scheduling
  - Comprehensive logging and progress reporting
  - SQLite database for operation tracking and inventory

### Changed
- **Consolidated Duplicate Cmdlets**
  - Merged `Install-WindowsUpdateFile` and `Install-WindowsImageUpdate` into unified `Install-WindowsImageUpdate`
  - Enhanced with dual parameter sets for both file-based and pipeline workflows
  - Improved pipeline integration and object flow

- **Enhanced Process Monitoring**
  - Removed async/await patterns for PowerShell compatibility
  - Added command line transparency and runtime tracking
  - Implemented timeout management (60 min ADK, 30 min uninstall, 15 min components)
  - Added graceful process termination on timeout

- **Improved Documentation**
  - Complete rewrite of README.md with comprehensive feature overview
  - Updated cmdlet reference with all new cmdlets and examples
  - Enhanced Windows Update Catalog guide with enterprise workflows
  - New Image Customization guide with advanced techniques

### Removed
- Excessive sample and debug scripts (cleaned up Scripts folder)
- Duplicate cmdlet exports from module manifest
- Legacy async/await patterns that caused PowerShell compatibility issues

### Fixed
- All compilation errors and warnings resolved
- String.Contains overload issues for .NET Standard 2.0 compatibility
- Registry namespace conflicts
- Model property references for proper compilation
- Null reference warnings and unreachable code

## [1.0.0] - Previous Version

### Initial Release
- Basic Windows image management
- Windows Update Catalog integration
- Database operations
- Core image mounting and dismounting functionality

---

## Version Numbering

This project follows [Semantic Versioning](https://semver.org/):
- **MAJOR** version for incompatible API changes
- **MINOR** version for backwards-compatible functionality additions
- **PATCH** version for backwards-compatible bug fixes

## Contributing

See [Contributing Guidelines](../CONTRIBUTING.md) for information on how to contribute to this project.