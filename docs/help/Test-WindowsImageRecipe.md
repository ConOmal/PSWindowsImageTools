---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Test-WindowsImageRecipe

## SYNOPSIS
Validates a Windows image recipe.
## SYNTAX

### ByPath
```
Test-WindowsImageRecipe [-RecipePath] <String> [-ImagePath <String>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

### ByRecipe
```
Test-WindowsImageRecipe [-Recipe] <BuildRecipe> [-ImagePath <String>] [-ProgressAction <ActionPreference>]
 [<CommonParameters>]
```

## DESCRIPTION
Checks recipe structure, regex patterns, referenced file paths, and section enablement. With ImagePath, also verifies the image filter selects at least one available image. Output includes all validation problems.
## EXAMPLES

## PARAMETERS

### -ImagePath
Optional WIM/ESD path to validate image selection against

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

### -Recipe
Recipe object to validate

```yaml
Type: BuildRecipe
Parameter Sets: ByRecipe
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -RecipePath
Path to the recipe JSON file

```yaml
Type: String
Parameter Sets: ByPath
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

### PSWindowsImageTools.Models.BuildRecipe

## OUTPUTS

### PSWindowsImageTools.Models.RecipeValidationResult

## NOTES

## RELATED LINKS
