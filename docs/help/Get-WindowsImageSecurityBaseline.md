---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsImageSecurityBaseline

## SYNOPSIS
Reports compliance of one or more mounted Windows images against the curated security baseline (offline SOFTWARE, SYSTEM and default-user hives).

## SYNTAX

```
Get-WindowsImageSecurityBaseline [-MountedImages] <MountedWindowsImage[]> [-ContinueOnError]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Checks a curated, documented set of 22 security-relevant registry values (UAC, LSA/NTLM hardening, SMB signing, RDP and Remote Assistance, AutoRun, logon UX, and the default-profile screen-saver lock) against each image's offline SOFTWARE hive, SYSTEM hive, and default-user profile hive (Users\Default\NTUSER.DAT).

Each baseline entry is reported with its expected value, the observed value, and a state: Compliant (the value matches the baseline), NonCompliant (the value is present but wrong), or NotPresent (the key, value or whole hive is absent). The report also carries per-image counts and an overall IsCompliant verdict.

Hives are parsed in memory; no hive is mounted, so elevation is not required. Pair with Set-WindowsImageSecurityBaseline to remediate an image to the baseline.

## EXAMPLES

### EXAMPLE 1
```
Get-WindowsImageSecurityBaseline -MountedImages $img
```

Reports compliance of the image mounted at the path referenced by $img against all 22 baseline entries.

### EXAMPLE 2
```
Get-MountWindowsImageList ... | Get-WindowsImageSecurityBaseline
```

Checks every mounted image on the pipeline and returns one report per image.

### EXAMPLE 3
```
$report = Get-WindowsImageSecurityBaseline -MountedImages $img
$report.Entries | Where-Object State -ne 'Compliant' | Format-Table Hive, KeyPath, ValueName, ExpectedValue, ObservedValue, State
```

Lists only the baseline entries the image does not satisfy.

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

### -MountedImages
Mounted Windows images to check against the security baseline

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

### PSWindowsImageTools.Models.WindowsImageSecurityBaselineReport[]

## NOTES
The baseline definition (hive, key path, value name, expected value and rationale for every entry) is documented in docs/superpowers/specs/2026-09-04-security-baselines-design.md. DWORD values are compared numerically and strings case-insensitively, so "1" and 1 are equivalent observations.

## RELATED LINKS

