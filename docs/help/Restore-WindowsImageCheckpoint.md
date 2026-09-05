---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Restore-WindowsImageCheckpoint

## SYNOPSIS
Restores a mounted Windows image's directory to a previously taken checkpoint.
## SYNTAX

```
Restore-WindowsImageCheckpoint [-Checkpoint] <ImageCheckpointInfo[]> [-MountedImage] <MountedWindowsImage>
 [-ContinueOnError] [-RemoveAfterRestore] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## DESCRIPTION
Replaces the current contents of a mounted image's mount directory with the contents of the specified checkpoint, discarding any changes made after the checkpoint was taken. The target must currently be mounted read-write (Mounted status, not read-only). SupportsShouldProcess is honored.
## EXAMPLES

## PARAMETERS

### -Checkpoint
Checkpoint(s) to restore

```yaml
Type: ImageCheckpointInfo[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ContinueOnError
Continue processing other checkpoints if one fails

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

### -MountedImage
Mounted image to restore into

```yaml
Type: MountedWindowsImage
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -RemoveAfterRestore
Delete the checkpoint after successfully restoring it

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

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
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

### PSWindowsImageTools.Models.ImageCheckpointInfo[]

## OUTPUTS

### None

## NOTES

## RELATED LINKS
