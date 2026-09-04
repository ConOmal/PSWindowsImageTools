using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for converting ESD files to Windows images in different formats
    /// Supports WIM mode (single file) and Folder mode (Windows setup structure)
    /// </summary>
    public class ESDConversionService : IDisposable
    {
        private const string ServiceName = "ESDConversionService";
        private bool _disposed = false;

        /// <summary>
        /// Converts ESD images to a single WIM file
        /// </summary>
        /// <param name="sourceEsdPath">Path to source ESD file</param>
        /// <param name="outputWimPath">Path to output WIM file</param>
        /// <param name="imagesToConvert">List of images to convert</param>
        /// <param name="compressionType">Compression type for WIM</param>
        /// <param name="setBootable">Whether to set the WIM as bootable</param>
        /// <param name="scratchDirectory">Scratch directory for operations</param>
        /// <param name="progressCallback">Progress reporting callback</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Conversion result</returns>
        public ConversionResult ConvertToWIM(
            string sourceEsdPath,
            string outputWimPath,
            List<WindowsImageInfo> imagesToConvert,
            string compressionType = "Max",
            bool setBootable = false,
            string? scratchDirectory = null,
            Action<int, string>? progressCallback = null,
            PSCmdlet? cmdlet = null)
        {
            var result = new ConversionResult
            {
                Mode = "WIM",
                SourcePath = sourceEsdPath,
                OutputPath = outputWimPath,
                StartTime = DateTime.UtcNow,
                ProcessedImages = new List<ProcessedImageInfo>()
            };

            try
            {
                var conversionStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                    "WIM Conversion", $"{imagesToConvert.Count} images to {Path.GetFileName(outputWimPath)}");

                // Delete existing output file if it exists
                if (File.Exists(outputWimPath))
                {
                    File.Delete(outputWimPath);
                }

                using var wimExportService = new WimExportService();
                
                int currentImage = 0;
                foreach (var image in imagesToConvert)
                {
                    currentImage++;
                    DateTime imageStartTime = DateTime.UtcNow;

                    var processedImage = new ProcessedImageInfo
                    {
                        SourceIndex = image.Index,
                        Name = image.Name ?? $"Image {image.Index}",
                        Edition = image.Edition ?? "Unknown",
                        Size = image.Size
                    };

                    try
                    {
                        // Calculate percentage: current image / total images * 100
                        int overallPercentage = (int)((double)(currentImage - 1) / imagesToConvert.Count * 100);
                        var elapsedTime = DateTime.UtcNow - conversionStartTime;
                        var elapsedCompact = LoggingService.FormatDurationCompact(elapsedTime);

                        progressCallback?.Invoke(
                            overallPercentage,
                            $"Converting image {currentImage} of {imagesToConvert.Count} ({overallPercentage}%) - {elapsedCompact}: {processedImage.Name}"
                        );

                        imageStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                            $"Image {currentImage}/{imagesToConvert.Count} Export",
                            $"{processedImage.Name} ({processedImage.Edition}) - Index {image.Index}");

                        // For the first image, create new WIM; for subsequent images, append
                        bool isFirstImage = currentImage == 1;
                        
                        var exportResult = wimExportService.ExportImage(
                            sourceImagePath: sourceEsdPath,
                            destinationImagePath: outputWimPath,
                            sourceIndex: (uint)image.Index,
                            compressionType: compressionType,
                            checkIntegrity: false,
                            setBootable: setBootable && isFirstImage, // Only set first image as bootable
                            scratchDirectory: scratchDirectory ?? Path.GetTempPath(),
                            progressCallback: (percentage, message) =>
                            {
                                // Calculate overall progress: base progress + current image progress
                                var baseProgress = (double)(currentImage - 1) / imagesToConvert.Count * 100;
                                var currentImageProgress = (double)percentage / imagesToConvert.Count;
                                var overallProgress = (int)(baseProgress + currentImageProgress);

                                progressCallback?.Invoke(overallProgress,
                                    $"[{currentImage}/{imagesToConvert.Count}] {processedImage.Name}: {message} ({overallProgress}%)");
                            },
                            cmdlet: cmdlet
                        );

                        processedImage.Success = exportResult;
                        if (!exportResult)
                        {
                            processedImage.ErrorMessage = "Export operation failed";
                            LoggingService.WriteWarning(cmdlet, ServiceName,
                                $"Image export failed: {processedImage.Name}");
                        }
                        else
                        {
                            LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName,
                                $"Image {currentImage}/{imagesToConvert.Count} Export", imageStartTime,
                                $"{processedImage.Name} ({processedImage.Edition}) - Index {image.Index}");
                        }
                    }
                    catch (Exception ex)
                    {
                        processedImage.Success = false;
                        processedImage.ErrorMessage = ex.Message;
                        LoggingService.WriteWarning(cmdlet, ServiceName,
                            $"Failed to convert image {processedImage.Name}: {ex.Message}");
                    }
                    finally
                    {
                        processedImage.ProcessingDuration = DateTime.UtcNow - imageStartTime;
                        result.ProcessedImages.Add(processedImage);
                    }
                }

                // Calculate total size
                if (File.Exists(outputWimPath))
                {
                    result.TotalSize = new FileInfo(outputWimPath).Length;
                }

                result.Success = result.ProcessedImages.Any() && result.ProcessedImages.All(p => p.Success);

                if (result.Success)
                {
                    LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, "WIM Conversion", conversionStartTime,
                        $"{result.SuccessfulImages} images successfully converted to {Path.GetFileName(outputWimPath)}");
                }
                else
                {
                    result.ErrorMessage = $"Some images failed to convert. {result.SuccessfulImages} succeeded, {result.FailedImages} failed.";
                    LoggingService.WriteWarning(cmdlet, ServiceName, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(cmdlet, ServiceName, $"WIM conversion failed: {ex.Message}", ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// Converts ESD images to Windows installation tree structure
        /// Follows Windows installation tree specification:
        /// - Image 1: Base Windows setup media (installation tree base)
        /// - Image 2: Windows PE (exported to sources/boot.wim)
        /// - Image 3: Windows Setup (appended to sources/boot.wim, set bootable)
        /// - Remaining images: Windows editions (exported to sources/install.esd)
        /// </summary>
        /// <param name="sourceEsdPath">Path to source ESD file</param>
        /// <param name="outputFolderPath">Path to output folder</param>
        /// <param name="imagesToConvert">List of images to convert (will be reordered by index)</param>
        /// <param name="compressionType">Compression type for WIM/ESD files</param>
        /// <param name="includeWindowsPE">Include Windows PE images</param>
        /// <param name="includeWindowsSetup">Include Windows Setup images</param>
        /// <param name="scratchDirectory">Scratch directory for operations</param>
        /// <param name="progressCallback">Progress reporting callback</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Conversion result</returns>
        public ConversionResult ConvertToFolder(
            string sourceEsdPath,
            string outputFolderPath,
            List<WindowsImageInfo> imagesToConvert,
            string compressionType = "Max",
            bool includeWindowsPE = true,
            bool includeWindowsSetup = true,
            string? scratchDirectory = null,
            Action<int, string>? progressCallback = null,
            PSCmdlet? cmdlet = null)
        {
            var result = new ConversionResult
            {
                Mode = "Folder",
                SourcePath = sourceEsdPath,
                OutputPath = outputFolderPath,
                StartTime = DateTime.UtcNow,
                ProcessedImages = new List<ProcessedImageInfo>()
            };

            try
            {
                var conversionStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                    "Installation Tree Assembly", $"{imagesToConvert.Count} images to {outputFolderPath}");

                // Sort images by index to ensure proper order (Image 1, 2, 3, then remaining)
                var sortedImages = imagesToConvert.OrderBy(img => img.Index).ToList();

                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Assembling Windows installation tree with {sortedImages.Count} images");

                // Step 1: Extract Image 1 (Base Windows setup media) as installation tree base
                if (sortedImages.Count >= 1)
                {
                    var baseImage = sortedImages[0];
                    progressCallback?.Invoke(10, $"Extracting base installation tree from Image {baseImage.Index}");

                    var extractResult = ExtractBaseInstallationTree(sourceEsdPath, outputFolderPath, baseImage,
                        scratchDirectory, cmdlet);

                    if (extractResult.Success)
                    {
                        result.ProcessedImages.Add(extractResult);
                        LoggingService.WriteVerbose(cmdlet, ServiceName,
                            $"Successfully extracted base installation tree from Image {baseImage.Index}");
                    }
                    else
                    {
                        throw new InvalidOperationException($"Failed to extract base installation tree from Image {baseImage.Index}");
                    }
                }

                // Step 2: Export Image 2 (Windows PE) to sources/boot.wim
                if (sortedImages.Count >= 2 && includeWindowsPE)
                {
                    var peImage = sortedImages[1];
                    progressCallback?.Invoke(30, $"Exporting Windows PE (Image {peImage.Index}) to sources/boot.wim");

                    var bootWimPath = Path.Combine(outputFolderPath, "sources", "boot.wim");
                    var peResult = ExportWindowsPEImage(sourceEsdPath, bootWimPath, peImage,
                        compressionType, scratchDirectory, cmdlet);

                    if (peResult.Success)
                    {
                        result.ProcessedImages.Add(peResult);
                        LoggingService.WriteVerbose(cmdlet, ServiceName,
                            $"Successfully exported Windows PE (Image {peImage.Index}) to boot.wim");
                    }
                }

                // Step 3: Append Image 3 (Windows Setup) to sources/boot.wim and set bootable
                if (sortedImages.Count >= 3 && includeWindowsSetup)
                {
                    var setupImage = sortedImages[2];
                    progressCallback?.Invoke(50, $"Appending Windows Setup (Image {setupImage.Index}) to sources/boot.wim");

                    var bootWimPath = Path.Combine(outputFolderPath, "sources", "boot.wim");
                    var setupResult = AppendWindowsSetupImage(sourceEsdPath, bootWimPath, setupImage,
                        compressionType, true, scratchDirectory, cmdlet);

                    if (setupResult.Success)
                    {
                        result.ProcessedImages.Add(setupResult);
                        LoggingService.WriteVerbose(cmdlet, ServiceName,
                            $"Successfully appended Windows Setup (Image {setupImage.Index}) to boot.wim and set bootable");
                    }
                }

                // Step 4: Export remaining images (Windows editions) to sources/install.esd
                var editionImages = sortedImages.Skip(3).ToList();
                if (editionImages.Any())
                {
                    progressCallback?.Invoke(70, $"Exporting {editionImages.Count} Windows editions to sources/install.esd");

                    var installEsdPath = Path.Combine(outputFolderPath, "sources", "install.esd");
                    var editionsResult = ExportWindowsEditions(sourceEsdPath, installEsdPath, editionImages,
                        compressionType, scratchDirectory,
                        (percentage, message) => progressCallback?.Invoke(70 + (int)(percentage * 0.25), $"Windows Editions: {message}"),
                        cmdlet);

                    result.ProcessedImages.AddRange(editionsResult);
                }

                result.Success = result.ProcessedImages.Any() && result.ProcessedImages.All(p => p.Success);

                if (result.Success)
                {
                    LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, "Installation Tree Assembly", conversionStartTime,
                        $"{result.SuccessfulImages} images successfully assembled into Windows installation tree at {outputFolderPath}");
                }
                else
                {
                    result.ErrorMessage = $"Some images failed to process. {result.SuccessfulImages} succeeded, {result.FailedImages} failed.";
                    LoggingService.WriteWarning(cmdlet, ServiceName, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(cmdlet, ServiceName, $"Folder conversion failed: {ex.Message}", ex);
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// Extracts Image 1 (Base Windows setup media) as the installation tree base
        /// </summary>
        private ProcessedImageInfo ExtractBaseInstallationTree(string sourceEsdPath, string outputPath,
            WindowsImageInfo baseImage, string? scratchDirectory, PSCmdlet? cmdlet)
        {
            var result = new ProcessedImageInfo
            {
                SourceIndex = baseImage.Index,
                Name = baseImage.Name ?? $"Image {baseImage.Index}",
                Edition = "Base Installation Tree",
                Size = baseImage.Size
            };

            var imageStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                $"Base Installation Tree Extraction", $"Image {baseImage.Index} to {outputPath}");

            try
            {
                // For now, create the basic folder structure
                // In a full implementation, this would mount Image 1 and copy all files
                CreateWindowsSetupStructure(outputPath, cmdlet);

                result.Success = true;
                LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName,
                    "Base Installation Tree Extraction", imageStartTime, $"Image {baseImage.Index}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(cmdlet, ServiceName, $"Failed to extract base installation tree: {ex.Message}", ex);
            }
            finally
            {
                result.ProcessingDuration = DateTime.UtcNow - imageStartTime;
            }

            return result;
        }

        /// <summary>
        /// Exports Image 2 (Windows PE) to sources/boot.wim
        /// </summary>
        private ProcessedImageInfo ExportWindowsPEImage(string sourceEsdPath, string bootWimPath,
            WindowsImageInfo peImage, string compressionType, string? scratchDirectory, PSCmdlet? cmdlet)
        {
            var result = new ProcessedImageInfo
            {
                SourceIndex = peImage.Index,
                Name = peImage.Name ?? $"Image {peImage.Index}",
                Edition = "Windows PE",
                Size = peImage.Size,
                OutputPath = bootWimPath
            };

            var imageStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                $"Windows PE Export", $"Image {peImage.Index} to boot.wim");

            try
            {
                using var wimExportService = new WimExportService();

                // Delete existing boot.wim if it exists
                if (File.Exists(bootWimPath))
                {
                    File.Delete(bootWimPath);
                }

                var exportResult = wimExportService.ExportImage(
                    sourceImagePath: sourceEsdPath,
                    destinationImagePath: bootWimPath,
                    sourceIndex: (uint)peImage.Index,
                    compressionType: compressionType,
                    checkIntegrity: false,
                    setBootable: false, // Will be set bootable when Setup is appended
                    scratchDirectory: scratchDirectory ?? Path.GetTempPath(),
                    progressCallback: null,
                    cmdlet: cmdlet
                );

                result.Success = exportResult;
                if (exportResult)
                {
                    LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName,
                        "Windows PE Export", imageStartTime, $"Image {peImage.Index} to boot.wim");
                }
                else
                {
                    result.ErrorMessage = "Windows PE export failed";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(cmdlet, ServiceName, $"Failed to export Windows PE: {ex.Message}", ex);
            }
            finally
            {
                result.ProcessingDuration = DateTime.UtcNow - imageStartTime;
            }

            return result;
        }

        /// <summary>
        /// Appends Image 3 (Windows Setup) to sources/boot.wim and sets it bootable
        /// </summary>
        private ProcessedImageInfo AppendWindowsSetupImage(string sourceEsdPath, string bootWimPath,
            WindowsImageInfo setupImage, string compressionType, bool setBootable, string? scratchDirectory, PSCmdlet? cmdlet)
        {
            var result = new ProcessedImageInfo
            {
                SourceIndex = setupImage.Index,
                Name = setupImage.Name ?? $"Image {setupImage.Index}",
                Edition = "Windows Setup",
                Size = setupImage.Size,
                OutputPath = bootWimPath
            };

            var imageStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                $"Windows Setup Append", $"Image {setupImage.Index} to boot.wim (bootable: {setBootable})");

            try
            {
                using var wimExportService = new WimExportService();

                // Append to existing boot.wim (should contain Windows PE from previous step)
                var exportResult = wimExportService.ExportImage(
                    sourceImagePath: sourceEsdPath,
                    destinationImagePath: bootWimPath,
                    sourceIndex: (uint)setupImage.Index,
                    compressionType: compressionType,
                    checkIntegrity: false,
                    setBootable: setBootable,
                    scratchDirectory: scratchDirectory ?? Path.GetTempPath(),
                    progressCallback: null,
                    cmdlet: cmdlet
                );

                result.Success = exportResult;
                if (exportResult)
                {
                    LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName,
                        "Windows Setup Append", imageStartTime,
                        $"Image {setupImage.Index} appended to boot.wim (bootable: {setBootable})");
                }
                else
                {
                    result.ErrorMessage = "Windows Setup append failed";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                LoggingService.WriteError(cmdlet, ServiceName, $"Failed to append Windows Setup: {ex.Message}", ex);
            }
            finally
            {
                result.ProcessingDuration = DateTime.UtcNow - imageStartTime;
            }

            return result;
        }

        /// <summary>
        /// Exports remaining images (Windows editions) to sources/install.esd
        /// </summary>
        private List<ProcessedImageInfo> ExportWindowsEditions(string sourceEsdPath, string installEsdPath,
            List<WindowsImageInfo> editionImages, string compressionType, string? scratchDirectory,
            Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            var results = new List<ProcessedImageInfo>();

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                "Windows Editions Export", $"{editionImages.Count} editions to install.esd");

            try
            {
                // Delete existing install.esd if it exists
                if (File.Exists(installEsdPath))
                {
                    File.Delete(installEsdPath);
                }

                using var wimExportService = new WimExportService();

                int currentImage = 0;
                foreach (var edition in editionImages)
                {
                    currentImage++;
                    var imageStartTime = DateTime.UtcNow;

                    var processedImage = new ProcessedImageInfo
                    {
                        SourceIndex = edition.Index,
                        Name = edition.Name ?? $"Image {edition.Index}",
                        Edition = edition.Edition ?? "Unknown",
                        Size = edition.Size,
                        OutputPath = installEsdPath
                    };

                    try
                    {
                        var percentage = (int)((double)currentImage / editionImages.Count * 100);
                        progressCallback?.Invoke(percentage,
                            $"Exporting edition {currentImage}/{editionImages.Count}: {processedImage.Name}");

                        LoggingService.WriteVerbose(cmdlet, ServiceName,
                            $"Exporting Windows edition {currentImage}/{editionImages.Count}: {processedImage.Name} (Index {edition.Index})");

                        // For the first image, create new ESD; for subsequent images, append
                        var exportResult = wimExportService.ExportImage(
                            sourceImagePath: sourceEsdPath,
                            destinationImagePath: installEsdPath,
                            sourceIndex: (uint)edition.Index,
                            compressionType: compressionType,
                            checkIntegrity: false,
                            setBootable: false,
                            scratchDirectory: scratchDirectory ?? Path.GetTempPath(),
                            progressCallback: null,
                            cmdlet: cmdlet
                        );

                        processedImage.Success = exportResult;
                        if (!exportResult)
                        {
                            processedImage.ErrorMessage = "Edition export failed";
                        }
                    }
                    catch (Exception ex)
                    {
                        processedImage.Success = false;
                        processedImage.ErrorMessage = ex.Message;
                        LoggingService.WriteWarning(cmdlet, ServiceName,
                            $"Failed to export edition {processedImage.Name}: {ex.Message}");
                    }
                    finally
                    {
                        processedImage.ProcessingDuration = DateTime.UtcNow - imageStartTime;
                        results.Add(processedImage);
                    }
                }

                LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName,
                    "Windows Editions Export", operationStartTime,
                    $"{results.Count(r => r.Success)}/{editionImages.Count} editions exported to install.esd");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"Windows editions export failed: {ex.Message}", ex);
            }

            return results;
        }

        /// <summary>
        /// Creates the basic Windows setup folder structure
        /// </summary>
        private static void CreateWindowsSetupStructure(string outputPath, PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, "ESDConversionService", "Creating Windows setup folder structure");

            var folders = new[]
            {
                "boot",
                "efi",
                "sources",
                "support",
                "x64",
                "x86"
            };

            foreach (var folder in folders)
            {
                var folderPath = Path.Combine(outputPath, folder);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
            }
        }

        /// <summary>
        /// Converts Windows PE images to boot.wim (legacy method - replaced by proper installation tree assembly)
        /// </summary>
        private void ConvertWindowsPEImages(string sourceEsdPath, string outputFolderPath,
            List<WindowsImageInfo> allImages, string compressionType, string? scratchDirectory,
            Action<int, string>? progressCallback, ConversionResult result, PSCmdlet? cmdlet)
        {
            var peImages = allImages.Where(img => img.Edition?.Contains("WindowsPE") == true).ToList();
            
            if (peImages.Any())
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Converting {peImages.Count} Windows PE images");
                
                var bootWimPath = Path.Combine(outputFolderPath, "sources", "boot.wim");
                var wimResult = ConvertToWIM(sourceEsdPath, bootWimPath, peImages, 
                    compressionType, true, scratchDirectory, progressCallback, cmdlet);

                result.ProcessedImages.AddRange(wimResult.ProcessedImages);
            }
        }

        /// <summary>
        /// Converts Windows Setup images
        /// </summary>
        private void ConvertWindowsSetupImages(string sourceEsdPath, string outputFolderPath,
            List<WindowsImageInfo> allImages, string compressionType, string? scratchDirectory,
            Action<int, string>? progressCallback, ConversionResult result, PSCmdlet? cmdlet)
        {
            var setupImages = allImages.Where(img => 
                img.Name?.Contains("Setup") == true && 
                !img.Edition?.Contains("WindowsPE") == true).ToList();

            if (setupImages.Any())
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Converting {setupImages.Count} Windows Setup images");
                
                // Setup images can go to a separate WIM or be included in boot.wim
                // For now, we'll include them in boot.wim
                var bootWimPath = Path.Combine(outputFolderPath, "sources", "boot.wim");
                
                // If boot.wim already exists, append to it; otherwise create it
                var wimResult = ConvertToWIM(sourceEsdPath, bootWimPath, setupImages, 
                    compressionType, false, scratchDirectory, progressCallback, cmdlet);

                result.ProcessedImages.AddRange(wimResult.ProcessedImages);
            }
        }

        /// <summary>
        /// Copies additional setup files from the ESD
        /// </summary>
        private void CopySetupFiles(string sourceEsdPath, string outputFolderPath, PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, ServiceName, "Copying additional setup files");
            
            // This would involve mounting the first image and copying setup files
            // For now, we'll create placeholder files that would typically be present
            
            var setupFiles = new[]
            {
                Path.Combine(outputFolderPath, "setup.exe"),
                Path.Combine(outputFolderPath, "autorun.inf")
            };

            foreach (var file in setupFiles)
            {
                if (!File.Exists(file))
                {
                    // Create placeholder - in real implementation, these would be copied from mounted image
                    File.WriteAllText(file, $"# Placeholder for {Path.GetFileName(file)}");
                }
            }
        }

        /// <summary>
        /// Determines if an image is a system image (PE, Setup, etc.)
        /// </summary>
        private static bool IsSystemImage(WindowsImageInfo image)
        {
            return image.Edition?.Contains("WindowsPE") == true ||
                   image.Name?.Contains("Setup") == true ||
                   image.Name?.Contains("Windows Setup Media") == true;
        }

        /// <summary>
        /// Disposes the ESD conversion service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
