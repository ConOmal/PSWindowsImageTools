# Media Dynamic Update Guide

This document provides a comprehensive guide to understanding and implementing Media Dynamic Update for Windows installation media, based on Microsoft's official documentation and best practices.

## Overview

Media Dynamic Update is a critical process that ensures Windows installation media contains the latest updates before deployment. This process eliminates the need for separate quality updates during in-place upgrades and preserves language packs and Features on Demand (FODs) that were previously installed.

### What is Dynamic Update?

Dynamic Update is one of the first steps when a Windows feature update installation begins. Windows Setup contacts Microsoft endpoints to fetch Dynamic Update packages and applies them to the operating system installation media. This process includes:

- **Setup Updates**: Updates to Setup.exe binaries and other Setup-related files
- **Safe OS Updates**: Updates for the Windows Recovery Environment (WinRE)
- **Servicing Stack Updates**: Critical updates necessary to complete feature updates
- **Latest Cumulative Updates**: The most recent quality updates
- **Driver Updates**: Manufacturer-published drivers specifically for Dynamic Update

## Dynamic Update Package Types

### Core Package Categories

| Package Type | Purpose | Target |
|--------------|---------|--------|
| **Setup Dynamic Update** | Updates Setup.exe and installation components | Installation media |
| **Safe OS Dynamic Update** | Updates Windows Recovery Environment | WinRE (winre.wim) |
| **Servicing Stack Update** | Updates servicing infrastructure | All images |
| **Latest Cumulative Update** | Quality and security updates | Operating system |
| **Driver Updates** | Hardware compatibility updates | Operating system |

### Package Identification by Windows Version

#### Windows 11 Version 22H2 and Later

Updates can be identified by **Title** alone:

- **Safe OS**: `YYYY-MM Safe OS Dynamic Update for Windows 11 Version 22H2`
- **Setup**: `YYYY-MM Setup Dynamic Update for Windows 11 Version 22H2`
- **Cumulative**: `YYYY-MM Cumulative Update for Windows 11 Version 22H2`
- **Servicing Stack**: `YYYY-MM Servicing Stack Update for Windows 11 Version 22H2`

#### Windows 11 Version 21H2

Requires **Title**, **Product**, and **Description** for identification:

| Update Type | Title | Product | Description |
|-------------|-------|---------|-------------|
| Safe OS | `YYYY-MM Dynamic Update for Windows 11` | Windows Safe OS Dynamic Update | ComponentUpdate |
| Setup | `YYYY-MM Dynamic Update for Windows 11` | Windows 10 and later Dynamic Update | SetupUpdate |
| Cumulative | `YYYY-MM Cumulative Update for Windows 11` | | |
| Servicing Stack | `YYYY-MM Servicing Stack Update for Windows 11 Version 21H2` | | |

#### Windows 10 Version 22H2

Similar to Windows 11 21H2, requires multiple fields for identification:

| Update Type | Title | Product | Description |
|-------------|-------|---------|-------------|
| Safe OS | `YYYY-MM Dynamic Update for Windows 10 Version 22H2` | Windows Safe OS Dynamic Update | ComponentUpdate |
| Setup | `YYYY-MM Dynamic Update for Windows 10 Version 22H2` | Windows 10 and later Dynamic Update | SetupUpdate |

#### Windows Server Versions

**Windows Server 2025** and **Server 23H2** use simplified title-based identification:
- `YYYY-MM Safe OS Dynamic Update for Microsoft server operating system version 24H2`
- `YYYY-MM Setup Dynamic Update for Microsoft server operating system version 24H2`

**Windows Server 2022** and **Azure Stack HCI 22H2** require full field identification similar to Windows 11 21H2.

## Acquiring Dynamic Update Packages

### Microsoft Update Catalog

