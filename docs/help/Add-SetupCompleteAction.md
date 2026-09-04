---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Add-SetupCompleteAction

## SYNOPSIS
Adds custom first-boot actions to a Windows image.
## SYNTAX

```
Add-SetupCompleteAction [-ImagePath] <DirectoryInfo> [[-Command] <String[]>] [-Description <String>]
 [-Priority <Int32>] [-ContinueOnError] [-ScriptFile <FileInfo>] [-CopyFiles <FileSystemInfo[]>]
 [-CopyDestination <String>] [-Backup] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Copies scripts/files into the image and registers them to run at setup completion. Supports inline commands, script files, and file copy operations with priorities.
## EXAMPLES

## PARAMETERS

### -Backup
Whether to create a backup of the existing SetupComplete.cmd

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

### -Command
Command or script to execute during SetupComplete phase

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ContinueOnError
Whether to continue execution if this action fails

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

### -CopyDestination
Destination path in the image for copied files (relative to C:)

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

### -CopyFiles
Path to files/directories to copy to the image

```yaml
Type: FileSystemInfo[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Description
Description of the action for documentation purposes

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
Path to the mounted Windows image directory

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -Priority
Priority order for the action (lower numbers execute first)

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

### -ScriptFile
Path to a script file to copy and execute

```yaml
Type: FileInfo
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

### System.String[]

## OUTPUTS

### PSWindowsImageTools.Models.SetupCompleteActionResult

## NOTES

## RELATED LINKS
