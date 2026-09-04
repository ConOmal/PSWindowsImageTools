---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Compare-WindowsImageDriver

## SYNOPSIS
Compares driver packages between two mounted Windows images.
## SYNTAX

```
Compare-WindowsImageDriver [-MountedImages] <MountedWindowsImage[]> [-All] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Compares driver packages present in two mounted images and reports drivers added or removed between them based on original name, provider, and version. Useful for auditing driver injection changes between image generations.
## EXAMPLES

## PARAMETERS

### -All
Include inbox (Windows-provided) drivers, not just third-party

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
Two mounted images: first is the reference, second is current

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

### PSWindowsImageTools.Models.DriverComparisonResult

## NOTES

## RELATED LINKS
