# Verifies that shipped help matches the live module: a guardrail against documentation drift.
# Checks:
#   1. Every exported cmdlet has a docs/help/<Name>.md markdown source.
#   2. Every live parameter (minus common parameters) is documented in that md's PARAMETERS section.
#   3. New-ExternalHelp can compile the markdown without errors (round-trip to a temp MAML).
#   4. The shipped MAML (Module/.../en-US/PSWindowsImageTools.dll-Help.xml) contains every cmdlet with a synopsis.
# Exits non-zero on any drift so CI can fail the build.
[CmdletBinding()]
param(
    # Skip the New-ExternalHelp round-trip (checks 1, 2, 4 only)
    [switch]$SkipCompile,
    # Optional explicit path to a platyPS module folder (when not on PSModulePath)
    [string]$PlatyPSPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Join-Path $PSScriptRoot '..'
$helpDir = Join-Path $repoRoot 'docs\help'
$moduleManifest = Join-Path $repoRoot 'Module\PSWindowsImageTools\PSWindowsImageTools.psd1'
$mamlPath = Join-Path $repoRoot 'Module\PSWindowsImageTools\en-US\PSWindowsImageTools.dll-Help.xml'

$problems = [System.Collections.Generic.List[string]]::new()

Import-Module $moduleManifest -Force
$commands = Get-Command -Module PSWindowsImageTools | Sort-Object Name
Write-Output "Module exports $($commands.Count) commands"

# Common parameters that PlatyPS markdown does not document
$commonParams = @(
    'Verbose', 'Debug', 'ErrorAction', 'ErrorVariable', 'WarningAction', 'WarningVariable',
    'InformationAction', 'InformationVariable', 'OutVariable', 'OutBuffer', 'PipelineVariable',
    'ProgressAction', 'WhatIf', 'Confirm', 'UseTransaction', 'SupportsShouldProcess'
)

# --- Check 1: markdown source exists per cmdlet ---
$mdFiles = @{}
Get-ChildItem $helpDir -Filter *.md | ForEach-Object { $mdFiles[$_.BaseName] = $_.FullName }

$missingMd = $commands | Where-Object { -not $mdFiles.ContainsKey($_.Name) }
foreach ($m in $missingMd) {
    $problems.Add("docs/help/$($m.Name).md is missing (cmdlet is exported)")
}
Write-Output "Check 1: $($mdFiles.Count) markdown files, $($missingMd.Count) cmdlets missing md"

# --- Check 2: every live parameter documented in md PARAMETERS section ---
$paramDrift = 0
foreach ($cmd in $commands) {
    if (-not $mdFiles.ContainsKey($cmd.Name)) { continue }
    $md = Get-Content $mdFiles[$cmd.Name] -Raw
    $paramsMatch = [regex]::Match($md, '(?s)## PARAMETERS(.*?)(^## |\z)')
    if (-not $paramsMatch.Success) {
        $problems.Add("$($cmd.Name): markdown has no PARAMETERS section")
        $paramDrift++
        continue
    }

    $documented = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($m in [regex]::Matches($paramsMatch.Groups[1].Value, '(?m)^###\s+-(\w+)')) {
        [void]$documented.Add($m.Groups[1].Value)
    }

    $live = $cmd.Parameters.Keys | Where-Object { $commonParams -notcontains $_ }
    foreach ($p in $live) {
        if (-not $documented.Contains($p)) {
            $problems.Add("$($cmd.Name): live parameter -$p is not documented in markdown")
            $paramDrift++
        }
    }
}
Write-Output "Check 2: $paramDrift parameter drift issues"

# --- Check 3: New-ExternalHelp round-trip compiles ---
if (-not $SkipCompile) {
    $platyPS = $null
    if ($PlatyPSPath -and (Test-Path $PlatyPSPath)) {
        $platyPS = Get-ChildItem $PlatyPSPath -Recurse -Filter "platyPS.psd1" | Select-Object -First 1
    } else {
        $platyPS = Get-Module platyPS -ListAvailable | Select-Object -First 1
    }
    if ($platyPS) {
        Import-Module $platyPS.FullName -Force
        $tempOut = Join-Path ([System.IO.Path]::GetTempPath()) "PSWIT-verify-help-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $tempOut | Out-Null
        try {
            $null = New-ExternalHelp -Path $helpDir -OutputPath $tempOut -Force -ErrorAction Stop 2>&1
            $generated = (Get-ChildItem $tempOut -Filter *.xml).Count
            Write-Output "Check 3: New-ExternalHelp compiled $generated xml from $($mdFiles.Count) markdown files"
        } finally {
            Remove-Item $tempOut -Recurse -Force -ErrorAction SilentlyContinue
        }
    } else {
        Write-Warning "platyPS not installed - skipping New-ExternalHelp round-trip (check 3). Install with: Install-Module platyPS -Scope CurrentUser"
    }
}

# --- Check 4: shipped MAML covers every cmdlet with a synopsis ---
[xml]$maml = Get-Content $mamlPath
$covered = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($node in $maml.helpItems.command) {
    $name = $node.details.name
    $synopsis = $node.details.description.para | Out-String
    if ($name -and $synopsis.Trim()) {
        [void]$covered.Add($name)
    }
}

$mamlMissing = $commands | Where-Object { -not $covered.Contains($_.Name) }
foreach ($m in $mamlMissing) {
    $problems.Add("shipped MAML is missing synopsis for $($m.Name) (re-run Scripts/build-help.ps1 then New-ExternalHelp)")
}
Write-Output "Check 4: MAML covers $($covered.Count) commands, $($mamlMissing.Count) missing"

# --- Result ---
if ($problems.Count -gt 0) {
    Write-Output ""
    Write-Output "Help drift detected ($($problems.Count) problems):"
    $problems | ForEach-Object { Write-Output "  - $_" }
    exit 1
}

Write-Output ""
Write-Output "Help verification passed: markdown sources, parameter coverage, compile round-trip, and shipped MAML are in sync."
exit 0
