---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-WindowsImageSecurityBaseline

## SYNOPSIS
Applies the curated security baseline to one or more mounted Windows images (writes the expected values to the offline SOFTWARE, SYSTEM and default-user hives).

## SYNTAX

```
Set-WindowsImageSecurityBaseline [-MountedImages] <MountedWindowsImage[]> [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Brings a mounted image to the curated 22-entry security baseline: entries the image already satisfies are skipped (reported as AlreadyApplied), entries in an absent hive file are skipped (reported as Skipped), and every remaining entry is written with its expected value and type (DWORD or REG_SZ).

Writes use the hive-mounted native registry path (RegLoadKey → apply → RegUnLoadKey in a finally block), which requires an elevated session. Reads used for the pre-flight are in memory and never mount a hive. The cmdlet supports -WhatIf and -Confirm; declined confirmations write nothing.

SMB signing is enforced client- and server-side and SMB1 is disabled by the baseline; legacy SMB1-only peers will stop interoperating after remediation.

## EXAMPLES

### EXAMPLE 1
```
Set-WindowsImageSecurityBaseline -MountedImages $img -WhatIf
```

Shows which baseline entries would be written to the image without changing anything.

### EXAMPLE 2
```
Set-WindowsImageSecurityBaseline -MountedImages $img
```

Applies the baseline to the image and returns one apply result with per-entry outcomes.

### EXAMPLE 3
```
Get-MountWindowsImageList ... | Set-WindowsImageSecurityBaseline -ContinueOnError
```

Applies the baseline to every mounted image, continuing past a failed image.

## PARAMETERS

### -ContinueOnError
Continue processing other images if one fails

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

### -MountedImages
Mounted Windows images to bring to the security baseline

```yaml
Type: MountedWindowsImage[]
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

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs. The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageSecurityBaselineApplyResult[]

## NOTES
The baseline definition (hive, key path, value name, expected value and rationale for every entry) is documented in docs/superpowers/specs/2026-09-04-security-baselines-design.md. Remediation is additive: only the documented values are written, and stray values are never deleted.

## RELATED LINKS

