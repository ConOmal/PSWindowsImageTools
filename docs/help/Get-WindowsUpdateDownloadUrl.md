---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsUpdateDownloadUrl

## SYNOPSIS
Extracts download URLs from catalog search results.
## SYNTAX

```
Get-WindowsUpdateDownloadUrl -InputObject <WindowsUpdateCatalogResult[]> [-DebugMode]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Resolves the download URL for each update in the catalog results. DebugMode outputs troubleshooting detail.
## EXAMPLES

## PARAMETERS

### -DebugMode
Specifies the DebugMode parameter.

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

### -InputObject
Specifies the InputObject parameter.

```yaml
Type: WindowsUpdateCatalogResult[]
Parameter Sets: (All)
Aliases:

Required: True
Position: Named
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

### PSWindowsImageTools.Models.WindowsUpdateCatalogResult[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsUpdateCatalogResult[]

## NOTES

## RELATED LINKS
