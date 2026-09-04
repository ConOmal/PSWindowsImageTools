# PSWindowsImageTools

A comprehensive PowerShell module for Windows image management, customization, and deployment automation. Built for enterprise environments requiring advanced WIM/ESD manipulation, driver integration, registry operations, and system customization with native Windows APIs and best practices.

## 🚀 Key Features

### 🖼️ **Advanced Windows Image Management**
- Mount/unmount WIM and ESD files with native DISM API integration and real-time progress callbacks
- Read-only and read-write mount modes with GUID-based mount point management
- Cross-session mount re-discovery (`Get-MountedWindowsImage`) — mounts survive pipeline breaks
- Advanced image information retrieval with offline registry data extraction (no hive mounting)
- ISO input support: point at an `.iso` and the installation image is located automatically
- Full pipeline support for batch operations with progress tracking

### 📦 **Comprehensive Update Management**
- Search Microsoft Update Catalog with advanced filtering and KB correlation
- Download updates with resume capability and integrity verification
- Install CAB/MSU files into mounted images with validation and progress tracking
- Dynamic update integration for Windows installation media (SSU → SafeOS → LCU → Setup)
- **One-liner servicing**: `Update-WindowsImageOnline` discovers the latest KB, downloads, and installs it
- Patch Tuesday calculation for automation

### 🍳 **Recipe-Driven Image Builds**
- Declarative JSON recipes: `New-WindowsImageRecipe` → `Test-WindowsImageRecipe` → `Invoke-WindowsImageRecipe`
- Sections: AppX removal, file copy, wallpapers, feature enablement, driver/update/FoD integration, registry writes
- Validation of regex patterns, referenced paths, and image selection before execution

### 🛠️ **ADK Management & Automation**
- Automatic detection and installation of latest Windows ADK
- Dynamic parsing of Microsoft's download pages
- WinPE Optional Component management with validation
- Enhanced process monitoring with command-line transparency

### 🔧 **Advanced Image Customization**
- Package, feature, and capability (Features on Demand) management via DISM API
- Registry operations with direct hive reading (no mounting) and hive-mounted writes
- Driver integration with INF parsing and hardware ID extraction
- Wallpaper and lockscreen configuration with multiple resolution support
- Native Windows API integration for permission management (TrustedInstaller)
- Autopilot configuration management with JSON validation
- Unattend.xml creation, modification, and validation
- AppX package removal with advanced regex filtering
- Custom setup actions and first-boot scripts with comprehensive error handling

### 📊 **Auditing, Export & Diffing**
- `Export-WindowsImage`: WIM export with compression, boot flag, rename, split-ready native API
- `New-WindowsImageISO`: bootable ISO creation via oscdimg (UEFI/BIOS/Both)
- `Get-WindowsImageSnapshot` + `Compare-WindowsImage`: inventory snapshots and before/after diffing
- Windows release information and KB correlation
- PowerShell 5.1 and 7+ compatibility
- Full `Get-Help` coverage for every cmdlet, comprehensive logging, and CI-tested builds

## 🏃‍♂️ Quick Start

```powershell
# Import the module
Import-Module PSWindowsImageTools

# Install latest ADK automatically
Install-ADK -IncludeWinPE -IncludeDeploymentTools

# Get image information
$images = Get-WindowsImageList -ImagePath "C:\Images\install.wim"

# Search and download latest updates
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

# Mount, customize, and update image
$mounted = $images | Mount-WindowsImageList -MountRoot "C:\Mount" -ReadWrite
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates
$mounted | Dismount-WindowsImageList -Save
```

### **Recipe-driven build (declarative)**
```powershell
New-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -Name "Corporate Baseline" -InclusionExpression "Pro|Enterprise"
# ... edit the JSON: AppX removal, drivers, updates, wallpapers, registry, FoD ...
Test-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim"
Invoke-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim"
```

### **One-liner Patch Tuesday servicing**
```powershell
# Discovers the latest Windows 11 x64 cumulative update, downloads it, and services every image
Update-WindowsImageOnline -ImagePath "C:\Images\install.wim"
```

## 📋 Complete Cmdlet Reference

### **Image Management**
| Cmdlet | Description |
|--------|-------------|
| `Get-WindowsImageList` | Enumerate images in WIM/ESD/ISO files (ISO mounted automatically) |
| `Mount-WindowsImageList` | Mount images for modification |
| `Dismount-WindowsImageList` | Unmount and save or discard changes |
| `Get-MountedWindowsImage` | Re-discover active mounts across sessions |
| `Convert-ESDToWindowsImage` | Convert ESD to WIM format or folder layout |
| `Reset-WindowsImageBase` | Component cleanup for space optimization |

