# Windows Image Customization Guide

PSWindowsImageTools provides comprehensive capabilities for customizing Windows images with drivers, registry settings, applications, and enterprise configurations.

## Overview

Image customization in PSWindowsImageTools follows a structured approach:

1. **Mount Images** - Prepare images for modification
2. **Apply Customizations** - Install drivers, updates, configure settings
3. **Enterprise Configuration** - Autopilot, registry, AppX management
4. **Save Changes** - Commit modifications and dismount

## Core Customization Workflow

### Basic Image Preparation

```powershell
# Mount image for customization
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

# Verify mount status
$mounted | Where-Object { $_.Status -eq "Mounted" }
```

### Driver Integration

```powershell
# Discover and install drivers
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse -ParseHardwareIDs
$mounted | Add-INFDriverList -Drivers $drivers

# Install specific architecture drivers
$x64Drivers = Get-INFDriverList -Path "C:\Drivers" -Architecture amd64 -ParseHardwareIDs
$mounted | Add-INFDriverList -Drivers $x64Drivers -Force
```

### Windows Update Integration

```powershell
# Install latest cumulative updates
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 -MaxResults 3 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"

$mounted | Install-WindowsImageUpdate -UpdatePackages $updates -IgnoreCheck
```

## Advanced Customization Features

### Registry Configuration

```powershell
# Parse and apply registry settings
$regOps = Get-RegistryOperationList -Path "C:\Config\enterprise-settings.reg" -ParseValues
$mounted | Write-RegistryOperationList -Operations $regOps

# Example registry operations for enterprise settings
$enterpriseReg = @"
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate]
"WUServer"="https://wsus.company.com"
"WUStatusServer"="https://wsus.company.com"

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU]
"UseWUServer"=dword:00000001
"@

$enterpriseReg | Out-File "C:\Config\enterprise.reg" -Encoding ASCII
$regOps = Get-RegistryOperationList -Path "C:\Config\enterprise.reg"
$mounted | Write-RegistryOperationList -Operations $regOps
```

### AppX Package Management

```powershell
# Remove consumer applications
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire|Music|Video|News|Weather" -ExclusionFilter "Store|Calculator|Photos"

# Remove all except essential apps
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter ".*" -ExclusionFilter "Store|Calculator|Photos|Mail|Calendar|Camera"

# Remove gaming and entertainment with error handling
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Gaming|Solitaire" -ErrorAction Continue
```

### Autopilot Configuration

```powershell
# Create Autopilot configuration
$autopilot = New-AutopilotConfiguration -TenantId "12345678-1234-1234-1234-123456789012" -DeviceName "%SERIAL%" -ForcedEnrollment -UpdateTimeout 60

# Apply to mounted images
$mounted | Install-AutopilotConfiguration -Configuration $autopilot

# Load existing configuration and modify
$existingConfig = Get-AutopilotConfiguration -Path "C:\Config\autopilot.json"
$modifiedConfig = Set-AutopilotConfiguration -Configuration $existingConfig -DeviceName "%COMPUTERNAME%" -UpdateTimeout 120
$mounted | Install-AutopilotConfiguration -Configuration $modifiedConfig
```

### Custom Setup Actions

```powershell
# Add first-boot scripts with priorities
$mounted | Add-SetupCompleteAction -Command "powershell.exe -ExecutionPolicy Bypass -File C:\Scripts\domain-join.ps1" -Priority 10 -Description "Domain Join Script"

$mounted | Add-SetupCompleteAction -ScriptFile "C:\Scripts\enterprise-config.cmd" -Priority 20 -Description "Enterprise Configuration"

$mounted | Add-SetupCompleteAction -ScriptContent @"
echo Configuring enterprise settings...
reg add "HKLM\SOFTWARE\Company" /v "Configured" /t REG_SZ /d "True" /f
echo Configuration complete
"@ -Priority 30 -Description "Registry Configuration"
```

## Enterprise Deployment Scenarios

### Complete Enterprise Image

```powershell
# 1. Setup environment
Install-ADK -Force

# 2. Prepare base image
$images = Get-WindowsImageList -ImagePath "install.wim" | Where-Object { $_.ImageName -like "*Enterprise*" }
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite

# 3. Install drivers
$drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse -ParseHardwareIDs
$mounted | Add-INFDriverList -Drivers $drivers

# 4. Install updates
$latestRelease = Get-WindowsReleaseInfo -OperatingSystem "Windows 11" -Latest
$updates = Search-WindowsUpdateCatalog -Query $latestRelease.LatestKBArticle -Architecture x64 |
    Get-WindowsUpdateDownloadUrl |
    Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"
$mounted | Install-WindowsImageUpdate -UpdatePackages $updates

# 5. Configure enterprise settings
$regOps = Get-RegistryOperationList -Path "C:\Config\enterprise.reg" -ParseValues
$mounted | Write-RegistryOperationList -Operations $regOps

# 6. Remove consumer apps
$mounted | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Candy|Solitaire|Music|Video" -ExclusionFilter "Store|Calculator"

# 7. Configure Autopilot
$autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -DeviceName "%SERIAL%" -ForcedEnrollment
$mounted | Install-AutopilotConfiguration -Configuration $autopilot

# 8. Add setup scripts
$mounted | Add-SetupCompleteAction -ScriptFile "C:\Scripts\enterprise-setup.ps1" -Priority 10

# 9. Save and cleanup
$mounted | Dismount-WindowsImageList -Save
```

