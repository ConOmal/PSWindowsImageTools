# PSWindowsImageTools Cmdlet Reference

Complete reference for all 62 cmdlets in the PSWindowsImageTools module. Signatures below reflect
the actual exported cmdlets. `*` marks mandatory parameters.

## Table of Contents

- [Image Management](#image-management)
- [Package, Feature & Capability Management](#package-feature--capability-management)
- [Recipe-Driven Image Builds](#recipe-driven-image-builds)
- [Windows Update Workflow](#windows-update-workflow)
- [Image Customization](#image-customization)
- [Autopilot & Unattend Configuration](#autopilot--unattend-configuration)
- [Registry Operations](#registry-operations)
- [ADK Management](#adk-management)
- [Image Export & ISO](#image-export--iso)
- [Mount Session & One-liner Servicing](#mount-session--one-liner-servicing)
- [Image Diffing](#image-diffing)
- [Drivers, Component Store, Health Check & SBOM](#drivers-component-store-health-check--sbom)

---

## Image Management

### Get-WindowsImageList
Get detailed information about Windows images in WIM/ESD/ISO files.

```powershell
Get-WindowsImageList -ImagePath <FileInfo*> [-Advanced] [-IncludeHash]
    [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>]
    [-SkipDismount] [-ReadWrite] [-MountRoot <DirectoryInfo>]
```

- `-Advanced`: mount each image to collect registry metadata (slower)
- `-IncludeHash`: SHA256 hash of the source file
- `-SkipDismount`: keep images mounted for use with other cmdlets (registers in the mount session registry)
- ISO input is supported: the ISO is mounted automatically and `sources\install.wim` / `install.esd` is used

### Mount-WindowsImageList
Mount images for modification. Pipeline input comes from `Get-WindowsImageList`.

```powershell
$images | Mount-WindowsImageList [-ReadWrite] [-MountRoot <DirectoryInfo>]
```

- Parameter sets: `FromPipeline` (`-InputObject`) and `FromParameter` (`-ImageInfo`)
- Mount directories are GUID-organized under `-MountRoot` (default: `%TEMP%\PSWindowsImageTools\Mounts`)
- Successful mounts are registered for `Get-MountedWindowsImage` re-discovery

### Dismount-WindowsImageList
Dismount mounted images with save/discard options.

```powershell
$mounted | Dismount-WindowsImageList [-Save] [-Discard] [-Append] [-Force] [-RemoveDirectories]
Dismount-WindowsImageList -Path <DirectoryInfo[]> [-Save] [-Discard] ...
```

### Convert-ESDToWindowsImage
Convert ESD files to WIM format (or folder layout).

```powershell
Convert-ESDToWindowsImage -SourcePath <FileInfo*> -OutputPath <String*> -Mode <String*>
    [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>] [-CompressionType <String>]
    [-Force] [-Bootable] [-IncludeWindowsPE] [-IncludeWindowsSetup] [-ScratchDirectory <DirectoryInfo>]
```

### Reset-WindowsImageBase
Component cleanup on mounted images (superseded payload removal).

```powershell
$mounted | Reset-WindowsImageBase [-ComponentCleanup] [-AnalyzeOnly] [-ContinueOnError] [-Defer]
Reset-WindowsImageBase -Path <DirectoryInfo[]> [-ComponentCleanup] ...
```

---

## Package, Feature & Capability Management

### Get-WindowsImagePackageList
```powershell
$mounted | Get-WindowsImagePackageList [-Filter <String>]
```

### Get-WindowsImageFeatureList
```powershell
$mounted | Get-WindowsImageFeatureList [-Filter <String>]
```

### Add-WindowsImagePackage
Install .cab/.msu packages into mounted images.

```powershell
$mounted | Add-WindowsImagePackage -PackagePath <String[]> [-IgnoreCheck] [-PreventPending] [-ContinueOnError]
```

### Enable-WindowsImageFeature / Disable-WindowsImageFeature
```powershell
$mounted | Enable-WindowsImageFeature -FeatureName <String[]> [-EnableAll] [-SourcePath <String[]>] [-ContinueOnError]
$mounted | Disable-WindowsImageFeature -FeatureName <String[]> [-RemovePayload] [-ContinueOnError]
```

### Add-WindowsImageCapability / Remove-WindowsImageCapability
Capabilities are Features on Demand, e.g. `Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0`.

```powershell
$mounted | Add-WindowsImageCapability -CapabilityName <String[]> [-LimitAccess] [-SourcePath <String[]>] [-ContinueOnError]
$mounted | Remove-WindowsImageCapability -CapabilityName <String[]> [-ContinueOnError]
```

---

## Recipe-Driven Image Builds

### New-WindowsImageRecipe
Create a recipe scaffold JSON file.

```powershell
New-WindowsImageRecipe -RecipePath <String*> [-Name <String>] [-Description <String>] [-Author <String>]
    [-InclusionExpression <String>] [-ExclusionExpression <String>] [-Force]
```

### Test-WindowsImageRecipe
Validate a recipe: structure, regex patterns, referenced paths, and image selection.

```powershell
Test-WindowsImageRecipe -RecipePath <String*> [-ImagePath <String>]
$recipe | Test-WindowsImageRecipe [-ImagePath <String>]
```

### Invoke-WindowsImageRecipe
Apply a recipe to matching images: mounts read-write, applies enabled sections in deterministic
order (AppX removal → file copy → wallpapers → features → drivers → updates → FoD → registry),
then saves each image.

```powershell
Invoke-WindowsImageRecipe -RecipePath <String*> -ImagePath <String*>
    [-MountPath <String>] [-MaxImages <Int32>] [-SkipValidation]
$recipe | Invoke-WindowsImageRecipe -ImagePath <String*>
```

Recipe sections (all optional, enabled per-section):

```json
{
  "metadata": { "name": "Corporate Baseline", "description": "", "version": "1.0.0" },
  "imageFilter": { "enabled": true, "inclusionExpression": "Pro", "exclusionExpression": "" },
  "removeAppxPackages": { "enabled": true, "patterns": ["Xbox", "Bing"] },
  "copyFiles": { "enabled": true, "items": [{ "source": "C:\\Branding\\logo.png", "destination": "Windows\\Branding\\logo.png", "overwrite": true }] },
  "setWallpapers": { "enabled": false, "wallpaper": "C:\\Branding\\wallpaper.jpg", "lockScreen": "C:\\Branding\\lock.jpg" },
  "enableFeatures": { "enabled": false, "patterns": ["TelnetClient"] },
  "integrateDrivers": { "enabled": false, "paths": ["C:\\Drivers"] },
  "integrateUpdates": { "enabled": false, "paths": ["C:\\Updates\\KB.msu"] },
  "integrateFeaturesOnDemand": { "enabled": false, "paths": ["Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0"] },
  "registryModifications": { "enabled": false, "modifications": [{ "hive": "HKLM", "key": "SOFTWARE\\Policies\\Test", "valueName": "Enabled", "valueData": "1", "valueType": "DWord" }] }
}
```

---

## Windows Update Workflow

### Search-WindowsUpdateCatalog
Search the Microsoft Update Catalog.

```powershell
Search-WindowsUpdateCatalog [-Query <String[]>] [-Architecture <String>] [-MaxResults <Int32>]
    [-Classification <String>] [-Product <String>] [-Page <Int32>] [-DebugMode]
```

Pipeline: accepts query strings via `-InputObject`.

### Get-WindowsUpdateDownloadUrl
Extract download URLs from catalog results.

```powershell
$results | Get-WindowsUpdateDownloadUrl [-DebugMode]
```

### Save-WindowsUpdateCatalogResult
Download update files with resume and verification.

```powershell
$urls | Save-WindowsUpdateCatalogResult [-DestinationPath <DirectoryInfo>] [-Force] [-Verify] [-Resume]
```

### Install-WindowsImageUpdate
Install updates into mounted images. Two parameter sets:

```powershell
# From downloaded packages (pipeline)
$mounted | Install-WindowsImageUpdate -UpdatePackages <WindowsUpdatePackage[]> [-IgnoreCheck] [-PreventPending] [-ContinueOnError]

# From files
Install-WindowsImageUpdate -UpdatePath <FileSystemInfo[]> -ImagePath <DirectoryInfo> [-ValidateImage] ...
```

### Get-PatchTuesday
Calculate Patch Tuesday dates.

```powershell
Get-PatchTuesday [-After <DateTime>] [-All] [-Remaining]
```

- `-Remaining`: upcoming Patch Tuesdays
- `-All`: all Tuesdays in the calendar year
- `-After`: only dates after this date

---

## Image Customization

### Get-INFDriverList
Parse INF files and extract driver information.

```powershell
Get-INFDriverList -Path <DirectoryInfo[]> [-Recurse] [-ParseINF]
```

### Add-INFDriverList
Install drivers into mounted images.

```powershell
$mounted | Add-INFDriverList -Drivers <INFDriverInfo[]> [-ForceUnsigned]
```

### Set-WindowsImageWallpaper
Configure wallpaper and lockscreen images in mounted images.

```powershell
Set-WindowsImageWallpaper -WallpaperPath <FileInfo*> [-MountPath <DirectoryInfo>] [-MountedImages <MountedWindowsImage[]>]
    [-LockscreenPath <FileInfo>] [-ResolutionList <ResolutionInfo[]>] [-Force]
```

### Remove-AppXProvisionedPackageList
Remove provisioned AppX packages with regex filtering.

```powershell
$mounted | Remove-AppXProvisionedPackageList [-InclusionFilter <String>] [-ExclusionFilter <String>]
```

### Add-SetupCompleteAction
Add custom first-boot actions.

```powershell
Add-SetupCompleteAction -ImagePath <DirectoryInfo*> [-Command <String[]>] [-ScriptFile <FileInfo>]
    [-CopyFiles <FileSystemInfo[]>] [-CopyDestination <String>] [-Description <String>]
    [-Priority <Int32>] [-ContinueOnError] [-Backup]
```

### Invoke-MediaDynamicUpdate
Apply Dynamic Updates to Windows installation media (SSU → SafeOS → LCU → Setup).

```powershell
Invoke-MediaDynamicUpdate -MediaPath <DirectoryInfo*> -UpdatesPath <DirectoryInfo*>
    [-MountBasePath <DirectoryInfo>] [-SkipBootImages] [-SkipWindowsImages] [-PerformCleanup]
    [-ValidateImages] [-AutoDismount] [-ResultOnly] [-ContinueOnError]
```

---

## Autopilot & Unattend Configuration

### Autopilot
```powershell
Get-AutopilotConfiguration -File <FileInfo*> [-Validate]
Set-AutopilotConfiguration -Configuration <AutopilotConfiguration*> [-TenantId] [-TenantDomain] [-DeviceName]
    [-OobeConfig] [-DomainJoinMethod] [-DisableAutopilotUpdate] [-EnableAutopilotUpdate] [-UpdateTimeout] [-ForcedEnrollment] [-PassThru]
Export-AutopilotConfiguration -Configuration <AutopilotConfiguration*> -OutputFile <FileInfo*> [-Force] [-PassThru]
New-AutopilotConfiguration -TenantId <String*> -TenantDomain <String*> [-DeviceName] [-Comment]
$mounted | Install-AutopilotConfiguration -Configuration <AutopilotConfiguration*> [-Force]
```

### Unattend XML
```powershell
Get-UnattendXMLConfiguration -File <FileInfo*> [-Validate] [-ShowComponents] [-ShowElements] [-ElementFilter <String>]
Set-UnattendXMLConfiguration -Configuration <UnattendXMLConfiguration*> -XPath <String*> -ElementName <String*>
    [-Pass] [-ComponentName] [-Value] [-AttributeName] [-Remove] [-CreateIfNotExists] [-PassThru]
Export-UnattendXMLConfiguration -Configuration <UnattendXMLConfiguration*> -OutputFile <FileInfo*>
    [-Encoding] [-Force] [-Indent] [-IndentChars] [-OmitXmlDeclaration] [-PassThru]
New-UnattendXMLConfiguration [-Template] [-Architecture] [-Language] [-ConfigurationPasses] [-IncludeSamples]
$mounted | Install-UnattendXMLConfiguration -Configuration <UnattendXMLConfiguration*> [-Force] [-Encoding]
```

---

## Registry Operations

### Get-RegistryHiveOnDemand
Read registry data from offline hive files without mounting (via `RegistryHiveOnDemand`).

```powershell
Get-RegistryHiveOnDemand -Path <FileInfo*> [-KeyPath <String[]>] [-MaxDepth <Int32>]
```

- `SOFTWARE` hives are auto-detected and return version info, installed software, and WU config
- Use `-KeyPath` with `-MaxDepth` for arbitrary keys (e.g., `Software`, `-MaxDepth 0`)

### Get-RegistryOperationList
Parse `.reg` files into operations.

```powershell
Get-RegistryOperationList -Path <String[]> [-Recurse] [-FilterHive <String>] [-FilterOperation <String>]
```

### Write-RegistryOperationList
Apply registry operations to mounted images (hive-mounted writes).

```powershell
$mounted | Write-RegistryOperationList -Operations <RegistryOperation[]> [-ContinueOnError]
```

---

## ADK Management

### Get-ADKInstallation
```powershell
Get-ADKInstallation [-Latest] [-MinimumVersion <Version>] [-RequireWinPE] [-RequireDeploymentTools] [-RequiredArchitecture <String>]
```

### Install-ADK
```powershell
Install-ADK [-InstallPath <String>] [-IncludeWinPE] [-IncludeDeploymentTools] [-Force]
```

### Uninstall-ADK
```powershell
Uninstall-ADK [-All] [-Force]
```

### Get-WinPEOptionalComponent
```powershell
Get-WinPEOptionalComponent [-ADKInstallation <ADKInfo>] [-Architecture <String>] [-IncludeLanguagePacks] [-Category <String[]>] [-Name <String[]>]
```

### Add-WinPEOptionalComponent
```powershell
$mounted | Add-WinPEOptionalComponent -Components <WinPEOptionalComponent[]> [-ContinueOnError]
```

---

## Image Export & ISO

### Export-WindowsImage
Export images from a WIM/ESD to a new WIM using the native WIM API.

```powershell
Export-WindowsImage -SourcePath <String*> -DestinationPath <String*>
    [-SourceIndex <Int32>] [-SourceName <String>] [-DestinationName <String>] [-DestinationDescription <String>]
    [-CompressionType <String>] [-CheckIntegrity] [-SetBootable] [-Force] [-ContinueOnError]
```

- `-SourceIndex 0` (default) exports all images
- `-CompressionType`: None, Fast, Max, Recovery

### New-WindowsImageISO
Create a bootable ISO from a Windows setup folder using oscdimg (Windows ADK).

```powershell
New-WindowsImageISO -SourcePath <String*> -OutputIsoPath <String*> [-VolumeLabel <String>] [-BootMode <String>] [-Force]
```

`Get-WindowsImageList -ImagePath "x.iso"` mounts the ISO automatically and locates the
installation image file inside it.

---

## Mount Session & One-liner Servicing

### Get-MountedWindowsImage
Re-discover mounts registered by previous cmdlet runs, including from other PowerShell sessions.

```powershell
Get-MountedWindowsImage [-Filter <String>] [-Prune]
```

- `Mount-WindowsImageList`, `Dismount-WindowsImageList`, and `Get-WindowsImageList -SkipDismount`
  auto-register/unregister mounts
- `-Prune` removes entries whose mount directories no longer exist

### Update-WindowsImageOnline
One-liner update servicing: discovers the latest cumulative KB for a Windows release, downloads it
from the Update Catalog, and installs it into the images of a WIM/ESD file.

```powershell
# Fully automatic: latest KB for the OS (default Windows 11, x64)
Update-WindowsImageOnline -ImagePath <String*>

# Explicit catalog query
Update-WindowsImageOnline -ImagePath <String*> -Query "KB5065429" [-Architecture x64]

# Pre-downloaded packages (skips the catalog step)
$packages | Update-WindowsImageOnline -ImagePath <String*>

Common: [-OperatingSystem] [-DestinationPath] [-MountPath] [-MaxImages 5] [-MaxUpdates 10] [-ContinueOnError]
```

---

## Image Diffing

### Get-WindowsImageSnapshot
Capture an inventory snapshot of mounted images (packages, features, capabilities, provisioned
AppX, installed software). Snapshots can be exported as JSON for point-in-time comparisons.

```powershell
$mounted | Get-WindowsImageSnapshot [-ExportPath <String>]
```

### Compare-WindowsImage
Compare two snapshots to surface what changed (added / removed / changed per category).

```powershell
# Two mounted images (e.g., vanilla vs customized)
$reference, $difference | Compare-WindowsImage

# Two exported snapshot files (before/after audits)
Compare-WindowsImage -ReferencePath "before.json" -DifferencePath "after.json"
```

Output: `ImageComparisonResult` with per-category `Added` / `Removed` / `Changed` lists,
`TotalDifferences`, and `AreIdentical`.

```powershell
$diff = Compare-WindowsImage -ReferencePath vanilla.json -DifferencePath corporate.json
$diff.Categories | Format-Table Category, Count
$diff.Categories | ForEach-Object { $_.Added } | Format-Table Name, State
```

---

## Drivers, Component Store, Health Check & SBOM

### Get-WindowsImageDriver
Lists driver packages present in one or more mounted Windows images.

```powershell
Get-WindowsImageDriver -MountedImages <MountedWindowsImage*> [-All] [-ContinueOnError]
```

`-All` includes inbox (Windows-provided) drivers, not just third-party. `-ContinueOnError`
keeps processing other images if one fails.

### Remove-WindowsImageDriver
Removes a driver package from a mounted Windows image.

```powershell
Remove-WindowsImageDriver -Driver <WindowsImageDriverInfo*> [-ContinueOnError] [-WhatIf] [-Confirm]
```

Takes driver objects from `Get-WindowsImageDriver` via the pipeline.

### Compare-WindowsImageDriver
Compares driver packages between two mounted Windows images.

```powershell
Compare-WindowsImageDriver -MountedImages <MountedWindowsImage*> [-All]
```

### Export-WindowsImageDriver
Exports driver package files from a mounted Windows image to a destination directory.

```powershell
Export-WindowsImageDriver -Driver <WindowsImageDriverInfo*> -DestinationPath <DirectoryInfo*> [-ContinueOnError]
```

### Get-WindowsImageComponentStore
Analyzes the WinSxS component store of one or more mounted Windows images.

```powershell
Get-WindowsImageComponentStore -MountedImages <MountedWindowsImage*> [-ContinueOnError]
```

Reports superseded package counts, store size, and cleanup recommendations.

### Optimize-WindowsImageComponentStore
Runs component cleanup (and optionally ResetBase) against one or more mounted Windows images.

```powershell
Optimize-WindowsImageComponentStore -MountedImages <MountedWindowsImage*> [-ResetBase]
    [-TimeoutMinutes <int>] [-ContinueOnError] [-WhatIf] [-Confirm]
```

Reports before/after store state and the underlying DISM exit code.

### Invoke-WindowsImageHealthCheck
Runs a composite health check against one or more mounted Windows images.

```powershell
Invoke-WindowsImageHealthCheck -MountedImages <MountedWindowsImage*> [-RestoreHealth]
    [-ContinueOnError] [-WhatIf] [-Confirm]
```

Rolls drivers, packages, features, and the component store into a single `OverallHealth`
verdict (`Healthy`/`Warning`/`Unhealthy`). `-RestoreHealth` additionally runs DISM
health restoration.

### Export-WindowsImageSBOM
Builds a Software Bill of Materials (SBOM) from a captured Windows image snapshot.

```powershell
Export-WindowsImageSBOM -Snapshot <ImageSnapshot*> -DestinationPath <DirectoryInfo*>
Export-WindowsImageSBOM -SnapshotPath <string> -DestinationPath <DirectoryInfo*>
```

Accepts a snapshot object (from `Get-WindowsImageSnapshot`) or a previously exported
snapshot JSON file.

---

## Pipeline Examples

### Complete Enterprise Deployment
```powershell
# Setup environment
Install-ADK -Force

# Get latest updates
$latestRelease = Get-WindowsReleaseInfo -After (Get-Date).AddDays(-60) -Detailed
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

# Customize images
$images = Get-WindowsImageList -ImagePath "install.wim" -InclusionFilter { $_.Name -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -ReadWrite -MountRoot "C:\Mount"

# Apply customizations
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse
$mounted | Add-INFDriverList -Drivers $drivers
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire" -ExclusionFilter "Store|Calculator"

# Save and cleanup
$mounted | Dismount-WindowsImageList -Save -RemoveDirectories
```

### Recipe-Driven Build
```powershell
New-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -Name "Corporate" -InclusionExpression "Pro|Enterprise"
# ... edit the JSON to add sections ...
Test-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim"
Invoke-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim"
```

### One-liner Patch Tuesday Servicing
```powershell
# Mount, discover latest KB, download from the catalog, install, save
Update-WindowsImageOnline -ImagePath "C:\Images\install.wim" -Architecture x64
```
