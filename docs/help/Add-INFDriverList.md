---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Add-INFDriverList

## SYNOPSIS
Installs drivers into mounted Windows images.
## SYNTAX

```
Add-INFDriverList [-MountedImages] <MountedWindowsImage[]> [-Drivers] <INFDriverInfo[]> [-ForceUnsigned]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Adds all INF drivers from the provided driver list into the mounted images using the DISM API. Use Get-INFDriverList to discover drivers. ForceUnsigned installs unsigned drivers.
## EXAMPLES

## PARAMETERS

### -Drivers
INF driver objects to install (from Get-INFDriverList)

```yaml
Type: INFDriverInfo[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -ForceUnsigned
Force installation of unsigned drivers

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
Mounted Windows images to add drivers to

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

### PSWindowsImageTools.Models.INFDriverInfo[]

## OUTPUTS

### PSWindowsImageTools.Models.DriverInstallationResult[]

## NOTES

## RELATED LINKS
