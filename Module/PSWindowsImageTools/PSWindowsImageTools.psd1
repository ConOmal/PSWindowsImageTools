@{
    # Script module or binary module file associated with this manifest.
    RootModule = 'bin\PSWindowsImageTools.dll'

    # Version number of this module.
    ModuleVersion = '2026.09.04.1'

    # Supported PSEditions
    CompatiblePSEditions = @('Desktop', 'Core')

    # ID used to uniquely identify this module
    GUID = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'

    # Author of this module
    Author = 'PSWindowsImageTools'

    # Company or vendor of this module
    CompanyName = 'PSWindowsImageTools'

    # Copyright statement for this module
    Copyright = 'Copyright (c) 2025 PSWindowsImageTools. All rights reserved.'

    # Description of the functionality provided by this module
    Description = 'Comprehensive PowerShell module for Windows image management, customization, and deployment automation. Features native DISM API integration, registry operations, driver management, wallpaper configuration, Autopilot setup, and Windows Update catalog integration with enterprise-grade tools for WIM/ESD manipulation.'

    # Minimum version of the PowerShell engine required by this module
    PowerShellVersion = '5.1'

    # Minimum version of Microsoft .NET Framework required by this module
    DotNetFrameworkVersion = '4.8'

    # Minimum version of the common language runtime (CLR) required by this module
    CLRVersion = '4.0'

    # Assemblies that must be loaded prior to importing this module
    RequiredAssemblies = @(
        'bin\PSWindowsImageTools.dll',
        'bin\Microsoft.Dism.dll',
        'bin\Registry.dll',
        'bin\Newtonsoft.Json.dll',
        'bin\HtmlAgilityPack.dll'
    )

    # Functions to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no functions to export.
    FunctionsToExport = @()

    # Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
    CmdletsToExport = @(
        # Core Windows Image Management
        'Get-WindowsImageList',
        'Mount-WindowsImageList',
        'Dismount-WindowsImageList',

        # ESD/ISO Conversion
        'Convert-ESDToWindowsImage',
        'Export-WindowsImage',
        'New-WindowsImageISO',

        # Windows 11 ISO Acquisition
        'Get-WindowsISODownloadInfo',
        'Save-WindowsISO',
        'Export-WindowsISO',

        # Windows Update Workflow
        'Search-WindowsUpdateCatalog',
        'Get-WindowsUpdateDownloadUrl',
        'Get-PatchTuesday',
        'Save-WindowsUpdateCatalogResult',
        'Install-WindowsImageUpdate',

        # Windows Release Information
        'Get-WindowsReleaseInfo',

        # Image Customization
        'Add-SetupCompleteAction',
        'Reset-WindowsImageBase',
        'Invoke-MediaDynamicUpdate',
        'Set-WindowsImageWallpaper',

        # Driver Management
        'Get-INFDriverList',
        'Add-INFDriverList',
        'Get-WindowsImageDriver',
        'Remove-WindowsImageDriver',
        'Compare-WindowsImageDriver',
        'Export-WindowsImageDriver',

        # ADK and Optional Component Management
        'Get-ADKInstallation',
        'Get-WinPEOptionalComponent',
        'Add-WinPEOptionalComponent',
        'Install-ADK',
        'Uninstall-ADK',

        # AppX Package Management
        'Remove-AppXProvisionedPackageList',

        # Package, Feature, and Capability Management
        'Get-WindowsImagePackageList',
        'Get-WindowsImageFeatureList',
        'Add-WindowsImagePackage',
        'Enable-WindowsImageFeature',
        'Disable-WindowsImageFeature',
        'Add-WindowsImageCapability',
        'Remove-WindowsImageCapability',

        # Component Store Analysis
        'Get-WindowsImageComponentStore',
        'Optimize-WindowsImageComponentStore',

        # Composite Health Check
        'Invoke-WindowsImageHealthCheck',

        # Servicing Chain Intelligence
        'Get-WindowsImageServicingChain',
        'Test-WindowsImageServicing',

        # Reserved Storage
        'Get-WindowsImageReservedStorage',
        'Set-WindowsImageReservedStorage',

        # Edition Servicing
        'Set-WindowsImageEdition',

        # WinRE Intelligence
        'Get-WindowsImageWinRE',

        # OOBE Configuration
        'Get-WindowsImageOOBE',
        'Set-WindowsImageOOBE',

        # Service Configuration
        'Get-WindowsImageService',
        'Set-WindowsImageService',

        # Scheduled Tasks
        'Get-WindowsImageScheduledTask',

        # Security Baselines
        'Get-WindowsImageSecurityBaseline',
        'Set-WindowsImageSecurityBaseline',

        # Boot Image Servicing
        'Get-WindowsBootImage',
        'Add-WindowsBootDriver',
        'Optimize-WindowsBootImage',

        # App Provisioning
        'Get-WindowsImageProvisionedApp',
        'Add-WindowsImageProvisionedApp',
        'Export-WindowsImageWinGetConfiguration',

        # Image Checkpoints
        'Checkpoint-WindowsImage',
        'Get-WindowsImageCheckpoint',
        'Restore-WindowsImageCheckpoint',

        # Recipe-Driven Image Builds
        'New-WindowsImageRecipe',
        'Test-WindowsImageRecipe',
        'Invoke-WindowsImageRecipe',

        # Mount Session & One-liner Servicing
        'Get-MountedWindowsImage',
        'Update-WindowsImageOnline',

        # Image Diffing
        'Get-WindowsImageSnapshot',
        'Compare-WindowsImage',
        'Export-WindowsImageSBOM',



        # Registry Operations
        'Get-RegistryOperationList',
        'Write-RegistryOperationList',
        'Get-RegistryHiveOnDemand',

        # Autopilot Configuration Management
        'Get-AutopilotConfiguration',
        'Set-AutopilotConfiguration',
        'Export-AutopilotConfiguration',
        'Install-AutopilotConfiguration',
        'New-AutopilotConfiguration',

        # Unattend XML Configuration Management
        'Get-UnattendXMLConfiguration',
        'Set-UnattendXMLConfiguration',
        'Export-UnattendXMLConfiguration',
        'Install-UnattendXMLConfiguration',
        'New-UnattendXMLConfiguration'
    )

    # Variables to export from this module
    VariablesToExport = @()

    # Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
    AliasesToExport = @()

    # Private data to pass to the module specified in RootModule/ModuleToProcess. This may also contain a PSData hashtable with additional module metadata used by PowerShell.
    PrivateData = @{
        PSData = @{
            # Tags applied to this module. These help with module discovery in online galleries.
            Tags = @('Windows', 'Image', 'WIM', 'ESD', 'ISO', 'DISM', 'Customization', 'Updates', 'WindowsUpdate', 'Catalog', 'Autopilot', 'Unattend', 'Drivers', 'WinPE', 'ADK', 'Recipe', 'PowerShell')

            # A URL to the license for this module.
            LicenseUri = 'https://www.gnu.org/licenses/gpl-3.0.html'

            # A URL to the main website for this project.
            ProjectUri = 'https://github.com/Grace-Solutions/PSWindowsImageTools'

            # ReleaseNotes of this module
            ReleaseNotes = @'
Modernized architecture and major feature expansion:

NEW CMDLETS (18):
• Recipe-driven builds: New/Test/Invoke-WindowsImageRecipe (declarative JSON image builds)
• Package/feature management: Get-WindowsImagePackageList, Get-WindowsImageFeatureList,
  Add-WindowsImagePackage, Enable/Disable-WindowsImageFeature, Add/Remove-WindowsImageCapability
• Export-WindowsImage (native WIM API: compression, boot flag, rename, index-by-name)
• New-WindowsImageISO (bootable ISOs via oscdimg)
• Get-MountedWindowsImage (cross-session mount registry)
• Update-WindowsImageOnline (one-liner: latest KB discovery, download, install)
• Image diffing: Get-WindowsImageSnapshot + Compare-WindowsImage (before/after audits)

IMPROVEMENTS:
• Unified DISM service: single API lifecycle, native progress callbacks, real error HRESULTs
• Consolidated registry services: in-memory hive reads (no mounting, no file handles)
• ISO input support in Get-WindowsImageList
• Full DISM write API implemented (packages, features, capabilities, AppX, drivers)
• Full Get-Help coverage for every cmdlet (PlatyPS-generated)
• 99 unit tests with CI; complete documentation rewrite

FIXES:
• Get-RegistryHiveOnDemand is now actually exported (was silently missing)
• Mount/unmount failures carry real DISM error messages
• Removed all GC-based handle workarounds
'@
        }
    }
}
