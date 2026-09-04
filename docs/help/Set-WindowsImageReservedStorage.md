---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-WindowsImageReservedStorage

## SYNOPSIS
Enables or disables reserved storage in a mounted Windows image.

## SYNTAX

```
Set-WindowsImageReservedStorage [-ImagePath] <string> -Enable [-ProgressAction <ActionPreference>]
 [<CommonParameters>]

Set-WindowsImageReservedStorage [-ImagePath] <string> -Disable [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Enables or disables Windows Reserved Storage (space reserved for Windows Update and servicing operations) in a mounted image, via the DISM `/Set-ReservedStorageState` operation. Requires elevation. SupportsShouldProcess is honored, so -WhatIf and -Confirm are available.
## EXAMPLES

### EXAMPLE 1
```
Set-WindowsImageReservedStorage -ImagePath C:\Win11Mount -Disable -WhatIf
```

Previews disabling reserved storage on the image mounted at C:\Win11Mount without making a change.

### EXAMPLE 2
```
Set-WindowsImageReservedStorage -ImagePath C:\Win11Mount -Enable
```

Enables reserved storage on the image mounted at C:\Win11Mount.

## PARAMETERS

### -Disable
Disable reserved storage in the image.

```yaml
Type: SwitchParameter
Parameter Sets: Disable
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Enable
Enable reserved storage in the image.

```yaml
Type: SwitchParameter
Parameter Sets: Enable
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImagePath
Path to the mounted Windows image directory.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
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

### System.String

## OUTPUTS

### PSWindowsImageTools.Models.ReservedStorageOperationResult

## NOTES

## RELATED LINKS