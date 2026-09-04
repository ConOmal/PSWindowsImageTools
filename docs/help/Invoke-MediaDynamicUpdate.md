---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Invoke-MediaDynamicUpdate

## SYNOPSIS
Applies Dynamic Updates to Windows installation media.
## SYNTAX

```
Invoke-MediaDynamicUpdate [-MediaPath] <DirectoryInfo> [-UpdatesPath] <DirectoryInfo>
 [[-MountBasePath] <DirectoryInfo>] [-SkipBootImages] [-SkipWindowsImages] [-ContinueOnError] [-PerformCleanup]
 [-ValidateImages] [-AutoDismount] [-ResultOnly] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Services installation media in the documented order: Servicing Stack Update, SafeOS update, Cumulative Update, then Setup update, across boot.wim and install.wim. Supports cleanup, validation, auto-dismount, and result-only output.
## EXAMPLES

## PARAMETERS

### -AutoDismount
Automatically dismount images after processing

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

### -ContinueOnError
Continue processing even if individual operations fail

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

### -MediaPath
Root directory containing Windows installation media

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -MountBasePath
Base path for mounting images during processing

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PerformCleanup
Perform image cleanup after applying updates

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

### -ResultOnly
Return only the result object, not the mounted images

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

### -SkipBootImages
Skip boot image processing

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

### -SkipWindowsImages
Skip Windows image processing

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

### -UpdatesPath
Directory containing Dynamic Update packages

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

### -ValidateImages
Validate images after processing

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

### System.IO.DirectoryInfo

## OUTPUTS

### PSWindowsImageTools.Models.MediaDynamicUpdateResult

### PSWindowsImageTools.Models.MountedWindowsImage[]

## NOTES

## RELATED LINKS
