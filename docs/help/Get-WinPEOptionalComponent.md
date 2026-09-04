---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WinPEOptionalComponent

## SYNOPSIS
Discovers available WinPE optional components.
## SYNTAX

```
Get-WinPEOptionalComponent [[-ADKInstallation] <ADKInfo>] [-Architecture <String>] [-IncludeLanguagePacks]
 [-Category <String[]>] [-Name <String[]>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Lists DISM optional components available for WinPE images from an installed ADK. Filters: Architecture, Category, Name, IncludeLanguagePacks.
## EXAMPLES

## PARAMETERS

### -ADKInstallation
ADK installation to scan for components (from Get-ADKInstallation)

```yaml
Type: ADKInfo
Parameter Sets: (All)
Aliases:

Required: False
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Architecture
Target architecture for components (x86, amd64, arm64)

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: x86, amd64, arm64

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Category
Filter components by category

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:
Accepted values: Networking, Storage, Scripting, Security, Hardware, Development, Fonts, Language, General, Other

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -IncludeLanguagePacks
Include language pack components

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

### -Name
Filter components by name pattern (supports wildcards)

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

### PSWindowsImageTools.Models.ADKInfo

## OUTPUTS

### PSWindowsImageTools.Models.WinPEOptionalComponent[]

## NOTES

## RELATED LINKS
