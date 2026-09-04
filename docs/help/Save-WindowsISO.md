---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Save-WindowsISO

## SYNOPSIS
Downloads a Windows ISO, resolved via Get-WindowsISODownloadInfo or supplied directly with -Url.
## SYNTAX

### FromDownloadInfo
```
Save-WindowsISO [-InputObject] <WindowsISODownloadInfo> [-DestinationPath] <FileInfo> [-Force] [-Resume]
 [-Verify] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### FromUrl
```
Save-WindowsISO -Url <Uri> [-DestinationPath] <FileInfo> [-Force] [-Resume] [-Verify]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Downloads the ISO resolved by Get-WindowsISODownloadInfo (or a supplied Url) to a destination path with progress reporting. Force overwrites existing files.
## EXAMPLES

## PARAMETERS

### -DestinationPath
{{ Fill DestinationPath Description }}

```yaml
Type: FileInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Force
{{ Fill Force Description }}

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

### -InputObject
{{ Fill InputObject Description }}

```yaml
Type: WindowsISODownloadInfo
Parameter Sets: FromDownloadInfo
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Resume
{{ Fill Resume Description }}

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

### -Url
{{ Fill Url Description }}

```yaml
Type: Uri
Parameter Sets: FromUrl
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Verify
{{ Fill Verify Description }}

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

### PSWindowsImageTools.Models.WindowsISODownloadInfo

## OUTPUTS

### PSWindowsImageTools.Models.WindowsISOFile

## NOTES

## RELATED LINKS
