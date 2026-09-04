---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageList

## SYNOPSIS
Gets detailed information about Windows images in WIM/ESD/ISO files.
## SYNTAX

```
Get-WindowsImageList [-ImagePath] <FileInfo> [-Advanced] [-IncludeHash] [-InclusionFilter <ScriptBlock>]
 [-ExclusionFilter <ScriptBlock>] [-SkipDismount] [-ReadWrite] [-MountRoot <DirectoryInfo>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Enumerates images with edition, architecture, version, and language details. Advanced mounts each image to collect registry metadata; SkipDismount keeps images mounted (registered for Get-MountedWindowsImage); ISO files are mounted automatically to locate install.wim/esd. InclusionFilter/ExclusionFilter are scriptblocks evaluated per image.
## EXAMPLES

### Example 1
```powershell
Get-WindowsImageList -ImagePath "C:\Images\install.wim`
Get-WindowsImageList -ImagePath "C:\Media\Win11.iso`" -InclusionFilter { $_.Name -like "*Pro*" } -Advanced
```

Performs the operation shown above.

## PARAMETERS

### -Advanced
Enables advanced metadata collection by mounting images (slower but more detailed)

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

### -ImagePath
Path to the image file (ISO, WIM, or ESD)

```yaml
Type: FileInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -IncludeHash
Calculate SHA256 hash of the source image file (slower but provides integrity verification)

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

### -MountRoot
Specifies the MountRoot parameter.

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

### -ReadWrite
Specifies the ReadWrite parameter.

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

### -SkipDismount
Skip dismounting images after processing (keeps them mounted for use with other cmdlets)

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

### System.IO.FileInfo

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageInfo[]

## NOTES

## RELATED LINKS
