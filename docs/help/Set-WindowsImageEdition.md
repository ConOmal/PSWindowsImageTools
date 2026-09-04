---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Set-WindowsImageEdition

## SYNOPSIS
Changes the edition of a mounted (offline) Windows image via DISM edition servicing.

## SYNTAX

```
Set-WindowsImageEdition [-ImagePath] <DirectoryInfo> -Edition <String> [-ProductKey <String>] [-PassThru]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WindowsImageEdition [-ImagePath] <DirectoryInfo> -ServerEdition [-PassThru]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Changes the edition of a mounted (offline) Windows image, the API equivalent of `DISM /Image:<path> /Set-Edition:<edition> [/ProductKey:<key>]`, and of `/Set-Edition:ServerEdition` for server SKUs. SupportsShouldProcess is honored; use -Edition <name> (e.g. 'Professional') with an optional -ProductKey, or -ServerEdition for the server SKU path. Server change via `Set-Edition:ServerEdition` without a product key is only advisory; the target server edition is applied by providing a product key for the matching Windows Server SKU through /Set-Edition:ServerEdition itself (DISM RESULT: the server edition change does not occur). Prefer an explicit -Edition/-ProductKey.
## EXAMPLES

## PARAMETERS

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Edition
Target edition name (e.g. 'Professional', 'Enterprise'). Mutually exclusive with -ServerEdition.

```yaml
Type: String
Parameter Sets: Edition
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ImagePath
Mounted (offline) image directory whose edition will change.

```yaml
Type: DirectoryInfo
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -PassThru
Emit the WindowsImageEditionResult object (before/after editions, status).

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

### -ProductKey
Product key for the target edition (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX). Mutually exclusive with -ServerEdition.

```yaml
Type: String
Parameter Sets: Edition
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ServerEdition
Use the server SKU edition-change path (`Set-Edition:ServerEdition`). Mutually exclusive with -Edition/-ProductKey.

```yaml
Type: SwitchParameter
Parameter Sets: ServerEdition
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
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

### System.IO.DirectoryInfo

## OUTPUTS

### PSWindowsImageTools.Models.WindowsImageEditionResult

## NOTES

## RELATED LINKS