---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Export-WindowsImageSBOM

## SYNOPSIS
Builds a Software Bill of Materials (SBOM) from a captured Windows image snapshot.
## SYNTAX

### BySnapshot
```
Export-WindowsImageSBOM [-Snapshot] <ImageSnapshot[]> [-DestinationPath] <DirectoryInfo>
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### BySnapshotFile
```
Export-WindowsImageSBOM -SnapshotPath <String> [-DestinationPath] <DirectoryInfo>
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Serializes the component and package inventory of an ImageSnapshot (from Get-WindowsImageSnapshot) into an SBOM document for compliance and inventory tracking.
## EXAMPLES

## PARAMETERS

### -DestinationPath
Destination directory for the SBOM JSON file(s)

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

### -Snapshot
Snapshot(s) from Get-WindowsImageSnapshot

```yaml
Type: ImageSnapshot[]
Parameter Sets: BySnapshot
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -SnapshotPath
Path to a saved snapshot JSON file

```yaml
Type: String
Parameter Sets: BySnapshotFile
Aliases:

Required: True
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

### PSWindowsImageTools.Models.ImageSnapshot[]

## OUTPUTS

### PSWindowsImageTools.Models.SbomReport[]

## NOTES

## RELATED LINKS
