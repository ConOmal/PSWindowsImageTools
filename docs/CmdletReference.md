# PSWindowsImageTools Cmdlet Reference

Complete reference for all cmdlets in the PSWindowsImageTools module.

## Table of Contents

- [ADK Management](#adk-management)
- [Image Management](#image-management)
- [Windows Update Workflow](#windows-update-workflow)
- [Image Customization](#image-customization)
- [Autopilot & Configuration](#autopilot--configuration)
- [Release Information](#release-information)

---

## ADK Management

### Get-ADKInstallation
Detect and enumerate installed Windows ADK versions.

```powershell
Get-ADKInstallation [-Latest] [-RequireWinPE] [-RequireDeploymentTools]
```

**Parameters:**
- `Latest`: Return only the latest installed version
- `RequireWinPE`: Only return installations that include WinPE add-on
- `RequireDeploymentTools`: Only return installations that include Deployment Tools

**Examples:**
```powershell
# Get all ADK installations
$allADKs = Get-ADKInstallation

# Get latest ADK with WinPE
$latestADK = Get-ADKInstallation -Latest -RequireWinPE

# Check if deployment tools are available
$deploymentADK = Get-ADKInstallation -RequireDeploymentTools
```

### Install-ADK
Download and install the latest Windows ADK silently with automatic patch detection.

```powershell
Install-ADK [-InstallPath <String>] [-IncludeWinPE] [-IncludeDeploymentTools] [-Force]
```

**Parameters:**
- `InstallPath`: Custom installation path for ADK
- `IncludeWinPE`: Include WinPE add-on in the installation (default: true)
- `IncludeDeploymentTools`: Include Deployment Tools in the installation (default: true)
- `Force`: Force installation even if ADK is already installed (default: skip if present)

**Features:**
- Automatically parses Microsoft's ADK download page for latest version
- Downloads and installs both ADK and WinPE add-on
- Detects and applies available patches (ZIP files with MSP files)
- Enhanced process monitoring with command line display and timeouts

**Examples:**
```powershell
# Install latest ADK with all components
$adk = Install-ADK -IncludeWinPE -IncludeDeploymentTools

# Force fresh installation to custom path
$adk = Install-ADK -InstallPath "C:\CustomADK" -Force

# Install ADK (skips if already present by default)
$adk = Install-ADK
```

### Uninstall-ADK
Remove Windows ADK installations.

```powershell
Uninstall-ADK [-All] [-Force]
```

**Parameters:**
- `All`: Remove all ADK installations (default: remove latest only)
- `Force`: Skip confirmation prompts

**Examples:**
```powershell
# Remove latest ADK with confirmation
Uninstall-ADK

# Remove all ADK installations silently
Uninstall-ADK -All -Force
```

### Get-WinPEOptionalComponent
Discover available WinPE Optional Components.

```powershell
Get-WinPEOptionalComponent -ADKInstallation <ADKInfo> [-Architecture <String>] [-Category <String[]>]
```

**Parameters:**
- `ADKInstallation`: ADK installation object from Get-ADKInstallation
- `Architecture`: Filter by architecture (x86, amd64, arm64)
- `Category`: Filter by component categories

**Examples:**
```powershell
# Get all components for latest ADK
$adk = Get-ADKInstallation -Latest
$components = Get-WinPEOptionalComponent -ADKInstallation $adk

# Get scripting components for x64
$scripting = Get-WinPEOptionalComponent -ADKInstallation $adk -Architecture amd64 -Category "Scripting"
```

### Add-WinPEOptionalComponent
Install WinPE Optional Components into boot images.

```powershell
Add-WinPEOptionalComponent -MountedImage <MountedWindowsImage[]> -Components <WinPEOptionalComponent[]>
```

**Parameters:**
- `MountedImage`: Mounted boot images from Mount-WindowsImageList
- `Components`: Components to install from Get-WinPEOptionalComponent

**Examples:**
```powershell
# Install PowerShell support into WinPE
$winpe = Get-WindowsImageList -ImagePath "boot.wim" | Mount-WindowsImageList -MountPath "C:\WinPE" -ReadWrite
$psComponents = Get-WinPEOptionalComponent -ADKInstallation $adk | Where-Object { $_.Name -like "*PowerShell*" }
$winpe | Add-WinPEOptionalComponent -Components $psComponents
$winpe | Dismount-WindowsImageList -Save
```

---

## Image Management

### Get-WindowsImageList
Get detailed information about Windows images in WIM/ESD files.

```powershell
Get-WindowsImageList -ImagePath <String> [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>] [-IncludeMetadata]
```

**Parameters:**
- `ImagePath`: Path to WIM or ESD file
- `InclusionFilter`: Script block to filter included images
- `ExclusionFilter`: Script block to filter excluded images  
- `IncludeMetadata`: Include detailed metadata in results

**Examples:**
```powershell
# Get all images
$images = Get-WindowsImageList -ImagePath "install.wim"

# Filter for Pro editions
$proImages = Get-WindowsImageList -ImagePath "install.wim" -InclusionFilter { $_.ImageName -like "*Pro*" }

# Get with metadata
$detailed = Get-WindowsImageList -ImagePath "install.wim" -IncludeMetadata
```

### Mount-WindowsImageList
Mount Windows images for modification with GUID-based organization.

```powershell
Mount-WindowsImageList -ImagePath <String> [-Index <Int32>] -MountPath <String> [-ReadWrite] [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>]
```

**Parameters:**
- `ImagePath`: Path to WIM/ESD file
- `Index`: Specific image index to mount
- `MountPath`: Base directory for mounting
- `ReadWrite`: Mount as read-write (default: read-only)
- `InclusionFilter`: Filter for specific images
- `ExclusionFilter`: Exclude specific images

**Examples:**
```powershell
# Mount specific image index
$mounted = Mount-WindowsImageList -ImagePath "install.wim" -Index 1 -MountPath "C:\Mount" -ReadWrite

# Mount all Pro editions
$mounted = Mount-WindowsImageList -ImagePath "install.wim" -MountPath "C:\Mount" -ReadWrite -InclusionFilter { $_.ImageName -like "*Pro*" }

# Pipeline from Get-WindowsImageList
$images = Get-WindowsImageList -ImagePath "install.wim"
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
```

### Dismount-WindowsImageList
Dismount mounted Windows images with save and cleanup options.

```powershell
Dismount-WindowsImageList [-MountPath <String>] [-Save] [-Discard] [-CleanupDirectory]
```

**Parameters:**
- `MountPath`: Specific mount path to dismount (optional with pipeline)
- `Save`: Save changes to the image
- `Discard`: Discard all changes
- `CleanupDirectory`: Remove mount directories after dismounting

**Examples:**
```powershell
# Save changes and cleanup
$mounted | Dismount-WindowsImageList -Save -CleanupDirectory

# Discard changes
Dismount-WindowsImageList -MountPath "C:\Mount\{guid}\1" -Discard

# Save specific mount
Dismount-WindowsImageList -MountPath "C:\Mount\{guid}\1" -Save
```

### Convert-ESDToWindowsImage
Convert ESD files to WIM format with filtering options.

```powershell
Convert-ESDToWindowsImage -ESDPath <String> -WIMPath <String> [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>] [-CompressionType <String>]
```

**Parameters:**
- `ESDPath`: Path to source ESD file
- `WIMPath`: Path for output WIM file
- `InclusionFilter`: Filter for specific images to convert
- `ExclusionFilter`: Exclude specific images from conversion
- `CompressionType`: WIM compression type (None, Fast, Maximum, LZX, XPRESS)

**Examples:**
```powershell
# Convert entire ESD to WIM
Convert-ESDToWindowsImage -ESDPath "install.esd" -WIMPath "install.wim"

# Convert only Pro editions with maximum compression
Convert-ESDToWindowsImage -ESDPath "install.esd" -WIMPath "install_pro.wim" -InclusionFilter { $_.ImageName -like "*Pro*" } -CompressionType Maximum
```

### Reset-WindowsImageBase
Reset image base and perform cleanup operations.

```powershell
Reset-WindowsImageBase -MountedImage <MountedWindowsImage[]>
```

**Parameters:**
- `MountedImage`: Mounted images from Mount-WindowsImageList

**Examples:**
```powershell
# Reset base for mounted images
$mounted | Reset-WindowsImageBase
```

---

## Windows Update Workflow

### Search-WindowsUpdateCatalog
Search Microsoft Update Catalog with advanced filtering.

```powershell
Search-WindowsUpdateCatalog -Query <String> [-Architecture <String>] [-ProductFilter <String[]>] [-Before <DateTime>] [-After <DateTime>] [-MaxResults <Int32>]
```

**Parameters:**
- `Query`: Search query string
- `Architecture`: Filter by architecture (x86, x64, arm64)
- `ProductFilter`: Filter by product names
- `Before`: Updates released before this date
- `After`: Updates released after this date
- `MaxResults`: Maximum number of results to return

**Examples:**
```powershell
# Search for Windows 11 cumulative updates
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64

# Search with date filtering
$recent = Search-WindowsUpdateCatalog -Query "Security Update" -After (Get-Date).AddDays(-30) -MaxResults 10

# Search for specific products
$serverUpdates = Search-WindowsUpdateCatalog -Query "Cumulative" -ProductFilter "Windows Server 2022"
```

### Get-WindowsUpdateDownloadUrl
Extract download URLs from catalog search results.

```powershell
Get-WindowsUpdateDownloadUrl -SearchResults <WindowsUpdate[]>
```

**Parameters:**
- `SearchResults`: Results from Search-WindowsUpdateCatalog

**Examples:**
```powershell
# Get download URLs from search results
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64
$downloadUrls = $updates | Get-WindowsUpdateDownloadUrl
```

### Save-WindowsUpdateCatalogResult
Download update files with resume capability and integrity verification.

```powershell
Save-WindowsUpdateCatalogResult -UpdateResults <WindowsUpdateDownloadInfo[]> -DestinationPath <String> [-Resume] [-VerifyIntegrity]
```

**Parameters:**
- `UpdateResults`: Download info from Get-WindowsUpdateDownloadUrl
- `DestinationPath`: Directory to save downloaded files
- `Resume`: Resume interrupted downloads
- `VerifyIntegrity`: Verify file integrity after download

**Examples:**
```powershell
# Download with resume and verification
$packages = $downloadUrls | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Resume -VerifyIntegrity

# Simple download
$packages = $downloadUrls | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"
```

### Install-WindowsImageUpdate
Install Windows updates into mounted images. Supports both file paths and WindowsUpdatePackage objects.

```powershell
# Install from file paths
Install-WindowsImageUpdate -UpdatePath <FileSystemInfo[]> -ImagePath <DirectoryInfo> [-ValidateImage] [-IgnoreCheck] [-PreventPending] [-ContinueOnError]

# Install from pipeline (WindowsUpdatePackage objects)
Install-WindowsImageUpdate -MountedImages <MountedWindowsImage[]> -UpdatePackages <WindowsUpdatePackage[]> [-IgnoreCheck] [-PreventPending] [-ContinueOnError]
```

**Parameters:**
- `UpdatePath`: Path to update file(s) or directory (FromFiles parameter set)
- `ImagePath`: Path to mounted Windows image (FromFiles parameter set)
- `MountedImages`: Mounted Windows images from Mount-WindowsImageList (FromPackages parameter set)
- `UpdatePackages`: WindowsUpdatePackage objects from Save-WindowsUpdateCatalogResult (FromPackages parameter set)
- `ValidateImage`: Validate image before installation (FromFiles only)
- `IgnoreCheck`: Skip applicability checks
- `PreventPending`: Prevent prerequisite installation
- `ContinueOnError`: Continue on individual failures

**Output:**
- FromFiles: Returns `WindowsImageUpdateResult[]` objects
- FromPackages: Returns updated `MountedWindowsImage[]` objects for pipeline continuation

**Examples:**
```powershell
# Install from file paths
Install-WindowsImageUpdate -UpdatePath "C:\Updates\KB5000001.msu" -ImagePath "C:\Mount\Image1" -ValidateImage

# Install from pipeline
$mounted | Install-WindowsImageUpdate -UpdatePackages $packages -IgnoreCheck

# Complete workflow
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

$mounted = Get-WindowsImageList -ImagePath "install.wim" | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates
$mounted | Dismount-WindowsImageList -Save
```

### Get-PatchTuesday
Calculate Patch Tuesday dates for automation.

```powershell
Get-PatchTuesday [-Year <Int32>] [-Month <Int32>] [-Next] [-Previous] [-All]
```

**Parameters:**
- `Year`: Specific year (default: current year)
- `Month`: Specific month (default: current month)
- `Next`: Get next Patch Tuesday
- `Previous`: Get previous Patch Tuesday
- `All`: Get all Patch Tuesdays for the year

**Examples:**
```powershell
# Get next Patch Tuesday
$nextPatch = Get-PatchTuesday -Next

# Get all Patch Tuesdays for 2024
$allPatches = Get-PatchTuesday -Year 2024 -All

# Get current month's Patch Tuesday
$currentPatch = Get-PatchTuesday
```

---

## Image Customization

### Get-INFDriverList
Parse INF files and extract driver information with hardware ID analysis.

```powershell
Get-INFDriverList -Path <DirectoryInfo> [-Recurse] [-Architecture <String>] [-ParseHardwareIDs]
```

**Parameters:**
- `Path`: Directory containing INF files
- `Recurse`: Search subdirectories recursively
- `Architecture`: Filter by architecture (x86, amd64, arm64)
- `ParseHardwareIDs`: Extract and parse hardware IDs from INF files

**Examples:**
```powershell
# Get all drivers from directory
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse

# Get x64 drivers with hardware ID parsing
$x64Drivers = Get-INFDriverList -Path "C:\Drivers" -Architecture amd64 -ParseHardwareIDs
```

### Add-INFDriverList
Install drivers into mounted Windows images.

```powershell
Add-INFDriverList -MountedImage <MountedWindowsImage[]> -Drivers <DriverInfo[]> [-Force]
```

**Parameters:**
- `MountedImage`: Mounted images from Mount-WindowsImageList
- `Drivers`: Driver information from Get-INFDriverList
- `Force`: Force installation of unsigned drivers

**Examples:**
```powershell
# Install drivers into mounted image
$mounted | Add-INFDriverList -Drivers $drivers

# Force install all drivers
$mounted | Add-INFDriverList -Drivers $drivers -Force
```

### Remove-AppXProvisionedPackageList
Remove AppX packages from mounted images with regex filtering.

```powershell
Remove-AppXProvisionedPackageList -MountedImage <MountedWindowsImage[]> [-InclusionFilter <String>] [-ExclusionFilter <String>] [-ErrorAction <ActionPreference>]
```

**Parameters:**
- `MountedImage`: Mounted images from Mount-WindowsImageList
- `InclusionFilter`: Regex pattern for packages to include for removal
- `ExclusionFilter`: Regex pattern for packages to exclude from removal
- `ErrorAction`: Action to take on errors (Continue, Stop, SilentlyContinue)

**Examples:**
```powershell
# Remove gaming and entertainment apps
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire|Music|Video"

# Remove all except essential apps
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter ".*" -ExclusionFilter "Store|Calculator|Photos|Mail"
```

### Get-RegistryOperationList
Parse registry files and extract operations.

```powershell
Get-RegistryOperationList -Path <FileInfo[]> [-ParseValues]
```

**Parameters:**
- `Path`: Registry files (.reg) to parse
- `ParseValues`: Parse and validate registry values

**Examples:**
```powershell
# Parse registry file
$regOps = Get-RegistryOperationList -Path "C:\Config\settings.reg" -ParseValues
```

### Write-RegistryOperationList
Apply registry operations to mounted Windows images.

```powershell
Write-RegistryOperationList -MountedImage <MountedWindowsImage[]> -Operations <RegistryOperation[]>
```

**Parameters:**
- `MountedImage`: Mounted images from Mount-WindowsImageList
- `Operations`: Registry operations from Get-RegistryOperationList

**Examples:**
```powershell
# Apply registry operations
$mounted | Write-RegistryOperationList -Operations $regOps
```

### Add-SetupCompleteAction
Add custom first-boot actions to Windows images.

```powershell
Add-SetupCompleteAction -MountedImage <MountedWindowsImage[]> [-Command <String>] [-ScriptFile <FileInfo>] [-ScriptContent <String>] [-Priority <Int32>] [-Description <String>]
```

**Parameters:**
- `MountedImage`: Mounted images from Mount-WindowsImageList
- `Command`: Command line to execute
- `ScriptFile`: Script file to copy and execute
- `ScriptContent`: Inline script content
- `Priority`: Execution priority (lower numbers run first)
- `Description`: Description for logging

**Examples:**
```powershell
# Add command
$mounted | Add-SetupCompleteAction -Command "reg add HKLM\Software\..." -Priority 100

# Add script file
$mounted | Add-SetupCompleteAction -ScriptFile "C:\Scripts\setup.cmd" -Priority 50

# Add inline script
$mounted | Add-SetupCompleteAction -ScriptContent "echo Setup complete" -Priority 200
```

---

## Autopilot & Configuration

### Get-AutopilotConfiguration
Load Autopilot JSON configuration from file.

```powershell
Get-AutopilotConfiguration -Path <FileInfo>
```

### Set-AutopilotConfiguration
Modify Autopilot configuration settings.

```powershell
Set-AutopilotConfiguration -Configuration <AutopilotConfiguration> [-TenantId <String>] [-DeviceName <String>] [-UpdateTimeout <Int32>]
```

### Export-AutopilotConfiguration
Save Autopilot configuration to JSON file.

```powershell
Export-AutopilotConfiguration -Configuration <AutopilotConfiguration> -Path <FileInfo>
```

### Install-AutopilotConfiguration
Apply Autopilot configuration to mounted Windows images.

```powershell
Install-AutopilotConfiguration -MountedImage <MountedWindowsImage[]> -Configuration <AutopilotConfiguration>
```

### New-AutopilotConfiguration
Create new Autopilot configuration.

```powershell
New-AutopilotConfiguration -TenantId <String> [-DeviceName <String>] [-UpdateTimeout <Int32>] [-ForcedEnrollment] [-DisableUpdate]
```

**Examples:**
```powershell
# Create and apply Autopilot configuration
$autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -DeviceName "%SERIAL%" -ForcedEnrollment
$mounted | Install-AutopilotConfiguration -Configuration $autopilot
```



---

## Release Information

### Get-WindowsReleaseInfo
Get Windows release history and KB information for all versions.

```powershell
Get-WindowsReleaseInfo [-OperatingSystem <String>] [-Latest] [-WithKBOnly] [-ReleaseId <String>]
```

**Parameters:**
- `OperatingSystem`: Filter by OS (Windows 10, Windows 11, Windows Server)
- `Latest`: Return only the latest release
- `WithKBOnly`: Only return releases that have KB articles
- `ReleaseId`: Filter by specific release ID

**Examples:**
```powershell
# Get latest Windows 11 release info
$latest = Get-WindowsReleaseInfo -OperatingSystem "Windows 11" -Latest

# Get all Windows 10 releases with KB info
$win10Releases = Get-WindowsReleaseInfo -OperatingSystem "Windows 10" -WithKBOnly

# Get specific release
$release = Get-WindowsReleaseInfo -ReleaseId "22H2"
```

---

## Package, Feature & Capability Management

### Get-WindowsImagePackageList
List packages in mounted images.

```powershell
$mounted | Get-WindowsImagePackageList [-Filter <regex>]
```

### Get-WindowsImageFeatureList
List Windows features in mounted images.

```powershell
$mounted | Get-WindowsImageFeatureList [-Filter <regex>]
```

### Add-WindowsImagePackage
Install .cab/.msu packages into mounted images.

```powershell
$mounted | Add-WindowsImagePackage -PackagePath "C:\Updates\KB.msu" [-IgnoreCheck] [-PreventPending] [-ContinueOnError]
```

### Enable-WindowsImageFeature / Disable-WindowsImageFeature
Enable or disable Windows features in mounted images.

```powershell
$mounted | Enable-WindowsImageFeature -FeatureName "NetFx3" [-EnableAll] [-SourcePath "C:\Sources"] [-ContinueOnError]
$mounted | Disable-WindowsImageFeature -FeatureName "Xps-Foundation-Xps-Viewer" [-RemovePayload] [-ContinueOnError]
```

### Add-WindowsImageCapability / Remove-WindowsImageCapability
Add or remove capabilities (Features on Demand) in mounted images.

```powershell
$mounted | Add-WindowsImageCapability -CapabilityName "Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0" [-LimitAccess] [-SourcePath "C:\FoD"]
$mounted | Remove-WindowsImageCapability -CapabilityName "Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0" [-ContinueOnError]
```

---

## Recipe-Driven Image Builds

### New-WindowsImageRecipe
Create a recipe scaffold JSON file.

```powershell
New-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -Name "Corporate Baseline" [-Description] [-Author] [-InclusionExpression "Pro"] [-Force]
```

### Test-WindowsImageRecipe
Validate a recipe (structure, regex patterns, referenced paths, image selection).

```powershell
Test-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" [-ImagePath "install.wim"]
Get-WindowsImageRecipe...  # recipes are plain objects; -Recipe accepts pipeline input
```

### Invoke-WindowsImageRecipe
Apply a recipe to matching images: mounts read-write, applies enabled sections in deterministic
order (AppX removal → file copy → wallpapers → features → drivers → updates → FoD → registry),
then saves each image.

```powershell
Invoke-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim" [-MountPath "C:\Mount"] [-MaxImages 10] [-SkipValidation]
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

## Image Export & ISO

### Export-WindowsImage
Export images from a WIM/ESD to a new WIM using the native WIM API.

```powershell
Export-WindowsImage -SourcePath "install.esd" -DestinationPath "install.wim" [-SourceIndex 2 | -SourceName "Windows 11 Pro"] [-CompressionType Max] [-CheckIntegrity] [-SetBootable] [-DestinationName "Custom Name"] [-Force]
```

### New-WindowsImageISO
Create a bootable ISO from a Windows setup folder using oscdimg (Windows ADK).

```powershell
New-WindowsImageISO -SourcePath "C:\Media\Win11" -OutputIsoPath "C:\Media\Win11.iso" [-VolumeLabel "Windows"] [-BootMode Both] [-Force]
```

`Get-WindowsImageList -ImagePath "x.iso"` now mounts the ISO automatically and locates the
installation image file inside it.

---

## Mount Session & One-liner Servicing

### Get-MountedWindowsImage
Re-discover mounts registered by previous cmdlet runs, including from other PowerShell sessions.

```powershell
Get-MountedWindowsImage [-Filter <regex>] [-Prune]
```

### Update-WindowsImageOnline
One-liner update servicing: discovers the latest cumulative KB for a Windows release, downloads it
from the Update Catalog, and installs it into the images of a WIM/ESD file.

```powershell
# Fully automatic: latest Windows 11 KB for x64
Update-WindowsImageOnline -ImagePath "install.wim"

# Explicit query
Update-WindowsImageOnline -ImagePath "install.wim" -Query "KB5065429" -Architecture x64

# Pre-downloaded packages (skips catalog step)
$packages | Update-WindowsImageOnline -ImagePath "install.wim" -MaxImages 3
```

---

## Pipeline Examples

### Complete Enterprise Deployment
```powershell
# Setup environment
Install-ADK -Force

# Get latest updates
$latestRelease = Get-WindowsReleaseInfo -OperatingSystem "Windows 11" -Latest
$updates = Search-WindowsUpdateCatalog -Query $latestRelease.LatestKBArticle -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

# Customize images
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

# Apply customizations
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse
$mounted | Add-INFDriverList -Drivers $drivers
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire" -ExclusionFilter "Store|Calculator"

# Configure Autopilot
$autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -DeviceName "%SERIAL%"
$mounted | Install-AutopilotConfiguration -Configuration $autopilot

# Save and cleanup
$mounted | Dismount-WindowsImageList -Save
```

### Automated Patch Tuesday Workflow
```powershell
# Calculate next Patch Tuesday
$nextPatchTuesday = Get-PatchTuesday -Next

# Download updates for that date
$updates = Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 |
    Where-Object { $_.LastModified.Date -eq $nextPatchTuesday.Date } |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\PatchTuesday\$($nextPatchTuesday.Date.ToString('yyyy-MM'))"

Write-Output "Downloaded $($updates.Count) updates for Patch Tuesday: $($nextPatchTuesday.Date.ToString('MMMM dd, yyyy'))"
```
