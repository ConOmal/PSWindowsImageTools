---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageServicingChain

## SYNOPSIS
Analyzes the servicing chain (SSU/LCU classification and version consistency) of one or more mounted Windows images.
## SYNTAX

```
Get-WindowsImageServicingChain [-MountedImages] <MountedWindowsImage[]> [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Classifies installed servicing packages (Servicing Stack Updates, Cumulative Updates, and other update-like packages) in each mounted image and reports whether the SSU/LCU pairing looks version-consistent. Read-only; pairs with Test-WindowsImageServicing for a boolean pass/fail view.
## EXAMPLES

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
Mounted Windows images to analyze

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.ServicingChainReport[]

## NOTES

## RELATED LINKS