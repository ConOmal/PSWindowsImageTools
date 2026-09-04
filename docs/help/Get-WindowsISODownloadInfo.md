---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsISODownloadInfo

## SYNOPSIS
Resolves a time-limited direct download URL for the latest official Windows 11 ISO.
## SYNTAX

```
Get-WindowsISODownloadInfo [-Edition <String>] [-Architecture <String>] [-Language <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Queries Microsoft's public download endpoint and resolves the latest Windows 11 ISO direct download URL together with edition, language, and architecture details. Results feed Save-WindowsISO.
## EXAMPLES

## PARAMETERS

### -Architecture
{{ Fill Architecture Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: x64, arm64

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Edition
{{ Fill Edition Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Language
{{ Fill Language Description }}

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
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

### None

## OUTPUTS

### PSWindowsImageTools.Models.WindowsISODownloadInfo

## NOTES

## RELATED LINKS
