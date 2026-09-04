using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Result of exporting a single image to a WIM file
    /// </summary>
    public class WindowsImageExportResult
    {
        /// <summary>
        /// Path to the source WIM/ESD file
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Path to the destination WIM file
        /// </summary>
        public string DestinationPath { get; set; } = string.Empty;

        /// <summary>
        /// Index of the exported image
        /// </summary>
        public int SourceIndex { get; set; }

        /// <summary>
        /// Whether the export succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message when the export failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// How long the export took
        /// </summary>
        public TimeSpan Duration { get; set; }

        public override string ToString()
        {
            var status = Success ? "SUCCESS" : $"FAILED: {ErrorMessage}";
            return $"Export image {SourceIndex} -> {DestinationPath}: {status} ({Duration.TotalSeconds:F1}s)";
        }
    }

    /// <summary>
    /// Result of validating a Windows image recipe
    /// </summary>
    public class RecipeValidationResult
    {
        /// <summary>
        /// Path of the validated recipe file (when validated by path)
        /// </summary>
        public string? RecipePath { get; set; }

        /// <summary>
        /// Name of the validated recipe
        /// </summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the recipe is structurally valid
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Validation problems (empty when valid)
        /// </summary>
        public List<string> Problems { get; set; } = new List<string>();

        /// <summary>
        /// Images available in the referenced image file (when validated with ImagePath)
        /// </summary>
        public int ImageCountAvailable { get; set; }

        /// <summary>
        /// Images selected by the recipe filter (when validated with ImagePath)
        /// </summary>
        public int ImageCountSelected { get; set; }

        public override string ToString()
        {
            var status = IsValid ? "VALID" : $"INVALID ({Problems.Count} problems)";
            var counts = ImageCountAvailable > 0 ? $", selects {ImageCountSelected}/{ImageCountAvailable} images" : string.Empty;
            return $"Recipe '{RecipeName}': {status}{counts}";
        }
    }

    /// <summary>
    /// Result of applying a recipe to a single Windows image
    /// </summary>
    public class RecipeImageExecutionResult
    {
        /// <summary>
        /// Name of the recipe that was applied
        /// </summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>
        /// Name of the image the recipe was applied to
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image the recipe was applied to
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Path to the source WIM/ESD file
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Mount directory used during execution
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// Whether the whole recipe execution succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Top-level error message when execution failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Results for each recipe section, in application order
        /// </summary>
        public List<RecipeSectionResult> Sections { get; set; } = new List<RecipeSectionResult>();

        /// <summary>
        /// How long execution took
        /// </summary>
        public TimeSpan Duration { get; set; }

        public override string ToString()
        {
            var status = Success ? "SUCCESS" : $"FAILED: {ErrorMessage}";
            return $"Recipe '{RecipeName}' on [{ImageIndex}] {ImageName}: {status} ({Duration.TotalSeconds:F1}s)";
        }
    }

    /// <summary>
    /// Result of applying one recipe section
    /// </summary>
    public class RecipeSectionResult
    {
        /// <summary>
        /// Name of the section (e.g., "removeAppxPackages")
        /// </summary>
        public string SectionName { get; set; } = string.Empty;

        /// <summary>
        /// Whether the section was enabled in the recipe
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Items processed by the section
        /// </summary>
        public int ItemsProcessed { get; set; }

        /// <summary>
        /// Items that succeeded
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Items that failed
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Error messages for failed items
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        public override string ToString()
        {
            var status = FailureCount == 0 ? "OK" : $"{FailureCount} failed";
            return $"{SectionName}: {SuccessCount}/{ItemsProcessed} ({status})";
        }
    }
}
