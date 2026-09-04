---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-RegistryHiveOnDemand

## SYNOPSIS
Reads registry data from offline hive files without mounting.
## SYNTAX

```
Get-RegistryHiveOnDemand [-Path] <FileInfo> [[-KeyPath] <String[]>] [-MaxDepth <Int32>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Parses hive files in memory (no RegLoadKey, no file handles held). SOFTWARE hives are auto-detected and return Windows version info, installed software, and Windows Update configuration. Use KeyPath with MaxDepth for arbitrary key trees.
## EXAMPLES

### Example 1
```powershell
Get-RegistryHiveOnDemand -Path "C:\Mount\Windows\System32\config\SOFTWARE`" -KeyPath "Microsoft\Windows NT\CurrentVersion"
```

Performs the operation shown above.

## PARAMETERS

### -KeyPath
Registry key paths to read (e.g., 'Microsoft\Windows\CurrentVersion')

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MaxDepth
Maximum depth to recurse into subkeys (default: 1, 0 = no recursion, -1 = unlimited)

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

### -Path
Path to the registry hive file

```yaml
Type: FileInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
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

### System.IO.FileInfo

## OUTPUTS

### System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e],[System.Object, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]

## NOTES

## RELATED LINKS
