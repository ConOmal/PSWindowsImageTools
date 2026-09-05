---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-WindowsImageService

## SYNOPSIS
Changes the start type (and optionally enables delayed auto start) of a service in one or more mounted Windows images' offline SYSTEM hives.

## SYNTAX

```
Set-WindowsImageService [-MountedImages] <MountedWindowsImage[]> [-Name] <String> [-StartType <WindowsImageServiceStartType>]
 [-DelayedAutoStart] [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Writes the Start value under ControlSet001\Services\<Name> in each image's offline SYSTEM hive (values: Boot=0, System=1, Automatic=2, Manual=3, Disabled=4). -DelayedAutoStart writes the DelayedAutoStart DWORD (1) and is only valid together with -StartType Automatic. The write is delegated to the existing hive-mounted native registry path, which loads, modifies and unloads the SYSTEM hive for each image. Requires elevation. SupportsShouldProcess is honored, so -WhatIf and -Confirm are available. The service must already exist in the image; use Get-WindowsImageService to inspect first.

## EXAMPLES

### EXAMPLE 1
```
Set-WindowsImageService -MountedImages $img -Name XblAuthManager -StartType Manual -WhatIf
```

Previews changing XblAuthManager to a Manual start type without making a change.

### EXAMPLE 2
```
Set-WindowsImageService -MountedImages $img -Name 'bthserv' -StartType Disabled
```

Disables the bthserv Bluetooth driver service in the image.

### EXAMPLE 3
```
Set-WindowsImageService -MountedImages $img -Name 'wuauserv' -StartType Automatic -DelayedAutoStart
```

Sets wuauserv to Automatic and enables delayed auto start.

### EXAMPLE 4
```
Set-WindowsImageService -MountedImages $img -Name 'pcasvc' -StartType System
```

Sets the pcasvc driver to a System-class start (value 1), for boot-time driver services.

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

### -DelayedAutoStart
Enable DelayedAutoStart (DWORD 1). Only valid with -StartType Automatic.

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

### -Name
Name of the service to configure

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -StartType
New start type (Boot, System, Automatic, Manual, Disabled). Boot and System are intended for driver services only.

```yaml
Type: WindowsImageServiceStartType
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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageServiceOperationResult[]

## NOTES

## RELATED LINKS