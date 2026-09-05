---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Checkpoint-WindowsImage

## SYNOPSIS
Creates a checkpoint of a mounted Windows image's current on-disk state.
## SYNTAX

```
Checkpoint-WindowsImage [-MountedImage] <MountedWindowsImage> [-Label <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Copies the current contents of a mounted Windows image's mount directory into a checkpoint directory and records it in the checkpoint index. Checkpoints are plain recursive file mirrors (not VSS), scoped to a single mount directory, and can later be rolled back with Restore-WindowsImageCheckpoint.
## EXAMPLES

## PARAMETERS

### -Label
Optional label for this checkpoint

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

### -MountedImage
Mounted Windows image to checkpoint

```yaml
Type: MountedWindowsImage
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

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

### PSWindowsImageTools.Models.MountedWindowsImage

## OUTPUTS

### PSWindowsImageTools.Models.ImageCheckpointInfo

## NOTES

## RELATED LINKS
