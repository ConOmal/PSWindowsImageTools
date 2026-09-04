---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Search-WindowsUpdateCatalog

## SYNOPSIS
Searches the Microsoft Update Catalog.
## SYNTAX

### FromPipeline
```
Search-WindowsUpdateCatalog [-InputObject] <String[]> [-Architecture <String>] [-MaxResults <Int32>]
 [-Classification <String>] [-Product <String>] [-Page <Int32>] [-DebugMode]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### FromParameter
```
Search-WindowsUpdateCatalog [-Query] <String[]> [-Architecture <String>] [-MaxResults <Int32>]
 [-Classification <String>] [-Product <String>] [-Page <Int32>] [-DebugMode]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Performs paged catalog searches with filters for architecture, classification, product, and maximum results. Accepts query strings from the pipeline. Output feeds Get-WindowsUpdateDownloadUrl and Save-WindowsUpdateCatalogResult.
## EXAMPLES

### Example 1
```powershell
$updates = Search-WindowsUpdateCatalog -Query "Windows 11 Cumulative" -Architecture x64 -MaxResults 10
```

Performs the operation shown above.

## PARAMETERS

### -Architecture
Specifies the Architecture parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: x86, x64, ARM64

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Classification
Specifies the Classification parameter.

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
Type: String[]
Parameter Sets: FromPipeline
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -MaxResults
Specifies the MaxResults parameter.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Page
Specifies the Page parameter.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Product
Specifies the Product parameter.

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

### -Query
Specifies the Query parameter.

```yaml
Type: String[]
Parameter Sets: FromParameter
Aliases:

Required: True
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

### System.String[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsUpdateCatalogResult[]

## NOTES

## RELATED LINKS
