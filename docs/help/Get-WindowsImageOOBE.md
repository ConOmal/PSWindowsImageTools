---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageOOBE

## SYNOPSIS
Reports the Out-of-Box Experience (OOBE) configuration of one or more mounted Windows images from each image's offline SOFTWARE hive.

## SYNTAX

```
Get-WindowsImageOOBE [-MountedImages] <MountedWindowsImage[]> [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Reads the offline SOFTWARE hive (Windows\System32\config\SOFTWARE) of each mounted image and reports every documented OOBE setting under Microsoft\Windows\CurrentVersion\OOBE: SkipMachineOOBE, SkipUserOOBE, SkipPrivacyExperience, ProtectYourPC, BypassNRO, HideOnlineAccountScreens, and HideWirelessSetupInOOBE. Each result carries the setting name, its current DWORD value, and whether it is set at all (a stock image without an OOBE key reports every setting as "Not set"). The hive is parsed in memory, so no hive is mounted and elevation is not required. Pair with Set-WindowsImageOOBE to change the settings.

## EXAMPLES

### EXAMPLE 1
```
Get-WindowsImageOOBE -MountedImages $img
```

Lists every documented OOBE setting of the image mounted at the path referenced by $img, with its current value and set state.

### EXAMPLE 2
```
Get-WindowsImageOOBE -MountedImages $img | Where-Object IsSet | Format-Table SettingName, Value, State
```

Shows only the OOBE values that are actually present in the image.

### EXAMPLE 3
```
Mount-WindowsImage -ImagePath C:\Media\install.wim -Index 1 | Get-WindowsImageOOBE
```

Mounts an image and reports its OOBE configuration via the pipeline.

## PARAMETERS

### -ContinueOnError
Continue processing other images if one fails

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
Mounted Windows images to query

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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageOobeSetting[]

## NOTES
SkipMachineOOBE and SkipUserOOBE are legacy switches (informational on Windows 10/11 images). BypassNRO is a Windows 11 value that some newer builds no longer honor. This cmdlet is registry-based and never calls DISM.

## RELATED LINKS
