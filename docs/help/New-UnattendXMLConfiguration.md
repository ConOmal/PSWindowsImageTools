---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# New-UnattendXMLConfiguration

## SYNOPSIS
Creates a new Unattend XML configuration.
## SYNTAX

```
New-UnattendXMLConfiguration [[-Template] <String>] [-Architecture <String>] [-Language <String>]
 [-ConfigurationPasses <String[]>] [-IncludeSamples] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Generates an unattend.xml structure, optionally from a template with specific architecture, language, and configuration passes. IncludeSamples adds illustrative values.
## EXAMPLES

## PARAMETERS

### -Architecture
Specifies the Architecture parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: amd64, x86, arm64

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ConfigurationPasses
Specifies the ConfigurationPasses parameter.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeSamples
Specifies the IncludeSamples parameter.

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

### -Language
Specifies the Language parameter.

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

### -Template
Specifies the Template parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: Basic, OOBE, Sysprep, Custom, Minimal

Required: False
Position: 0
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

### None

## OUTPUTS

### PSWindowsImageTools.Models.UnattendXMLConfiguration

## NOTES

## RELATED LINKS
