# Enriches PlatyPS markdown help stubs with real synopses/descriptions and strips empty examples.
$ErrorActionPreference = 'Stop'
$helpDir = Join-Path $PSScriptRoot "..\docs\help"
$onlineVersion = "https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md"
$help = @{
    'Add-INFDriverList' = @{ S = 'Installs drivers into mounted Windows images.'
                             D = "Adds all INF drivers from the provided driver list into the mounted images using the DISM API. Use Get-INFDriverList to discover drivers. ForceUnsigned installs unsigned drivers." }
    'Add-SetupCompleteAction' = @{ S = 'Adds custom first-boot actions to a Windows image.'
                             D = "Copies scripts/files into the image and registers them to run at setup completion. Supports inline commands, script files, and file copy operations with priorities." }
    'Add-WinPEOptionalComponent' = @{ S = 'Installs WinPE optional components into boot images.'
                             D = "Adds DISM optional components (e.g., PowerShell, WMI, .NET) to mounted WinPE boot images. Components come from Get-WinPEOptionalComponent." }
    'Add-WindowsImageCapability' = @{ S = 'Adds capabilities (Features on Demand) to mounted Windows images.'
                             D = "Adds DISM capabilities such as Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0. Optionally restrict sources with LimitAccess and provide offline SourcePath locations." }
    'Add-WindowsImagePackage' = @{ S = 'Installs .cab/.msu packages into mounted Windows images.'
                             D = "Adds one or more package files to each mounted image via the DISM AddPackage API. IgnoreCheck skips applicability checks; PreventPending blocks on pending operations." }
    'Compare-WindowsImage' = @{ S = 'Compares two Windows image snapshots to surface what changed.'
                             D = "Compares two inventory snapshots (from Get-WindowsImageSnapshot) and reports added, removed, and changed items per category (packages, features, capabilities, AppX, software). Accepts two mounted images or two snapshot JSON files - useful for before/after customization audits." }
    'Convert-ESDToWindowsImage' = @{ S = 'Converts ESD files to WIM format or folder layout.'
                             D = "Exports images from an ESD into a compressed WIM or extracts an installation tree. Supports image filters, compression type selection, and bootable flag handling." }
    'Disable-WindowsImageFeature' = @{ S = 'Disables Windows features in mounted images.'
                             D = "Disables one or more Windows features via the DISM API. RemovePayload also removes the feature's payload from the image." }
    'Dismount-WindowsImageList' = @{ S = 'Dismounts mounted Windows images with save or discard options.'
                             D = "Unmounts images committing changes (Save) or discarding them (Discard). Append merges into an existing WIM. Registered mount session entries are cleaned up automatically." }
    'Enable-WindowsImageFeature' = @{ S = 'Enables Windows features in mounted images.'
                             D = "Enables one or more Windows features via the DISM API. EnableAll includes parent features; SourcePath provides offline payload locations." }
    'Export-AutopilotConfiguration' = @{ S = 'Saves an Autopilot configuration to a JSON file.'
                             D = "Serializes an AutopilotConfiguration object to disk. Force overwrites existing files; PassThru returns the configuration object." }
    'Export-UnattendXMLConfiguration' = @{ S = 'Saves an Unattend XML configuration to a file.'
                             D = "Writes an UnattendXMLConfiguration object as XML with controllable encoding, indentation, and XML declaration handling." }
    'Export-WindowsImage' = @{ S = 'Exports images from a WIM/ESD file to a new WIM using the native WIM API.'
                             D = "Exports one image (by index or name) or all images into a new WIM with chosen compression (None/Fast/Max/Recovery), integrity checking, bootable flag, and optional destination rename/description. Supports in-place and multi-image exports." }
    'Get-ADKInstallation' = @{ S = 'Detects installed Windows ADK versions.'
                             D = "Enumerates installed Windows Assessment and Deployment Kit installations. Filters: Latest, MinimumVersion, RequireWinPE, RequireDeploymentTools, RequiredArchitecture." }
    'Get-AutopilotConfiguration' = @{ S = 'Loads an Autopilot JSON configuration.'
                             D = "Reads an Autopilot configuration file into a strongly typed object. Validate checks the JSON structure." }
    'Get-INFDriverList' = @{ S = 'Parses INF files and extracts driver information.'
                             D = "Scans directories for INF driver packages. Recurse includes subdirectories; ParseINF extracts detailed metadata for use with Add-INFDriverList." }
    'Get-MountedWindowsImage' = @{ S = 'Re-discovers mounted Windows images registered by previous cmdlet runs.'
                             D = "Lists active mounts from the cross-session mount registry. Mount-WindowsImageList, Dismount-WindowsImageList, and Get-WindowsImageList -SkipDismount maintain the registry automatically. Filter selects by image name; Prune removes stale entries." }
    'Get-PatchTuesday' = @{ S = 'Calculates Patch Tuesday dates.'
                             D = "Returns Patch Tuesday (second Tuesday) dates. Remaining lists upcoming dates; All lists every month of the calendar year; After filters by date." }
    'Get-RegistryHiveOnDemand' = @{ S = 'Reads registry data from offline hive files without mounting.'
                             D = "Parses hive files in memory (no RegLoadKey, no file handles held). SOFTWARE hives are auto-detected and return Windows version info, installed software, and Windows Update configuration. Use KeyPath with MaxDepth for arbitrary key trees." }
    'Get-RegistryOperationList' = @{ S = 'Parses .reg files into registry operations.'
                             D = "Converts registry editor files into RegistryOperation objects (create/modify/remove/remove-key). Recurse searches subdirectories; FilterHive and FilterOperation narrow results." }
    'Get-UnattendXMLConfiguration' = @{ S = 'Loads and inspects an Unattend XML configuration.'
                             D = "Reads an unattend.xml file into a typed object. Validate checks structure; ShowComponents/ShowElements/ElementFilter control inspection output." }
    'Get-WindowsImageFeatureList' = @{ S = 'Lists Windows features in mounted images.'
                             D = "Enumerates DISM features per mounted image. Filter narrows by regex on feature name." }
    'Get-WindowsImageList' = @{ S = 'Gets detailed information about Windows images in WIM/ESD/ISO files.'
                             D = "Enumerates images with edition, architecture, version, and language details. Advanced mounts each image to collect registry metadata; SkipDismount keeps images mounted (registered for Get-MountedWindowsImage); ISO files are mounted automatically to locate install.wim/esd. InclusionFilter/ExclusionFilter are scriptblocks evaluated per image." }
    'Get-WindowsImagePackageList' = @{ S = 'Lists DISM packages in mounted images.'
                             D = "Enumerates operating system packages per mounted image, including package state and install time. Filter narrows by regex on package name." }
    'Get-WindowsImageSnapshot' = @{ S = 'Captures an inventory snapshot of a mounted Windows image.'
                             D = "Collects packages, features, capabilities, provisioned AppX packages, and installed software (from the offline SOFTWARE hive) into an ImageSnapshot object. ExportPath writes the snapshot as JSON for later Compare-WindowsImage audits." }
    'Get-WindowsReleaseInfo' = @{ S = 'Gets Windows release history and KB information.'
                             D = "Retrieves Windows release history (10/11/Server) including availability dates and KB articles. After/Before filter by date; Detailed adds extended output." }
    'Get-WindowsUpdateDownloadUrl' = @{ S = 'Extracts download URLs from catalog search results.'
                             D = "Resolves the download URL for each update in the catalog results. DebugMode outputs troubleshooting detail." }
    'Get-WinPEOptionalComponent' = @{ S = 'Discovers available WinPE optional components.'
                             D = "Lists DISM optional components available for WinPE images from an installed ADK. Filters: Architecture, Category, Name, IncludeLanguagePacks." }
    'Install-ADK' = @{ S = 'Downloads and installs the latest Windows ADK.'
                             D = "Parses Microsoft's ADK download page for the latest version, downloads and silently installs it with optional WinPE add-on and Deployment Tools, then applies available patches. Skips when already installed unless Force is used." }
    'Install-AutopilotConfiguration' = @{ S = 'Applies an Autopilot configuration to mounted images.'
                             D = "Writes Autopilot JSON into the mounted image so devices enroll during OOBE. Force overwrites existing configuration files." }
    'Install-UnattendXMLConfiguration' = @{ S = 'Applies an Unattend XML configuration to mounted images.'
                             D = "Writes the unattend.xml into the mounted image's Windows\System32\Sysprep location. Encoding controls the written file's character set." }
    'Install-WindowsImageUpdate' = @{ S = 'Installs Windows updates into mounted images.'
                             D = "Installs .cab/.msu update packages into mounted images via the DISM API. Parameter sets accept downloaded WindowsUpdatePackage objects (pipeline) or file paths. IgnoreCheck skips applicability checks; PreventPending blocks on pending operations." }
    'Invoke-MediaDynamicUpdate' = @{ S = 'Applies Dynamic Updates to Windows installation media.'
                             D = "Services installation media in the documented order: Servicing Stack Update, SafeOS update, Cumulative Update, then Setup update, across boot.wim and install.wim. Supports cleanup, validation, auto-dismount, and result-only output." }
    'Invoke-WindowsImageRecipe' = @{ S = 'Applies a Windows image recipe to matching images.'
                             D = "Loads a BuildRecipe JSON, validates it, selects matching images by regex, then for each image: mounts read-write, applies enabled sections in deterministic order (AppX removal, file copy, wallpapers, features, drivers, updates, Features on Demand, registry modifications), and saves. MaxImages guards runaway selections; SkipValidation bypasses pre-flight checks." }
    'Mount-WindowsImageList' = @{ S = 'Mounts Windows images for modification.'
                             D = "Mounts images from Get-WindowsImageList (pipeline) into GUID-organized directories under MountRoot. ReadWrite enables modifications. Successful mounts register in the cross-session mount registry." }
    'New-AutopilotConfiguration' = @{ S = 'Creates a new Autopilot configuration.'
                             D = "Builds an AutopilotConfiguration object with tenant identity and optional device naming (e.g., %SERIAL%). Pipe to Export-AutopilotConfiguration to save or Install-AutopilotConfiguration to apply." }
    'New-UnattendXMLConfiguration' = @{ S = 'Creates a new Unattend XML configuration.'
                             D = "Generates an unattend.xml structure, optionally from a template with specific architecture, language, and configuration passes. IncludeSamples adds illustrative values." }
    'New-WindowsImageISO' = @{ S = 'Creates a bootable ISO from a Windows setup folder.'
                             D = "Uses oscdimg from an installed Windows ADK (Install-ADK -IncludeDeploymentTools) to build a UEFI/BIOS-bootable ISO with a chosen volume label. Force overwrites existing ISOs." }
    'New-WindowsImageRecipe' = @{ S = 'Creates a Windows image recipe scaffold JSON file.'
                             D = "Generates a BuildRecipe JSON with metadata and an optional image filter. Edit the file to add sections, validate with Test-WindowsImageRecipe, then apply with Invoke-WindowsImageRecipe." }
    'Remove-AppXProvisionedPackageList' = @{ S = 'Removes provisioned AppX packages from mounted images with regex filtering.'
                             D = "Enumerates provisioned AppX packages via DISM and removes those matching InclusionFilter, excluding matches of ExclusionFilter. Results include per-package success/failure detail." }
    'Remove-WindowsImageCapability' = @{ S = 'Removes capabilities from mounted Windows images.'
                             D = "Removes DISM capabilities (Features on Demand) by name. ContinueOnError processes remaining capabilities after a failure." }
    'Reset-WindowsImageBase' = @{ S = 'Performs component cleanup on mounted Windows images.'
                             D = "Runs DISM component cleanup (with optional ComponentCleanup /resetbase behavior) to shrink images by removing superseded components. AnalyzeOnly reports savings without cleaning; Defer defers cleanup tasks." }
    'Save-WindowsUpdateCatalogResult' = @{ S = 'Downloads update files with resume and integrity verification.'
                             D = "Downloads each catalog result's update file to DestinationPath. Resume continues interrupted downloads; Verify checks integrity; Force overwrites existing files." }
    'Search-WindowsUpdateCatalog' = @{ S = 'Searches the Microsoft Update Catalog.'
                             D = "Performs paged catalog searches with filters for architecture, classification, product, and maximum results. Accepts query strings from the pipeline. Output feeds Get-WindowsUpdateDownloadUrl and Save-WindowsUpdateCatalogResult." }
    'Set-AutopilotConfiguration' = @{ S = 'Modifies Autopilot configuration settings.'
                             D = "Updates tenant identity, device naming, OOBE behavior, enrollment, and update settings on an AutopilotConfiguration object. PassThru returns the modified object." }
    'Set-UnattendXMLConfiguration' = @{ S = 'Modifies elements in an Unattend XML configuration.'
                             D = "Sets, replaces, or removes elements located via XPath, optionally creating missing elements. PassThru returns the modified configuration." }
    'Set-WindowsImageWallpaper' = @{ S = 'Configures wallpaper and lockscreen images in mounted images.'
                             D = "Resizes and installs wallpaper/lockscreen images for multiple resolutions into the mounted image's branding locations, handling TrustedInstaller permissions automatically." }
    'Test-WindowsImageRecipe' = @{ S = 'Validates a Windows image recipe.'
                             D = "Checks recipe structure, regex patterns, referenced file paths, and section enablement. With ImagePath, also verifies the image filter selects at least one available image. Output includes all validation problems." }
    'Uninstall-ADK' = @{ S = 'Removes Windows ADK installations.'
                             D = "Uninstalls the latest ADK (or all with All) silently. Force skips confirmation prompts." }
    'Update-WindowsImageOnline' = @{ S = 'Discovers, downloads, and installs the latest updates into Windows images.'
                             D = "One-liner update servicing: discovers the latest cumulative KB for a Windows release (or uses -Query/-UpdatePackages), downloads from the Microsoft Update Catalog, then mounts, services, and saves each selected image. MaxImages and MaxUpdates bound the work; ContinueOnError keeps servicing after failures." }
    'Write-RegistryOperationList' = @{ S = 'Applies registry operations to mounted Windows images.'
                             D = "Applies RegistryOperation objects (from Get-RegistryOperationList) to mounted images using offline hive mounting. ContinueOnError processes remaining operations after failures." }
}

