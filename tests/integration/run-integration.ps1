# Runs the PSWindowsImageTools integration test suite locally.
# Requires: elevated session (DISM image operations) and Windows with DISM available.
# These tests are LOCAL-ONLY: they exercise real mount/save/hive-mount operations that need admin.

[CmdletBinding()]
param(
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'

# Must be elevated
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "Integration tests require an elevated (admin) session for DISM operations. Re-run from an elevated terminal."
    exit 1
}

# Pin Pester 5 API (Pester 6 may be installed locally)
$pester5 = Get-Module Pester -ListAvailable | Where-Object { $_.Version.Major -eq 5 } | Select-Object -First 1
if (-not $pester5) {
    Write-Warning "Pester 5.x not found. Install with: Save-Module Pester -Path <module-dir>"
    exit 1
}

Import-Module $pester5.Path -Force

$testDir = $PSScriptRoot
Invoke-Pester -Path $testDir -Tag Integration -Output Detailed
exit ($LASTEXITCODE)
