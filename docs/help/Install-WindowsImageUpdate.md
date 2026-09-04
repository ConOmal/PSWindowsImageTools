---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Install-WindowsImageUpdate

## SYNOPSIS
Installs Windows updates into mounted images.
## SYNTAX

### FromPackages
```
Install-WindowsImageUpdate [-MountedImages] <MountedWindowsImage[]> [-UpdatePackages] <WindowsUpdatePackage[]>
 [-IgnoreCheck] [-PreventPending] [-ContinueOnError] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### FromFiles
```
Install-WindowsImageUpdate [-UpdatePath] <FileSystemInfo[]> [-ImagePath] <DirectoryInfo> [-IgnoreCheck]
 [-PreventPending] [-ContinueOnError] [-ValidateImage] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Installs .cab/.msu update packages into mounted images via the DISM API. Parameter sets accept downloaded WindowsUpdatePackage objects (pipeline) or file paths. IgnoreCheck skips applicability checks; PreventPending blocks on pending operations.
## EXAMPLES

### Example 1
```powershell
$mounted | Install-WindowsImageUpdate -UpdatePackages $packages
```

Performs the operation shown above.

## PARAMETERS

### -ContinueOnError
Continues processing other updates even if one fails

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

### -IgnoreCheck
Prevents DISM from checking the applicability of the package

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

### -ImagePath
Path to the mounted Windows image directory

```yaml
Type: DirectoryInfo
Parameter Sets: FromFiles
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -MountedImages
Mounted Windows images from Mount-WindowsImageList

```yaml
Type: MountedWindowsImage[]
Parameter Sets: FromPackages
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -PreventPending
Prevents the automatic installation of prerequisite packages

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

### -UpdatePackages
Windows Update packages from Save-WindowsUpdateCatalogResult

```yaml
Type: WindowsUpdatePackage[]
Parameter Sets: FromPackages
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -UpdatePath
Path to the update file (CAB/MSU) or directory containing updates

```yaml
Type: FileSystemInfo[]
Parameter Sets: FromFiles
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -ValidateImage
Validates that the image is suitable for update integration

```yaml
Type: SwitchParameter
Parameter Sets: FromFiles
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

### PSWindowsImageTools.Models.WindowsUpdatePackage[]

### System.IO.FileSystemInfo[]

### System.IO.DirectoryInfo

## OUTPUTS

### PSWindowsImageTools.Models.MountedWindowsImage[]

### PSWindowsImageTools.Models.WindowsImageUpdateResult[]

## NOTES

## RELATED LINKS
