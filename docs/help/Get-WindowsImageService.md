---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageService

## SYNOPSIS
Enumerates the services configured in one or more mounted Windows images from each image's offline SYSTEM hive.

## SYNTAX

```
Get-WindowsImageService [-MountedImages] <MountedWindowsImage[]> [-Name <String>] [-Detailed] [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Reports the start type, display name, image path, description, and delayed-auto-start state of every service under ControlSet001\Services in each image's offline SYSTEM hive. No hive is mounted, so elevation is not required. Pair with Set-WindowsImageService to change the start type.

## EXAMPLES

### EXAMPLE 1
```
Get-WindowsImageService -MountedImages $img
```

Lists every service configured in the image mounted at the path referenced by $img.

### EXAMPLE 2
```
Get-WindowsImageService -MountedImages $img -Name 'Dhcp'
```

Returns the Dhcp service. The name is matched exactly (case-insensitive).

### EXAMPLE 3
```
Get-WindowsImageService -MountedImages $img -Name '^.*Agent$' -Detailed
```

Returns every service whose name ends in "Agent", including the raw registry values of each service key.

## PARAMETERS

### -ContinueOnError
Continue processing other images if one fails

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

### -Detailed
Include the raw registry values of each service key

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
Mounted Windows images to query

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

### -Name
Service name to filter by. Matched exactly (case-insensitive) first; otherwise treated as an anchored, case-insensitive regular expression. An invalid pattern matches nothing.

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

### PSWindowsImageTools.Models.MountedWindowsImage[]

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageServiceInfo[]

## NOTES

## RELATED LINKS