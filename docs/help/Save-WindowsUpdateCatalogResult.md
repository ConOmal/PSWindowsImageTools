---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Save-WindowsUpdateCatalogResult

## SYNOPSIS
Downloads update files with resume and integrity verification.
## SYNTAX

### FromPipeline
```
Save-WindowsUpdateCatalogResult [-InputObject] <WindowsUpdateCatalogResult[]>
 [-DestinationPath <DirectoryInfo>] [-Force] [-Verify] [-Resume] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### FromParameter
```
Save-WindowsUpdateCatalogResult [-CatalogResults] <WindowsUpdateCatalogResult[]>
 [-DestinationPath <DirectoryInfo>] [-Force] [-Verify] [-Resume] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Downloads each catalog result's update file to DestinationPath. Resume continues interrupted downloads; Verify checks integrity; Force overwrites existing files.
## EXAMPLES

### Example 1
```powershell
$urls | Save-WindowsUpdateCatalogResult -DestinationPath "C:\Updates" -Verify
```

Performs the operation shown above.

## PARAMETERS

### -CatalogResults
Specifies the CatalogResults parameter.

```yaml
Type: WindowsUpdateCatalogResult[]
Parameter Sets: FromParameter
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DestinationPath
Specifies the DestinationPath parameter.

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Specifies the Force parameter.

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
Parameter Sets: FromPipeline
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Resume
Specifies the Resume parameter.

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

### -Verify
Specifies the Verify parameter.

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

### PSWindowsImageTools.Models.WindowsUpdateCatalogResult[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsUpdatePackage[]

## NOTES

## RELATED LINKS
