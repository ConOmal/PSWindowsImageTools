using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using System.Threading.Tasks;
using System.Threading;

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

        /// <summary>
        /// Maximum parallel mount operations (0 = auto based on processor count)
        /// </summary>
        [Parameter(Mandatory = false)]
        public int MaxParallel { get; set; } = 0;

        private readonly List<WindowsImageInfo> _allImageInfo = new List<WindowsImageInfo>();

        // PowerShell cmdlet output methods (WriteVerbose/WriteWarning/WriteError/WriteProgress)
        // are pipeline-thread-only. Parallel mounting runs on worker threads, so messages and
        // progress produced there are buffered and drained on the pipeline thread afterwards.
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _bufferedVerbose =
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _bufferedWarnings =
            new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string Message, Exception Exception)> _bufferedErrors =
            new System.Collections.Concurrent.ConcurrentQueue<(string Message, Exception Exception)>();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(int Percent, string Activity, string Status)> _bufferedProgress =
            new System.Collections.Concurrent.ConcurrentQueue<(int Percent, string Activity, string Status)>();

        private void BufferVerbose(string message) => _bufferedVerbose.Enqueue(message);

        private void BufferWarning(string message) => _bufferedWarnings.Enqueue(message);

        private void BufferError(string message, Exception exception) => _bufferedErrors.Enqueue((message, exception));

        /// <summary>
        /// Creates ModuleCallbacks that buffer output for later drain on the pipeline thread
        /// </summary>
        private ModuleCallbacks CreateBufferedCallbacks()
        {
            return new ModuleCallbacks
            {
                Verbose = BufferVerbose,
                Warning = BufferWarning,
                Error = (exception, message) => BufferError(message, exception)
            };
        }

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

                // Generate one GUID per unique source path for mount organization
                var sourcePathGuids = new Dictionary<string, string>();
                foreach (var imageInfo in _allImageInfo)
                {
                    if (!sourcePathGuids.ContainsKey(imageInfo.SourcePath))
                    {
                        sourcePathGuids[imageInfo.SourcePath] = Guid.NewGuid().ToString();
                    }
                }

                // Parallel mounting logic
                var parallelOptions = new ParallelOptions();
                parallelOptions.MaxDegreeOfParallelism = MaxParallel > 0 ? MaxParallel : Environment.ProcessorCount;
                int processedCount = 0;
                object lockObj = new object();

                Parallel.ForEach(_allImageInfo, parallelOptions, imageInfo =>
                {
                    int currentIndex = Interlocked.Increment(ref processedCount);
                    var wimGuid = sourcePathGuids[imageInfo.SourcePath];
                    try
                    {
                        var mountedImage = MountSingleImage(imageInfo, mountRoot, wimGuid, currentIndex, _allImageInfo.Count);
                        lock (lockObj) { mountedImages.Add(mountedImage); }
                        BufferVerbose($"[{currentIndex} of {_allImageInfo.Count}] - Successfully mounted: {mountedImage.MountPath}");
                    }
                    catch (Exception ex)
                    {
                        BufferError($"[{currentIndex} of {_allImageInfo.Count}] - Failed to mount image {imageInfo.Index}: {ex.Message}", ex);
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
                        lock (lockObj) { mountedImages.Add(failedMount); }
                    }
                });

                // Drain worker-thread buffers on the pipeline thread — cmdlet output methods
                // cannot be called from outside the pipeline thread
                while (_bufferedVerbose.TryDequeue(out var bufferedMessage))
                {
                    LoggingService.WriteVerbose(this, bufferedMessage);
                }

                while (_bufferedProgress.TryDequeue(out var bufferedProgress))
                {
                    LoggingService.WriteProgress(this, bufferedProgress.Activity, bufferedProgress.Status, bufferedProgress.Percent);
                }

                while (_bufferedWarnings.TryDequeue(out var bufferedWarning))
                {
                    LoggingService.WriteWarning(this, bufferedWarning);
                }

                while (_bufferedErrors.TryDequeue(out var bufferedError))
                {
                    LoggingService.WriteError(this, bufferedError.Message, bufferedError.Exception);
                }

                LoggingService.CompleteProgress(this, "Mounting Windows Images");

                // Show summary
                var successCount = mountedImages.Count(m => m.Status == MountStatus.Mounted);
                var failCount = mountedImages.Count(m => m.Status == MountStatus.Failed);

                LoggingService.WriteVerbose(this, $"Mount operation complete: {successCount} successful, {failCount} failed");

                // Output results — enumerate explicitly so each MountedWindowsImage flows
                // through the pipeline individually (the single-argument WriteObject overload
                // emits the array as ONE item, which breaks single-object parameter binding
                // downstream)
                WriteObject(mountedImages.ToArray(), true);

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
            
            BufferVerbose($"[{currentIndex} of {totalCount}] - Created mount directory: {mountPath}");

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
                BufferVerbose($"[{currentIndex} of {totalCount}] - Mounting image {imageInfo.Index} to {mountPath} using native DISM API");

                // Real-time mount progress is buffered: DISM invokes the callback on the worker
                // thread, and cmdlet output methods are pipeline-thread-only.
                var progressCallback = new Action<int, string>((percent, status) =>
                    _bufferedProgress.Enqueue((percent, "Mounting Windows Images",
                        $"{imageInfo.Name} [{currentIndex} of {totalCount}]: {status}")));

                var mountStartTime = DateTime.UtcNow;

                // Use unified image service for mounting with buffered callbacks (worker thread)
                using var imageService = new WindowsImageService(CreateBufferedCallbacks());
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

                BufferVerbose($"[{currentIndex} of {totalCount}] - Image mounted successfully using native API: {imageInfo.Name} (Duration: {LoggingService.FormatDuration(mountDuration)})");

                TryMountEmbeddedWinRE(mountedImage, currentIndex, totalCount);

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
                    BufferWarning($"Failed to clean up mount directory {mountPath}: {cleanupEx.Message}");
                }

                throw;
            }
        }

        /// <summary>
        /// Detects an embedded winre.wim inside a just-mounted image and mounts it too, exposed as .WinRE
        /// </summary>
        private void TryMountEmbeddedWinRE(MountedWindowsImage mountedImage, int currentIndex, int totalCount)
        {
            if (mountedImage.MountPath == null)
            {
                return;
            }

            if (!WinREImageService.TryGetEmbeddedWinREPath(mountedImage.MountPath.FullName, out _))
            {
                return;
            }

            var winREWimPath = Path.Combine(
                Path.GetDirectoryName(mountedImage.MountPath.FullName) ?? Path.GetTempPath(),
                $"WinRE_{Guid.NewGuid():N}.wim");
            var winREMountPath = mountedImage.MountPath.FullName + "_WinRE";

            try
            {
                BufferVerbose($"[{currentIndex} of {totalCount}] - Found embedded WinRE image, extracting and mounting");

                WinREImageService.ExtractEmbeddedWinRE(mountedImage.MountPath.FullName, winREWimPath);

                using var winREImageService = new WindowsImageService(CreateBufferedCallbacks());
                winREImageService.MountImage(
                    winREWimPath,
                    winREMountPath,
                    imageIndex: 1,
                    readOnly: mountedImage.IsReadOnly);

                var winRE = new MountedWindowsImage
                {
                    MountId = Guid.NewGuid().ToString(),
                    SourceImagePath = winREWimPath,
                    ImageIndex = 1,
                    ImageName = $"{mountedImage.ImageName} (WinRE)",
                    MountPath = new DirectoryInfo(winREMountPath),
                    WimGuid = mountedImage.WimGuid,
                    Status = MountStatus.Mounted,
                    IsReadOnly = mountedImage.IsReadOnly,
                    MountedAt = DateTime.UtcNow
                };

                MountSessionService.Register(winRE);
                mountedImage.WinRE = winRE;

                BufferVerbose($"[{currentIndex} of {totalCount}] - WinRE image mounted at {winREMountPath}");
            }
            catch (Exception ex)
            {
                BufferWarning($"[{currentIndex} of {totalCount}] - Failed to mount embedded WinRE image: {ex.Message}");
                mountedImage.WinRE = null;
            }
        }
    }
}
