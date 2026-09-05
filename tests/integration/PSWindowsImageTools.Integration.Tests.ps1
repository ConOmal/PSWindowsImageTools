# Integration tests: exercise real DISM operations against a synthetic WIM.
# Requires an elevated session. Run via Tests/integration/run-integration.ps1.
# Tagged 'Integration' so unit-level CI runs never pick these up.

BeforeAll {
    $script:ModuleManifest = Join-Path $PSScriptRoot "..\..\Module\PSWindowsImageTools\PSWindowsImageTools.psd1"
    Import-Module $script:ModuleManifest -Force

    # Unique workspace per run
    $script:Workspace = Join-Path ([System.IO.Path]::GetTempPath()) "PSWIT-IT-$([Guid]::NewGuid().ToString('N'))"
    $script:SourceDir = Join-Path $script:Workspace "src"
    $script:MountRoot = Join-Path $script:Workspace "mounts"
    $script:BaselineWim = Join-Path $script:Workspace "baseline.wim"
    $script:ModifiedWim = Join-Path $script:Workspace "modified.wim"

    function New-IntegrationSource {
        # Build a tiny fake image layout. The SOFTWARE hive is a REAL hive (copy of the Default
        # user's NTUSER.DAT) so RegistryHiveOnDemand can parse it inside the mounted image.
        $dir = Join-Path $SourceDir "Windows\System32\config"
        New-Item -ItemType Directory -Force -Path $dir, (Join-Path $SourceDir "sources"), (Join-Path $SourceDir "boot") | Out-Null
        Set-Content -Path (Join-Path $SourceDir "marker.txt") -Value "integration-test"
        Copy-Item "C:\Users\Default\NTUSER.DAT" (Join-Path $dir "SOFTWARE") -Force
    }

    function New-IntegrationWim {
        param([string]$ImageFile, [string]$Name)
        dism /Capture-Image /ImageFile:$ImageFile /CaptureDir:$SourceDir /Name:$Name /Compress:max | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "DISM capture failed for $ImageFile (exit $LASTEXITCODE)" }
    }

    # Optional real-image mode: point PSWIT_IT_WIM at a WIM that has a real CBS
    # servicing stack + driver store to exercise the full servicing surface
    # (CI exports one from a clean Windows 11 24H2 install.wim for this). The
    # synthetic image is the default because it builds in seconds everywhere.
    $script:RealWim = $env:PSWIT_IT_WIM
    if ($script:RealWim -and (Test-Path $script:RealWim)) {
        Write-Host "Real-image mode: $script:RealWim"
        $script:BaselineWim = $script:RealWim
    }
    else {
        New-IntegrationSource
        New-IntegrationWim -ImageFile $BaselineWim -Name "Windows 11 Pro IT"
    }

    # The synthetic image has no CBS servicing stack or driver store, so offline
    # servicing queries (packages/features/capabilities/AppX/driver store) fail
    # even on healthy hosts. Probe once so affected tests skip honestly.
    $script:HasServicingStack = $false
    $script:HasDriverStore = $false
    try {
        $probe = Get-WindowsImageList -ImagePath $BaselineWim | Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        try {
            try { $null = $probe | Get-WindowsImagePackageList -ErrorAction Stop; $script:HasServicingStack = $true } catch { }
            try { $null = $probe | Get-WindowsImageDriver -ErrorAction Stop; $script:HasDriverStore = $true } catch { }
        }
        finally {
            $probe | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
    catch {
        Write-Host "Capability probe mount failed: $($_.Exception.Message)"
    }
    Write-Host "Synthetic image capabilities: servicing=$($script:HasServicingStack) drivers=$($script:HasDriverStore)"

    # Seed mount-root cleanup of stale entries so assertions are precise
    $script:CleanMountIds = @()
}

AfterAll {
    # Best-effort cleanup of any mounts left open by failed tests
    Get-MountedWindowsImage -ErrorAction SilentlyContinue | Where-Object {
        $_.MountPath -and $_.MountPath.FullName.StartsWith($script:Workspace)
    } | ForEach-Object {
        try { Dismount-WindowsImageList -Path $_.MountPath -Discard -Force -ErrorAction SilentlyContinue } catch { }
    }

    if ($script:Workspace -and (Test-Path $script:Workspace)) {
        Remove-Item $script:Workspace -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Describe "Integration: image discovery" -Tag Integration {

    It "discovers the synthetic image with correct metadata" {
        $images = Get-WindowsImageList -ImagePath $BaselineWim
        $images | Should -Not -BeNullOrEmpty
        $image = $images | Select-Object -First 1
        $image.Index | Should -Be 1
        $image.Name | Should -Be "Windows 11 Pro IT"
        $image.SourcePath | Should -BeLike "*.wim"
    }

    It "supports scriptblock filtering" {
        $match = Get-WindowsImageList -ImagePath $BaselineWim -InclusionFilter { $_.Name -like "*IT*" }
        $match | Should -HaveCount 1
        $none = Get-WindowsImageList -ImagePath $BaselineWim -InclusionFilter { $_.Name -like "*Nope*" }
        $none | Should -BeNullOrEmpty
    }
}

Describe "Integration: mount lifecycle" -Tag Integration {

    It "mounts read-write, registers in the session registry, and cleans up on save-dismount" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        $mounted | Should -Not -BeNullOrEmpty
        $mounted.Status.ToString() | Should -Be "Mounted"
        Test-Path $mounted.MountPath.FullName | Should -BeTrue
        Test-Path (Join-Path $mounted.MountPath.FullName "marker.txt") | Should -BeTrue

        # Registered for cross-session re-discovery
        $rediscovered = Get-MountedWindowsImage | Where-Object { $_.MountId -eq $mounted.MountId }
        $rediscovered | Should -Not -BeNullOrEmpty

        # Save-dismount cleans up the mount directory and the registry entry
        $result = $mounted | Dismount-WindowsImageList -Save -RemoveDirectories
        $result.Status.ToString() | Should -Be "Unmounted"

        $stillThere = Get-MountedWindowsImage | Where-Object { $_.MountId -eq $mounted.MountId }
        $stillThere | Should -BeNullOrEmpty
    }

    It "discards changes on discard-dismount" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        # Modify inside the image, then discard
        Set-Content -Path (Join-Path $mounted.MountPath.FullName "should-not-persist.txt") -Value "temp"
        $result = $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories
        $result.Status.ToString() | Should -Be "Unmounted"
    }
}

Describe "Integration: snapshot and diff" -Tag Integration {

    It "captures a complete snapshot with all five categories" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot

        try {
            $snapshot = $mounted | Get-WindowsImageSnapshot
            $snapshot | Should -Not -BeNullOrEmpty
            $snapshot.Packages | Should -Not -BeNullOrEmpty
            $snapshot.Features | Should -Not -BeNullOrEmpty
            $snapshot.Capabilities | Should -Not -BeNullOrEmpty
            $snapshot.AppxPackages | Should -Not -BeNull
            $snapshot.Software | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }

    It "exports and reimports snapshot JSON" -Skip:(-not $script:HasServicingStack) {
        $exportDir = Join-Path $Workspace "snapshots"
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot

        try {
            $snapshot = $mounted | Get-WindowsImageSnapshot -ExportPath $exportDir
            $file = Get-ChildItem $exportDir -Filter "snapshot_*.json" | Select-Object -First 1
            $file | Should -Not -BeNullOrEmpty

            $loaded = [PSWindowsImageTools.Services.ImageComparisonService]::LoadSnapshot($file.FullName)
            $loaded.ImageName | Should -Be $snapshot.ImageName
            $loaded.TotalItems | Should -Be $snapshot.TotalItems
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }
}

Describe "Integration: recipe end-to-end" -Tag Integration {

    It "applies copyFiles and registryModifications, persists after save, and shows up in the diff" -Skip:(-not $script:HasServicingStack) {
        # Copy baseline -> modified, then run a recipe against the modified WIM
        Copy-Item $BaselineWim $ModifiedWim -Force

        $recipe = [ordered]@{
            metadata = @{
                name        = "IT Recipe"
                description = "integration"
                version     = "1.0.0"
            }
            imageFilter = @{
                enabled             = $true
                inclusionExpression = "IT"
            }
            copyFiles = @{
                enabled = $true
                items   = @(
                    @{
                        source      = Join-Path $Workspace "recipe-file.txt"
                        destination = "Windows\IT-Recipe-File.txt"
                        overwrite   = $true
                    }
                )
            }
            registryModifications = @{
                enabled       = $true
                modifications = @(
                    @{ hive = "HKLM"; key = "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ITTestApp"; valueName = "DisplayName";    valueData = "IT Test App"; valueType = "String" },
                    @{ hive = "HKLM"; key = "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ITTestApp"; valueName = "DisplayVersion"; valueData = "9.9.9";     valueType = "String" },
                    @{ hive = "HKLM"; key = "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ITTestApp"; valueName = "Publisher";      valueData = "IT";          valueType = "String" }
                )
            }
        }
        $recipePath = Join-Path $Workspace "recipe.json"
        $recipe | ConvertTo-Json -Depth 6 | Set-Content $recipePath -Encoding UTF8

        # Baseline snapshot (reference) mounted read-only from the untouched WIM
        $referenceMounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot
        try {
            $script:referenceSnapshot = $referenceMounted | Get-WindowsImageSnapshot
        }
        finally {
            $referenceMounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }

        # Run the recipe on the modified WIM
        $results = Invoke-WindowsImageRecipe -RecipePath $recipePath -ImagePath $ModifiedWim -MountPath $MountRoot
        $results | Should -HaveCount 1
        $results[0].Success | Should -BeTrue
        $results[0].Sections | Should -Not -BeNullOrEmpty

        $copySection = $results[0].Sections | Where-Object SectionName -eq "copyFiles"
        $copySection.SuccessCount | Should -Be 1

        $regSection = $results[0].Sections | Where-Object SectionName -eq "registryModifications"
        $regSection.SuccessCount | Should -Be 3
        $regSection.FailureCount | Should -Be 0

        # Re-mount the saved image and verify both changes persisted
        $verifyMounted = Get-WindowsImageList -ImagePath $ModifiedWim |
            Mount-WindowsImageList -MountRoot $MountRoot
        try {
            Test-Path (Join-Path $verifyMounted.MountPath.FullName "Windows\IT-Recipe-File.txt") | Should -BeTrue

            $hiveResult = Get-RegistryHiveOnDemand -Path (Join-Path $verifyMounted.MountPath.FullName "Windows\System32\config\SOFTWARE") `
                -KeyPath "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ITTestApp" -MaxDepth 0
            $hiveResult | Should -Not -BeNullOrEmpty
            $appKey = $hiveResult['ITTestApp']
            $appKey | Should -Not -BeNullOrEmpty
            ($appKey.Values['DisplayName']) | Should -Be "IT Test App"
            ($appKey.Values['DisplayVersion']) | Should -Be "9.9.9"
        }
        finally {
            $verifyMounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }

        # Snapshot the modified image and diff against baseline: exactly one software addition
        $modifiedMounted = Get-WindowsImageList -ImagePath $ModifiedWim |
            Mount-WindowsImageList -MountRoot $MountRoot
        try {
            $modifiedSnapshot = $modifiedMounted | Get-WindowsImageSnapshot
        }
        finally {
            $modifiedMounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }

        $diff = [PSWindowsImageTools.Services.ImageComparisonService]::new().Compare($referenceSnapshot, $modifiedSnapshot)
        $diff.AreIdentical | Should -BeFalse

        $softwareDiff = $diff.Categories | Where-Object Category -eq "Software"
        $softwareDiff.Added | Should -HaveCount 1
        $softwareDiff.Added[0].Name | Should -Be "IT Test App"
        $softwareDiff.Removed | Should -HaveCount 0
    }
}

Describe "Integration: error contracts" -Tag Integration {

    It "reports a clear error for a missing package file" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $err = $null
            $mounted | Add-WindowsImagePackage -PackagePath (Join-Path $Workspace "does-not-exist.cab") -ErrorVariable err -ErrorAction SilentlyContinue
            $err | Should -Not -BeNullOrEmpty
            ($err | Where-Object { $_.FullyQualifiedErrorId -like "*PackageFileNotFound*" }) | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }

    It "returns a failed result for an invalid package with ContinueOnError" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $bogusCab = Join-Path $Workspace "bogus.cab"
            Set-Content $bogusCab -Value "this is not a real cab"

            $results = $mounted | Add-WindowsImagePackage -PackagePath $bogusCab -ContinueOnError -ErrorAction SilentlyContinue
            $results | Should -Not -BeNullOrEmpty
            $results[0].Success | Should -BeFalse
            $results[0].ErrorMessage | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }

    It "returns a failed result for a missing feature with ContinueOnError" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $results = $mounted | Enable-WindowsImageFeature -FeatureName "NoSuchFeature-IT" -ContinueOnError -ErrorAction SilentlyContinue
            $results | Should -Not -BeNullOrEmpty
            $results[0].Success | Should -BeFalse
            $results[0].ErrorMessage | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -Force -ErrorAction SilentlyContinue | Out-Null
        }
    }
}

Describe "Integration: component store" -Tag Integration {

    It "reports package counts and WinSxS size for a mounted image" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Get-WindowsImageComponentStore
            $report | Should -Not -BeNullOrEmpty
            $report.ImageName | Should -Be $mounted.ImageName
            $report.TotalPackages | Should -BeGreaterOrEqual 0
            $report.WinSxSSizeMB | Should -BeGreaterOrEqual 0
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }

    It "optimizes the component store and reports before/after" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = $mounted | Optimize-WindowsImageComponentStore -Confirm:$false
            $result | Should -Not -BeNullOrEmpty
            $result.Before | Should -Not -BeNullOrEmpty
            $result.ExitCode | Should -Be 0
            $result.After | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: image drivers" -Tag Integration {

    It "lists drivers for a mounted image without error" -Skip:(-not $script:HasDriverStore) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            { $mounted | Get-WindowsImageDriver } | Should -Not -Throw
            $allDrivers = $mounted | Get-WindowsImageDriver -All
            $allDrivers.Count | Should -BeGreaterThan 0
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }

    It "removes a third-party driver from a mounted image" -Skip:(-not $script:HasDriverStore) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $before = $mounted | Get-WindowsImageDriver
            if ($before.Count -gt 0) {
                $target = $before | Select-Object -First 1
                $result = $target | Remove-WindowsImageDriver -Confirm:$false
                $result.Success | Should -Be $true
                $after = $mounted | Get-WindowsImageDriver
                $after.PublishedName | Should -Not -Contain $target.PublishedName
            }
            else {
                Set-ItResult -Skipped -Because "synthetic baseline image has no third-party drivers to remove"
            }
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }

    It "exports a driver's files to a destination directory" -Skip:(-not $script:HasDriverStore) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        $exportDest = Join-Path $Workspace "driver-export"

        try {
            $drivers = $mounted | Get-WindowsImageDriver
            if ($drivers.Count -gt 0) {
                $drivers | Select-Object -First 1 | Export-WindowsImageDriver -DestinationPath $exportDest
                (Get-ChildItem $exportDest -Recurse -File).Count | Should -BeGreaterThan 0
            }
            else {
                Set-ItResult -Skipped -Because "synthetic baseline image has no third-party drivers to export"
            }
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: driver comparison" -Tag Integration {

    It "reports no differences between a mounted image and itself" -Skip:(-not $script:HasDriverStore) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = Compare-WindowsImageDriver -MountedImages @($mounted, $mounted)
            $result.Added | Should -BeNullOrEmpty
            $result.Removed | Should -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: health check" -Tag Integration {

    It "produces a health report with a computed OverallHealth" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Invoke-WindowsImageHealthCheck
            $report | Should -Not -BeNullOrEmpty
            $report.OverallHealth | Should -BeIn @("Healthy", "Warning", "Unhealthy")
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: SBOM export" -Tag Integration {

    It "exports a snapshot to an SBOM JSON file and round-trips" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        $sbomDest = Join-Path $Workspace "sbom-export"

        try {
            $snapshot = $mounted | Get-WindowsImageSnapshot
            $sbom = $snapshot | Export-WindowsImageSBOM -DestinationPath $sbomDest

            $sbom | Should -Not -BeNullOrEmpty
            $sbom.ImageName | Should -Be $mounted.ImageName

            $files = Get-ChildItem $sbomDest -Filter "sbom_*.json"
            $files.Count | Should -Be 1

            $roundTripped = Get-Content $files[0].FullName -Raw | ConvertFrom-Json
            $roundTripped.ImageName | Should -Be $mounted.ImageName
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: servicing chain" -Tag Integration {

    It "analyzes the servicing chain of a mounted image without error" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Get-WindowsImageServicingChain
            $report | Should -Not -BeNullOrEmpty
            $report.ImageName | Should -Be $mounted.ImageName
            # Real image: packages enumerate; the report reflects actual SSU/LCU state.
            $report.OrderingValid | Should -BeOfType [bool]
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }

    It "returns a boolean by default and a full report with -Detailed" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = $mounted | Test-WindowsImageServicing
            $result | Should -BeOfType [bool]

            $detailed = $mounted | Test-WindowsImageServicing -Detailed
            $detailed.OrderingValid | Should -Be $result
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: boot image servicing" -Tag Integration {

    It "adds drivers and optimizes a mounted boot image without error" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        $emptyDriverDir = Join-Path $Workspace "empty-drivers"
        New-Item -ItemType Directory -Force -Path $emptyDriverDir | Out-Null

        try {
            { $mounted | Add-WindowsBootDriver -DriverPath $emptyDriverDir -Confirm:$false } | Should -Not -Throw
            $result = $mounted | Optimize-WindowsBootImage -Confirm:$false
            $result | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: app provisioning" -Tag Integration {

    It "lists provisioned apps for a mounted image without error" -Skip:(-not $script:HasServicingStack) {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            { $mounted | Get-WindowsImageProvisionedApp } | Should -Not -Throw
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}

Describe "Integration: image checkpoint" -Tag Integration {

It "checkpoints, modifies, and restores a mounted image" {
        # Take the first mount result explicitly: the checkpoint/restore cmdlets take a
        # single MountedWindowsImage. Index the pipeline result directly (no @() wrapper —
        # that would re-wrap the emitted collection instead of unwrapping it).
        $mounted = (Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite)[0]

        try {
            $markerPath = Join-Path $mounted.MountPath.FullName "marker.txt"
            $checkpoint = $mounted | Checkpoint-WindowsImage -Label "baseline"
            $checkpoint | Should -Not -BeNullOrEmpty

            Set-Content -Path $markerPath -Value "modified-after-checkpoint"

            $checkpoint | Restore-WindowsImageCheckpoint -MountedImage $mounted -Confirm:$false

            Get-Content $markerPath -Raw | Should -Match "integration-test"
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
