---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Update-WindowsImageOnline

## SYNOPSIS
Discovers, downloads, and installs the latest updates into Windows images.
## SYNTAX

### ByPackages
```
Update-WindowsImageOnline [-ImagePath] <String> [[-UpdatePackages] <WindowsUpdatePackage[]>]
 [-OperatingSystem <String>] [-Architecture <String>] [-DestinationPath <String>] [-MountPath <String>]
 [-MaxImages <Int32>] [-MaxUpdates <Int32>] [-ContinueOnError] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### ByQuery
```
Update-WindowsImageOnline [-ImagePath] <String> -Query <String> [-OperatingSystem <String>]
 [-Architecture <String>] [-DestinationPath <String>] [-MountPath <String>] [-MaxImages <Int32>]
 [-MaxUpdates <Int32>] [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
One-liner update servicing: discovers the latest cumulative KB for a Windows release (or uses -Query/-UpdatePackages), downloads from the Microsoft Update Catalog, then mounts, services, and saves each selected image. MaxImages and MaxUpdates bound the work; ContinueOnError keeps servicing after failures.
## EXAMPLES

### Example 1
```powershell
Update-WindowsImageOnline -ImagePath "C:\Images\install.wim" -Architecture x64
```

Performs the operation shown above.

## PARAMETERS

### -Architecture
Architecture filter for catalog search (default: x64)

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: x64, x86, arm64

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ContinueOnError
Continue servicing remaining images when one fails

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

### -DestinationPath
Directory for downloaded updates

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

### -ImagePath
Path to the WIM/ESD file to service

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

### -MaxImages
Maximum number of images to service

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MaxUpdates
Maximum number of updates to install per image

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MountPath
Base directory for mounting

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

### -OperatingSystem
Operating system for automatic latest-KB discovery (default: Windows 11)

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

### -Query
Catalog search query (overrides automatic latest-KB discovery)

```yaml
Type: String
Parameter Sets: ByQuery
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -UpdatePackages
Pre-downloaded update packages from Save-WindowsUpdateCatalogResult

```yaml
Type: WindowsUpdatePackage[]
Parameter Sets: ByPackages
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: True (ByValue)
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

### PSWindowsImageTools.Models.WindowsUpdatePackage[]

## OUTPUTS

### PSWindowsImageTools.Models.ImageOperationResult[]

## NOTES

## RELATED LINKS
