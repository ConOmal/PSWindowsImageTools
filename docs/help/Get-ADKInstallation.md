---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-ADKInstallation

## SYNOPSIS
Detects installed Windows ADK versions.
## SYNTAX

```
Get-ADKInstallation [-Latest] [-MinimumVersion <Version>] [-RequireWinPE] [-RequireDeploymentTools]
 [-RequiredArchitecture <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Enumerates installed Windows Assessment and Deployment Kit installations. Filters: Latest, MinimumVersion, RequireWinPE, RequireDeploymentTools, RequiredArchitecture.
## EXAMPLES

## PARAMETERS

### -Latest
Return only the latest version if multiple ADK installations are found

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

### -MinimumVersion
Minimum required version of ADK (e.g., '10.0.22000.1')

```yaml
Type: Version
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RequireDeploymentTools
Require Deployment Tools to be installed

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

### -RequireWinPE
Require WinPE add-on to be installed

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

### -RequiredArchitecture
Specific architecture support required (x86, amd64, arm64)

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

### PSWindowsImageTools.Models.ADKInfo[]

## NOTES

## RELATED LINKS
