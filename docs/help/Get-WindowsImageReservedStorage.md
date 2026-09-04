---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageReservedStorage

## SYNOPSIS
Reports the reserved storage state (Enabled or Disabled) of a mounted Windows image.

## SYNTAX

```
Get-WindowsImageReservedStorage [-ImagePath] <string> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Reports whether Windows Reserved Storage (space reserved for Windows Update and servicing operations) is enabled or disabled in a mounted image, via the DISM `/Get-ReservedStorageState` operation. Pairs with Set-WindowsImageReservedStorage to change the state.
## EXAMPLES

### EXAMPLE 1
```
Get-WindowsImageReservedStorage -ImagePath C:\Win11Mount
```

Returns the reserved storage state for the image mounted at C:\Win11Mount.

## PARAMETERS

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

### PSWindowsImageTools.Models.WindowsImageReservedStorage

## NOTES

## RELATED LINKS