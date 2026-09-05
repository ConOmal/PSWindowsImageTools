---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsBootImage

## SYNOPSIS
Locates boot.wim under an extracted Windows installation media root and reports the images it contains.
## SYNTAX

```
Get-WindowsBootImage [-MediaRoot] <DirectoryInfo> [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Locates sources\boot.wim under an extracted Windows installation media root and reports the images it contains. A missing boot.wim is an expected outcome for some media layouts: the cmdlet emits a warning and does not write an object, rather than throwing an error.
## EXAMPLES

## PARAMETERS

### -MediaRoot
Root directory of extracted Windows installation media

```yaml
Type: DirectoryInfo
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

### System.IO.DirectoryInfo

## OUTPUTS

### PSWindowsImageTools.Models.BootImageInfo

## NOTES

## RELATED LINKS