### Multi-Edition Customization

```powershell
# Customize multiple editions with different configurations
$allImages = Get-WindowsImageList -ImagePath "install.wim"

# Enterprise edition - full customization
$enterprise = $allImages | Where-Object { $_.ImageName -like "*Enterprise*" } | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
$enterprise | Add-INFDriverList -Drivers $drivers
$enterprise | Install-WindowsImageUpdate -UpdatePackages $updates
$enterprise | Remove-AppXProvisionedPackageList -InclusionFilter "Xbox|Gaming" -ExclusionFilter "Store"
$enterprise | Install-AutopilotConfiguration -Configuration $autopilot
$enterprise | Dismount-WindowsImageList -Save

# Professional edition - basic customization
$professional = $allImages | Where-Object { $_.ImageName -like "*Pro*" } | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
$professional | Add-INFDriverList -Drivers $drivers
$professional | Install-WindowsImageUpdate -UpdatePackages $updates
$professional | Dismount-WindowsImageList -Save
```

### WinPE Boot Image Customization

```powershell
# Customize WinPE boot image
$adk = Get-ADKInstallation -Latest -RequireWinPE
$components = Get-WinPEOptionalComponent -ADKInstallation $adk -Category "Scripting","Networking"

$winpe = Get-WindowsImageList -ImagePath "boot.wim" | Mount-WindowsImageList -MountPath "C:\WinPE" -ReadWrite

# Add PowerShell and networking support
$psComponents = $components | Where-Object { $_.Name -like "*PowerShell*" -or $_.Name -like "*NetFx*" -or $_.Name -like "*WMI*" }
$winpe | Add-WinPEOptionalComponent -Components $psComponents

# Add custom drivers to WinPE
$winpeDrivers = Get-INFDriverList -Path "C:\WinPE-Drivers" -Recurse
$winpe | Add-INFDriverList -Drivers $winpeDrivers

$winpe | Dismount-WindowsImageList -Save
```

## Best Practices

### Error Handling and Validation

```powershell
# Validate image before customization
$mounted = $images | Mount-WindowsImageList -MountPath "C:\Mount" -ReadWrite
if ($mounted.Status -ne "Mounted") {
    throw "Failed to mount image: $($mounted.ErrorMessage)"
}

# Use try-catch for critical operations
try {
    $mounted | Install-WindowsImageUpdate -UpdatePackages $updates -ContinueOnError
} catch {
    Write-Error "Update installation failed: $($_.Exception.Message)"
    $mounted | Dismount-WindowsImageList -Discard
    throw
}

# Validate customizations before saving
$mounted | Add-SetupCompleteAction -Command "echo Validation complete" -Priority 999 -Description "Validation marker"
$mounted | Dismount-WindowsImageList -Save
```

### Performance Optimization

```powershell
# Batch operations for efficiency
$allCustomizations = @{
    Drivers = Get-INFDriverList -Path "C:\Drivers" -Recurse
    Updates = Search-WindowsUpdateCatalog -Query "Cumulative" -Architecture x64 -MaxResults 3 | Get-WindowsUpdateDownloadUrl | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates"
    RegistryOps = Get-RegistryOperationList -Path "C:\Config\settings.reg"
    Autopilot = New-AutopilotConfiguration -TenantId "your-tenant-id" -DeviceName "%SERIAL%"
}

# Apply all customizations in sequence
$mounted | Add-INFDriverList -Drivers $allCustomizations.Drivers
$mounted | Install-WindowsImageUpdate -UpdatePackages $allCustomizations.Updates
$mounted | Write-RegistryOperationList -Operations $allCustomizations.RegistryOps
$mounted | Install-AutopilotConfiguration -Configuration $allCustomizations.Autopilot
```

## Troubleshooting

### Common Issues

1. **Mount failures**: Ensure no other processes are using the mount directory
2. **Driver installation failures**: Verify INF files are valid and architecture matches
3. **Registry operation failures**: Check registry file syntax and permissions
4. **AppX removal failures**: Some packages may be protected; use `-ErrorAction Continue`

### Debugging Commands

```powershell
# Check mount status
Get-WindowsImage -Mounted

# Validate driver files
$drivers | Where-Object { -not $_.ComponentFile.Exists } | ForEach-Object { Write-Warning "Missing: $($_.ComponentFile.FullName)" }

# Test registry operations
$regOps | Where-Object { $_.Operation -eq "Create" } | ForEach-Object { Write-Output "Creating: $($_.Key)" }

# Verify Autopilot configuration
$autopilot | ConvertTo-Json -Depth 10
```

This comprehensive customization framework enables enterprise-grade Windows image preparation with full automation and tracking capabilities.
