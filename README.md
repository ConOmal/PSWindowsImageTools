# PSWindowsImageTools

A comprehensive PowerShell module for Windows image management, customization, and deployment automation. Built for enterprise environments requiring advanced WIM/ESD manipulation, driver integration, registry operations, and system customization with native Windows APIs and best practices.

## 🚀 Key Features

### 🖼️ **Advanced Windows Image Management**
- Mount/unmount WIM and ESD files with native DISM API integration
- Read-only and read-write mount modes with automatic permission management
- Robust mount point management with GUID-based temporary directories
- Advanced image information retrieval with comprehensive registry data extraction
- Full pipeline support for batch operations with progress tracking
- Automatic cleanup and error handling with proper resource disposal

### 📦 **Comprehensive Update Management**
- Search Microsoft Update Catalog with advanced filtering and KB correlation
- Download updates with resume capability and integrity verification
- Install CAB/MSU files into mounted images with validation and progress tracking
- Dynamic update integration for Windows installation media
- Patch Tuesday automation with scheduling and rollback capabilities
- Support for both file-based and pipeline workflows with error handling

### 🛠️ **ADK Management & Automation**
- Automatic detection and installation of latest Windows ADK
- Dynamic parsing of Microsoft's download pages
- WinPE Optional Component management with validation
- Enhanced process monitoring with command-line transparency

### 🔧 **Advanced Image Customization**
- Registry operations with direct hive reading and mounting/unmounting
- Driver integration with INF parsing, hardware ID extraction, and validation
- Wallpaper and lockscreen configuration with multiple resolution support
- Native Windows API integration for permission management (TrustedInstaller)
- Autopilot configuration management with JSON validation
- Unattend.xml creation, modification, and validation
- AppX package removal with advanced regex filtering and dependency checking
- Custom setup actions and first-boot scripts with comprehensive error handling

### 📊 **Enterprise Integration & Features**
- WSUS and Windows Update for Business support with policy management
- Active Directory integration for deployment automation
- Group Policy and registry customization with validation
- Hardware-specific driver deployment with automatic detection
- Windows release information and KB correlation with comprehensive reporting
- PowerShell 5.1 and 7+ compatibility with cross-platform considerations
- Comprehensive logging, progress reporting, and automated testing workflows

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
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates
$mounted | Dismount-WindowsImageList -Save
```

## 📋 Complete Cmdlet Reference

### **ADK & Component Management**
| Cmdlet | Description |
|--------|-------------|
| `Get-ADKInstallation` | Detect installed Windows ADK versions |
| `Install-ADK` | Download and install latest ADK with patches |
| `Uninstall-ADK` | Remove ADK installations |
| `Get-WinPEOptionalComponent` | Discover available WinPE components |
| `Add-WinPEOptionalComponent` | Install components into boot images |

### **Image Management**
| Cmdlet | Description |
|--------|-------------|
| `Get-WindowsImageList` | Enumerate images in WIM/ESD files |
| `Mount-WindowsImageList` | Mount images for modification |
| `Dismount-WindowsImageList` | Unmount and save changes |
| `Convert-ESDToWindowsImage` | Convert ESD to WIM format |
| `Reset-WindowsImageBase` | Reset image base and cleanup |

### **Windows Update Workflow**
| Cmdlet | Description |
|--------|-------------|
| `Search-WindowsUpdateCatalog` | Search Microsoft Update Catalog |
| `Get-WindowsUpdateDownloadUrl` | Extract download URLs |
| `Save-WindowsUpdateCatalogResult` | Download with resume capability |
| `Install-WindowsImageUpdate` | Install updates into mounted images |
| `Get-PatchTuesday` | Calculate Patch Tuesday dates |

### **Image Customization**
| Cmdlet | Description |
|--------|-------------|
| `Get-INFDriverList` | Parse INF files and extract driver info |
| `Add-INFDriverList` | Install drivers into mounted images |
| `Set-WindowsImageWallpaper` | Configure wallpaper and lockscreen images |
| `Remove-AppXProvisionedPackageList` | Remove AppX packages with filtering |
| `Get-RegistryOperationList` | Parse registry files |
| `Write-RegistryOperationList` | Apply registry operations |
| `Read-RegistryHiveOnDemand` | Read registry hives without mounting |
| `Add-SetupCompleteAction` | Add custom first-boot actions |
| `Install-WindowsUpdateFile` | Install CAB/MSU files into images |
| `Reset-WindowsImageBase` | Reset image base for space optimization |

### **Autopilot & Configuration**
| Cmdlet | Description |
|--------|-------------|
| `Get-AutopilotConfiguration` | Load Autopilot JSON configuration |
| `Set-AutopilotConfiguration` | Modify Autopilot settings |
| `Export-AutopilotConfiguration` | Save Autopilot configuration |
| `Install-AutopilotConfiguration` | Apply to mounted images |
| `New-AutopilotConfiguration` | Create new configuration |
| `Install-UnattendXMLConfiguration` | Apply unattend.xml to images |

### **WinPE & Optional Components**
| Cmdlet | Description |
|--------|-------------|
| `Add-WinPEOptionalComponent` | Add optional components to WinPE |
| `Invoke-MediaDynamicUpdate` | Apply dynamic updates to installation media |



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
$latestRelease = Get-WindowsReleaseInfo -OperatingSystem "Windows 11" -Latest
$updates = Search-WindowsUpdateCatalog -Query $latestRelease.LatestKBArticle -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

# 3. Customize image with drivers and updates
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

# Install drivers
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse
$mounted | Add-INFDriverList -Drivers $drivers

# Install updates
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates

# Configure wallpaper and lockscreen
$mounted | Set-WindowsImageWallpaper -WallpaperPath "C:\Branding\wallpaper.jpg" -LockscreenPath "C:\Branding\lockscreen.jpg"

# Configure Autopilot
$autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -DeviceName "%SERIAL%"
$mounted | Install-AutopilotConfiguration -Configuration $autopilot

# Remove unwanted AppX packages
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire" -ExclusionFilter "Store|Calculator"

# Save and cleanup
$mounted | Dismount-WindowsImageList -Save
```

### **Wallpaper and Lockscreen Configuration**
```powershell
# Configure wallpaper and lockscreen for mounted images
$mounted = Get-WindowsImageList -ImagePath "install.wim" | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

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
$nextPatchTuesday = Get-PatchTuesday -Next

# Setup automated download for that date
$updates = Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 |
    Where-Object { $_.LastModified.Date -eq $nextPatchTuesday.Date } |
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
$mounted = $winpe | Mount-WindowsImageList -MountPath "C:\WinPE\Mount" -ReadWrite

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
