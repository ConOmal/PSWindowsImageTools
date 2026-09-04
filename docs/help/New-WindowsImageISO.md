---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# New-WindowsImageISO

## SYNOPSIS
Creates a bootable ISO from a Windows setup folder.
## SYNTAX

```
New-WindowsImageISO [-SourcePath] <String> [-OutputIsoPath] <String> [-VolumeLabel <String>]
 [-BootMode <String>] [-Force] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Uses oscdimg from an installed Windows ADK (Install-ADK -IncludeDeploymentTools) to build a UEFI/BIOS-bootable ISO with a chosen volume label. Force overwrites existing ISOs.
## EXAMPLES

### Example 1
```powershell
New-WindowsImageISO -SourcePath "C:\Media\Win11" -OutputIsoPath "C:\Media\Win11.iso" -BootMode Both
```

Performs the operation shown above.

## PARAMETERS

### -BootMode
Boot mode: UEFI, BIOS, or Both

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: UEFI, BIOS, Both

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
Overwrite the output ISO if it exists

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

### -OutputIsoPath
Path for the output ISO file

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

### -SourcePath
Path to the Windows setup folder (containing boot/, efi/, sources/)

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

### -VolumeLabel
Volume label for the ISO

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

### None

## OUTPUTS

### PSWindowsImageTools.Models.ISOCreationResult

## NOTES

## RELATED LINKS
