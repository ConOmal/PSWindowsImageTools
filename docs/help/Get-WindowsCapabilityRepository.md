---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsCapabilityRepository

## SYNOPSIS
Indexes a Features on Demand (FoD) payload source directory and reports the capability packages it offers.
## SYNTAX

```
Get-WindowsCapabilityRepository [-SourcePath] <String> [[-Name] <String>] [[-Architecture] <String>]
 [[-Language] <String>] [-GroupByName] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Scans a Features on Demand payload source directory (FoD disk/ISO layout) for capability .cab packages, so users can discover what a source offers before calling Add-WindowsImageCapability -SourcePath.

Capability metadata is parsed from the .cab file names per the convention `Microsoft-Windows-<CapabilityName>~<token>~<arch>~<language>~<version>.cab`: the capability name is the segment after the Microsoft-Windows- prefix, and token, architecture, language and version are the following tilde-separated segments. Empty architecture or language segments are reported as 'neutral' (language-neutral packages); an empty version segment is reported as an empty string.

All metadata is filename-derived — the cab file itself is never opened. Files whose names do not follow the convention are skipped (with a verbose note and a count in the scan summary). Parsed names are not guaranteed to equal the DISM capability strings reported by Get-WindowsImageCapability; use that cmdlet on the target image to confirm the exact name before an add.

By default one entry is emitted per indexed cab. GroupByName collapses multi-architecture/multi-language cabs into one summary entry per capability name. Strictly read-only: no DISM, no mounted image.

## EXAMPLES

### Example 1: Index a FoD disk
```
PS C:\> Get-WindowsCapabilityRepository -SourcePath 'E:\'
```
Lists every capability package found at the root of the FoD disk mounted on E:.

### Example 2: Find RSAT packages for amd64
```
PS C:\> Get-WindowsCapabilityRepository -SourcePath 'E:\' -Name 'Rsat\.' -Architecture 'amd64'
```

### Example 3: Summarize per capability name
```
PS C:\> Get-WindowsCapabilityRepository -SourcePath 'E:\' -GroupByName
```

## PARAMETERS

### -Architecture
Regular expression the architecture must match (e.g., 'amd64'; 'neutral' for language-neutral)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -GroupByName
Collapse multi-architecture/multi-language packages into one summary entry per capability name

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

### -Language
Regular expression the language must match (e.g., 'en-us'; 'neutral' for language-neutral)

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Name
Regular expression the capability name must match (e.g., 'Rsat\.')

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

### -SourcePath
Directory containing the FoD payload .cab files to index (e.g., a FoD disk/ISO root or sources\LanguagesAndOptionalFeatures)

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

### PSWindowsImageTools.Models.CapabilityRepositoryEntry[]

### PSWindowsImageTools.Models.CapabilityRepositoryGroup[]

## NOTES
Only the top level of -SourcePath is scanned; point it at the directory that contains the cabs. This cmdlet never touches DISM or a mounted image.

## RELATED LINKS
