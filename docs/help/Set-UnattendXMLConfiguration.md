---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-UnattendXMLConfiguration

## SYNOPSIS
Modifies elements in an Unattend XML configuration.
## SYNTAX

### XPath
```
Set-UnattendXMLConfiguration [-Configuration] <UnattendXMLConfiguration> [-XPath] <String> [[-Value] <String>]
 [-AttributeName <String>] [-Remove] [-CreateIfNotExists] [-PassThru] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### ElementName
```
Set-UnattendXMLConfiguration [-Configuration] <UnattendXMLConfiguration> [-ElementName] <String>
 [-Pass <String>] [-ComponentName <String>] [[-Value] <String>] [-AttributeName <String>] [-Remove]
 [-CreateIfNotExists] [-PassThru] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Sets, replaces, or removes elements located via XPath, optionally creating missing elements. PassThru returns the modified configuration.
## EXAMPLES

## PARAMETERS

### -AttributeName
Specifies the AttributeName parameter.

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

### -ComponentName
Specifies the ComponentName parameter.

```yaml
Type: String
Parameter Sets: ElementName
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Configuration
Specifies the Configuration parameter.

```yaml
Type: UnattendXMLConfiguration
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -CreateIfNotExists
Specifies the CreateIfNotExists parameter.

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

### -ElementName
Specifies the ElementName parameter.

```yaml
Type: String
Parameter Sets: ElementName
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Pass
Specifies the Pass parameter.

```yaml
Type: String
Parameter Sets: ElementName
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Specifies the PassThru parameter.

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

### -Remove
Specifies the Remove parameter.

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

### -Value
Specifies the Value parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -XPath
Specifies the XPath parameter.

```yaml
Type: String
Parameter Sets: XPath
Aliases:

Required: True
Position: 1
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

### PSWindowsImageTools.Models.UnattendXMLConfiguration

## OUTPUTS

### PSWindowsImageTools.Models.UnattendXMLConfiguration

## NOTES

## RELATED LINKS
