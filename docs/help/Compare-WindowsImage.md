---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Compare-WindowsImage

## SYNOPSIS
Compares two Windows image snapshots to surface what changed.
## SYNTAX

### ByMountedImages
```
Compare-WindowsImage [-MountedImages] <MountedWindowsImage[]> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### BySnapshotFiles
```
Compare-WindowsImage -ReferencePath <String> [-DifferencePath] <String> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Compares two inventory snapshots (from Get-WindowsImageSnapshot) and reports added, removed, and changed items per category (packages, features, capabilities, AppX, software). Accepts two mounted images or two snapshot JSON files - useful for before/after customization audits.
## EXAMPLES

### Example 1
```powershell
$diff = Compare-WindowsImage -ReferencePath vanilla.json -DifferencePath corporate.json
$diff.Categories | Format-Table Category, Count
```

Performs the operation shown above.

## PARAMETERS

### -DifferencePath
Difference (after) snapshot JSON file

```yaml
Type: String
Parameter Sets: BySnapshotFiles
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MountedImages
Two mounted images: first is the reference (before), second the difference (after)

```yaml
Type: MountedWindowsImage[]
Parameter Sets: ByMountedImages
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ReferencePath
Reference (before) snapshot JSON file

```yaml
Type: String
Parameter Sets: BySnapshotFiles
Aliases:

Required: True
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

### PSWindowsImageTools.Models.ImageComparisonResult

## NOTES

## RELATED LINKS
