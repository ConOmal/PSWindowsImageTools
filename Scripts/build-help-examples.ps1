# Adds real examples to major cmdlets' help markdown.
$ErrorActionPreference = 'Stop'
$helpDir = Join-Path $PSScriptRoot "..\docs\help"

$examples = @{
    'Get-WindowsImageList' = "Get-WindowsImageList -ImagePath `"C:\Images\install.wim```n" +
"Get-WindowsImageList -ImagePath `"C:\Media\Win11.iso```" -InclusionFilter { `$_.Name -like `"*Pro*`" } -Advanced"
    'Mount-WindowsImageList' = "`$images = Get-WindowsImageList -ImagePath `"install.wim```" -InclusionFilter { `$_.Name -like `"*Pro*`" }`n" +
"`$mounted = `$images | Mount-WindowsImageList -ReadWrite -MountRoot `"C:\Mount```""
    'Dismount-WindowsImageList' = "`$mounted | Dismount-WindowsImageList -Save -RemoveDirectories"
    'Search-WindowsUpdateCatalog' = "`$updates = Search-WindowsUpdateCatalog -Query `"Windows 11 Cumulative`" -Architecture x64 -MaxResults 10"
    'Save-WindowsUpdateCatalogResult' = "`$urls | Save-WindowsUpdateCatalogResult -DestinationPath `"C:\Updates`" -Verify"
    'Install-WindowsImageUpdate' = "`$mounted | Install-WindowsImageUpdate -UpdatePackages `$packages"
    'Get-RegistryHiveOnDemand' = "Get-RegistryHiveOnDemand -Path `"C:\Mount\Windows\System32\config\SOFTWARE```" -KeyPath `"Microsoft\Windows NT\CurrentVersion`""
    'Invoke-WindowsImageRecipe' = "Invoke-WindowsImageRecipe -RecipePath `"C:\Recipes\corporate.json`" -ImagePath `"install.wim```""
    'Export-WindowsImage' = "Export-WindowsImage -SourcePath `"install.esd`" -DestinationPath `"install.wim`" -CompressionType Max"
    'Update-WindowsImageOnline' = "Update-WindowsImageOnline -ImagePath `"C:\Images\install.wim`" -Architecture x64"
    'Compare-WindowsImage' = "`$diff = Compare-WindowsImage -ReferencePath vanilla.json -DifferencePath corporate.json`n" +
"`$diff.Categories | Format-Table Category, Count"
    'Get-WindowsImageSnapshot' = "`$mounted | Get-WindowsImageSnapshot -ExportPath `"C:\Snapshots```""
    'Add-WindowsImagePackage' = "`$mounted | Add-WindowsImagePackage -PackagePath `"C:\Updates\KB5065429.msu```""
    'Enable-WindowsImageFeature' = "`$mounted | Enable-WindowsImageFeature -FeatureName `"NetFx3`" -EnableAll"
    'New-WindowsImageISO' = "New-WindowsImageISO -SourcePath `"C:\Media\Win11`" -OutputIsoPath `"C:\Media\Win11.iso`" -BootMode Both"
    'Get-MountedWindowsImage' = "Get-MountedWindowsImage -Filter `"Pro`"`n" +
"Get-MountedWindowsImage -Prune"
    'Convert-ESDToWindowsImage' = "Convert-ESDToWindowsImage -SourcePath `"install.esd`" -OutputPath `"install.wim`" -Mode WIM -CompressionType Max"
    'Get-PatchTuesday' = "`$next = Get-PatchTuesday -Remaining | Select-Object -First 1"
    'Add-WindowsImageCapability' = "`$mounted | Add-WindowsImageCapability -CapabilityName `"Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0```""
    'Reset-WindowsImageBase' = "`$mounted | Reset-WindowsImageBase -ComponentCleanup"
}

$count = 0
foreach ($name in $examples.Keys) {
    $file = Join-Path $helpDir "$name.md"
    if (-not (Test-Path $file)) {
        Write-Warning "No help file for $name"
        continue
    }

    $md = Get-Content $file -Raw
    if ($md -notmatch '(?s)## EXAMPLES\s*## PARAMETERS') {
        Write-Warning "Examples section already filled for $name"
        continue
    }

    $block = "## EXAMPLES`n`n### Example 1`n``````powershell`n" + $examples[$name] + "`n```````n`nPerforms the operation shown above.`n`n## PARAMETERS"
    # Literal replacement via MatchEvaluator avoids PowerShell $-substitution mangling code examples
    $md = [regex]::Replace($md, '(?s)## EXAMPLES\s*## PARAMETERS', { param($m) $block })
    Set-Content $file $md -Encoding UTF8 -NoNewline
    $count++
}

Write-Output "Added examples to $count help files"
