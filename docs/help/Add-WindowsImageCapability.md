---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Add-WindowsImageCapability

## SYNOPSIS
Adds capabilities (Features on Demand) to mounted Windows images.
## SYNTAX

```
Add-WindowsImageCapability [-MountedImages] <MountedWindowsImage[]> [-CapabilityName] <String[]> [-LimitAccess]
 [-SourcePath <String[]>] [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Adds DISM capabilities such as Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0. Optionally restrict sources with LimitAccess and provide offline SourcePath locations.
## EXAMPLES

### Example 1
```powershell
$mounted | Add-WindowsImageCapability -CapabilityName "Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0`"
```

Performs the operation shown above.

## PARAMETERS

### -CapabilityName
Names of the capabilities to add (e.g., 'Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0')

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ContinueOnError
Continue processing remaining capabilities when one fails

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

### -LimitAccess
Prevent Windows Update as a source

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
Mounted Windows images to add capabilities to

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

### -SourcePath
Optional source paths for the capability payload

```yaml
Type: String[]
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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.ImageOperationResult[]

## NOTES

## RELATED LINKS
