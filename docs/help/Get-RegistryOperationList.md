---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-RegistryOperationList

## SYNOPSIS
Parses .reg files into registry operations.
## SYNTAX

```
Get-RegistryOperationList [-Path] <String[]> [-Recurse] [-FilterHive <String>] [-FilterOperation <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Converts registry editor files into RegistryOperation objects (create/modify/remove/remove-key). Recurse searches subdirectories; FilterHive and FilterOperation narrow results.
## EXAMPLES

## PARAMETERS

### -FilterHive
Specifies the FilterHive parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: HKLM, HKCU, HKU, HKCR, HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER, HKEY_USERS, HKEY_CLASSES_ROOT

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -FilterOperation
Specifies the FilterOperation parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: Create, Modify, Remove, RemoveKey

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Specifies the Path parameter.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -Recurse
Specifies the Recurse parameter.

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

### System.String[]

## OUTPUTS

### PSWindowsImageTools.Models.RegistryOperation

## NOTES

## RELATED LINKS
