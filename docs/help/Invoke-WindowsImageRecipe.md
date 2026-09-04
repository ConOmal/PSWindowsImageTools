---
external help file: PSWindowsImageTools.dll-Help.xml
Module Name: PSWindowsImageTools
online version: https://github.com/Grace-Solutions/PSWindowsImageTools/blob/main/docs/CmdletReference.md
schema: 2.0.0
---

# Invoke-WindowsImageRecipe

## SYNOPSIS
Applies a Windows image recipe to matching images.
## SYNTAX

### ByPath
```
Invoke-WindowsImageRecipe [-RecipePath] <String> [-ImagePath] <String> [-MountPath <String>]
 [-MaxImages <Int32>] [-SkipValidation] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### ByRecipe
```
Invoke-WindowsImageRecipe [-Recipe] <BuildRecipe> [-ImagePath] <String> [-MountPath <String>]
 [-MaxImages <Int32>] [-SkipValidation] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Loads a BuildRecipe JSON, validates it, selects matching images by regex, then for each image: mounts read-write, applies enabled sections in deterministic order (AppX removal, file copy, wallpapers, features, drivers, updates, Features on Demand, registry modifications), and saves. MaxImages guards runaway selections; SkipValidation bypasses pre-flight checks.
## EXAMPLES

### Example 1
```powershell
Invoke-WindowsImageRecipe -RecipePath "C:\Recipes\corporate.json" -ImagePath "install.wim`"
```

Performs the operation shown above.

## PARAMETERS

### -ImagePath
Path to the WIM/ESD file to apply the recipe to

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MaxImages
Maximum number of images to process

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

### -MountPath
Base directory for mounting

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
Recipe object to apply

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

### -SkipValidation
Skip structural validation before executing

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

### PSWindowsImageTools.Models.RecipeImageExecutionResult[]

## NOTES

## RELATED LINKS
