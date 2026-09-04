---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-MountedWindowsImage

## SYNOPSIS
Re-discovers mounted Windows images registered by previous cmdlet runs.
## SYNTAX

```
Get-MountedWindowsImage [[-Filter] <String>] [-Prune] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Lists active mounts from the cross-session mount registry. Mount-WindowsImageList, Dismount-WindowsImageList, and Get-WindowsImageList -SkipDismount maintain the registry automatically. Filter selects by image name; Prune removes stale entries.
## EXAMPLES

### Example 1
```powershell
Get-MountedWindowsImage -Filter "Pro"
Get-MountedWindowsImage -Prune
```

Performs the operation shown above.

## PARAMETERS

### -Filter
Regex pattern to filter by image name

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 0
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Prune
Remove entries whose mount directories no longer exist

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

### None

## OUTPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## NOTES

## RELATED LINKS