### **Recipe-Driven Builds**
| Cmdlet | Description |
|--------|-------------|
| `New-WindowsImageRecipe` | Create a recipe scaffold JSON file |
| `Test-WindowsImageRecipe` | Validate recipe structure, patterns, and image selection |
| `Invoke-WindowsImageRecipe` | Apply a recipe to matching images end-to-end |

### **Windows Update Workflow**
| Cmdlet | Description |
|--------|-------------|
| `Search-WindowsUpdateCatalog` | Search Microsoft Update Catalog |
| `Get-WindowsUpdateDownloadUrl` | Extract download URLs |
| `Save-WindowsUpdateCatalogResult` | Download with resume and verification |
| `Install-WindowsImageUpdate` | Install updates into mounted images |
| `Update-WindowsImageOnline` | One-liner: latest KB → download → install → save |
| `Get-PatchTuesday` | Calculate Patch Tuesday dates |
| `Invoke-MediaDynamicUpdate` | Apply Dynamic Updates to installation media |

### **Package, Feature & Capability Management**
| Cmdlet | Description |
|--------|-------------|
| `Get-WindowsImagePackageList` | List DISM packages in mounted images |
| `Get-WindowsImageFeatureList` | List Windows features in mounted images |
| `Add-WindowsImagePackage` | Install .cab/.msu packages |
| `Enable-WindowsImageFeature` | Enable Windows features |
| `Disable-WindowsImageFeature` | Disable Windows features |
| `Add-WindowsImageCapability` | Add capabilities (Features on Demand) |
| `Remove-WindowsImageCapability` | Remove capabilities |

### **Image Customization**
| Cmdlet | Description |
|--------|-------------|
| `Get-INFDriverList` | Parse INF files and extract driver info |
| `Add-INFDriverList` | Install drivers into mounted images |
| `Set-WindowsImageWallpaper` | Configure wallpaper and lockscreen images |
| `Remove-AppXProvisionedPackageList` | Remove AppX packages with regex filtering |
| `Get-RegistryOperationList` | Parse .reg files into operations |
| `Write-RegistryOperationList` | Apply registry operations to mounted images |
| `Get-RegistryHiveOnDemand` | Read offline registry hives without mounting |
| `Add-SetupCompleteAction` | Add custom first-boot actions |

### **Auditing, Export & Diffing**
| Cmdlet | Description |
|--------|-------------|
| `Export-WindowsImage` | Export images to WIM (compression, boot flag, rename) |
| `New-WindowsImageISO` | Create bootable ISOs via oscdimg |
| `Get-WindowsImageSnapshot` | Capture inventory snapshots (JSON export) |
| `Compare-WindowsImage` | Diff two snapshots (added/removed/changed) |

### **Autopilot & Configuration**
| Cmdlet | Description |
|--------|-------------|
| `Get-AutopilotConfiguration` | Load Autopilot JSON configuration |
| `Set-AutopilotConfiguration` | Modify Autopilot settings |
| `Export-AutopilotConfiguration` | Save Autopilot configuration |
| `Install-AutopilotConfiguration` | Apply to mounted images |
| `New-AutopilotConfiguration` | Create new configuration |
| `Get-UnattendXMLConfiguration` | Load and inspect unattend.xml |
| `Set-UnattendXMLConfiguration` | Modify unattend.xml elements |
| `Export-UnattendXMLConfiguration` | Save unattend.xml |
| `New-UnattendXMLConfiguration` | Create new unattend.xml |
| `Install-UnattendXMLConfiguration` | Apply unattend.xml to images |

### **ADK & WinPE Management**
| Cmdlet | Description |
|--------|-------------|
| `Get-ADKInstallation` | Detect installed Windows ADK versions |
| `Install-ADK` | Download and install latest ADK with patches |
| `Uninstall-ADK` | Remove ADK installations |
| `Get-WinPEOptionalComponent` | Discover available WinPE components |
| `Add-WinPEOptionalComponent` | Install components into boot images |

### **Release Information**
| Cmdlet | Description |
|--------|-------------|
| `Get-WindowsReleaseInfo` | Get Windows release history and KB info |

## 💡 Usage Examples

