---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Get-WindowsDynamicUpdate

## SYNOPSIS
Discovers the available Windows media Dynamic Updates for a Windows build in the Microsoft Update Catalog.
## SYNTAX

```
Get-WindowsDynamicUpdate [-Build] <String> [-Architecture <String>] [-Type <String>] [-OSLabel <String>]
 [-DebugMode] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Discovers the Dynamic Updates needed by media servicing (Servicing Stack Update, Safe OS Dynamic Update, Latest Cumulative Update and Setup Dynamic Update) for a Windows build, so the workflow discover -> download -> apply becomes one-liner-able. The build is resolved to catalog title labels (for example build 26100 maps to "Windows 11 Version 24H2" and "Windows Server 2025"), one catalog search per unique query is performed (Servicing Stack / Cumulative / Dynamic Update), the results are classified into Dynamic Update types from their titles, filtered by architecture, deduplicated and reduced to the latest package per requested type in apply order (ServicingStack, SafeOS, Cumulative, Setup). A download URL is resolved for each selected package when cheaply available. Output feeds download plumbing (Save-WindowsUpdateCatalogResult) and Invoke-MediaDynamicUpdate for apply. This cmdlet is read-only and makes no changes to media or images.

## EXAMPLES

### Example 1
```powershell
Get-WindowsDynamicUpdate -Build 26100
```

Discovers the latest Servicing Stack, SafeOS, Cumulative and Setup Dynamic Updates for Windows 11 24H2 / Windows Server 2025 (amd64).

### Example 2
```powershell
Get-WindowsDynamicUpdate -Build 26100.1234 -Type Cumulative -Architecture x64
```

Discovers only the latest Cumulative Update for build 26100 (x64).

### Example 3
```powershell
Get-WindowsDynamicUpdate -Build 26100 | Select-Object UpdateType, KBNumber, SizeFormatted, DownloadUrl
```

Shows the discovered updates with their KB article, size and resolved download URL.

## PARAMETERS

### -Architecture
Target architecture; "amd64" and "x64" both map to the catalog's x64 results.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: amd64, x64, x86, arm64

Required: False
Position: Named
Default value: amd64
Accept pipeline input: False
Accept wildcard characters: False
```

### -Build
Windows build to discover Dynamic Updates for (e.g., "26100", "26100.1234" or "10.0.26100.1234").

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -DebugMode
Enables detailed catalog HTTP logging.

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

### -OSLabel
Explicit catalog title label override (e.g., "Windows Server 2025"). By default the label(s) are resolved from the build number.

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

### -Type
Dynamic Update type filter.

```yaml
Type: String
Parameter Sets: (All)
Aliases:
Accepted values: ServicingStack, Cumulative, SafeOS, Setup, All

Required: False
Position: Named
Default value: All
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

### System.String

## OUTPUTS

### PSWindowsImageTools.Models.WindowsDynamicUpdate[]

## NOTES

Catalog queries use title full-text search sorted by last-updated (first results page only, 25 rows per search). Shared client/server builds resolve both labels and run one search per label.

## RELATED LINKS
