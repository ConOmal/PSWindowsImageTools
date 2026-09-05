---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Mount-WindowsImageList

## SYNOPSIS
Mounts Windows images for modification.
## SYNTAX

### FromPipeline
```
Mount-WindowsImageList [-InputObject] <WindowsImageInfo[]> [-ReadWrite] [-MountRoot <DirectoryInfo>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### FromParameter
```
Mount-WindowsImageList [-ImageInfo] <WindowsImageInfo[]> [-ReadWrite] [-MountRoot <DirectoryInfo>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Mounts images from Get-WindowsImageList (pipeline) into GUID-organized directories under MountRoot. ReadWrite enables modifications. Successful mounts register in the cross-session mount registry.
## EXAMPLES

### Example 1
```powershell
$images = Get-WindowsImageList -ImagePath "install.wim`" -InclusionFilter { $_.Name -like "*Pro*" }
$mounted = $images | Mount-WindowsImageList -ReadWrite -MountRoot "C:\Mount`"
```

Performs the operation shown above.

## PARAMETERS

### -ImageInfo
Specifies the ImageInfo parameter.

```yaml
Type: WindowsImageInfo[]
Parameter Sets: FromParameter
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -InputObject
Specifies the InputObject parameter.

```yaml
Type: WindowsImageInfo[]
Parameter Sets: FromPipeline
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -MaxParallel
Maximum parallel mount operations (0 = auto based on processor count).

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: 0
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

### PSWindowsImageTools.Models.WindowsImageInfo[]

## OUTPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## NOTES

## RELATED LINKS
