---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsImageComplianceManifest

## SYNOPSIS
Exports a compliance manifest combining an image snapshot's inventory summary with optional security baseline and servicing chain evaluations plus tool provenance.
## SYNTAX

```
Export-WindowsImageComplianceManifest [-Snapshot] <ImageSnapshot> [-DestinationPath] <String>
 [-BaselineReport <WindowsImageSecurityBaselineReport>] [-ServicingChainReport <ServicingChainReport>]
 [-Force] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Writes a single JSON compliance manifest (schema version 1.0) documenting the audited image's identity and provenance (tool name/version, manifest generation time, snapshot capture time, image name/index/path/mount), aggregate per-category inventory counts, the optional security baseline policy evaluation from Get-WindowsImageSecurityBaseline (per-entry verdicts and a rolled-up OverallStatus), and the optional servicing chain verdict from Get-WindowsImageServicingChain (SSU/LCU pairing and ordering).

This is not a generic inventory export: item lists stay in the snapshot JSON (Get-WindowsImageSnapshot -ExportPath) and the SBOM (Export-WindowsImageSBOM); the manifest carries aggregate counts and evaluation results only. The cmdlet is read-only regarding images — no DISM, no mounting. Without -Force the cmdlet refuses to overwrite an existing manifest file.
## EXAMPLES

### Example 1: Manifest from a snapshot with baseline and servicing evidence
```powershell
$manifest = Export-WindowsImageComplianceManifest -Snapshot $snapshot -BaselineReport $baseline -ServicingChainReport $servicing -DestinationPath C:\audit\win11-compliance.json
$manifest.OverallStatus
```
Combines the snapshot, the security baseline report and the servicing chain report into one audit artifact, saves it, and returns the manifest object.

### Example 2: Manifest from a saved snapshot file with baseline only
```powershell
$snapshot = Get-WindowsImageSnapshot -MountedImages $image | Select-Object -First 1
Export-WindowsImageComplianceManifest -Snapshot $snapshot -BaselineReport (Get-WindowsImageSecurityBaseline -MountedImages $image) -DestinationPath .\manifest.json -Force
```
Re-exports the manifest, overwriting any previous file.
## PARAMETERS

### -BaselineReport
Optional security baseline compliance report from Get-WindowsImageSecurityBaseline. When supplied, its per-entry verdicts and roll-up are embedded in the manifest and OverallStatus becomes Compliant or NonCompliant.

```yaml
Type: WindowsImageSecurityBaselineReport
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DestinationPath
Destination JSON file path for the compliance manifest. Parent directories are created when missing.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Overwrite the destination file if it exists.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ServicingChainReport
Optional servicing chain report from Get-WindowsImageServicingChain. When supplied, its SSU/LCU classification and ordering verdict are embedded in the manifest.

```yaml
Type: ServicingChainReport
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Snapshot
Snapshot from Get-WindowsImageSnapshot.

```yaml
Type: ImageSnapshot
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSWindowsImageTools.Models.ImageSnapshot

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageComplianceManifest

## NOTES
- OverallStatus semantics: Unknown when no -BaselineReport is supplied; otherwise Compliant when the baseline report is fully compliant and NonCompliant otherwise. The servicing chain verdict is advisory and does not change OverallStatus.
- ToolVersion records the assembly version of PSWindowsImageTools.dll; the module manifest version (psd1 ModuleVersion) is not embedded.
- If a supplied report's ImageName does not match the snapshot's, the report is still embedded and a warning names both images.

## RELATED LINKS