Dynamic Update packages are available from the [Microsoft Update Catalog](https://catalog.update.microsoft.com).

#### Search Strategy

1. **Use Multiple Search Terms**: Different packages may not appear in a single search
2. **Check All Results**: Packages may be scattered throughout search results
3. **Verify Package Types**: Ensure you have all required package types
4. **Match Versions**: Ensure packages correspond to the same release month

#### Common Search Terms

- `Windows 11 Dynamic Update`
- `Safe OS Dynamic Update`
- `Setup Dynamic Update`
- `Cumulative Update [Version]`
- `Servicing Stack Update`

### Package Validation

Before using packages, verify:

1. **Version Compatibility**: Packages match your target Windows version
2. **Architecture Match**: x64, x86, ARM64 as appropriate
3. **Release Consistency**: All packages from the same update cycle
4. **File Integrity**: Download verification when possible

## Update Sequence and Target Images

### Target Image Files

The update process operates on multiple image files:

1. **WinPE (boot.wim)**: Windows Preinstallation Environment
2. **WinRE (winre.wim)**: Windows Recovery Environment
3. **Operating System (install.wim)**: Main Windows installation
4. **Installation Media**: Complete media file structure

### Correct Update Sequence

The following table shows the proper sequence for applying updates:

| Step | Target | Operation |
|------|--------|-----------|
| 1-7 | WinRE | Servicing stack → Languages → Localized packages → Fonts → TTS → Safe OS → Cleanup |
| 8 | WinRE | Export image |
| 9-16 | OS | Servicing stack → Languages → FOD → Optional Components → Cumulative → Cleanup → .NET → Export |
| 17-25 | WinPE | Servicing stack → Languages → Localized packages → Fonts → TTS → Lang.ini → Cumulative → Cleanup → Export |
| 26-28 | Media | Setup Dynamic Update → Setup.exe → Boot manager |

### Critical Sequencing Rules

1. **Servicing Stack First**: Always apply servicing stack updates before other updates
2. **Languages Before Features**: Install language packs before Features on Demand
3. **Cumulative Updates Last**: Apply cumulative updates after all other components
4. **Cleanup After Major Changes**: Perform image cleanup after significant modifications

## Checkpoint Cumulative Updates

Starting with Windows 11 24H2 and Windows Server 2025, Microsoft introduced **checkpoint cumulative updates**.

### Understanding Checkpoints

- **Prerequisite Updates**: Some cumulative updates require previous cumulative updates
- **Differential Updates**: File-level differences based on previous updates, not RTM
- **Smaller Packages**: Reduced download size and faster installation
- **Automatic Discovery**: DISM automatically discovers and installs required checkpoints

### Implementation

```powershell
# Place all related cumulative updates in a single folder
$updateFolder = "C:\Updates\Cumulative"

# DISM will automatically process checkpoints
Add-WindowsPackage -Path $mountPath -PackagePath $updateFolder
```

### Checkpoint Rules

1. **Folder-Based Processing**: Place target and checkpoint updates in the same folder
2. **Automatic Sequencing**: DISM processes updates in correct order
3. **Revision Checking**: Only updates with revision ≤ target are processed
4. **Clean Folder**: Only include relevant cumulative updates in the folder

## Language and Feature Customization

### Language Pack Integration

When adding languages to installation media:

1. **WinRE Language Support**:
   - Install language pack (`lp.cab`)
   - Add localized versions of installed optional packages
   - Include font support for the language
   - Add text-to-speech support

2. **Main OS Language Support**:
   - Install language pack
   - Add language-specific Features on Demand
   - Include font capabilities
   - Add OCR, handwriting, and speech features

3. **WinPE Language Support**:
   - Install language pack
   - Add localized optional components
   - Update `lang.ini` file
   - Include font and TTS support

### Features on Demand (FOD)

FOD packages should be installed:

1. **After Language Packs**: Languages must be installed first
2. **Before Cumulative Updates**: FOD installation before final updates
3. **With Proper Sources**: Use FOD ISO or online sources
4. **Architecture-Specific**: Match target architecture

### Optional Components

Legacy Windows features (Optional Components):

1. **Enable After Languages**: Install after language support
2. **Before Final Updates**: Enable before cumulative updates
3. **Handle Pending Operations**: May require online completion
4. **Cleanup Considerations**: May prevent offline cleanup

## Error Handling and Troubleshooting

### Common Issues

#### Combined Cumulative Update Errors

**Error 0x8007007e**: Known issue with combined cumulative updates in WinRE and WinPE
- **Cause**: Compatibility issue with combined SSU+LCU packages
- **Resolution**: Ignore error, continue processing
- **Impact**: No functional impact on final image

#### Image Cleanup Failures

**Error 0x800F0806 (CBS_E_PENDING)**: Pending operations prevent cleanup
- **Cause**: Optional Components requiring online operations
- **Resolution**: Skip cleanup or complete operations online
- **Impact**: Larger image size if cleanup skipped

#### Binary Version Mismatches

**Setup.exe/Setuphost.exe Mismatches**: Version conflicts between images
- **Cause**: Different update levels between WinPE and installation media
- **Resolution**: Copy updated binaries from WinPE to media
- **Critical**: Required for Windows 11 24H2 and later

### Best Practices for Error Prevention

1. **Version Consistency**: Use packages from the same update cycle
2. **Proper Sequencing**: Follow the documented update order
3. **Binary Synchronization**: Update Setup binaries from WinPE
4. **Cleanup Strategy**: Plan cleanup around Optional Components
5. **Validation**: Test updated media before deployment

## Performance Optimization

### Large Image Processing

1. **Reuse Serviced Images**: Save and reuse updated WinRE across editions
2. **Export After Updates**: Reduce image size through export
3. **Cleanup Strategy**: Balance size reduction vs. processing time
4. **Parallel Processing**: Process multiple editions efficiently

### Storage Considerations

1. **Working Space**: Ensure adequate temporary storage
2. **Image Exports**: Plan for image size during export operations
3. **Package Storage**: Organize packages for efficient access
4. **Cleanup Timing**: Remove temporary files promptly

## PowerShell Automation Scripts

Microsoft provides comprehensive PowerShell scripts for automating the Media Dynamic Update process. These scripts demonstrate the complete workflow from package acquisition to media finalization.

### Script Prerequisites

Before running automation scripts, ensure the following folder structure:

```
C:\mediaRefresh\
├── oldMedia\           # Original installation media
├── newMedia\           # Updated media (created by script)
├── packages\
│   ├── CU\            # Cumulative update packages
│   └── Other\         # Setup DU, Safe OS DU, .NET updates
├── temp\              # Working directory (created by script)
└── script.ps1         # Automation script
```

### Required Package Declarations

```powershell
#Requires -RunAsAdministrator

# Declare Dynamic Update packages
$LCU_PATH        = "C:\mediaRefresh\packages\CU\LCU.msu"
$SETUP_DU_PATH   = "C:\mediaRefresh\packages\Other\Setup_DU.cab"
$SAFE_OS_DU_PATH = "C:\mediaRefresh\packages\Other\SafeOS_DU.cab"
$DOTNET_CU_PATH  = "C:\mediaRefresh\packages\Other\DotNet_CU.msu"

# Features on Demand ISO
$FOD_ISO_PATH    = "C:\mediaRefresh\packages\CLIENT_LOF_PACKAGES_OEM.iso"
```

### Language and Feature Configuration

```powershell
# Optional language configuration (example: Japanese)
$LANG  = "ja-jp"
$LANG_FONT_CAPABILITY = "jpan"

# Features On Demand array
$FOD = @(
    'XPS.Viewer~~~~0.0.1.0'
)

# Legacy Optional Components
$OC = @(
    'MediaPlayback',
    'WindowsMediaPlayer'
)
```

### Working Directory Setup

```powershell
function Get-TS { return "{0:HH:mm:ss}" -f [DateTime]::Now }

# Create working directories
$WORKING_PATH    = "C:\mediaRefresh\temp"
$MAIN_OS_MOUNT   = "C:\mediaRefresh\temp\MainOSMount"
$WINRE_MOUNT     = "C:\mediaRefresh\temp\WinREMount"
$WINPE_MOUNT     = "C:\mediaRefresh\temp\WinPEMount"

New-Item -ItemType directory -Path $WORKING_PATH -ErrorAction Stop | Out-Null
New-Item -ItemType directory -Path $MAIN_OS_MOUNT -ErrorAction stop | Out-Null
New-Item -ItemType directory -Path $WINRE_MOUNT -ErrorAction stop | Out-Null
New-Item -ItemType directory -Path $WINPE_MOUNT -ErrorAction stop | Out-Null
```

### Media Preparation

```powershell
# Copy original media and remove read-only attributes
Write-Output "$(Get-TS): Copying original media to new media path"
Copy-Item -Path $MEDIA_OLD_PATH"\*" -Destination $MEDIA_NEW_PATH -Force -Recurse -ErrorAction stop | Out-Null
Get-ChildItem -Path $MEDIA_NEW_PATH -Recurse |
    Where-Object { -not $_.PSIsContainer -and $_.IsReadOnly } |
    ForEach-Object { $_.IsReadOnly = $false }
```

## WinRE Update Process

### Complete WinRE Update Workflow

```powershell
# Process each Windows edition in install.wim
$WINOS_IMAGES = Get-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\install.wim"

Foreach ($IMAGE in $WINOS_IMAGES) {
    # Mount main OS image
    Mount-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\install.wim" -Index $IMAGE.ImageIndex -Path $MAIN_OS_MOUNT

    if ($IMAGE.ImageIndex -eq "1") {
        # Extract and mount WinRE
        Copy-Item -Path $MAIN_OS_MOUNT"\windows\system32\recovery\winre.wim" -Destination $WORKING_PATH"\winre.wim" -Force
        Mount-WindowsImage -ImagePath $WORKING_PATH"\winre.wim" -Index 1 -Path $WINRE_MOUNT

        # Apply servicing stack update
        try {
            Add-WindowsPackage -Path $WINRE_MOUNT -PackagePath $LCU_PATH | Out-Null
        }
        Catch {
            if ($_.Exception -like "*0x8007007e*") {
                Write-Warning "Known issue with combined cumulative update, continuing..."
            } else {
                throw
            }
        }

        # Add language support (optional)
        Add-WindowsPackage -Path $WINRE_MOUNT -PackagePath $WINPE_OC_LP_PATH | Out-Null

        # Install localized packages for existing optional components
        $WINRE_INSTALLED_OC = Get-WindowsPackage -Path $WINRE_MOUNT
        Foreach ($PACKAGE in $WINRE_INSTALLED_OC) {
            if (($PACKAGE.PackageState -eq "Installed") -and
                ($PACKAGE.PackageName.startsWith("WinPE-")) -and
                ($PACKAGE.ReleaseType -eq "FeaturePack")) {

                $INDEX = $PACKAGE.PackageName.IndexOf("-Package")
                if ($INDEX -ge 0) {
                    $OC_CAB = $PACKAGE.PackageName.Substring(0, $INDEX) + "_" + $LANG + ".cab"
                    if ($WINPE_OC_LANG_CABS.Contains($OC_CAB)) {
                        $OC_CAB_PATH = Join-Path $WINPE_OC_LANG_PATH $OC_CAB
                        Add-WindowsPackage -Path $WINRE_MOUNT -PackagePath $OC_CAB_PATH | Out-Null
                    }
                }
            }
        }

        # Add Safe OS Dynamic Update
        Add-WindowsPackage -Path $WINRE_MOUNT -PackagePath $SAFE_OS_DU_PATH | Out-Null

        # Perform cleanup and export
        DISM /image:$WINRE_MOUNT /cleanup-image /StartComponentCleanup /ResetBase /Defer | Out-Null
        Dismount-WindowsImage -Path $WINRE_MOUNT -Save | Out-Null
        Export-WindowsImage -SourceImagePath $WORKING_PATH"\winre.wim" -SourceIndex 1 -DestinationImagePath $WORKING_PATH"\winre2.wim" | Out-Null
    }

    # Copy updated WinRE back to main OS
    Copy-Item -Path $WORKING_PATH"\winre2.wim" -Destination $MAIN_OS_MOUNT"\windows\system32\recovery\winre.wim" -Force
}
```

## Main OS Update Process

### Operating System Update Workflow

```powershell
# Update main operating system for each edition
Foreach ($IMAGE in $WINOS_IMAGES) {
    Mount-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\install.wim" -Index $IMAGE.ImageIndex -Path $MAIN_OS_MOUNT

    # Apply servicing stack update
    Add-WindowsPackage -Path $MAIN_OS_MOUNT -PackagePath $LCU_PATH | Out-Null

    # Add language support (optional)
    Add-WindowsPackage -Path $MAIN_OS_MOUNT -PackagePath $OS_LP_PATH | Out-Null

    # Add language-specific Features on Demand
    Add-WindowsCapability -Name "Language.Fonts.$LANG_FONT_CAPABILITY~~~und-$LANG_FONT_CAPABILITY~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsCapability -Name "Language.Basic~~~$LANG~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsCapability -Name "Language.OCR~~~$LANG~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsCapability -Name "Language.Handwriting~~~$LANG~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsCapability -Name "Language.TextToSpeech~~~$LANG~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsCapability -Name "Language.Speech~~~$LANG~0.0.1.0" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null

    # Add additional Features on Demand
    For ($index = 0; $index -lt $FOD.count; $index++) {
        Add-WindowsCapability -Name $($FOD[$index]) -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    }

    # Add Legacy Optional Components
    For ($index = 0; $index -lt $OC.count; $index++) {
        DISM /Image:$MAIN_OS_MOUNT /Enable-Feature /FeatureName:$($OC[$index]) /All | Out-Null
    }

    # Apply latest cumulative update (must be last)
    Add-WindowsPackage -Path $MAIN_OS_MOUNT -PackagePath $LCU_PATH | Out-Null

    # Perform image cleanup
    try {
        DISM /image:$MAIN_OS_MOUNT /cleanup-image /StartComponentCleanup | Out-Null
    }
    catch {
        if ($LastExitCode -eq -2146498554) {
            Write-Warning "Cleanup failed due to pending operations - image must be booted to complete"
        } else {
            throw
        }
    }

    # Add .NET 3.5 and cumulative update
    Add-WindowsCapability -Name "NetFX3~~~~" -Path $MAIN_OS_MOUNT -Source $FOD_PATH | Out-Null
    Add-WindowsPackage -Path $MAIN_OS_MOUNT -PackagePath $DOTNET_CU_PATH | Out-Null

    # Dismount and export
    Dismount-WindowsImage -Path $MAIN_OS_MOUNT -Save | Out-Null
    Export-WindowsImage -SourceImagePath $MEDIA_NEW_PATH"\sources\install.wim" -SourceIndex $IMAGE.ImageIndex -DestinationImagePath $WORKING_PATH"\install2.wim" | Out-Null
}

# Replace original install.wim with updated version
Move-Item -Path $WORKING_PATH"\install2.wim" -Destination $MEDIA_NEW_PATH"\sources\install.wim" -Force
```

## WinPE Update Process

### Windows Preinstallation Environment Updates

```powershell
# Update all images in boot.wim
$WINPE_IMAGES = Get-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\boot.wim"

Foreach ($IMAGE in $WINPE_IMAGES) {
    Mount-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\boot.wim" -Index $IMAGE.ImageIndex -Path $WINPE_MOUNT

    # Apply servicing stack update
    try {
        Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $LCU_PATH | Out-Null
    }
    Catch {
        if ($_.Exception -like "*0x8007007e*") {
            Write-Warning "Known issue with combined cumulative update, continuing..."
        } else {
            throw
        }
    }

    # Add language support
    Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $WINPE_OC_LP_PATH | Out-Null

    # Install localized packages for existing optional components
    $WINPE_INSTALLED_OC = Get-WindowsPackage -Path $WINPE_MOUNT
    Foreach ($PACKAGE in $WINPE_INSTALLED_OC) {
        if (($PACKAGE.PackageState -eq "Installed") -and
            ($PACKAGE.PackageName.startsWith("WinPE-")) -and
            ($PACKAGE.ReleaseType -eq "FeaturePack")) {

            $INDEX = $PACKAGE.PackageName.IndexOf("-Package")
            if ($INDEX -ge 0) {
                $OC_CAB = $PACKAGE.PackageName.Substring(0, $INDEX) + "_" + $LANG + ".cab"
                if ($WINPE_OC_LANG_CABS.Contains($OC_CAB)) {
                    $OC_CAB_PATH = Join-Path $WINPE_OC_LANG_PATH $OC_CAB
                    Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $OC_CAB_PATH | Out-Null
                }
            }
        }
    }

    # Add font and TTS support
    if (Test-Path -Path $WINPE_FONT_SUPPORT_PATH) {
        Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $WINPE_FONT_SUPPORT_PATH | Out-Null
    }

    if (Test-Path -Path $WINPE_SPEECH_TTS_PATH) {
        if (Test-Path -Path $WINPE_SPEECH_TTS_LANG_PATH) {
            Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $WINPE_SPEECH_TTS_PATH | Out-Null
            Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $WINPE_SPEECH_TTS_LANG_PATH | Out-Null
        }
    }

    # Update lang.ini for language support
    if (Test-Path -Path $WINPE_MOUNT"\sources\lang.ini") {
        DISM /image:$WINPE_MOUNT /Gen-LangINI /distribution:$WINPE_MOUNT | Out-Null
    }

    # Apply latest cumulative update (must be last)
    Add-WindowsPackage -Path $WINPE_MOUNT -PackagePath $LCU_PATH | Out-Null

    # Perform cleanup
    DISM /image:$WINPE_MOUNT /cleanup-image /StartComponentCleanup /ResetBase /Defer | Out-Null

    # Save critical files from second image (Setup environment)
    if ($IMAGE.ImageIndex -eq "2") {
        # Save setup.exe and setuphost.exe for version consistency
        Copy-Item -Path $WINPE_MOUNT"\sources\setup.exe" -Destination $WORKING_PATH"\setup.exe" -Force

        # setuphost.exe only required for Windows 11 24H2 and later
        $TEMP = Get-WindowsImage -ImagePath $MEDIA_NEW_PATH"\sources\boot.wim" -Index $IMAGE.ImageIndex
        if ([System.Version]$TEMP.Version -ge [System.Version]"10.0.26100") {
            Copy-Item -Path $WINPE_MOUNT"\sources\setuphost.exe" -Destination $WORKING_PATH"\setuphost.exe" -Force
        }

        # Save updated boot manager files
        Copy-Item -Path $WINPE_MOUNT"\Windows\boot\efi\bootmgfw.efi" -Destination $WORKING_PATH"\bootmgfw.efi" -Force
        Copy-Item -Path $WINPE_MOUNT"\Windows\boot\efi\bootmgr.efi" -Destination $WORKING_PATH"\bootmgr.efi" -Force
    }

    # Dismount and export
    Dismount-WindowsImage -Path $WINPE_MOUNT -Save | Out-Null
    Export-WindowsImage -SourceImagePath $MEDIA_NEW_PATH"\sources\boot.wim" -SourceIndex $IMAGE.ImageIndex -DestinationImagePath $WORKING_PATH"\boot2.wim" | Out-Null
}

# Replace original boot.wim with updated version
Move-Item -Path $WORKING_PATH"\boot2.wim" -Destination $MEDIA_NEW_PATH"\sources\boot.wim" -Force
```

## Media Finalization

### Setup Dynamic Update Application

```powershell
# Apply Setup Dynamic Update to installation media
cmd.exe /c $env:SystemRoot\System32\expand.exe $SETUP_DU_PATH -F:* $MEDIA_NEW_PATH"\sources" | Out-Null

# Replace setup binaries with updated versions from WinPE
Copy-Item -Path $WORKING_PATH"\setup.exe" -Destination $MEDIA_NEW_PATH"\sources\setup.exe" -Force

# Copy setuphost.exe if available (Windows 11 24H2+)
if (Test-Path -Path $WORKING_PATH"\setuphost.exe") {
    Copy-Item -Path $WORKING_PATH"\setuphost.exe" -Destination $MEDIA_NEW_PATH"\sources\setuphost.exe" -Force
}
```

### Boot Manager Updates

```powershell
# Update all boot manager files with serviced versions
$MEDIA_NEW_FILES = Get-ChildItem $MEDIA_NEW_PATH -Force -Recurse -Filter b*.efi

Foreach ($File in $MEDIA_NEW_FILES) {
    if (($File.Name -ieq "bootmgfw.efi") -or ($File.Name -ieq "bootx64.efi") -or
        ($File.Name -ieq "bootia32.efi") -or ($File.Name -ieq "bootaa64.efi")) {
        Copy-Item -Path $WORKING_PATH"\bootmgfw.efi" -Destination $File.FullName -Force
    }
    elseif ($File.Name -ieq "bootmgr.efi") {
        Copy-Item -Path $WORKING_PATH"\bootmgr.efi" -Destination $File.FullName -Force
    }
}
```

### Cleanup and Finalization

```powershell
# Remove working directories
Remove-Item -Path $WORKING_PATH -Recurse -Force

# Dismount ISO images
Dismount-DiskImage -ImagePath $FOD_ISO_PATH

Write-Output "$(Get-TS): Media refresh completed!"
```

## Integration with PSWindowsImageTools

### Production-Ready Dynamic Update Implementation

This section provides robust, production-ready PowerShell code for implementing Dynamic Update using PSWindowsImageTools cmdlets with comprehensive error handling and proper resource management.

#### Configuration and Setup

```powershell
#Requires -RunAsAdministrator
#Requires -Modules PSWindowsImageTools

# Import required modules
Import-Module PSWindowsImageTools -Force

# Configuration
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'Continue'

# Define paths
$MediaPath = "C:\Media"
$UpdatesPath = "C:\DynamicUpdates"
$MountBasePath = "C:\Mount"

# Create required directories
@($UpdatesPath, $MountBasePath) | ForEach-Object {
    if (-not (Test-Path $_)) { New-Item -ItemType Directory -Path $_ -Force | Out-Null }
}
```

#### Dynamic Update Search Expressions

```powershell
# Define comprehensive search expressions for Dynamic Update packages
$DynamicUpdateSearches = [System.Collections.Generic.List[PSCustomObject]]::new()

# Windows 11 Dynamic Updates
$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 11 Safe OS Dynamic Update'
    Type = 'SafeOS'
    Priority = 1
    TargetImages = @('Boot', 'Windows')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 11 Setup Dynamic Update'
    Type = 'Setup'
    Priority = 2
    TargetImages = @('Media')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 11 Servicing Stack Update'
    Type = 'ServicingStack'
    Priority = 3
    TargetImages = @('Boot', 'Windows')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 11 Cumulative Update'
    Type = 'Cumulative'
    Priority = 4
    TargetImages = @('Boot', 'Windows')
})

# Windows 10 Dynamic Updates
$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 10 Safe OS Dynamic Update'
    Type = 'SafeOS'
    Priority = 1
    TargetImages = @('Boot', 'Windows')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 10 Setup Dynamic Update'
    Type = 'Setup'
    Priority = 2
    TargetImages = @('Media')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 10 Servicing Stack Update'
    Type = 'ServicingStack'
    Priority = 3
    TargetImages = @('Boot', 'Windows')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Windows 10 Cumulative Update'
    Type = 'Cumulative'
    Priority = 4
    TargetImages = @('Boot', 'Windows')
})

# Server Dynamic Updates
$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Microsoft server operating system Safe OS Dynamic Update'
    Type = 'SafeOS'
    Priority = 1
    TargetImages = @('Boot', 'Windows')
})

$DynamicUpdateSearches.Add([PSCustomObject]@{
    Query = 'Microsoft server operating system Setup Dynamic Update'
    Type = 'Setup'
    Priority = 2
    TargetImages = @('Media')
})
```

#### Dynamic Update Package Acquisition

```powershell
# Search and download Dynamic Update packages
Write-Output "$(Get-Date -Format 'HH:mm:ss'): Starting Dynamic Update package acquisition"

$AllDynamicUpdates = [System.Collections.Generic.List[PSCustomObject]]::new()
$FailedSearches = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($SearchExpression in $DynamicUpdateSearches) {
    try {
        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Searching for $($SearchExpression.Type) updates: $($SearchExpression.Query)"

        $SearchResults = Search-WindowsUpdateCatalog -Query $SearchExpression.Query -Architecture x64 -MaxResults 10

        if ($SearchResults) {
            # Filter to most recent updates
            $LatestUpdates = $SearchResults |
                Sort-Object LastUpdated -Descending |
                Select-Object -First 3

            foreach ($Update in $LatestUpdates) {
                $Update | Add-Member -NotePropertyName 'UpdateType' -NotePropertyValue $SearchExpression.Type -Force
                $Update | Add-Member -NotePropertyName 'Priority' -NotePropertyValue $SearchExpression.Priority -Force
                $Update | Add-Member -NotePropertyName 'TargetImages' -NotePropertyValue $SearchExpression.TargetImages -Force
                $AllDynamicUpdates.Add($Update)
            }

            Write-Output "$(Get-Date -Format 'HH:mm:ss'): Found $($LatestUpdates.Count) $($SearchExpression.Type) updates"
        } else {
            Write-Warning "$(Get-Date -Format 'HH:mm:ss'): No results found for $($SearchExpression.Type) search"
            $FailedSearches.Add($SearchExpression)
        }
    }
    catch {
        Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to search for $($SearchExpression.Type) updates: $($_.Exception.Message)"
        $FailedSearches.Add($SearchExpression)
    }
}

# Download all found updates
if ($AllDynamicUpdates.Count -gt 0) {
    try {
        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Downloading $($AllDynamicUpdates.Count) Dynamic Update packages"

        $DownloadedUpdates = $AllDynamicUpdates |
            Get-WindowsUpdateDownloadUrl |
            Save-WindowsUpdateCatalogResult -DestinationPath $UpdatesPath

        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successfully downloaded $($DownloadedUpdates.Count) packages"
    }
    catch {
        Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to download Dynamic Update packages: $($_.Exception.Message)"
        throw
    }
} else {
    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): No Dynamic Update packages found to download"
}
```

#### Boot Image Dynamic Update Processing

```powershell
# Process all boot images (boot.wim) with Dynamic Updates
Write-Output "$(Get-Date -Format 'HH:mm:ss'): Starting boot image Dynamic Update processing"

$BootImagePath = Join-Path $MediaPath "sources\boot.wim"
$BootImageResults = [System.Collections.Generic.List[PSCustomObject]]::new()
$BootImageErrors = [System.Collections.Generic.List[PSCustomObject]]::new()

if (Test-Path $BootImagePath) {
    # Get all images in boot.wim
    $BootImages = Get-WindowsImageList -ImagePath $BootImagePath

    foreach ($BootImage in $BootImages) {
        $MountPath = Join-Path $MountBasePath "Boot_Index$($BootImage.ImageIndex)"
        $MountedImage = $null

        try {
            Write-Output "$(Get-Date -Format 'HH:mm:ss'): Processing boot image index $($BootImage.ImageIndex): $($BootImage.ImageName)"

            # Mount the boot image
            $MountedImage = Mount-WindowsImageList -ImagePath $BootImagePath -Index $BootImage.ImageIndex -MountPath $MountPath

            if ($MountedImage -and $MountedImage.Count -gt 0) {
                $CurrentMountPath = $MountedImage[0].MountPath

                # Filter updates applicable to boot images
                $BootUpdates = $DownloadedUpdates | Where-Object {
                    $_.TargetImages -contains 'Boot'
                } | Sort-Object Priority

                # Apply updates in priority order
                foreach ($Update in $BootUpdates) {
                    try {
                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Applying $($Update.UpdateType) update to boot image: $($Update.Title)"

                        $InstallResult = Install-WindowsUpdateFile -UpdatePath $Update.LocalFile -ImagePath $CurrentMountPath -ContinueOnError

                        $BootImageResults.Add([PSCustomObject]@{
                            ImageIndex = $BootImage.ImageIndex
                            ImageName = $BootImage.ImageName
                            UpdateType = $Update.UpdateType
                            UpdateTitle = $Update.Title
                            UpdateFile = $Update.LocalFile
                            Status = 'Success'
                            Timestamp = Get-Date
                        })

                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successfully applied $($Update.UpdateType) update"
                    }
                    catch {
                        $ErrorDetails = [PSCustomObject]@{
                            ImageIndex = $BootImage.ImageIndex
                            ImageName = $BootImage.ImageName
                            UpdateType = $Update.UpdateType
                            UpdateTitle = $Update.Title
                            UpdateFile = $Update.LocalFile
                            Error = $_.Exception.Message
                            Status = 'Failed'
                            Timestamp = Get-Date
                        }

                        $BootImageErrors.Add($ErrorDetails)
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to apply $($Update.UpdateType) update: $($_.Exception.Message)"

                        # Continue with other updates unless critical error
                        if ($Update.UpdateType -eq 'ServicingStack') {
                            Write-Error "$(Get-Date -Format 'HH:mm:ss'): Critical servicing stack update failed, stopping boot image processing"
                            break
                        }
                    }
                }

                # Perform image cleanup
                try {
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Performing cleanup on boot image index $($BootImage.ImageIndex)"

                    # Use DISM directly for cleanup as it's more reliable for boot images
                    $CleanupResult = & dism.exe /image:"$CurrentMountPath" /cleanup-image /StartComponentCleanup /ResetBase

                    if ($LASTEXITCODE -eq 0) {
                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successfully cleaned boot image"
                    } else {
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Image cleanup completed with warnings (Exit code: $LASTEXITCODE)"
                    }
                }
                catch {
                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Image cleanup failed: $($_.Exception.Message)"
                }
            } else {
                throw "Failed to mount boot image index $($BootImage.ImageIndex)"
            }
        }
        catch {
            $ErrorDetails = [PSCustomObject]@{
                ImageIndex = $BootImage.ImageIndex
                ImageName = $BootImage.ImageName
                UpdateType = 'MountOperation'
                UpdateTitle = 'Image Mount/Processing'
                UpdateFile = $BootImagePath
                Error = $_.Exception.Message
                Status = 'Failed'
                Timestamp = Get-Date
            }

            $BootImageErrors.Add($ErrorDetails)
            Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to process boot image index $($BootImage.ImageIndex): $($_.Exception.Message)"
        }
        finally {
            # Ensure image is properly dismounted
            if ($MountedImage -and $MountedImage.Count -gt 0) {
                try {
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Dismounting boot image index $($BootImage.ImageIndex)"
                    Dismount-WindowsImageList -MountPath $MountedImage[0].MountPath -Save
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successfully dismounted boot image"
                }
                catch {
                    Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to dismount boot image: $($_.Exception.Message)"

                    # Force dismount if normal dismount fails
                    try {
                        Dismount-WindowsImageList -MountPath $MountedImage[0].MountPath -Discard
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Force dismounted boot image (changes discarded)"
                    }
                    catch {
                        Write-Error "$(Get-Date -Format 'HH:mm:ss'): Force dismount also failed: $($_.Exception.Message)"
                    }
                }
            }

            # Clean up mount directory
            if (Test-Path $MountPath) {
                try {
                    Remove-Item -Path $MountPath -Recurse -Force -ErrorAction SilentlyContinue
                }
                catch {
                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to clean up mount directory: $MountPath"
                }
            }
        }
    }

    # Report boot image processing results
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Boot image processing completed"
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successful operations: $($BootImageResults.Count)"
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Failed operations: $($BootImageErrors.Count)"

    if ($BootImageErrors.Count -gt 0) {
        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Boot image errors occurred:"
        $BootImageErrors | ForEach-Object {
            Write-Warning "  - Index $($_.ImageIndex): $($_.UpdateType) - $($_.Error)"
        }
    }
} else {
    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Boot image not found at: $BootImagePath"
}
```

#### Windows Image Dynamic Update Processing

```powershell
# Process all Windows images (install.wim) with Dynamic Updates
Write-Output "$(Get-Date -Format 'HH:mm:ss'): Starting Windows image Dynamic Update processing"

$WindowsImagePath = Join-Path $MediaPath "sources\install.wim"
$WindowsImageResults = [System.Collections.Generic.List[PSCustomObject]]::new()
$WindowsImageErrors = [System.Collections.Generic.List[PSCustomObject]]::new()

if (Test-Path $WindowsImagePath) {
    # Get all images in install.wim
    $WindowsImages = Get-WindowsImageList -ImagePath $WindowsImagePath

    foreach ($WindowsImage in $WindowsImages) {
        $MountPath = Join-Path $MountBasePath "Windows_Index$($WindowsImage.ImageIndex)"
        $MountedImage = $null

        try {
            Write-Output "$(Get-Date -Format 'HH:mm:ss'): Processing Windows image index $($WindowsImage.ImageIndex): $($WindowsImage.ImageName)"

            # Mount the Windows image
            $MountedImage = Mount-WindowsImageList -ImagePath $WindowsImagePath -Index $WindowsImage.ImageIndex -MountPath $MountPath

            if ($MountedImage -and $MountedImage.Count -gt 0) {
                $CurrentMountPath = $MountedImage[0].MountPath

                # Filter updates applicable to Windows images
                $WindowsUpdates = $DownloadedUpdates | Where-Object {
                    $_.TargetImages -contains 'Windows'
                } | Sort-Object Priority

                # Apply updates in priority order with enhanced error handling
                foreach ($Update in $WindowsUpdates) {
                    try {
                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Applying $($Update.UpdateType) update to Windows image: $($Update.Title)"

                        # Special handling for different update types
                        switch ($Update.UpdateType) {
                            'ServicingStack' {
                                # Servicing stack updates are critical - fail fast if they don't work
                                $InstallResult = Install-WindowsUpdateFile -UpdatePath $Update.LocalFile -ImagePath $CurrentMountPath -ValidateImage
                                Write-Output "$(Get-Date -Format 'HH:mm:ss'): Critical servicing stack update applied successfully"
                            }
                            'SafeOS' {
                                # Safe OS updates for WinRE within the Windows image
                                $WinREPath = Join-Path $CurrentMountPath "Windows\System32\Recovery\winre.wim"
                                if (Test-Path $WinREPath) {
                                    # Extract, update, and replace WinRE
                                    $TempWinREPath = Join-Path $env:TEMP "winre_temp_$($WindowsImage.ImageIndex).wim"
                                    Copy-Item -Path $WinREPath -Destination $TempWinREPath -Force

                                    $WinREMountPath = Join-Path $MountBasePath "WinRE_Temp_$($WindowsImage.ImageIndex)"
                                    $WinREMounted = Mount-WindowsImageList -ImagePath $TempWinREPath -Index 1 -MountPath $WinREMountPath

                                    try {
                                        Install-WindowsUpdateFile -UpdatePath $Update.LocalFile -ImagePath $WinREMounted[0].MountPath
                                        Dismount-WindowsImageList -MountPath $WinREMounted[0].MountPath -Save
                                        Copy-Item -Path $TempWinREPath -Destination $WinREPath -Force
                                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): WinRE Safe OS update applied successfully"
                                    }
                                    finally {
                                        if ($WinREMounted) {
                                            try { Dismount-WindowsImageList -MountPath $WinREMounted[0].MountPath -Discard -ErrorAction SilentlyContinue } catch { }
                                        }
                                        if (Test-Path $TempWinREPath) { Remove-Item -Path $TempWinREPath -Force -ErrorAction SilentlyContinue }
                                        if (Test-Path $WinREMountPath) { Remove-Item -Path $WinREMountPath -Recurse -Force -ErrorAction SilentlyContinue }
                                    }
                                } else {
                                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): WinRE not found in Windows image, skipping Safe OS update"
                                }
                            }
                            'Cumulative' {
                                # Cumulative updates should be applied last and with validation
                                $InstallResult = Install-WindowsUpdateFile -UpdatePath $Update.LocalFile -ImagePath $CurrentMountPath -ValidateImage -ContinueOnError
                                Write-Output "$(Get-Date -Format 'HH:mm:ss'): Cumulative update applied successfully"
                            }
                            default {
                                # Standard update installation
                                $InstallResult = Install-WindowsUpdateFile -UpdatePath $Update.LocalFile -ImagePath $CurrentMountPath -ContinueOnError
                                Write-Output "$(Get-Date -Format 'HH:mm:ss'): $($Update.UpdateType) update applied successfully"
                            }
                        }

                        $WindowsImageResults.Add([PSCustomObject]@{
                            ImageIndex = $WindowsImage.ImageIndex
                            ImageName = $WindowsImage.ImageName
                            UpdateType = $Update.UpdateType
                            UpdateTitle = $Update.Title
                            UpdateFile = $Update.LocalFile
                            Status = 'Success'
                            Timestamp = Get-Date
                        })
                    }
                    catch {
                        $ErrorDetails = [PSCustomObject]@{
                            ImageIndex = $WindowsImage.ImageIndex
                            ImageName = $WindowsImage.ImageName
                            UpdateType = $Update.UpdateType
                            UpdateTitle = $Update.Title
                            UpdateFile = $Update.LocalFile
                            Error = $_.Exception.Message
                            Status = 'Failed'
                            Timestamp = Get-Date
                        }

                        $WindowsImageErrors.Add($ErrorDetails)
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to apply $($Update.UpdateType) update: $($_.Exception.Message)"

                        # Critical updates should stop processing
                        if ($Update.UpdateType -eq 'ServicingStack') {
                            Write-Error "$(Get-Date -Format 'HH:mm:ss'): Critical servicing stack update failed, stopping Windows image processing"
                            break
                        }
                    }
                }

                # Add custom setup actions for Dynamic Update tracking
                try {
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Adding setup complete actions for update tracking"

                    Add-SetupCompleteAction -ImagePath $CurrentMountPath -Command "echo Dynamic Update applied on %DATE% %TIME%" -Description "Dynamic Update completion marker"
                    Add-SetupCompleteAction -ImagePath $CurrentMountPath -Command "reg add HKLM\SOFTWARE\DynamicUpdate /v LastApplied /t REG_SZ /d %DATE% /f" -Description "Dynamic Update registry marker"

                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Setup complete actions added successfully"
                }
                catch {
                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to add setup complete actions: $($_.Exception.Message)"
                }

                # Perform image cleanup and optimization
                try {
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Performing cleanup and optimization on Windows image index $($WindowsImage.ImageIndex)"

                    # Component cleanup
                    $CleanupResult = & dism.exe /image:"$CurrentMountPath" /cleanup-image /StartComponentCleanup /ResetBase

                    if ($LASTEXITCODE -eq 0) {
                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Component cleanup completed successfully"
                    } else {
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Component cleanup completed with warnings (Exit code: $LASTEXITCODE)"
                    }

                    # Additional cleanup for superseded packages
                    $SupersededCleanup = & dism.exe /image:"$CurrentMountPath" /cleanup-image /SPSuperseded

                    if ($LASTEXITCODE -eq 0) {
                        Write-Output "$(Get-Date -Format 'HH:mm:ss'): Superseded package cleanup completed successfully"
                    } else {
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Superseded package cleanup completed with warnings (Exit code: $LASTEXITCODE)"
                    }
                }
                catch {
                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Image cleanup failed: $($_.Exception.Message)"
                }
            } else {
                throw "Failed to mount Windows image index $($WindowsImage.ImageIndex)"
            }
        }
        catch {
            $ErrorDetails = [PSCustomObject]@{
                ImageIndex = $WindowsImage.ImageIndex
                ImageName = $WindowsImage.ImageName
                UpdateType = 'MountOperation'
                UpdateTitle = 'Image Mount/Processing'
                UpdateFile = $WindowsImagePath
                Error = $_.Exception.Message
                Status = 'Failed'
                Timestamp = Get-Date
            }

            $WindowsImageErrors.Add($ErrorDetails)
            Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to process Windows image index $($WindowsImage.ImageIndex): $($_.Exception.Message)"
        }
        finally {
            # Ensure image is properly dismounted
            if ($MountedImage -and $MountedImage.Count -gt 0) {
                try {
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Dismounting Windows image index $($WindowsImage.ImageIndex)"
                    Dismount-WindowsImageList -MountPath $MountedImage[0].MountPath -Save
                    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successfully dismounted Windows image"
                }
                catch {
                    Write-Error "$(Get-Date -Format 'HH:mm:ss'): Failed to dismount Windows image: $($_.Exception.Message)"

                    # Force dismount if normal dismount fails
                    try {
                        Dismount-WindowsImageList -MountPath $MountedImage[0].MountPath -Discard
                        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Force dismounted Windows image (changes discarded)"
                    }
                    catch {
                        Write-Error "$(Get-Date -Format 'HH:mm:ss'): Force dismount also failed: $($_.Exception.Message)"
                    }
                }
            }

            # Clean up mount directory
            if (Test-Path $MountPath) {
                try {
                    Remove-Item -Path $MountPath -Recurse -Force -ErrorAction SilentlyContinue
                }
                catch {
                    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to clean up mount directory: $MountPath"
                }
            }
        }
    }

    # Report Windows image processing results
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Windows image processing completed"
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Successful operations: $($WindowsImageResults.Count)"
    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Failed operations: $($WindowsImageErrors.Count)"

    if ($WindowsImageErrors.Count -gt 0) {
        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Windows image errors occurred:"
        $WindowsImageErrors | ForEach-Object {
            Write-Warning "  - Index $($_.ImageIndex): $($_.UpdateType) - $($_.Error)"
        }
    }
} else {
    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Windows image not found at: $WindowsImagePath"
}
```

#### Comprehensive Results Reporting

```powershell
# Generate comprehensive operation report
Write-Output "$(Get-Date -Format 'HH:mm:ss'): Generating comprehensive Dynamic Update report"

$OverallResults = [PSCustomObject]@{
    StartTime = $StartTime
    EndTime = Get-Date
    TotalDuration = (Get-Date) - $StartTime
    SearchResults = @{
        TotalSearches = $DynamicUpdateSearches.Count
        SuccessfulSearches = $DynamicUpdateSearches.Count - $FailedSearches.Count
        FailedSearches = $FailedSearches.Count
        TotalPackagesFound = $AllDynamicUpdates.Count
        TotalPackagesDownloaded = $DownloadedUpdates.Count
    }
    BootImageResults = @{
        ImagesProcessed = $BootImages.Count
        SuccessfulOperations = $BootImageResults.Count
        FailedOperations = $BootImageErrors.Count
        SuccessRate = if ($BootImageResults.Count + $BootImageErrors.Count -gt 0) {
            [math]::Round(($BootImageResults.Count / ($BootImageResults.Count + $BootImageErrors.Count)) * 100, 2)
        } else { 0 }
    }
    WindowsImageResults = @{
        ImagesProcessed = $WindowsImages.Count
        SuccessfulOperations = $WindowsImageResults.Count
        FailedOperations = $WindowsImageErrors.Count
        SuccessRate = if ($WindowsImageResults.Count + $WindowsImageErrors.Count -gt 0) {
            [math]::Round(($WindowsImageResults.Count / ($WindowsImageResults.Count + $WindowsImageErrors.Count)) * 100, 2)
        } else { 0 }
    }
    OverallStatus = if (($BootImageErrors.Count + $WindowsImageErrors.Count) -eq 0) { 'Success' }
                   elseif (($BootImageResults.Count + $WindowsImageResults.Count) -gt 0) { 'Partial Success' }
                   else { 'Failed' }
}

# Display summary report
Write-Output ""
Write-Output "=== DYNAMIC UPDATE OPERATION SUMMARY ==="
Write-Output "Start Time: $($OverallResults.StartTime)"
Write-Output "End Time: $($OverallResults.EndTime)"
Write-Output "Total Duration: $($OverallResults.TotalDuration.ToString('hh\:mm\:ss'))"
Write-Output "Overall Status: $($OverallResults.OverallStatus)"
Write-Output ""
Write-Output "Package Acquisition:"
Write-Output "  - Searches Performed: $($OverallResults.SearchResults.TotalSearches)"
Write-Output "  - Successful Searches: $($OverallResults.SearchResults.SuccessfulSearches)"
Write-Output "  - Packages Found: $($OverallResults.SearchResults.TotalPackagesFound)"
Write-Output "  - Packages Downloaded: $($OverallResults.SearchResults.TotalPackagesDownloaded)"
Write-Output ""
Write-Output "Boot Image Processing:"
Write-Output "  - Images Processed: $($OverallResults.BootImageResults.ImagesProcessed)"
Write-Output "  - Successful Operations: $($OverallResults.BootImageResults.SuccessfulOperations)"
Write-Output "  - Failed Operations: $($OverallResults.BootImageResults.FailedOperations)"
Write-Output "  - Success Rate: $($OverallResults.BootImageResults.SuccessRate)%"
Write-Output ""
Write-Output "Windows Image Processing:"
Write-Output "  - Images Processed: $($OverallResults.WindowsImageResults.ImagesProcessed)"
Write-Output "  - Successful Operations: $($OverallResults.WindowsImageResults.SuccessfulOperations)"
Write-Output "  - Failed Operations: $($OverallResults.WindowsImageResults.FailedOperations)"
Write-Output "  - Success Rate: $($OverallResults.WindowsImageResults.SuccessRate)%"

# Export detailed results to files for analysis
try {
    $ReportPath = Join-Path $UpdatesPath "DynamicUpdate_Report_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    New-Item -ItemType Directory -Path $ReportPath -Force | Out-Null

    # Export summary report
    $OverallResults | ConvertTo-Json -Depth 10 | Out-File -FilePath (Join-Path $ReportPath "Summary.json") -Encoding UTF8

    # Export detailed results
    $BootImageResults | Export-Csv -Path (Join-Path $ReportPath "BootImage_Success.csv") -NoTypeInformation
    $BootImageErrors | Export-Csv -Path (Join-Path $ReportPath "BootImage_Errors.csv") -NoTypeInformation
    $WindowsImageResults | Export-Csv -Path (Join-Path $ReportPath "WindowsImage_Success.csv") -NoTypeInformation
    $WindowsImageErrors | Export-Csv -Path (Join-Path $ReportPath "WindowsImage_Errors.csv") -NoTypeInformation

    # Export package information
    $DownloadedUpdates | Export-Csv -Path (Join-Path $ReportPath "Downloaded_Packages.csv") -NoTypeInformation

    Write-Output "$(Get-Date -Format 'HH:mm:ss'): Detailed reports exported to: $ReportPath"
}
catch {
    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to export detailed reports: $($_.Exception.Message)"
}

# Final cleanup and validation
Write-Output ""
Write-Output "$(Get-Date -Format 'HH:mm:ss'): Performing final cleanup and validation"

# Validate that no images are still mounted
$MountedImages = Get-WindowsImage -Mounted -ErrorAction SilentlyContinue
if ($MountedImages) {
    Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Found $($MountedImages.Count) images still mounted - attempting cleanup"
    foreach ($MountedImage in $MountedImages) {
        try {
            Dismount-WindowsImage -Path $MountedImage.MountPath -Discard
            Write-Output "$(Get-Date -Format 'HH:mm:ss'): Cleaned up mounted image at $($MountedImage.MountPath)"
        }
        catch {
            Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to cleanup mounted image at $($MountedImage.MountPath): $($_.Exception.Message)"
        }
    }
}

# Clean up temporary mount directories
Get-ChildItem -Path $MountBasePath -Directory -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "$(Get-Date -Format 'HH:mm:ss'): Failed to cleanup mount directory $($_.FullName)"
    }
}

Write-Output ""
Write-Output "=== DYNAMIC UPDATE OPERATION COMPLETED ==="
Write-Output "Final Status: $($OverallResults.OverallStatus)"
Write-Output "Total Operations: $(($BootImageResults.Count + $WindowsImageResults.Count + $BootImageErrors.Count + $WindowsImageErrors.Count))"
Write-Output "Success Rate: $([math]::Round((($BootImageResults.Count + $WindowsImageResults.Count) / ($BootImageResults.Count + $WindowsImageResults.Count + $BootImageErrors.Count + $WindowsImageErrors.Count)) * 100, 2))%"

if ($OverallResults.OverallStatus -eq 'Success') {
    Write-Output "All Dynamic Update operations completed successfully!"
    exit 0
} elseif ($OverallResults.OverallStatus -eq 'Partial Success') {
    Write-Warning "Dynamic Update completed with some failures. Review error logs for details."
    exit 1
} else {
    Write-Error "Dynamic Update operation failed. Review error logs for details."
    exit 2
}
```

## Best Practices Summary

### Planning and Preparation

1. **Version Consistency**: Ensure all Dynamic Update packages are from the same release cycle
2. **Storage Planning**: Allocate sufficient space for working directories and image exports
3. **Backup Strategy**: Maintain original media for rollback scenarios
4. **Testing Environment**: Validate updated media in test environments before production

### Execution Guidelines

1. **Follow Sequence**: Adhere to the documented update sequence strictly
2. **Error Handling**: Implement proper error handling for known issues
3. **Binary Synchronization**: Always update Setup binaries from WinPE
4. **Cleanup Strategy**: Balance image size reduction with processing requirements

### Validation and Quality Assurance

1. **Image Integrity**: Validate image integrity after updates
2. **Functionality Testing**: Test installation and recovery scenarios
3. **Size Monitoring**: Monitor image size growth and optimize as needed
4. **Documentation**: Document customizations and update history

### Automation Considerations

1. **Script Modularity**: Break complex operations into manageable functions
2. **Progress Reporting**: Implement comprehensive progress reporting
3. **Logging**: Maintain detailed logs of all operations
4. **Recovery Procedures**: Plan for script failure recovery

This comprehensive guide provides the knowledge and tools necessary to implement Media Dynamic Update processes effectively, whether using Microsoft's reference scripts or integrating with PSWindowsImageTools cmdlets for enhanced functionality and automation.
