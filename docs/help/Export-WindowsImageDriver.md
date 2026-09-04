---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsImageDriver

## SYNOPSIS
Exports driver package files from a mounted Windows image to a destination directory.
## SYNTAX

```
Export-WindowsImageDriver [-Driver] <WindowsImageDriverInfo[]> [-DestinationPath] <DirectoryInfo>
 [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Copies a driver's INF and payload files out of a mounted image into a destination directory, preserving the driver package layout. Use Get-WindowsImageDriver to discover drivers.
## EXAMPLES

## PARAMETERS

### -ContinueOnError
Continue processing other drivers if one fails

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

### -DestinationPath
Destination directory for exported driver files

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Driver
Driver(s) to export, from Get-WindowsImageDriver

```yaml
Type: WindowsImageDriverInfo[]
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

### PSWindowsImageTools.Models.WindowsImageDriverInfo[]

## OUTPUTS

### System.Void

## NOTES

## RELATED LINKS
