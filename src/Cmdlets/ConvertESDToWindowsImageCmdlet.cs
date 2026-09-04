using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Converts ESD files to Windows images with two modes:
    /// - WIM mode: Export selected images to a single WIM file
    /// - Folder mode: Create proper Windows setup structure with setup files
    /// </summary>
    [Cmdlet(VerbsData.Convert, "ESDToWindowsImage")]
    [OutputType(typeof(ConversionResult))]
    public class ConvertESDToWindowsImageCmdlet : PSCmdlet
    {
        /// <summary>
        /// Path to the source ESD file
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        [ValidateNotNullOrEmpty]
        public FileInfo SourcePath { get; set; } = null!;

        /// <summary>
        /// Output path - for WIM mode: path to output WIM file, for Folder mode: path to output directory
        /// </summary>
        [Parameter(Mandatory = true, Position = 1)]
        [ValidateNotNullOrEmpty]
        public string OutputPath { get; set; } = null!;

        /// <summary>
        /// Conversion mode: WIM (single WIM file) or Folder (Windows setup structure)
        /// </summary>
        [Parameter(Mandatory = true, Position = 2)]
        [ValidateSet("WIM", "Folder")]
        public string Mode { get; set; } = null!;

        /// <summary>
        /// Inclusion filter scriptblock to select which images to convert (e.g., {$_.Edition -like "*Pro*"})
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        public ScriptBlock? InclusionFilter { get; set; }

        /// <summary>
        /// Exclusion filter scriptblock to exclude images from conversion (e.g., {$_.Edition -like "*Home*"})
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        public ScriptBlock? ExclusionFilter { get; set; }

        /// <summary>
        /// Compression type for WIM output (None, Fast, Max, Recovery)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("None", "Fast", "Max", "Recovery")]
        public string CompressionType { get; set; } = "Max";

        /// <summary>
        /// Force overwrite of existing output files/directories
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Create bootable image (for WIM mode)
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Bootable { get; set; }

        /// <summary>
        /// Include Windows PE images in conversion
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter IncludeWindowsPE { get; set; }

        /// <summary>
        /// Include Windows Setup images in conversion
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter IncludeWindowsSetup { get; set; }

        /// <summary>
        /// Scratch directory for temporary operations
        /// </summary>
        [Parameter(Mandatory = false)]
        public DirectoryInfo? ScratchDirectory { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                LoggingService.WriteVerbose(this, $"Starting ESD to Windows Image conversion");
                LoggingService.WriteVerbose(this, $"Source: {SourcePath.FullName}");
                LoggingService.WriteVerbose(this, $"Output: {OutputPath}");
                LoggingService.WriteVerbose(this, $"Mode: {Mode}");

                // Validate source file
                if (!SourcePath.Exists)
                {
                    throw new FileNotFoundException($"Source ESD file not found: {SourcePath.FullName}");
                }

                // Validate output path based on mode
                ValidateOutputPath();

                // Get images from source ESD with filtering
                var sourceImages = GetFilteredImages();
                if (!sourceImages.Any())
                {
                    throw new InvalidOperationException("No images match the specified filters");
                }

                LoggingService.WriteVerbose(this, $"Found {sourceImages.Count} images to convert after filtering");

                // Perform conversion based on mode
                ConversionResult result;
                if (Mode.Equals("WIM", StringComparison.OrdinalIgnoreCase))
                {
                    result = ConvertToWIM(sourceImages);
                }
                else
                {
                    result = ConvertToFolder(sourceImages);
                }

                WriteObject(result);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, $"ESD conversion failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Validates the output path based on the conversion mode
        /// </summary>
        private void ValidateOutputPath()
        {
            if (Mode.Equals("WIM", StringComparison.OrdinalIgnoreCase))
            {
                // For WIM mode, output should be a file path
                var outputDir = Path.GetDirectoryName(OutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                if (File.Exists(OutputPath) && !Force.IsPresent)
                {
                    throw new InvalidOperationException($"Output WIM file already exists: {OutputPath}. Use -Force to overwrite.");
                }
            }
            else
            {
                // For Folder mode, output should be a directory path
                if (Directory.Exists(OutputPath))
                {
                    if (!Force.IsPresent)
                    {
                        throw new InvalidOperationException($"Output directory already exists: {OutputPath}. Use -Force to overwrite.");
                    }
                    
                    // Clear existing directory if Force is specified
                    Directory.Delete(OutputPath, true);
                }

                Directory.CreateDirectory(OutputPath);
            }
        }

        /// <summary>
        /// Gets filtered images from the source ESD file
        /// </summary>
        private List<WindowsImageInfo> GetFilteredImages()
        {
            LoggingService.WriteVerbose(this, "Getting image list from source ESD file");

            using var imageService = WindowsImageService.ForCmdlet(this);
            var allImages = imageService.GetImageInfo(SourcePath.FullName);

            var filteredImages = allImages.AsEnumerable();

            // Apply inclusion filter
            if (InclusionFilter != null)
            {
                filteredImages = filteredImages.Where(image =>
                {
                    var result = InclusionFilter.InvokeWithContext(null, new List<PSVariable> { new PSVariable("_", image) });
                    return result.Any() && result.First().BaseObject is bool includeResult && includeResult;
                });
            }

            // Apply exclusion filter
            if (ExclusionFilter != null)
            {
                filteredImages = filteredImages.Where(image =>
                {
                    var result = ExclusionFilter.InvokeWithContext(null, new List<PSVariable> { new PSVariable("_", image) });
                    return !result.Any() || !(result.First().BaseObject is bool excludeResult && excludeResult);
                });
            }

            var resultList = filteredImages.ToList();

            // Add Windows PE and Setup images if requested
            if (IncludeWindowsPE.IsPresent || IncludeWindowsSetup.IsPresent)
            {
                var systemImages = allImages.Where(img => 
                    (IncludeWindowsPE.IsPresent && img.Edition?.Contains("WindowsPE") == true) ||
                    (IncludeWindowsSetup.IsPresent && img.Name?.Contains("Setup") == true)
                ).ToList();

                // Add system images that aren't already included
                foreach (var sysImg in systemImages)
                {
                    if (!resultList.Any(r => r.Index == sysImg.Index))
                    {
                        resultList.Add(sysImg);
                    }
                }
            }

            return resultList.OrderBy(img => img.Index).ToList();
        }

        /// <summary>
        /// Converts images to a single WIM file
        /// </summary>
        private ConversionResult ConvertToWIM(List<WindowsImageInfo> images)
        {
            LoggingService.WriteVerbose(this, $"Converting {images.Count} images to WIM file: {OutputPath}");

            var result = new ConversionResult
            {
                Mode = "WIM",
                OutputPath = OutputPath,
                SourcePath = SourcePath.FullName,
                StartTime = DateTime.UtcNow,
                ProcessedImages = new List<ProcessedImageInfo>()
            };

            try
            {
                using var conversionService = new ESDConversionService();
                
                var conversionResult = conversionService.ConvertToWIM(
                    sourceEsdPath: SourcePath.FullName,
                    outputWimPath: OutputPath,
                    imagesToConvert: images,
                    compressionType: CompressionType,
                    setBootable: Bootable.IsPresent,
                    scratchDirectory: ScratchDirectory?.FullName,
                    progressCallback: (percentage, message) =>
                    {
                        LoggingService.WriteProgress(this, "Converting ESD to WIM", message, percentage);
                    },
                    cmdlet: this
                );

                result.Success = conversionResult.Success;
                result.ProcessedImages = conversionResult.ProcessedImages;
                result.ErrorMessage = conversionResult.ErrorMessage;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(this, $"WIM conversion failed: {ex.Message}", ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// Converts images to Windows setup folder structure
        /// </summary>
        private ConversionResult ConvertToFolder(List<WindowsImageInfo> images)
        {
            LoggingService.WriteVerbose(this, $"Converting {images.Count} images to Windows setup structure: {OutputPath}");

            var result = new ConversionResult
            {
                Mode = "Folder",
                OutputPath = OutputPath,
                SourcePath = SourcePath.FullName,
                StartTime = DateTime.UtcNow,
                ProcessedImages = new List<ProcessedImageInfo>()
            };

            try
            {
                using var conversionService = new ESDConversionService();
                
                var conversionResult = conversionService.ConvertToFolder(
                    sourceEsdPath: SourcePath.FullName,
                    outputFolderPath: OutputPath,
                    imagesToConvert: images,
                    compressionType: CompressionType,
                    includeWindowsPE: IncludeWindowsPE.IsPresent,
                    includeWindowsSetup: IncludeWindowsSetup.IsPresent,
                    scratchDirectory: ScratchDirectory?.FullName,
                    progressCallback: (percentage, message) =>
                    {
                        LoggingService.WriteProgress(this, "Converting ESD to Folder", message, percentage);
                    },
                    cmdlet: this
                );

                result.Success = conversionResult.Success;
                result.ProcessedImages = conversionResult.ProcessedImages;
                result.ErrorMessage = conversionResult.ErrorMessage;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(this, $"Folder conversion failed: {ex.Message}", ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }
    }
}
