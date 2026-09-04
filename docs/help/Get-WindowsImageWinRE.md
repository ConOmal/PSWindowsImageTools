---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageWinRE

## SYNOPSIS
Reports on the embedded WinRE image (Windows\System32\Recovery\Winre.wim) inside a mounted Windows image.
## SYNTAX

```
Get-WindowsImageWinRE [-ImagePath] <DirectoryInfo> [-Detailed] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Inspects the embedded WinRE image without using DISM or WIMGAPI — purely file-based. Reports presence, file size, last-modified time, and identity fields parsed from the WIM file header (format version, image count, part geometry, compression type, WIM GUID). With -Detailed, also reads the WIM XML metadata for the first image's display name.
## EXAMPLES

### Example 1: Report the embedded WinRE image for a mounted image
```powershell
Get-WindowsImageWinRE -ImagePath C:\Mount\Win11
```

Reports whether the mounted image carries an embedded WinRE image, its path, size, last-modified time, and WIM header identity.

### Example 2: Include the XML metadata display name
```powershell
Get-WindowsImageWinRE -ImagePath C:\Mount\Win11 -Detailed
```

Adds a best-effort read of the WIM XML metadata, exposing the first image's display name as XmlImageDisplayName.

## PARAMETERS

### -ImagePath
Path to the mounted Windows image directory to inspect for an embedded WinRE image

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue, ByPropertyName)
Accept wildcard characters: False
```

### -Detailed
Also read the embedded WinRE WIM's XML metadata for a best-effort first-image display name

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

### System.IO.DirectoryInfo

## OUTPUTS

### PSWindowsImageTools.Models.WinREIntelligenceReport

## NOTES

## RELATED LINKS