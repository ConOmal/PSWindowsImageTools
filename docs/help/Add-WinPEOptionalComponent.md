---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Add-WinPEOptionalComponent

## SYNOPSIS
Installs WinPE optional components into boot images.
## SYNTAX

```
Add-WinPEOptionalComponent [-MountedImages] <MountedWindowsImage[]> [-Components] <WinPEOptionalComponent[]>
 [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Adds DISM optional components (e.g., PowerShell, WMI, .NET) to mounted WinPE boot images. Components come from Get-WinPEOptionalComponent.
## EXAMPLES

## PARAMETERS

### -Components
Optional components to install (from Get-WinPEOptionalComponent)

```yaml
Type: WinPEOptionalComponent[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -ContinueOnError
Continue processing other components if one fails

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
Mounted boot images to install components into (from Mount-WindowsImageList)

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

### PSWindowsImageTools.Models.WinPEOptionalComponent[]

## OUTPUTS

### PSWindowsImageTools.Models.OptionalComponentInstallationResult[]

## NOTES

## RELATED LINKS
