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
    /// Cmdlet for performing Media Dynamic Update on Windows installation media
    /// Processes WIM files in the correct order: boot images first, then Windows images by index
    /// Returns mounted images for additional customization unless AutoDismount is specified
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "MediaDynamicUpdate")]
    [OutputType(typeof(MediaDynamicUpdateResult))]
    [OutputType(typeof(MountedWindowsImage[]))]
    public class InvokeMediaDynamicUpdateCmdlet : PSCmdlet
    {
        /// <summary>
        /// Root directory containing Windows installation media
        /// </summary>
        [Parameter(
            Position = 0,
            Mandatory = true,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Root directory containing Windows installation media")]
        [ValidateNotNull]
        public DirectoryInfo MediaPath { get; set; } = null!;

        /// <summary>
        /// Directory containing Dynamic Update packages (Setup DU, Safe OS DU, SSU, LCU)
        /// </summary>
        [Parameter(
            Position = 1,
            Mandatory = true,
            HelpMessage = "Directory containing Dynamic Update packages")]
        [ValidateNotNull]
        public DirectoryInfo UpdatesPath { get; set; } = null!;

        /// <summary>
        /// Base path for mounting images during processing
        /// </summary>
        [Parameter(
            Position = 2,
            Mandatory = false,
            HelpMessage = "Base path for mounting images during processing")]
        public DirectoryInfo? MountBasePath { get; set; }

        /// <summary>
        /// Skip boot image processing (process only Windows images)
        /// </summary>
        [Parameter(HelpMessage = "Skip boot image processing")]
        public SwitchParameter SkipBootImages { get; set; }

        /// <summary>
        /// Skip Windows image processing (process only boot images)
        /// </summary>
        [Parameter(HelpMessage = "Skip Windows image processing")]
        public SwitchParameter SkipWindowsImages { get; set; }

        /// <summary>
        /// Continue processing even if individual operations fail
        /// </summary>
        [Parameter(HelpMessage = "Continue processing even if individual operations fail")]
        public SwitchParameter ContinueOnError { get; set; }

        /// <summary>
        /// Perform image cleanup after applying updates
        /// </summary>
        [Parameter(HelpMessage = "Perform image cleanup after applying updates")]
        public SwitchParameter PerformCleanup { get; set; }

        /// <summary>
        /// Validate images after processing
        /// </summary>
        [Parameter(HelpMessage = "Validate images after processing")]
        public SwitchParameter ValidateImages { get; set; }

        /// <summary>
        /// Automatically dismount images after processing (default: keep mounted for additional changes)
        /// </summary>
        [Parameter(HelpMessage = "Automatically dismount images after processing")]
        public SwitchParameter AutoDismount { get; set; }

        /// <summary>
        /// Return only the result object, not the mounted images
        /// </summary>
        [Parameter(HelpMessage = "Return only the result object, not the mounted images")]
        public SwitchParameter ResultOnly { get; set; }

        private const string ServiceName = "MediaDynamicUpdate";
        private DirectoryInfo _mountBasePath = null!;
        private List<FileInfo> _allWimFiles = new List<FileInfo>();
        private List<FileInfo> _updateFiles = new List<FileInfo>();
        private List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            // Set default mount base path if not provided
            _mountBasePath = MountBasePath ?? new DirectoryInfo(Path.Combine(Path.GetTempPath(), "MediaDynamicUpdate", Guid.NewGuid().ToString()));

            // Validate parameters
            if (!MediaPath.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException($"Media path not found: {MediaPath.FullName}"),
                    "MediaPathNotFound",
                    ErrorCategory.ObjectNotFound,
                    MediaPath));
            }

            if (!UpdatesPath.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException($"Updates path not found: {UpdatesPath.FullName}"),
                    "UpdatesPathNotFound",
                    ErrorCategory.ObjectNotFound,
                    UpdatesPath));
            }

            // Create mount base directory if it doesn't exist
            if (!_mountBasePath.Exists)
            {
                _mountBasePath.Create();
                LoggingService.WriteVerbose(this, ServiceName, $"Created mount base directory: {_mountBasePath.FullName}");
            }

            LoggingService.WriteVerbose(this, ServiceName, $"Media Dynamic Update starting");
            LoggingService.WriteVerbose(this, ServiceName, $"Media Path: {MediaPath.FullName}");
            LoggingService.WriteVerbose(this, ServiceName, $"Updates Path: {UpdatesPath.FullName}");
            LoggingService.WriteVerbose(this, ServiceName, $"Mount Base Path: {_mountBasePath.FullName}");
        }

        protected override void ProcessRecord()
        {
            var startTime = DateTime.UtcNow;
            var result = new MediaDynamicUpdateResult
            {
                MediaPath = MediaPath.FullName,
                UpdatesPath = UpdatesPath.FullName,
                StartTime = startTime
            };

            try
            {
                // Step 1: Discover WIM files and update packages
                DiscoverFiles(result);

                // Step 2: Process boot images first (per Media Dynamic Update sequence)
                if (!SkipBootImages.IsPresent)
                {
                    ProcessBootImages(result);
                }

                // Step 3: Process Windows images (install.wim) by index
                if (!SkipWindowsImages.IsPresent)
                {
                    ProcessWindowsImages(result);
                }

                // Step 4: Apply Setup Dynamic Updates to media
                ApplySetupDynamicUpdates(result);

                result.Success = true;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                LoggingService.WriteVerbose(this, ServiceName,
                    $"Media Dynamic Update completed successfully in {LoggingService.FormatDuration(result.Duration)}");

                // Output mounted images for additional customization unless AutoDismount is specified
                if (!AutoDismount.IsPresent && !ResultOnly.IsPresent)
                {
                    foreach (var mountedImage in _allMountedImages)
                    {
                        WriteObject(mountedImage);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;

                LoggingService.WriteError(this, ServiceName, $"Media Dynamic Update failed: {ex.Message}", ex);

                if (!ContinueOnError.IsPresent)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        ex,
                        "MediaDynamicUpdateFailed",
                        ErrorCategory.NotSpecified,
                        MediaPath));
                }
            }
            finally
            {
                // Auto-dismount if requested, otherwise cleanup only empty mount directories
                if (AutoDismount.IsPresent)
                {
                    DismountAllImages();
                    CleanupMountDirectories();
                }
                else
                {
                    CleanupEmptyMountDirectories();
                }
            }

            // Always output the result object
            WriteObject(result);
        }

        /// <summary>
        /// Discovers WIM files and update packages
        /// </summary>
        private void DiscoverFiles(MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, "Discovering WIM files and update packages");

            // Find all WIM files recursively
            _allWimFiles = MediaPath.GetFiles("*.wim", SearchOption.AllDirectories).ToList();
            LoggingService.WriteVerbose(this, ServiceName, $"Found {_allWimFiles.Count} WIM files");

            // Find all update files
            var updateExtensions = new[] { "*.msu", "*.cab", "*.exe" };
            _updateFiles = updateExtensions
                .SelectMany(ext => UpdatesPath.GetFiles(ext, SearchOption.AllDirectories))
                .ToList();
            LoggingService.WriteVerbose(this, ServiceName, $"Found {_updateFiles.Count} update files");

            result.DiscoveredWimFiles = _allWimFiles.Select(f => f.FullName).ToList();
            result.DiscoveredUpdateFiles = _updateFiles.Select(f => f.FullName).ToList();
        }

        /// <summary>
        /// Processes boot images (boot.wim) first per Media Dynamic Update sequence
        /// </summary>
        private void ProcessBootImages(MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, "Processing boot images");

            var bootWimFiles = _allWimFiles.Where(f => 
                f.Name.Equals("boot.wim", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var bootWim in bootWimFiles)
            {
                ProcessSingleWimFile(bootWim, "Boot", result);
            }
        }

        /// <summary>
        /// Processes Windows images (install.wim) by index per Media Dynamic Update sequence
        /// </summary>
        private void ProcessWindowsImages(MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, "Processing Windows images");

            var installWimFiles = _allWimFiles.Where(f => 
                f.Name.Equals("install.wim", StringComparison.OrdinalIgnoreCase) ||
                f.Name.Equals("sources.wim", StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var installWim in installWimFiles)
            {
                ProcessSingleWimFile(installWim, "Windows", result);
            }
        }

        /// <summary>
        /// Processes a single WIM file with all its images
        /// </summary>
        private void ProcessSingleWimFile(FileInfo wimFile, string imageType, MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, $"Processing {imageType} WIM: {wimFile.FullName}");

            try
            {
                // Get all images in the WIM file
                var images = GetWindowsImageList(wimFile);
                LoggingService.WriteVerbose(this, ServiceName, $"Found {images.Count} images in {wimFile.Name}");

                // Process each image by index (maintaining order)
                foreach (var image in images.OrderBy(i => i.Index))
                {
                    ProcessSingleImage(image, imageType, result);
                }
            }
            catch (Exception ex)
            {
                var error = $"Failed to process {imageType} WIM {wimFile.Name}: {ex.Message}";
                result.Errors.Add(error);
                LoggingService.WriteError(this, ServiceName, error, ex);

                if (!ContinueOnError.IsPresent)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Processes a single Windows image with Dynamic Updates
        /// </summary>
        private void ProcessSingleImage(WindowsImageInfo image, string imageType, MediaDynamicUpdateResult result)
        {
            var operationName = $"{imageType}_Index{image.Index}_{image.Name}";
            LoggingService.WriteVerbose(this, ServiceName, $"Processing image: {operationName}");

            MountedWindowsImage? mountedImage = null;
            try
            {
                // Mount the image
                mountedImage = MountImage(image);
                if (mountedImage == null)
                {
                    throw new InvalidOperationException($"Failed to mount image {operationName}");
                }

                // Track mounted image for later output or cleanup
                _allMountedImages.Add(mountedImage);

                // Apply Dynamic Updates in the correct sequence
                ApplyDynamicUpdatesSequence(mountedImage, imageType, result);

                // Perform cleanup if requested
                if (PerformCleanup.IsPresent)
                {
                    PerformImageCleanup(mountedImage);
                }

                // Validate if requested
                if (ValidateImages.IsPresent)
                {
                    ValidateImage(mountedImage);
                }

                result.ProcessedImages.Add(operationName);
                LoggingService.WriteVerbose(this, ServiceName, $"Successfully processed image: {operationName}");
            }
            catch (Exception ex)
            {
                var error = $"Failed to process image {operationName}: {ex.Message}";
                result.Errors.Add(error);
                LoggingService.WriteError(this, ServiceName, error, ex);

                if (!ContinueOnError.IsPresent)
                {
                    throw;
                }
            }
            finally
            {
                // Only dismount on error or if AutoDismount is specified
                if (mountedImage != null && (AutoDismount.IsPresent || !result.Success))
                {
                    DismountImage(mountedImage);
                    _allMountedImages.Remove(mountedImage);
                }
            }
        }

        /// <summary>
        /// Gets Windows image list using existing cmdlet functionality
        /// </summary>
        private List<WindowsImageInfo> GetWindowsImageList(FileInfo wimFile)
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            return imageService.GetImageInfo(wimFile.FullName);
        }

        /// <summary>
        /// Mounts a Windows image using existing cmdlet functionality
        /// </summary>
        private MountedWindowsImage? MountImage(WindowsImageInfo image)
        {
            try
            {
                var mountPath = Path.Combine(_mountBasePath.FullName, $"Image_{image.Index}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(mountPath);

                LoggingService.WriteVerbose(this, ServiceName, $"Mounting image {image.Index} to {mountPath} using native DISM API");

                // MountImage throws on failure with the underlying DISM error
                using var imageService = WindowsImageService.ForCmdlet(this);
                imageService.MountImage(
                    image.SourcePath,
                    mountPath,
                    (uint)image.Index,
                    readOnly: false,
                    progressCallback: null); // No progress callback needed for internal operations

                return new MountedWindowsImage
                {
                    SourceImagePath = image.SourcePath,
                    ImageIndex = image.Index,
                    ImageName = image.Name,
                    Edition = image.Edition,
                    Architecture = image.Architecture,
                    ImageSize = image.Size,
                    MountPath = new DirectoryInfo(mountPath),
                    Status = MountStatus.Mounted,
                    MountedAt = DateTime.UtcNow,
                    IsReadOnly = false
                };
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ServiceName, $"Failed to mount image {image.Index}: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Dismounts a Windows image
        /// </summary>
        private void DismountImage(MountedWindowsImage mountedImage)
        {
            try
            {
                LoggingService.WriteVerbose(this, ServiceName, $"Dismounting image from {mountedImage.MountPath?.FullName ?? "Unknown"} using native DISM API");

                if (mountedImage.MountPath == null)
                    throw new InvalidOperationException("Mount path is null");

                // UnmountImage throws on failure with the underlying DISM error
                using var imageService = WindowsImageService.ForCmdlet(this);
                imageService.UnmountImage(
                    mountedImage.MountPath.FullName,
                    commitChanges: true,
                    progressCallback: null); // No progress callback needed for internal operations

                // Clean up mount directory
                if (mountedImage.MountPath.Exists)
                {
                    mountedImage.MountPath.Delete(recursive: true);
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ServiceName, $"Failed to dismount image: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies Dynamic Updates in the correct sequence per Media Dynamic Update documentation
        /// </summary>
        private void ApplyDynamicUpdatesSequence(MountedWindowsImage mountedImage, string imageType, MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, $"Applying Dynamic Updates sequence for {imageType} image: {mountedImage.ImageName}");

            // Define update sequence per Media Dynamic Update documentation
            var updateSequence = new[]
            {
                "ServicingStack",  // Step 1: Servicing Stack Updates (critical first)
                "SafeOS",          // Step 2: Safe OS Dynamic Updates (for WinRE)
                "Cumulative",      // Step 3: Latest Cumulative Updates (last)
                "Setup"            // Step 4: Setup Dynamic Updates (media level)
            };

            foreach (var updateType in updateSequence)
            {
                var applicableUpdates = GetApplicableUpdates(updateType, imageType);

                foreach (var updateFile in applicableUpdates)
                {
                    ApplySingleUpdate(mountedImage, updateFile, updateType, result);
                }
            }
        }

        /// <summary>
        /// Gets applicable updates for a specific type and image type
        /// </summary>
        private List<FileInfo> GetApplicableUpdates(string updateType, string imageType)
        {
            // Filter updates based on filename patterns per Media Dynamic Update documentation
            return _updateFiles.Where(f => IsUpdateApplicable(f, updateType, imageType)).ToList();
        }

        /// <summary>
        /// Determines if an update is applicable based on filename and type
        /// </summary>
        private bool IsUpdateApplicable(FileInfo updateFile, string updateType, string imageType)
        {
            var fileName = updateFile.Name.ToLowerInvariant();

            return updateType.ToLowerInvariant() switch
            {
                "servicingstack" => fileName.Contains("ssu") || fileName.Contains("servicing"),
                "safeos" => fileName.Contains("safeos") || fileName.Contains("safe-os"),
                "cumulative" => fileName.Contains("cumulative") || fileName.Contains("lcu"),
                "setup" => fileName.Contains("setup") && fileName.Contains("du"),
                _ => false
            };
        }

        /// <summary>
        /// Applies a single update to a mounted image
        /// </summary>
        private void ApplySingleUpdate(MountedWindowsImage mountedImage, FileInfo updateFile, string updateType, MediaDynamicUpdateResult result)
        {
            try
            {
                LoggingService.WriteVerbose(this, ServiceName,
                    $"Applying {updateType} update: {updateFile.Name} to {mountedImage.ImageName}");

                // Use DISM API to add the package
                if (mountedImage.MountPath == null)
                    throw new InvalidOperationException("Mount path is null");
                using var session = Microsoft.Dism.DismApi.OpenOfflineSession(mountedImage.MountPath.FullName);
                Microsoft.Dism.DismApi.AddPackage(session, updateFile.FullName, ignoreCheck: false, preventPending: false);

                result.AppliedUpdates.Add($"{updateType}: {updateFile.Name}");
                LoggingService.WriteVerbose(this, ServiceName, $"Successfully applied {updateType} update: {updateFile.Name}");
            }
            catch (Exception ex)
            {
                var error = $"Failed to apply {updateType} update {updateFile.Name}: {ex.Message}";
                result.Errors.Add(error);
                LoggingService.WriteError(this, ServiceName, error, ex);

                // Critical updates should stop processing
                if (updateType == "ServicingStack" && !ContinueOnError.IsPresent)
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// Performs image cleanup using existing functionality
        /// </summary>
        private void PerformImageCleanup(MountedWindowsImage mountedImage)
        {
            try
            {
                LoggingService.WriteVerbose(this, ServiceName, $"Performing cleanup on {mountedImage.ImageName}");

                // Use DISM command line for component cleanup (API methods may not be available)
                var dismProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = $"/image:\"{mountedImage.MountPath?.FullName ?? "Unknown"}\" /cleanup-image /StartComponentCleanup /ResetBase",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                dismProcess?.WaitForExit();

                if (dismProcess?.ExitCode == 0)
                {
                    LoggingService.WriteVerbose(this, ServiceName, "Image cleanup completed successfully");
                }
                else
                {
                    LoggingService.WriteWarning(this, ServiceName, $"Image cleanup completed with warnings (Exit code: {dismProcess?.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ServiceName, $"Image cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates image integrity
        /// </summary>
        private void ValidateImage(MountedWindowsImage mountedImage)
        {
            try
            {
                LoggingService.WriteVerbose(this, ServiceName, $"Validating image {mountedImage.ImageName}");

                // Use DISM command line for image validation
                var dismProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = $"/image:\"{mountedImage.MountPath?.FullName ?? "Unknown"}\" /cleanup-image /CheckHealth",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                dismProcess?.WaitForExit();

                if (dismProcess?.ExitCode == 0)
                {
                    LoggingService.WriteVerbose(this, ServiceName, "Image validation completed successfully");
                }
                else
                {
                    LoggingService.WriteWarning(this, ServiceName, $"Image validation completed with warnings (Exit code: {dismProcess?.ExitCode})");
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ServiceName, $"Image validation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies Setup Dynamic Updates to the media (not to mounted images)
        /// </summary>
        private void ApplySetupDynamicUpdates(MediaDynamicUpdateResult result)
        {
            LoggingService.WriteVerbose(this, ServiceName, "Applying Setup Dynamic Updates to media");

            var setupUpdates = _updateFiles.Where(f => IsUpdateApplicable(f, "Setup", "Media")).ToList();

            foreach (var setupUpdate in setupUpdates)
            {
                try
                {
                    // Extract Setup DU to sources directory per Media Dynamic Update documentation
                    var sourcesPath = Path.Combine(MediaPath.FullName, "sources");
                    if (Directory.Exists(sourcesPath))
                    {
                        LoggingService.WriteVerbose(this, ServiceName, $"Extracting Setup DU: {setupUpdate.Name}");

                        // Use expand.exe to extract CAB files to sources directory
                        var expandProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "expand.exe",
                            Arguments = $"\"{setupUpdate.FullName}\" -F:* \"{sourcesPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });

                        expandProcess?.WaitForExit();

                        if (expandProcess?.ExitCode == 0)
                        {
                            result.AppliedUpdates.Add($"Setup: {setupUpdate.Name}");
                            LoggingService.WriteVerbose(this, ServiceName, $"Successfully applied Setup DU: {setupUpdate.Name}");
                        }
                        else
                        {
                            throw new InvalidOperationException($"Expand.exe failed with exit code: {expandProcess?.ExitCode}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    var error = $"Failed to apply Setup DU {setupUpdate.Name}: {ex.Message}";
                    result.Errors.Add(error);
                    LoggingService.WriteError(this, ServiceName, error, ex);
                }
            }
        }

        /// <summary>
        /// Dismounts all tracked mounted images
        /// </summary>
        private void DismountAllImages()
        {
            foreach (var mountedImage in _allMountedImages.ToList())
            {
                DismountImage(mountedImage);
            }
            _allMountedImages.Clear();
        }

        /// <summary>
        /// Cleans up mount directories (removes all)
        /// </summary>
        private void CleanupMountDirectories()
        {
            try
            {
                if (_mountBasePath.Exists)
                {
                    LoggingService.WriteVerbose(this, ServiceName, $"Cleaning up mount directories: {_mountBasePath.FullName}");
                    _mountBasePath.Delete(recursive: true);
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ServiceName, $"Failed to cleanup mount directories: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up only empty mount directories (preserves mounted images)
        /// </summary>
        private void CleanupEmptyMountDirectories()
        {
            try
            {
                if (_mountBasePath.Exists)
                {
                    // Only remove empty subdirectories, preserve those with mounted images
                    var subdirs = _mountBasePath.GetDirectories();
                    foreach (var subdir in subdirs)
                    {
                        try
                        {
                            // Check if this directory is still in use by a mounted image
                            var isInUse = _allMountedImages.Any(img =>
                                string.Equals(img.MountPath?.FullName, subdir.FullName, StringComparison.OrdinalIgnoreCase));

                            if (!isInUse && subdir.GetFileSystemInfos().Length == 0)
                            {
                                subdir.Delete();
                                LoggingService.WriteVerbose(this, ServiceName, $"Cleaned up empty mount directory: {subdir.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggingService.WriteVerbose(this, ServiceName, $"Could not clean up directory {subdir.Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ServiceName, $"Failed to cleanup empty mount directories: {ex.Message}");
            }
        }
    }
}
