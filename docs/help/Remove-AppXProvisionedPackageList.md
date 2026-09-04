---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Remove-AppXProvisionedPackageList

## SYNOPSIS
Removes provisioned AppX packages from mounted images with regex filtering.
## SYNTAX

```
Remove-AppXProvisionedPackageList [-MountedImages] <MountedWindowsImage[]> [-InclusionFilter <String>]
 [-ExclusionFilter <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Enumerates provisioned AppX packages via DISM and removes those matching InclusionFilter, excluding matches of ExclusionFilter. Results include per-package success/failure detail.
## EXAMPLES

## PARAMETERS

### -ExclusionFilter
Regex pattern to exclude packages based on DisplayName (e.g., 'Store|Calculator' to exclude Store and Calculator)

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

### -InclusionFilter
Regex pattern to include packages based on DisplayName (e.g., 'Microsoft.*' to include all Microsoft packages)

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

### -MountedImages
Mounted Windows images to remove AppX provisioned packages from

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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.AppXRemovalResult[]

## NOTES

## RELATED LINKS
