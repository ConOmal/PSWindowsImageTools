---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-PatchTuesday

## SYNOPSIS
Calculates Patch Tuesday dates.
## SYNTAX

### Next (Default)
```
Get-PatchTuesday [-After <DateTime>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### All
```
Get-PatchTuesday [-After <DateTime>] [-All] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### Remaining
```
Get-PatchTuesday [-After <DateTime>] [-Remaining] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Returns Patch Tuesday (second Tuesday) dates. Remaining lists upcoming dates; All lists every month of the calendar year; After filters by date.
## EXAMPLES

### Example 1
```powershell
$next = Get-PatchTuesday -Remaining | Select-Object -First 1
```

Performs the operation shown above.

## PARAMETERS

### -After
Specifies the After parameter.

```yaml
Type: DateTime
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -All
Specifies the All parameter.

```yaml
Type: SwitchParameter
Parameter Sets: All
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Remaining
Specifies the Remaining parameter.

```yaml
Type: SwitchParameter
Parameter Sets: Remaining
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

### None

## OUTPUTS

### PSWindowsImageTools.Models.PatchTuesday[]

## NOTES

## RELATED LINKS
