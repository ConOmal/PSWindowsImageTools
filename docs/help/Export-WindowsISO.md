---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsISO

## SYNOPSIS
Extracts a Windows ISO's contents to a working folder, ready for Get-WindowsImageList/New-WindowsImageISO.
## SYNTAX

```
Export-WindowsISO [-IsoPath] <FileInfo> [-DestinationPath] <DirectoryInfo> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Extracts the contents of a Windows ISO image into a working folder so servicing cmdlets can operate on the extracted install.wim/esd and boot images. Force overwrites an existing destination.
## EXAMPLES

## PARAMETERS

### -DestinationPath
Destination folder to extract the ISO contents to

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

### -IsoPath
Path to the Windows ISO file

```yaml
Type: FileInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
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

### System.IO.FileInfo

## OUTPUTS

### PSWindowsImageTools.Models.WindowsInstallationMedia

## NOTES

## RELATED LINKS
