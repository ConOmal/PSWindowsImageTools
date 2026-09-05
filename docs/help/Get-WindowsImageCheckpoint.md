---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageCheckpoint

## SYNOPSIS
Lists checkpoints, optionally for a specific mounted image.
## SYNTAX

```
Get-WindowsImageCheckpoint [-MountedImage <MountedWindowsImage>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Lists the checkpoints recorded in the checkpoint index, optionally filtered to a single mounted image by its mount ID.
## EXAMPLES

## PARAMETERS

### -MountedImage
Only list checkpoints for this mounted image

```yaml
Type: MountedWindowsImage
Parameter Sets: (All)
Aliases:

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

### None

## OUTPUTS

### PSWindowsImageTools.Models.ImageCheckpointInfo[]

## NOTES

## RELATED LINKS
