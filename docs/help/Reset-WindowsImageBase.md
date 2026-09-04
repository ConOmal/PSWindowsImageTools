---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Reset-WindowsImageBase

## SYNOPSIS
Performs component cleanup on mounted Windows images.
## SYNTAX

### ByObject
```
Reset-WindowsImageBase [-MountedImages] <MountedWindowsImage[]> [-ComponentCleanup] [-AnalyzeOnly]
 [-ContinueOnError] [-Defer] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByPath
```
Reset-WindowsImageBase [-Path] <DirectoryInfo[]> [-ComponentCleanup] [-AnalyzeOnly] [-ContinueOnError] [-Defer]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Runs DISM component cleanup (with optional ComponentCleanup /resetbase behavior) to shrink images by removing superseded components. AnalyzeOnly reports savings without cleaning; Defer defers cleanup tasks.
## EXAMPLES

### Example 1
```powershell
$mounted | Reset-WindowsImageBase -ComponentCleanup
```

Performs the operation shown above.

## PARAMETERS

### -AnalyzeOnly
Specifies the AnalyzeOnly parameter.

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

### -ComponentCleanup
Specifies the ComponentCleanup parameter.

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
Specifies the ContinueOnError parameter.

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

### -Defer
Specifies the Defer parameter.

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
Specifies the MountedImages parameter.

```yaml
Type: MountedWindowsImage[]
Parameter Sets: ByObject
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -Path
Specifies the Path parameter.

```yaml
Type: DirectoryInfo[]
Parameter Sets: ByPath
Aliases:

Required: True
Position: 0
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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

## NOTES

## RELATED LINKS
