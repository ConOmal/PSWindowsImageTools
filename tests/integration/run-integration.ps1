# Runs the PSWindowsImageTools integration test suite locally.
# Requires: elevated session (DISM image operations) and Windows with DISM available.
# These tests are LOCAL-ONLY: they exercise real mount/save/hive-mount operations that need admin.

[CmdletBinding()]
param(
    [switch]$KeepWorkspace,
    # Optional: run against a real captured WIM (real CBS + driver store) instead
    # of building the synthetic image. Enables the full servicing test surface.
    [string]$RealWim
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

if ($RealWim) {
    if (-not (Test-Path $RealWim)) { throw "RealWim not found: $RealWim" }
    $env:PSWIT_IT_WIM = (Resolve-Path $RealWim).Path
}

$testDir = $PSScriptRoot
$config = New-PesterConfiguration
$config.Run.Path = $testDir
$config.Run.PassThru = $true
$config.Filter.Tag = 'Integration'
$config.Output.Verbosity = 'Detailed'
$run = Invoke-Pester -Configuration $config
exit [int]($run.FailedCount -gt 0)