$count = 0
Get-ChildItem $helpDir -Filter *.md | ForEach-Object {
    $name = $_.BaseName
    $entry = $help[$name]
    if (-not $entry) {
        Write-Warning "No help mapping for $name"
        return
    }

    $md = Get-Content $_.FullName -Raw

    # Front matter: online version
    $md = [regex]::Replace($md, '(?m)^online version:\s*$', { param($m) "online version: $onlineVersion" })

    # Synopsis and Description (literal replacement via MatchEvaluator avoids $-substitution issues;
    # \s* handles CRLF line endings)
    $md = [regex]::Replace($md, '(?m)^\{\{ Fill in the Synopsis \}\}\s*$', { param($m) $entry.S })
    $md = [regex]::Replace($md, '(?m)^\{\{ Fill in the Description \}\}\s*$', { param($m) $entry.D })

    # Examples: replace placeholder block with a real example when provided
    if ($entry.Ex) {
        $exampleBlock = "## EXAMPLES`n`n### Example 1`n``````powershell`n$($entry.Ex)```````n`n$($entry.ExD)`n`n## PARAMETERS"
        $md = [regex]::Replace($md, '(?s)## EXAMPLES.*?## PARAMETERS', { param($m) $exampleBlock })
    }
    else {
        # Strip the placeholder example section entirely
        $md = [regex]::Replace($md, '(?s)## EXAMPLES.*?## PARAMETERS', { param($m) "## EXAMPLES`n`n## PARAMETERS" })
    }

    Set-Content $_.FullName $md -Encoding UTF8 -NoNewline
    $count++
}

Write-Output "Enriched $count help files"
