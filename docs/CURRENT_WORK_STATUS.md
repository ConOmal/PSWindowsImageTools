# Current Work Status

## Completed

### Phase 0 — Baseline & Safety Net
- Solution builds clean (0 warnings); package downgrade fixed (`System.Runtime.CompilerServices.Unsafe` 6.1.2)
- `tests/PSWindowsImageTools.Tests` (xUnit): 93 tests — format parsers, `.reg` parsing, registry
  operation model, BuildRecipe/recipe round-trips, RegistryHiveReader (real hives)
- GitHub Actions CI (`.github/workflows/ci.yml`): build + test on windows-latest

### Phase 1 — Hygiene & Correctness
- **Manifest bug fixed**: `Get-RegistryHiveOnDemand` actually exported now (was listed as the
  non-existent `Read-RegistryHiveOnDemand`); phantom `Install-WindowsUpdateFile` removed
- All GC.Collect/Thread.Sleep handle hacks removed — verified `RegistryHiveOnDemand` holds no
  file handles (parses into memory)
- Identity aligned (sln renamed, ProjectUri, README = GPL-3.0); stale Windows-Image-Database doc
  references purged; unused System.Text.Json removed; debug scaffolding removed

### Phase 2 — Architecture Refactor
- **`ModuleCallbacks`** infrastructure (src/Services/ModuleCallbacks.cs): verbose/warning/error/
  progress callbacks; services no longer need PSCmdlet (new services are cmdlet-free + testable)
- **DISM consolidated 2→1**: `WindowsImageService` (src/Services/WindowsImageService.cs, interface
  `Abstractions/IWindowsImageService.cs`) — managed DISM queries + native mount/unmount with
  progress + export; single Initialize/Shutdown; mount/unmount THROW with real DISM errors
  (bool returns eliminated); DismService/NativeDismService deleted
- **Registry consolidated 6→2+parser**: `RegistryHiveReader` (interface
  `Abstractions/IRegistryHiveReader.cs`) for reads via RegistryHiveOnDemand with typed access
  (no reflection); RegistryPackageService + OfflineRegistryService (dead) deleted;
  RegistryApplicationService + NativeRegistryService remain as the write path

### Phase 3 — Finish Half-Built Features
- **The "HONEST ASSESSMENT" stubs were wrong**: ManagedDism 3.3.12 has the complete API. Implemented
  the full write set on WindowsImageService: AddPackage, RemovePackageByName, Enable/DisableFeature,
  Add/RemoveCapability, GetProvisionedAppxPackages/RemoveProvisionedAppxPackage, AddDriversFromDirectory
- **New cmdlets (12)**: `Get-WindowsImagePackageList`, `Get-WindowsImageFeatureList`,
  `Add-WindowsImagePackage`, `Enable-WindowsImageFeature`, `Disable-WindowsImageFeature`,
  `Add-WindowsImageCapability`, `Remove-WindowsImageCapability`,
  `New-WindowsImageRecipe`, `Test-WindowsImageRecipe`, `Invoke-WindowsImageRecipe`,
  `Export-WindowsImage`, `New-WindowsImageISO`
- **BuildRecipe executor** (src/Services/RecipeService.cs): loads JSON recipes, validates structure,
  selects images by regex, mounts read-write, applies 8 section types in deterministic order, saves.
  Registry modifications reuse the proven .reg application path
- **WimExportService TODOs finished**: index-by-name lookup, image count, boot flag
  (WIMSetBootImage), destination name/description (new WIMSetImageName/WIMSetImageDescription P/Invoke)
- **ISO support**: `Get-WindowsImageList -ImagePath x.iso` now mounts the ISO (Mount-DiskImage),
  locates install.wim/install.esd, keeps the ISO mounted for servicing; `New-WindowsImageISO`
  exposes ISOService (oscdimg path via installed ADK)

### Phase 4 — New Capabilities
- **`Get-MountedWindowsImage`**: cross-session mount registry (JSON state in %TEMP%\PSWindowsImageTools\
  mounts.json); Mount/Dismount/Get-WindowsImageList-SkipDismount auto-register/unregister; `-Prune` cleans stale entries
- **`Update-WindowsImageOnline`**: one-liner servicing — auto-discovers latest KB from release
  history, searches/downloads from the Update Catalog, installs into selected images. Supports
  pre-downloaded `-UpdatePackages` and explicit `-Query` modes
- **Image diffing**: `Get-WindowsImageSnapshot` (packages/features/capabilities/AppX/software,
  JSON export) + `Compare-WindowsImage` (two mounted images or two snapshot files →
  added/removed/changed per category)

## Module Totals
- 51 exported cmdlets · 99 unit tests passing · build clean (0 warnings)

## Known Remaining Tech Debt
- Older services (catalog, ADK, INF, wallpaper, unattend, autopilot) still take PSCmdlet — burn
  down to ModuleCallbacks opportunistically when touched
- Docs drift in CmdletReference for older cmdlets fixed 2025-09-04 (reference rewritten from
  actual signatures); PlatyPS-generated help still planned
- Module bin refresh requires no other PowerShell session holding the DLLs (rename-swap used during dev)
