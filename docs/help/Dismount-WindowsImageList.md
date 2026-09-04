---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Dismount-WindowsImageList

## SYNOPSIS
Dismounts mounted Windows images with save or discard options.
## SYNTAX

### ByObject
```
Dismount-WindowsImageList [-MountedImages] <MountedWindowsImage[]> [-Force] [-RemoveDirectories]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByPath
```
Dismount-WindowsImageList [-Path] <DirectoryInfo[]> [-Force] [-RemoveDirectories]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### Save
```
Dismount-WindowsImageList [-Save] [-Append] [-Force] [-RemoveDirectories] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### Discard
```
Dismount-WindowsImageList [-Discard] [-Force] [-RemoveDirectories] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Unmounts images committing changes (Save) or discarding them (Discard). Append merges into an existing WIM. Registered mount session entries are cleaned up automatically.
## EXAMPLES

### Example 1
```powershell
$mounted | Dismount-WindowsImageList -Save -RemoveDirectories
```

Performs the operation shown above.

## PARAMETERS

### -Append
Specifies the Append parameter.

```yaml
Type: SwitchParameter
Parameter Sets: Save
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Discard
Specifies the Discard parameter.

```yaml
Type: SwitchParameter
Parameter Sets: Discard
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

### -MountedImages
Specifies the MountedImages parameter.

```yaml
Type: MountedWindowsImage[]
Parameter Sets: ByObject
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Path
Specifies the Path parameter.

```yaml
Type: DirectoryInfo[]
Parameter Sets: ByPath
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveDirectories
Specifies the RemoveDirectories parameter.

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

### -Save
Specifies the Save parameter.

```yaml
Type: SwitchParameter
Parameter Sets: Save
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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## NOTES

## RELATED LINKS
