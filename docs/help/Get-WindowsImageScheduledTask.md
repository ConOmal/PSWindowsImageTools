---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageScheduledTask

## SYNOPSIS
Reports the scheduled tasks registered in one or more mounted Windows images from each image's offline SOFTWARE hive (Schedule\TaskCache).

## SYNTAX

```
Get-WindowsImageScheduledTask [-MountedImages] <MountedWindowsImage[]> [[-Path] <String>] [-Detailed] [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Walks the Task Scheduler registration cache (Schedule\TaskCache) in each image's offline SOFTWARE hive and reports every registered task: the task path (composed from the Tree hierarchy), the GUID linking the Tree leaf to its Tasks entry, the friendly state derived from the entry's State DWORD (Unknown when absent or unrecognized, with the raw DWORD always available), the Uri value where present, and whether the Tasks cache entry exists. Raw cache-entry values are attached with -Detailed. No hive is mounted and no DISM session is opened, so elevation is not required.

This cmdlet is strictly read-only. The per-task definition blob inside Tasks\<GUID> (triggers, actions, principal, registration info) is undocumented binary and is intentionally not parsed or modified; only what is reliably readable is reported.

## EXAMPLES

### EXAMPLE 1
```
Get-WindowsImageScheduledTask -MountedImages $img
```

Lists every scheduled task registered in the image mounted at the path referenced by $img.

### EXAMPLE 2
```
Get-WindowsImageScheduledTask -MountedImages $img -Path '\Microsoft\Windows\Defrag\ScheduledDefrag'
```

Returns the ScheduledDefrag task. The path is matched exactly (case-insensitive).

### EXAMPLE 3
```
Get-WindowsImageScheduledTask -MountedImages $img -Path '^\\Microsoft\\Windows\\UpdateOrchestrator\\' -Detailed
```

Returns every task under \Microsoft\Windows\UpdateOrchestrator (the filter is an anchored, case-insensitive regular expression), including the raw registry values of each task cache entry.

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
Include the raw registry values of each task cache entry. Binary values (the undocumented task-definition blob and the validation hash) appear only in the registry reader's decoded string form and are not interpreted.

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

### -Path
Task path to filter by. Matched exactly (case-insensitive) first; otherwise treated as an anchored, case-insensitive regular expression. An invalid pattern matches nothing.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
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

### PSWindowsImageTools.Models.WindowsImageScheduledTaskInfo[]

## NOTES

## RELATED LINKS
