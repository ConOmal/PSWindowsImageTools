# Smoke test: Get-RegistryHiveOnDemand against a real registry hive
# Uses the Default user profile's NTUSER.DAT (present on every Windows installation, not locked)
param(
    [switch]$Verbose
)

if ($Verbose) { $VerbosePreference = 'Continue' }

try {
    Write-Output "Testing RegistryHiveOnDemand read path..."

    # Import the module relative to this script's location
    $ModulePath = Join-Path $PSScriptRoot "..\Module\PSWindowsImageTools\PSWindowsImageTools.psd1"
    Write-Output "Importing module from: $ModulePath"

    if (Test-Path $ModulePath) {
        Import-Module $ModulePath -Force -Verbose:$false
        Write-Output "Module imported successfully"
    } else {
        Write-Error "Module not found at: $ModulePath"
        exit 1
    }

    # Locate a real, non-locked hive
    $defaultUserProfile = Join-Path $env:SystemDrive "Users\Default\NTUSER.DAT"
    if (-not (Test-Path $defaultUserProfile)) {
        Write-Warning "Default user hive not found at $defaultUserProfile; skipping hive test"
        exit 0
    }

    # Copy to temp so we can also verify no file handle is held after reading
    $tempHive = Join-Path ([System.IO.Path]::GetTempPath()) "PSWIT-smoke-$([Guid]::NewGuid().ToString('N')).dat"
    Copy-Item $defaultUserProfile $tempHive -Force

    try {
        # Read an arbitrary key tree
        $result = Get-RegistryHiveOnDemand -Path $tempHive -KeyPath "Software" -MaxDepth 0
        if (-not $result -or -not $result.ContainsKey('Software')) {
            Write-Error "Get-RegistryHiveOnDemand did not return the Software key"
            exit 1
        }

        $subKeyCount = $result['Software'].SubKeys.Count
        Write-Output "Software key read OK ($subKeyCount subkeys)"

        # Verify no file handle is held after the read (deleting the copy must succeed)
        Remove-Item $tempHive -Force
        Write-Output "File handle release OK (hive copy deleted immediately after read)"

        Write-Output "`nRegistry service smoke test completed successfully!"
        exit 0
    }
    finally {
        if (Test-Path $tempHive) {
            Remove-Item $tempHive -Force -ErrorAction SilentlyContinue
        }
    }
} catch {
    Write-Error "Test failed: $($_.Exception.Message)"
    Write-Error "Stack trace: $($_.ScriptStackTrace)"
    exit 1
}
