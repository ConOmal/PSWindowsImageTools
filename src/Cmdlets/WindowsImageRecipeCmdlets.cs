using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Creates a new Windows image recipe scaffold
    /// </summary>
    [Cmdlet(VerbsCommon.New, "WindowsImageRecipe")]
    [OutputType(typeof(BuildRecipe))]
    public class NewWindowsImageRecipeCmdlet : PSCmdlet
    {
        private const string ComponentName = "New-WindowsImageRecipe";

        /// <summary>
        /// Destination path for the recipe JSON file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            HelpMessage = "Destination path for the recipe JSON file")]
        [ValidateNotNullOrEmpty]
        public string RecipePath { get; set; } = null!;

        /// <summary>
        /// Recipe name
        /// </summary>
        [Parameter(Position = 1, HelpMessage = "Recipe name")]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; } = "New Recipe";

        /// <summary>
        /// Recipe description
        /// </summary>
        [Parameter(HelpMessage = "Recipe description")]
        public string? Description { get; set; }

        /// <summary>
        /// Recipe author
        /// </summary>
        [Parameter(HelpMessage = "Recipe author")]
        public string? Author { get; set; }

        /// <summary>
        /// Regex expression to include images by name (e.g., 'Pro')
        /// </summary>
        [Parameter(HelpMessage = "Regex expression to include images by name")]
        [ValidateNotNullOrEmpty]
        public string? InclusionExpression { get; set; }

        /// <summary>
        /// Regex expression to exclude images by name (e.g., 'Home')
        /// </summary>
        [Parameter(HelpMessage = "Regex expression to exclude images by name")]
        [ValidateNotNullOrEmpty]
        public string? ExclusionExpression { get; set; }

        /// <summary>
        /// Overwrite the recipe file if it exists
        /// </summary>
        [Parameter(HelpMessage = "Overwrite the recipe file if it exists")]
        public SwitchParameter Force { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                var resolvedPath = GetUnresolvedProviderPathFromPSPath(RecipePath) ?? RecipePath;

                if (File.Exists(resolvedPath) && !Force.IsPresent)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new IOException($"Recipe file already exists: {resolvedPath}. Use -Force to overwrite."),
                        "RecipeFileExists",
                        ErrorCategory.ResourceExists,
                        resolvedPath));
                    return;
                }

                var recipe = new BuildRecipe
                {
                    Metadata = new RecipeMetadata
                    {
                        Name = Name,
                        Description = Description ?? string.Empty,
                        Author = Author ?? string.Empty,
                        CreatedUtc = DateTime.UtcNow,
                        ModifiedUtc = DateTime.UtcNow
                    },
                    ImageFilter = new ImageFilterSection
                    {
                        Enabled = !string.IsNullOrEmpty(InclusionExpression) || !string.IsNullOrEmpty(ExclusionExpression),
                        InclusionExpression = InclusionExpression ?? string.Empty,
                        ExclusionExpression = ExclusionExpression ?? string.Empty
                    }
                };

                RecipeService.SaveRecipe(recipe, resolvedPath);
                LoggingService.WriteVerbose(this, ComponentName, $"Recipe scaffold written to {resolvedPath}");

                WriteObject(recipe);
            }
            catch (Exception ex) when (!(ex is PSInvalidOperationException))
            {
                ThrowTerminatingError(new ErrorRecord(ex, "NewRecipeFailed", ErrorCategory.WriteError, RecipePath));
            }
        }
    }

    /// <summary>
    /// Validates a Windows image recipe
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Test, "WindowsImageRecipe")]
    [OutputType(typeof(RecipeValidationResult))]
    public class TestWindowsImageRecipeCmdlet : PSCmdlet
    {
        private const string ComponentName = "Test-WindowsImageRecipe";

        /// <summary>
        /// Path to the recipe JSON file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ByPath",
            HelpMessage = "Path to the recipe JSON file")]
        [ValidateNotNullOrEmpty]
        public string RecipePath { get; set; } = null!;

        /// <summary>
        /// Recipe object to validate (from pipeline)
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ByRecipe",
            ValueFromPipeline = true,
            HelpMessage = "Recipe object to validate")]
        [ValidateNotNull]
        public BuildRecipe? Recipe { get; set; }

        /// <summary>
        /// Optional WIM/ESD path to validate image selection against
        /// </summary>
        [Parameter(HelpMessage = "Optional WIM/ESD path to validate image selection against")]
        [ValidateNotNullOrEmpty]
        public string? ImagePath { get; set; }

        protected override void ProcessRecord()
        {
            var result = new RecipeValidationResult();

            try
            {
                var recipe = Recipe;
                string? resolvedRecipePath = null;

                if (ParameterSetName == "ByPath")
                {
                    resolvedRecipePath = GetUnresolvedProviderPathFromPSPath(RecipePath) ?? RecipePath;
                    result.RecipePath = resolvedRecipePath;

                    if (!File.Exists(resolvedRecipePath))
                    {
                        result.Problems.Add($"Recipe file not found: {resolvedRecipePath}");
                        result.IsValid = false;
                        WriteObject(result);
                        return;
                    }

                    try
                    {
                        recipe = RecipeService.LoadRecipe(resolvedRecipePath);
                    }
                    catch (Exception ex)
                    {
                        result.Problems.Add($"Failed to load recipe: {ex.Message}");
                        result.IsValid = false;
                        WriteObject(result);
                        return;
                    }
                }

                if (recipe == null)
                {
                    result.Problems.Add("Recipe is null");
                    result.IsValid = false;
                    WriteObject(result);
                    return;
                }

                result.RecipeName = recipe.Metadata.Name;

                var recipeService = new RecipeService(ModuleCallbacks.FromCmdlet(this));
                result.Problems.AddRange(recipeService.ValidateRecipe(recipe));

                // Validate image selection against a real image when provided
                if (ImagePath != null && result.Problems.Count == 0)
                {
                    var resolvedImagePath = GetUnresolvedProviderPathFromPSPath(ImagePath) ?? ImagePath;

                    if (!File.Exists(resolvedImagePath))
                    {
                        result.Problems.Add($"Image file not found: {resolvedImagePath}");
                    }
                    else
                    {
                        try
                        {
                            using var imageService = WindowsImageService.ForCmdlet(this);
                            var images = imageService.GetImageInfo(resolvedImagePath);
                            var selected = recipeService.SelectImages(recipe, images);
                            result.ImageCountAvailable = images.Count;
                            result.ImageCountSelected = selected.Count;

                            if (selected.Count == 0)
                            {
                                result.Problems.Add($"Image filter selects 0 of {images.Count} available images");
                            }
                        }
                        catch (Exception ex)
                        {
                            result.Problems.Add($"Failed to read image file: {ex.Message}");
                        }
                    }
                }

                result.IsValid = result.Problems.Count == 0;
            }
            catch (Exception ex)
            {
                result.Problems.Add($"Validation failed: {ex.Message}");
                result.IsValid = false;
            }

            WriteObject(result);
        }
    }

    /// <summary>
    /// Applies a Windows image recipe to matching images: mounts read-write, applies enabled
    /// sections, and saves each image
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "WindowsImageRecipe")]
    [OutputType(typeof(RecipeImageExecutionResult[]))]
    public class InvokeWindowsImageRecipeCmdlet : PSCmdlet
    {
        private const string ComponentName = "Invoke-WindowsImageRecipe";
        private const string ActivityName = "Applying Windows Image Recipe";

        /// <summary>
        /// Path to the recipe JSON file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ByPath",
            HelpMessage = "Path to the recipe JSON file")]
        [ValidateNotNullOrEmpty]
        public string RecipePath { get; set; } = null!;

        /// <summary>
        /// Recipe object to apply (from pipeline)
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ByRecipe",
            ValueFromPipeline = true,
            HelpMessage = "Recipe object to apply")]
        [ValidateNotNull]
        public BuildRecipe? Recipe { get; set; }

        /// <summary>
        /// Path to the WIM/ESD file to apply the recipe to
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Path to the WIM/ESD file to apply the recipe to")]
        [ValidateNotNullOrEmpty]
        public string ImagePath { get; set; } = null!;

        /// <summary>
        /// Base directory for mounting (uses the module default when omitted)
        /// </summary>
        [Parameter(HelpMessage = "Base directory for mounting")]
        [ValidateNotNullOrEmpty]
        public string? MountPath { get; set; }

        /// <summary>
        /// Maximum number of images to process (safety limit)
        /// </summary>
        [Parameter(HelpMessage = "Maximum number of images to process")]
        [ValidateRange(1, int.MaxValue)]
        public int MaxImages { get; set; } = 10;

        /// <summary>
        /// Skip structural validation before executing
        /// </summary>
        [Parameter(HelpMessage = "Skip structural validation before executing")]
        public SwitchParameter SkipValidation { get; set; }

        protected override void ProcessRecord()
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // Load recipe
                BuildRecipe recipe;
                if (ParameterSetName == "ByPath")
                {
                    var resolvedRecipePath = GetUnresolvedProviderPathFromPSPath(RecipePath) ?? RecipePath;
                    recipe = RecipeService.LoadRecipe(resolvedRecipePath);
                }
                else
                {
                    recipe = Recipe!;
                }

                LoggingService.WriteVerbose(this, ComponentName,
                    $"Recipe '{recipe.Metadata.Name}' ({recipe.Metadata.Version}) loaded");

                // Resolve image path
                var resolvedImagePath = GetUnresolvedProviderPathFromPSPath(ImagePath) ?? ImagePath;
                if (!File.Exists(resolvedImagePath))
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new FileNotFoundException($"Image file not found: {resolvedImagePath}"),
                        "ImageFileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedImagePath));
                    return;
                }

                // Resolve mount root
                var mountRoot = MountPath != null
                    ? (GetUnresolvedProviderPathFromPSPath(MountPath) ?? MountPath)
                    : ConfigurationService.DefaultMountRootDirectory;

                if (!ConfigurationService.ValidateMountRootDirectory(mountRoot))
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new DirectoryNotFoundException($"Cannot access or create mount root directory: {mountRoot}"),
                        "MountRootDirectoryInvalid",
                        ErrorCategory.InvalidArgument,
                        mountRoot));
                    return;
                }

                // Validate structure
                var recipeService = new RecipeService(ModuleCallbacks.FromCmdlet(this));

                if (!SkipValidation.IsPresent)
                {
                    var problems = recipeService.ValidateRecipe(recipe, resolvedImagePath);
                    if (problems.Count > 0)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException(
                                "Recipe validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, problems)),
                            "RecipeValidationFailed",
                            ErrorCategory.InvalidData,
                            recipe));
                        return;
                    }
                }

                // Select images
                using var imageService = WindowsImageService.ForCmdlet(this);
                var images = imageService.GetImageInfo(resolvedImagePath);
                var selected = recipeService.SelectImages(recipe, images);

                if (selected.Count == 0)
                {
                    WriteWarning("No images match the recipe's image filter; nothing to do");
                    return;
                }

                if (selected.Count > MaxImages)
                {
                    WriteWarning($"Image filter selected {selected.Count} images; limiting to MaxImages = {MaxImages}");
                    selected = selected.Take(MaxImages).ToList();
                }

                LoggingService.WriteProgress(this, ActivityName,
                    $"Applying recipe to {selected.Count} images",
                    $"Recipe: {recipe.Metadata.Name}", 0);

                // Execute for each image
                var wimGuid = Guid.NewGuid().ToString("N");
                var results = new List<RecipeImageExecutionResult>();

                for (int i = 0; i < selected.Count; i++)
                {
                    var image = selected[i];
                    var percent = (int)((double)i / selected.Count * 100);

                    LoggingService.WriteProgress(this, ActivityName,
                        $"[{i + 1} of {selected.Count}] - {image.Name}",
                        $"Image index {image.Index} ({percent}%)", percent);

                    var imageMountPath = ConfigurationService.CreateUniqueMountDirectory(mountRoot, image.Index, wimGuid);

                    try
                    {
                        var result = recipeService.ExecuteForImage(recipe, image, imageMountPath, imageService, this);
                        results.Add(result);

                        WriteObject(result);

                        if (result.Success)
                        {
                            LoggingService.WriteVerbose(this, ComponentName, $"[{i + 1} of {selected.Count}] - {result}");
                        }
                        else
                        {
                            WriteWarning($"[{i + 1} of {selected.Count}] - {result}");
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteError(new ErrorRecord(ex, "RecipeImageFailed", ErrorCategory.OperationStopped, image.Name));
                    }
                }

                LoggingService.CompleteProgress(this, ActivityName);

                var duration = DateTime.UtcNow - startTime;
                var successCount = results.Count(r => r.Success);
                LoggingService.LogOperationComplete(this, ComponentName, duration,
                    $"Applied recipe to {successCount} of {selected.Count} images");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                LoggingService.LogOperationFailure(this, ComponentName, ex);
                ThrowTerminatingError(new ErrorRecord(ex, "RecipeExecutionFailed", ErrorCategory.OperationStopped, ImagePath));
            }
        }
    }
}
