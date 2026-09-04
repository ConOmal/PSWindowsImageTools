---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Convert-ESDToWindowsImage

## SYNOPSIS
Converts ESD files to WIM format or folder layout.
## SYNTAX

```
Convert-ESDToWindowsImage [-SourcePath] <FileInfo> [-OutputPath] <String> [-Mode] <String>
 [-InclusionFilter <ScriptBlock>] [-ExclusionFilter <ScriptBlock>] [-CompressionType <String>] [-Force]
 [-Bootable] [-IncludeWindowsPE] [-IncludeWindowsSetup] [-ScratchDirectory <DirectoryInfo>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Exports images from an ESD into a compressed WIM or extracts an installation tree. Supports image filters, compression type selection, and bootable flag handling.
## EXAMPLES

### Example 1
```powershell
Convert-ESDToWindowsImage -SourcePath "install.esd" -OutputPath "install.wim" -Mode WIM -CompressionType Max
```

Performs the operation shown above.

## PARAMETERS

### -Bootable
Specifies the Bootable parameter.

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
Specifies the CompressionType parameter.

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

### -ExclusionFilter
Specifies the ExclusionFilter parameter.

```yaml
Type: ScriptBlock
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

### -IncludeWindowsPE
Specifies the IncludeWindowsPE parameter.

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

### -IncludeWindowsSetup
Specifies the IncludeWindowsSetup parameter.

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

### -InclusionFilter
Specifies the InclusionFilter parameter.

```yaml
Type: ScriptBlock
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Mode
Specifies the Mode parameter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: WIM, Folder

Required: True
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OutputPath
Specifies the OutputPath parameter.

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

### -ScratchDirectory
Specifies the ScratchDirectory parameter.

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

### -SourcePath
Specifies the SourcePath parameter.

```yaml
Type: FileInfo
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

### System.IO.FileInfo

## OUTPUTS

### PSWindowsImageTools.Models.ConversionResult

## NOTES

## RELATED LINKS
