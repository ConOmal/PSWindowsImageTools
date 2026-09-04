---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageSnapshot

## SYNOPSIS
Captures an inventory snapshot of a mounted Windows image.
## SYNTAX

```
Get-WindowsImageSnapshot [-MountedImages] <MountedWindowsImage[]> [-ExportPath <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Collects packages, features, capabilities, provisioned AppX packages, and installed software (from the offline SOFTWARE hive) into an ImageSnapshot object. ExportPath writes the snapshot as JSON for later Compare-WindowsImage audits.
## EXAMPLES

### Example 1
```powershell
$mounted | Get-WindowsImageSnapshot -ExportPath "C:\Snapshots`"
```

Performs the operation shown above.

## PARAMETERS

### -ExportPath
Optional directory to export snapshots as JSON files

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

### -MountedImages
Mounted Windows images to snapshot

```yaml
Type: MountedWindowsImage[]
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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.ImageSnapshot[]

## NOTES

## RELATED LINKS
