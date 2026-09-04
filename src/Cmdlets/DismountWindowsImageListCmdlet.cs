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
    /// Dismounts Windows images with options to save or discard changes
    /// </summary>
    [Cmdlet(VerbsData.Dismount, "WindowsImageList")]
    [OutputType(typeof(MountedWindowsImage[]))]
    public class DismountWindowsImageListCmdlet : PSCmdlet
    {
        /// <summary>
        /// Mounted image objects to dismount
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "ByObject")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Mount directories to dismount
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "ByPath")]
        [ValidateNotNullOrEmpty]
        public DirectoryInfo[] Path { get; set; } = Array.Empty<DirectoryInfo>();

        /// <summary>
        /// Save changes made to the mounted images (default is discard)
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = "ByObject")]
        [Parameter(Mandatory = false, ParameterSetName = "ByPath")]
        public SwitchParameter Save { get; set; }

        /// <summary>
        /// Discard changes made to the mounted images (explicit discard)
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = "ByObject")]
        [Parameter(Mandatory = false, ParameterSetName = "ByPath")]
        public SwitchParameter Discard { get; set; }

        /// <summary>
        /// Append changes to the image (for images with multiple indexes)
        /// </summary>
        [Parameter(Mandatory = false, ParameterSetName = "ByObject")]
        [Parameter(Mandatory = false, ParameterSetName = "ByPath")]
        public SwitchParameter Append { get; set; }

        /// <summary>
        /// Force dismount even if there are open handles
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Remove mount directories after dismounting
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter RemoveDirectories { get; set; }

        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Processes pipeline input
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ParameterSetName == "ByObject")
            {
                _allMountedImages.AddRange(MountedImages);
            }
            else if (ParameterSetName == "ByPath")
            {
                // Convert directory paths to MountedWindowsImage objects
                foreach (var directory in Path)
                {
                    var mountedImage = new MountedWindowsImage
                    {
                        MountId = Guid.NewGuid().ToString(),
                        MountPath = directory,
                        Status = MountStatus.Mounted,
                        ImageName = $"Image at {directory.FullName}",
                        MountedAt = DateTime.UtcNow
                    };
                    _allMountedImages.Add(mountedImage);
                }
            }
        }

        /// <summary>
        /// Processes all collected input at the end
        /// </summary>
        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted images provided for dismounting");
                return;
            }

            var startTime = DateTime.UtcNow;
            var results = new List<MountedWindowsImage>();

            try
            {


                // Determine operation mode for display
                var operationMode = Save.IsPresent && !Discard.IsPresent ?
                    (Append.IsPresent ? "Save with Append" : "Save") : "Discard";

                // Show initial progress
                LoggingService.WriteProgress(this, "Dismounting Windows Images",
                    $"Preparing to dismount {_allMountedImages.Count} images",
                    $"Mode: {operationMode}", 0);

                // Dismount each image
                for (int i = 0; i < _allMountedImages.Count; i++)
                {
                    var mountedImage = _allMountedImages[i];

                    try
                    {
                        var result = DismountSingleImage(mountedImage, i + 1, _allMountedImages.Count);
                        results.Add(result);
                        
                        LoggingService.WriteVerbose(this, $"[{i + 1} of {_allMountedImages.Count}] - Successfully dismounted: {mountedImage.MountPath?.FullName ?? "Unknown path"}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.WriteError(this, $"[{i + 1} of {_allMountedImages.Count}] - Failed to dismount {mountedImage.MountPath?.FullName ?? "Unknown path"}: {ex.Message}", ex);
                        
                        // Update status to failed
                        mountedImage.Status = MountStatus.Failed;
                        mountedImage.ErrorMessage = ex.Message;
                        results.Add(mountedImage);
                    }
                }

                LoggingService.CompleteProgress(this, "Dismounting Windows Images");

                // Show summary
                var successCount = results.Count(m => m.Status == MountStatus.Unmounted);
                var failCount = results.Count(m => m.Status == MountStatus.Failed);
                
                LoggingService.WriteVerbose(this, $"Dismount operation complete: {successCount} successful, {failCount} failed");

                // Output results
                WriteObject(results.ToArray());

                var duration = DateTime.UtcNow - startTime;
                LoggingService.LogOperationComplete(this, "DismountImageList", duration, $"Dismounted {successCount} of {_allMountedImages.Count} images");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, "Failed to dismount images", ex);
                throw;
            }
        }

        /// <summary>
        /// Dismounts a single image and returns the updated mounted image object
        /// </summary>
        private MountedWindowsImage DismountSingleImage(MountedWindowsImage mountedImage, int currentIndex, int totalCount)
        {
            // Create a copy to avoid modifying the original
            var result = new MountedWindowsImage
            {
                MountId = mountedImage.MountId,
                SourceImagePath = mountedImage.SourceImagePath,
                ImageIndex = mountedImage.ImageIndex,
                ImageName = mountedImage.ImageName,
                Edition = mountedImage.Edition,
                Architecture = mountedImage.Architecture,
                MountPath = mountedImage.MountPath,
                WimGuid = mountedImage.WimGuid,
                MountedAt = mountedImage.MountedAt,
                Status = MountStatus.Unmounting,
                IsReadOnly = mountedImage.IsReadOnly,
                ImageSize = mountedImage.ImageSize,
                WinRE = mountedImage.WinRE
            };

            try
            {
                // Validate mount path exists
                if (mountedImage.MountPath == null || !mountedImage.MountPath.Exists)
                {
                    LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Mount path does not exist: {mountedImage.MountPath?.FullName ?? "Unknown path"}");
                    result.Status = MountStatus.Unmounted;
                    return result;
                }

                // Determine save/discard behavior
                var shouldSave = Save.IsPresent && !Discard.IsPresent;
                var saveMode = shouldSave ? (Append.IsPresent ? "Save with Append" : "Save") : "Discard";

                DismountEmbeddedWinRE(mountedImage, shouldSave, currentIndex, totalCount);

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Dismounting image from {mountedImage.MountPath.FullName} using native DISM API");
                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Mode: {saveMode}");

                // Create native progress callback for real-time dismount progress
                var progressCallback = ProgressService.CreateMountProgressCallback(
                    this,
                    "Dismounting Windows Images",
                    mountedImage.ImageName ?? "Unknown Image",
                    mountedImage.MountPath.FullName,
                    currentIndex,
                    totalCount);

                var dismountStartTime = DateTime.UtcNow;

                // Use unified image service for dismounting with real progress callbacks
                using var imageService = WindowsImageService.ForCmdlet(this);
                // (UnmountImage throws on failure with the underlying DISM error)
                imageService.UnmountImage(
                    mountedImage.MountPath.FullName,
                    shouldSave,
                    progressCallback: progressCallback);

                var dismountDuration = DateTime.UtcNow - dismountStartTime;

                result.Status = MountStatus.Unmounted;

                // Unregister from the cross-session mount registry
                MountSessionService.Unregister(mountedImage.MountPath.FullName);

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Image dismounted successfully using native API (Duration: {LoggingService.FormatDuration(dismountDuration)})");

                // Remove mount directory if requested
                if (RemoveDirectories.IsPresent)
                {
                    try
                    {
                        if (mountedImage.MountPath.Exists)
                        {
                            mountedImage.MountPath.Delete(true);
                            LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Removed mount directory: {mountedImage.MountPath.FullName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Failed to remove mount directory {mountedImage.MountPath.FullName}: {ex.Message}");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                result.Status = MountStatus.Failed;
                result.ErrorMessage = ex.Message;
                
                LoggingService.WriteError(this, $"[{currentIndex} of {totalCount}] - Failed to dismount image: {ex.Message}", ex);
                
                // If force is specified, try to clean up anyway
                if (Force.IsPresent)
                {
                    try
                    {
                        LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Force flag specified, attempting cleanup");
                        
                        if (mountedImage.MountPath?.Exists == true)
                        {
                            mountedImage.MountPath.Delete(true);
                            LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Force removed mount directory: {mountedImage.MountPath.FullName}");
                        }
                        
                result.Status = MountStatus.Unmounted;

                // Unregister from the cross-session mount registry
                if (mountedImage.MountPath != null)
                {
                    MountSessionService.Unregister(mountedImage.MountPath.FullName);
                }
                    }
                    catch (Exception forceEx)
                    {
                        LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Force cleanup also failed: {forceEx.Message}");
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Dismounts a mounted embedded WinRE image first, re-embedding it into the parent when saving
        /// </summary>
        private void DismountEmbeddedWinRE(MountedWindowsImage mountedImage, bool shouldSave, int currentIndex, int totalCount)
        {
            var winRE = mountedImage.WinRE;
            if (winRE?.MountPath == null || winRE.Status != MountStatus.Mounted)
            {
                return;
            }

            try
            {
                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Dismounting embedded WinRE image from {winRE.MountPath.FullName}");

                using var winREImageService = WindowsImageService.ForCmdlet(this);
                winREImageService.UnmountImage(winRE.MountPath.FullName, commitChanges: shouldSave && !winRE.IsReadOnly);

                MountSessionService.Unregister(winRE.MountPath.FullName);

                if (shouldSave && !winRE.IsReadOnly && mountedImage.MountPath != null)
                {
                    WinREImageService.ReplaceEmbeddedWinRE(mountedImage.MountPath.FullName, winRE.SourceImagePath);
                    LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Re-embedded updated WinRE image into parent");
                }

                winRE.Status = MountStatus.Unmounted;
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Failed to dismount/re-embed WinRE image: {ex.Message}");
                winRE.Status = MountStatus.Failed;
                winRE.ErrorMessage = ex.Message;
            }
            finally
            {
                try
                {
                    if (winRE.MountPath.Exists)
                    {
                        winRE.MountPath.Delete(recursive: true);
                    }

                    if (File.Exists(winRE.SourceImagePath))
                    {
                        File.Delete(winRE.SourceImagePath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    LoggingService.WriteWarning(this, $"Failed to clean up WinRE mount artifacts: {cleanupEx.Message}");
                }
            }
        }
    }
}
