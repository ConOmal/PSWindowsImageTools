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
    /// Mounts Windows images from WindowsImageInfo objects (from Get-WindowsImageList)
    /// </summary>
    [Cmdlet(VerbsData.Mount, "WindowsImageList")]
    [OutputType(typeof(MountedWindowsImage[]))]
    public class MountWindowsImageListCmdlet : PSCmdlet
    {
        /// <summary>
        /// Windows image information objects to mount (from Get-WindowsImageList pipeline)
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "FromPipeline")]
        [ValidateNotNull]
        public WindowsImageInfo[] InputObject { get; set; } = null!;

        /// <summary>
        /// Windows image information objects to mount (from parameter)
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "FromParameter")]
        [ValidateNotNull]
        public WindowsImageInfo[] ImageInfo { get; set; } = null!;

        /// <summary>
        /// Mount images as read-write (default is read-only)
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter ReadWrite { get; set; }

        /// <summary>
        /// Custom mount root directory (uses temp if not specified)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNull]
        public DirectoryInfo? MountRoot { get; set; }

        private readonly List<WindowsImageInfo> _allImageInfo = new List<WindowsImageInfo>();

        /// <summary>
        /// Processes pipeline input
        /// </summary>
        protected override void ProcessRecord()
        {
            try
            {
                // Collect image info objects from pipeline or parameter
                var imagesToProcess = ParameterSetName == "FromPipeline" ? InputObject : ImageInfo;
                _allImageInfo.AddRange(imagesToProcess);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, $"Failed to process record: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Processes all collected images
        /// </summary>
        protected override void EndProcessing()
        {
            var startTime = DateTime.UtcNow;
            var mountedImages = new List<MountedWindowsImage>();

            try
            {
                if (_allImageInfo.Count == 0)
                {
                    LoggingService.WriteWarning(this, "No image information provided for mounting");
                    return;
                }

                // Get mount root directory
                var mountRoot = MountRoot?.FullName ?? ConfigurationService.DefaultMountRootDirectory;
                LoggingService.WriteVerbose(this, $"Using mount root directory: {mountRoot}");

                LoggingService.WriteVerbose(this, $"Mounting {_allImageInfo.Count} images");

                // Group images by source path to generate one GUID per WIM file
                var imageGroups = _allImageInfo.GroupBy(img => img.SourcePath).ToList();

                // Show initial progress
                LoggingService.WriteProgress(this, "Mounting Windows Images",
                    $"Found {_allImageInfo.Count} images to mount",
                    $"Preparing to mount {_allImageInfo.Count} images", 0);

                // Generate one GUID per unique source path for mount organization
                var sourcePathGuids = new Dictionary<string, string>();
                foreach (var imageInfo in _allImageInfo)
                {
                    if (!sourcePathGuids.ContainsKey(imageInfo.SourcePath))
                    {
                        sourcePathGuids[imageInfo.SourcePath] = Guid.NewGuid().ToString();
                    }
                }

                // Mount each image
                for (int i = 0; i < _allImageInfo.Count; i++)
                {
                    var imageInfo = _allImageInfo[i];
                    var wimGuid = sourcePathGuids[imageInfo.SourcePath];

                    try
                    {
                        var mountedImage = MountSingleImage(imageInfo, mountRoot, wimGuid, i + 1, _allImageInfo.Count);
                        mountedImages.Add(mountedImage);

                        LoggingService.WriteVerbose(this, $"[{i + 1} of {_allImageInfo.Count}] - Successfully mounted: {mountedImage.MountPath}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.WriteError(this, $"[{i + 1} of {_allImageInfo.Count}] - Failed to mount image {imageInfo.Index}: {ex.Message}", ex);

                        // Create a failed mount object for tracking
                        var failedMount = new MountedWindowsImage
                        {
                            MountId = Guid.NewGuid().ToString(),
                            SourceImagePath = imageInfo.SourcePath,
                            ImageIndex = imageInfo.Index,
                            ImageName = imageInfo.Name,
                            Edition = imageInfo.Edition,
                            Architecture = imageInfo.Architecture,
                            WimGuid = wimGuid,
                            Status = MountStatus.Failed,
                            ErrorMessage = ex.Message,
                            ImageSize = imageInfo.Size,
                            IsReadOnly = !ReadWrite.IsPresent
                        };
                        mountedImages.Add(failedMount);
                    }
                }

                LoggingService.CompleteProgress(this, "Mounting Windows Images");

                // Show summary
                var successCount = mountedImages.Count(m => m.Status == MountStatus.Mounted);
                var failCount = mountedImages.Count(m => m.Status == MountStatus.Failed);

                LoggingService.WriteVerbose(this, $"Mount operation complete: {successCount} successful, {failCount} failed");

                // Output results
                WriteObject(mountedImages.ToArray());

                var duration = DateTime.UtcNow - startTime;
                LoggingService.LogOperationComplete(this, "MountImageList", duration, $"Mounted {successCount} of {_allImageInfo.Count} images");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, "Failed to mount images", ex);
                throw;
            }
        }

        /// <summary>
        /// Mounts a single image and returns the mounted image object
        /// </summary>
        private MountedWindowsImage MountSingleImage(WindowsImageInfo imageInfo, string mountRoot, string wimGuid, int currentIndex, int totalCount)
        {
            var mountId = Guid.NewGuid().ToString();
            var mountPath = ConfigurationService.CreateUniqueMountDirectory(mountRoot, imageInfo.Index, wimGuid);
            
            LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Created mount directory: {mountPath}");

            var mountedImage = new MountedWindowsImage
            {
                MountId = mountId,
                SourceImagePath = imageInfo.SourcePath,
                ImageIndex = imageInfo.Index,
                ImageName = imageInfo.Name,
                Edition = imageInfo.Edition,
                Architecture = imageInfo.Architecture,
                MountPath = new DirectoryInfo(mountPath),
                WimGuid = wimGuid,
                Status = MountStatus.Mounting,
                IsReadOnly = !ReadWrite.IsPresent,
                ImageSize = imageInfo.Size
            };

            try
            {
                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Mounting image {imageInfo.Index} to {mountPath} using native DISM API");

                // Create native progress callback for real-time mount progress
                var progressCallback = ProgressService.CreateMountProgressCallback(
                    this,
                    "Mounting Windows Images",
                    imageInfo.Name,
                    mountPath,
                    currentIndex,
                    totalCount);

                var mountStartTime = DateTime.UtcNow;

                // Use unified image service for mounting with real progress callbacks
                using var imageService = WindowsImageService.ForCmdlet(this);
                // (MountImage throws on failure with the underlying DISM error)
                imageService.MountImage(
                    imageInfo.SourcePath,
                    mountPath,
                    (uint)imageInfo.Index,
                    readOnly: !ReadWrite.IsPresent,
                    progressCallback: progressCallback);

                var mountDuration = DateTime.UtcNow - mountStartTime;

                mountedImage.Status = MountStatus.Mounted;
                mountedImage.MountedAt = DateTime.UtcNow;

                // Register for re-discovery across sessions
                MountSessionService.Register(mountedImage);

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Image mounted successfully using native API: {imageInfo.Name} (Duration: {LoggingService.FormatDuration(mountDuration)})");

                return mountedImage;
            }
            catch (Exception ex)
            {
                mountedImage.Status = MountStatus.Failed;
                mountedImage.ErrorMessage = ex.Message;
                
                // Clean up mount directory if mount failed
                try
                {
                    if (Directory.Exists(mountPath))
                    {
                        Directory.Delete(mountPath, true);
                        LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Cleaned up failed mount directory: {mountPath}");
                    }
                }
                catch (Exception cleanupEx)
                {
                    LoggingService.WriteWarning(this, $"Failed to clean up mount directory {mountPath}: {cleanupEx.Message}");
                }
                
                throw;
            }
        }
    }
}
