---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsImage

## SYNOPSIS
Exports images from a WIM/ESD file to a new WIM using the native WIM API.
## SYNTAX

```
Export-WindowsImage [-SourcePath] <String> [-DestinationPath] <String> [-SourceIndex <Int32>]
 [-SourceName <String>] [-DestinationName <String>] [-DestinationDescription <String>]
 [-CompressionType <String>] [-CheckIntegrity] [-SetBootable] [-Force] [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Exports one image (by index or name) or all images into a new WIM with chosen compression (None/Fast/Max/Recovery), integrity checking, bootable flag, and optional destination rename/description. Supports in-place and multi-image exports.
## EXAMPLES

### Example 1
```powershell
Export-WindowsImage -SourcePath "install.esd" -DestinationPath "install.wim" -CompressionType Max
```

Performs the operation shown above.

## PARAMETERS

### -CheckIntegrity
Verify file integrity during export

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

### -CompressionType
Compression type for the destination WIM (None, Fast, Max, Recovery)

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: None, Fast, Max, Recovery

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ContinueOnError
Continue exporting remaining images when one fails

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

### -DestinationDescription
Description to set on the exported image(s)

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

### -DestinationName
Name to set on the exported image(s)

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

### -DestinationPath
Path for the destination WIM file

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Overwrite the destination file if it exists

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

### -SetBootable
Set the exported image(s) as bootable

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

### -SourceIndex
Source image index to export (0 = export all images)

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

### -SourceName
Source image name to export (overrides SourceIndex)

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

### -SourcePath
Path to the source WIM/ESD file

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SplitSize
Maximum size of each split part in MB (optional). When set, the export is written as split .swm parts of at most this size instead of a single .wim.

```yaml
Type: Int64
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

### None

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageExportResult

## NOTES

## RELATED LINKS
