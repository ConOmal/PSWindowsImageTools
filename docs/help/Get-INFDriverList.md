---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-INFDriverList

## SYNOPSIS
Parses INF files and extracts driver information.
## SYNTAX

```
Get-INFDriverList [-Path] <DirectoryInfo[]> [-Recurse] [-ParseINF] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Scans directories for INF driver packages. Recurse includes subdirectories; ParseINF extracts detailed metadata for use with Add-INFDriverList.
## EXAMPLES

## PARAMETERS

### -ParseINF
Parse INF files to extract driver metadata

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

### -Path
One or more directories to scan for INF driver files

```yaml
Type: DirectoryInfo[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -Recurse
Scan directories recursively for INF files

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

### -ProgressAction
Specifies the ProgressAction parameter.

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

### System.IO.DirectoryInfo[]

## OUTPUTS

### PSWindowsImageTools.Models.INFDriverInfo[]

## NOTES

## RELATED LINKS