### **Enterprise Deployment Workflow**
```powershell
# 1. Setup environment
Install-ADK -Force

# 2. Get latest Windows 11 updates
$latestRelease = Get-WindowsReleaseInfo -After (Get-Date).AddDays(-60) -Detailed
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

# 3. Customize image with drivers and updates
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountRoot "C:\Mount" -ReadWrite

# Install drivers
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse
$mounted | Add-INFDriverList -Drivers $drivers

# Install updates
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates

# Configure wallpaper and lockscreen
$mounted | Set-WindowsImageWallpaper -WallpaperPath "C:\Branding\wallpaper.jpg" -LockscreenPath "C:\Branding\lockscreen.jpg"

# Configure Autopilot
$autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -TenantDomain "your-tenant.onmicrosoft.com" -DeviceName "%SERIAL%"
$mounted | Install-AutopilotConfiguration -Configuration $autopilot

# Remove unwanted AppX packages
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire" -ExclusionFilter "Store|Calculator"

# Save and cleanup
$mounted | Dismount-WindowsImageList -Save
```

### **Wallpaper and Lockscreen Configuration**
```powershell
# Configure wallpaper and lockscreen for mounted images
$mounted = Get-WindowsImageList -ImagePath "install.wim" | Mount-WindowsImageList -MountRoot "C:\Mount" -ReadWrite

# Set both wallpaper and lockscreen
$mounted | Set-WindowsImageWallpaper -WallpaperPath "C:\Branding\corporate-wallpaper.jpg" -LockscreenPath "C:\Branding\lockscreen.jpg"

# Set wallpaper only with custom resolutions
$customResolutions = @(
    [PSWindowsImageTools.Models.ResolutionInfo]::new("img0_", 1920, 1080),
    [PSWindowsImageTools.Models.ResolutionInfo]::new("img0_", 2560, 1440),
    [PSWindowsImageTools.Models.ResolutionInfo]::new("img0_", 3840, 2160)
)
$mounted | Set-WindowsImageWallpaper -WallpaperPath "C:\Branding\wallpaper.png" -ResolutionList $customResolutions

# Direct path approach (without pipeline)
Set-WindowsImageWallpaper -MountPath "C:\Mount" -WallpaperPath "C:\Branding\wallpaper.jpg"

$mounted | Dismount-WindowsImageList -Save
```

### **Automated Patch Tuesday Updates**
```powershell
# Calculate next Patch Tuesday
$nextPatchTuesday = Get-PatchTuesday -Remaining | Select-Object -First 1

# Setup automated download for that date
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 |
    Where-Object { $_.LastUpdated.Date -eq $nextPatchTuesday.Date } |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\PatchTuesday\$($nextPatchTuesday.Date.ToString('yyyy-MM'))"

Write-Output "Downloaded $($updates.Count) updates for Patch Tuesday: $($nextPatchTuesday.Date.ToString('MMMM dd, yyyy'))"
```

### **WinPE Customization**
```powershell
# Install ADK with WinPE
Install-ADK -IncludeWinPE -IncludeDeploymentTools

# Get available components
$adk = Get-ADKInstallation -Latest
$components = Get-WinPEOptionalComponent -ADKInstallation $adk -Category "Scripting","Networking"

# Mount WinPE image and add components
$winpe = Get-WindowsImageList -ImagePath "C:\WinPE\boot.wim"
$mounted = $winpe | Mount-WindowsImageList -MountRoot "C:\WinPE\Mount" -ReadWrite

# Add PowerShell and networking support
$mounted | Add-WinPEOptionalComponent -Components ($components | Where-Object { $_.Name -like "*PowerShell*" -or $_.Name -like "*WMI*" })

$mounted | Dismount-WindowsImageList -Save
```

## 🔧 Installation & Requirements

### **Prerequisites**
- Windows 10/11 or Windows Server 2019/2022
- PowerShell 5.1 or PowerShell 7+
- Administrator privileges for image operations
- DISM tools (included with Windows)

### **Installation**
```powershell
# Clone repository
git clone https://github.com/Grace-Solutions/PSWindowsImageTools.git
cd PSWindowsImageTools

# Import module
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1

# Verify installation
Get-Command -Module PSWindowsImageTools
```

## 📚 Documentation

- **[Complete Cmdlet Reference](docs/CmdletReference.md)** - Detailed documentation for all cmdlets
- **[Windows Update Catalog Guide](docs/WindowsUpdateCatalog.md)** - Update management workflows
- **[Image Customization Guide](docs/ImageCustomization.md)** - Advanced customization techniques

## 🤝 Contributing

We welcome contributions! Please:
1. Fork the repository
2. Create a feature branch
3. Submit a pull request with detailed description

## 📄 License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

---

**PSWindowsImageTools** - Streamlining Windows deployment automation with PowerShell excellence.
