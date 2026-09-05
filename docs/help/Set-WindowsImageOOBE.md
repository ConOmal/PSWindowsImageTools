---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-WindowsImageOOBE

## SYNOPSIS
Applies Out-of-Box Experience (OOBE) settings to one or more mounted Windows images' offline SOFTWARE hives.

## SYNTAX

```
Set-WindowsImageOOBE [-MountedImages] <MountedWindowsImage[]> [-SkipMachineOOBE] [-SkipUserOOBE] [-SkipPrivacyExperience]
 [-BypassNRO] [-HideOnlineAccountScreens] [-HideWirelessSetupInOOBE] [-ProtectYourPC <WindowsImageOobeProtectYourPc>]
 [-Remove <String[]>] [-ContinueOnError] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Writes DWORD values under Microsoft\Windows\CurrentVersion\OOBE in each image's offline SOFTWARE hive. Every setting switch is tri-state: omit it to leave the value untouched, specify it to write 1, or specify it with :$false to write 0 (for example -SkipPrivacyExperience:$false). -ProtectYourPC writes the express-settings choice (Recommended = 1, ImportantOnly = 2, NotInProgram = 3). -Remove deletes documented OOBE values from the key. The writes are delegated to the existing hive-mounted native registry path, which loads, modifies and unloads the SOFTWARE hive for each image (the OOBE key is created when missing). Requires elevation. SupportsShouldProcess is honored, so -WhatIf and -Confirm are available. This cmdlet is registry-based and never calls DISM.

## EXAMPLES

### EXAMPLE 1
```
Set-WindowsImageOOBE -MountedImages $img -SkipPrivacyExperience -ProtectYourPC ImportantOnly -WhatIf
```

Previews writing SkipPrivacyExperience = 1 and ProtectYourPC = 2 without making a change.

### EXAMPLE 2
```
Set-WindowsImageOOBE -MountedImages $img -SkipPrivacyExperience -HideOnlineAccountScreens
```

Skips the privacy experience screen and hides the Microsoft-account online screens in the image.

### EXAMPLE 3
```
Set-WindowsImageOOBE -MountedImages $img -SkipPrivacyExperience:$false -Remove BypassNRO
```

Resets the privacy experience screen to 0 and deletes the BypassNRO value from the image.

### EXAMPLE 4
```
Mount-WindowsImage -ImagePath C:\Media\install.wim -Index 1 | Set-WindowsImageOOBE -BypassNRO
```

Mounts an image and writes BypassNRO = 1 so OOBE can be completed without a network connection (Windows 11).

## PARAMETERS

### -BypassNRO
BypassNRO value (Windows 11, allows OOBE without a network connection): omit to leave untouched, specify to write 1, or use -BypassNRO:$false to write 0. Some newer Windows 11 builds no longer honor this value.

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

### -HideOnlineAccountScreens
HideOnlineAccountScreens value: omit to leave untouched, specify to write 1, or use -HideOnlineAccountScreens:$false to write 0

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

### -HideWirelessSetupInOOBE
HideWirelessSetupInOOBE value: omit to leave untouched, specify to write 1, or use -HideWirelessSetupInOOBE:$false to write 0

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
Mounted Windows images to modify

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

### -ProtectYourPC
ProtectYourPC express-settings choice: Recommended (1, use recommended settings), ImportantOnly (2, recommended settings off - only important updates), NotInProgram (3, not in the recommended program); omit to leave untouched

```yaml
Type: WindowsImageOobeProtectYourPc
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Remove
Documented OOBE value names to remove from the OOBE key (e.g. BypassNRO). Unknown names are rejected before any hive is mounted.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -SkipMachineOOBE
SkipMachineOOBE value: omit to leave untouched, specify to write 1, or use -SkipMachineOOBE:$false to write 0 (legacy switch, informational on Windows 10/11 images)

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

### -SkipPrivacyExperience
SkipPrivacyExperience value: omit to leave untouched, specify to write 1, or use -SkipPrivacyExperience:$false to write 0

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

### -SkipUserOOBE
SkipUserOOBE value: omit to leave untouched, specify to write 1, or use -SkipUserOOBE:$false to write 0 (legacy switch, informational on Windows 10/11 images)

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageOobeOperationResult[]

## NOTES
SkipMachineOOBE and SkipUserOOBE are legacy switches that current Windows 10/11 setup may ignore; the values are still written and read faithfully. Pair with Get-WindowsImageOOBE to inspect the current configuration first.

## RELATED LINKS
