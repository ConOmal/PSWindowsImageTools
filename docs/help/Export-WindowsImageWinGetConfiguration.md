---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsImageWinGetConfiguration

## SYNOPSIS
Generates a WinGet Configuration artifact for first-boot application (WinGet cannot target an offline mounted image directly).
## SYNTAX

```
Export-WindowsImageWinGetConfiguration [-Package] <WinGetConfigurationEntry[]>
 [-DestinationPath] <DirectoryInfo> [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Generates a WinGet Configuration (DSC v3) YAML file describing desired package state, plus a Scheduled Task XML definition that applies it via `winget configure` on first boot. Pure file generation - the image is not touched. Apply the artifacts manually or during deployment to apply the packages after the image's first boot.
## EXAMPLES

## PARAMETERS

### -DestinationPath
Destination directory for the generated configuration files

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

### -Package
Desired package entries

```yaml
Type: WinGetConfigurationEntry[]
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

### PSWindowsImageTools.Models.WinGetConfigurationEntry[]

## OUTPUTS

### PSWindowsImageTools.Models.WinGetConfigurationExportResult

## NOTES

## RELATED LINKS
