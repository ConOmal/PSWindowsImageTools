---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Test-UnattendXMLConfiguration

## SYNOPSIS
Validates an Unattend XML configuration file and returns a structured validation report.
## SYNTAX

```
Test-UnattendXMLConfiguration [-Path] <FileInfo> [-Severity <UnattendValidationSeverity>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Read-only validation of an unattend.xml file. Checks well-formedness, root element and namespace, settings pass attributes (windowsPE, offlineServicing, generalize, specialize, oobeSystem), component name/duplicate/architecture sanity, RunSynchronous/RunAsynchronous command ordering, settings structure, and common mistakes (CopyProfile outside the specialize pass, deprecated SkipMachineOOBE/SkipUserOOBE settings). No DISM, no image mounting, no file writes.

Returns an UnattendValidationReport with per-issue details (Severity, Pass, ElementPath, Message, RuleId) plus an overall IsValid. IsValid is always computed over the complete issue set; -Severity only narrows the reported issues.
## EXAMPLES

### Example 1
```powershell
PS C:\> Test-UnattendXMLConfiguration -Path C:\media\unattend.xml
```
Validates the file and reports all errors and warnings.

### Example 2
```powershell
PS C:\> Test-UnattendXMLConfiguration -Path C:\media\unattend.xml -Severity Error
```
Validates the file and reports errors only; the report is valid (exit logic can key on IsValid) when there are no errors.
## PARAMETERS

### -Path
Unattend XML file to validate.

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

### -Severity
Minimum severity of issues to report. Warning (default) reports errors and warnings; Error reports errors only.

```yaml
Type: UnattendValidationSeverity
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: Warning
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

### System.IO.FileInfo

## OUTPUTS

### PSWindowsImageTools.Models.UnattendValidationReport

## NOTES

## RELATED LINKS
