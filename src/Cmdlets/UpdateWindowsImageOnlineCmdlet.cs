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
    /// One-liner update servicing: discovers the latest cumulative update for a Windows release,
    /// downloads it, and installs it into the matching images of a WIM/ESD file. Accepts
    /// pre-downloaded packages via -UpdatePackages to skip the catalog step.
    /// </summary>
    [Cmdlet(VerbsData.Update, "WindowsImageOnline")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class UpdateWindowsImageOnlineCmdlet : PSCmdlet
    {
        private const string ComponentName = "Update-WindowsImageOnline";
        private const string ActivityName = "Updating Windows Images";
        private readonly List<WindowsUpdatePackage> _allUpdatePackages = new List<WindowsUpdatePackage>();

        /// <summary>
        /// Path to the WIM/ESD file to service
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            HelpMessage = "Path to the WIM/ESD file to service")]
        [ValidateNotNullOrEmpty]
        public string ImagePath { get; set; } = null!;

        /// <summary>
        /// Pre-downloaded update packages (skips catalog search/download)
        /// </summary>
        [Parameter(
            Position = 1,
            ValueFromPipeline = true,
            ParameterSetName = "ByPackages",
            HelpMessage = "Pre-downloaded update packages from Save-WindowsUpdateCatalogResult")]
        [ValidateNotNull]
        public WindowsUpdatePackage[]? UpdatePackages { get; set; }

        /// <summary>
        /// Catalog search query (overrides the automatic latest-KB discovery)
        /// </summary>
        [Parameter(
            Mandatory = true,
            ParameterSetName = "ByQuery",
            HelpMessage = "Catalog search query (overrides automatic latest-KB discovery)")]
        [ValidateNotNullOrEmpty]
        public string Query { get; set; } = null!;

        /// <summary>
        /// Operating system for automatic latest-KB discovery (default: Windows 11)
        /// </summary>
        [Parameter(HelpMessage = "Operating system for automatic latest-KB discovery (default: Windows 11)")]
        [ValidateNotNullOrEmpty]
        public string OperatingSystem { get; set; } = "Windows 11";

        /// <summary>
        /// Architecture filter for catalog search (default: x64)
        /// </summary>
        [Parameter(HelpMessage = "Architecture filter for catalog search (default: x64)")]
        [ValidateSet("x64", "x86", "arm64")]
        public string Architecture { get; set; } = "x64";

        /// <summary>
        /// Directory for downloaded updates (default: temp subfolder)
        /// </summary>
        [Parameter(HelpMessage = "Directory for downloaded updates")]
        [ValidateNotNullOrEmpty]
        public string? DestinationPath { get; set; }

        /// <summary>
        /// Base directory for mounting (uses the module default when omitted)
        /// </summary>
        [Parameter(HelpMessage = "Base directory for mounting")]
        [ValidateNotNullOrEmpty]
        public string? MountPath { get; set; }

        /// <summary>
        /// Maximum number of images to service (safety limit)
        /// </summary>
        [Parameter(HelpMessage = "Maximum number of images to service")]
        [ValidateRange(1, int.MaxValue)]
        public int MaxImages { get; set; } = 5;

        /// <summary>
        /// Maximum number of updates to install per image
        /// </summary>
        [Parameter(HelpMessage = "Maximum number of updates to install per image")]
        [ValidateRange(1, int.MaxValue)]
        public int MaxUpdates { get; set; } = 10;

        /// <summary>
        /// Continue servicing remaining images when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue servicing remaining images when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            if (UpdatePackages != null)
            {
                _allUpdatePackages.AddRange(UpdatePackages);
            }
        }

        protected override void EndProcessing()
        {
            var startTime = DateTime.UtcNow;

            try
            {
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

                // Resolve update files
                var updateFiles = ResolveUpdateFiles(resolvedImagePath);
                if (updateFiles.Count == 0)
                {
                    WriteWarning("No update packages available; nothing to install");
                    return;
                }

                if (updateFiles.Count > MaxUpdates)
                {
                    WriteWarning($"{updateFiles.Count} updates available; limiting to MaxUpdates = {MaxUpdates}");
                    updateFiles = updateFiles.Take(MaxUpdates).ToList();
                }

                LoggingService.WriteVerbose(this, ComponentName,
                    $"Installing {updateFiles.Count} update package(s): {string.Join(", ", updateFiles.Select(Path.GetFileName))}");

                // Select images
                using var imageService = WindowsImageService.ForCmdlet(this);
                var images = imageService.GetImageInfo(resolvedImagePath);
                var selected = images.Take(MaxImages).ToList();

                LoggingService.WriteProgress(this, ActivityName,
                    $"Servicing {selected.Count} image(s) with {updateFiles.Count} update(s)",
                    $"Image file: {Path.GetFileName(resolvedImagePath)}", 0);

                // Service each image
                var wimGuid = Guid.NewGuid().ToString("N");
                var results = new List<ImageOperationResult>();

                for (int i = 0; i < selected.Count; i++)
                {
                    var image = selected[i];
                    var imagePercent = (int)((double)i / selected.Count * 100);
                    var imageMountPath = ConfigurationService.CreateUniqueMountDirectory(mountRoot, image.Index, wimGuid);

                    LoggingService.WriteProgress(this, ActivityName,
                        $"[{i + 1} of {selected.Count}] - {image.Name}",
                        $"Mounting for update servicing ({imagePercent}%)", imagePercent);

                    try
                    {
                        if (!Directory.Exists(imageMountPath))
                        {
                            Directory.CreateDirectory(imageMountPath);
                        }

                        // Mount read-write (throws on failure)
                        imageService.MountImage(resolvedImagePath, imageMountPath, (uint)image.Index, readOnly: false);

                        var imageFailed = false;

                        // Install each update
                        for (int u = 0; u < updateFiles.Count; u++)
                        {
                            var updateFile = updateFiles[u];
                            var result = new ImageOperationResult
                            {
                                ImageName = image.Name,
                                ImageIndex = image.Index,
                                MountPath = imageMountPath,
                                Target = updateFile,
                                Operation = "InstallUpdate"
                            };

                            var updatePercent = imagePercent + (int)((double)u / updateFiles.Count * (100.0 / selected.Count));

                            LoggingService.WriteProgress(this, ActivityName,
                                $"[{i + 1} of {selected.Count}] - {image.Name}",
                                $"Installing {Path.GetFileName(updateFile)} ({u + 1} of {updateFiles.Count}, {updatePercent}%)", updatePercent);

                            try
                            {
                                imageService.AddPackage(imageMountPath, updateFile);
                                result.Success = true;
                                LoggingService.WriteVerbose(this, ComponentName,
                                    $"[{i + 1} of {selected.Count}] Installed {Path.GetFileName(updateFile)} on [{image.Index}] {image.Name}");
                            }
                            catch (Exception ex)
                            {
                                result.Success = false;
                                result.ErrorMessage = ex.Message;
                                imageFailed = true;

                                LoggingService.WriteWarning(this,
                                    $"[{i + 1} of {selected.Count}] Failed to install {Path.GetFileName(updateFile)} on {image.Name}: {ex.Message}");
                            }

                            results.Add(result);
                            WriteObject(result);
                        }

                        // Unmount and save (discards a failed image set only when -ContinueOnError)
                        if (!imageFailed || ContinueOnError.IsPresent)
                        {
                            imageService.UnmountImage(imageMountPath, commitChanges: true);
                            TryCleanupMountDirectory(imageMountPath);
                        }
                        else if (!ContinueOnError.IsPresent)
                        {
                            ThrowTerminatingError(new ErrorRecord(
                                new InvalidOperationException($"Update installation failed on [{image.Index}] {image.Name}"),
                                "UpdateInstallFailed",
                                ErrorCategory.OperationStopped,
                                image.Name));
                            return;
                        }
                    }
                    catch (Exception ex) when (ContinueOnError.IsPresent)
                    {
                        WriteWarning($"Failed to service [{image.Index}] {image.Name}: {ex.Message}");
                        TryUnmountDiscard(imageService, imageMountPath);
                    }
                }

                LoggingService.CompleteProgress(this, ActivityName);

                var duration = DateTime.UtcNow - startTime;
                var successCount = results.Count(r => r.Success);
                LoggingService.LogOperationComplete(this, ComponentName, duration,
                    $"Installed {successCount} of {results.Count} update operations across {selected.Count} images");
            }
            catch (Exception ex)
            {
                LoggingService.LogOperationFailure(this, ComponentName, ex);
                ThrowTerminatingError(new ErrorRecord(ex, "UpdateWindowsImageOnlineFailed", ErrorCategory.OperationStopped, ImagePath));
            }
        }

        /// <summary>
        /// Resolves update file paths from packages, query mode, or automatic latest-KB discovery
        /// </summary>
        private List<string> ResolveUpdateFiles(string resolvedImagePath)
        {
            // Mode 1: pre-downloaded packages
            if (_allUpdatePackages.Count > 0)
            {
                return _allUpdatePackages
                    .Where(p => p.IsDownloaded && p.LocalFile != null && p.LocalFile.Exists)
                    .Select(p => p.LocalFile.FullName)
                    .ToList();
            }

            // Mode 2: explicit query
            var query = Query;
            var destination = DestinationPath;

            // Mode 3: automatic latest-KB discovery
            if (string.IsNullOrWhiteSpace(query))
            {
                LoggingService.WriteVerbose(this, ComponentName, $"Discovering latest KB for {OperatingSystem}");
                query = DiscoverLatestKB();
                if (string.IsNullOrWhiteSpace(query))
                {
                    WriteWarning($"Could not discover latest KB article for {OperatingSystem}; use -Query to specify one");
                    return new List<string>();
                }

                LoggingService.WriteVerbose(this, ComponentName, $"Latest KB: {query}");
            }

            if (string.IsNullOrEmpty(destination))
            {
                destination = Path.Combine(Path.GetTempPath(), "PSWindowsImageTools", "OnlineUpdates");
            }

            var resolvedDestination = GetUnresolvedProviderPathFromPSPath(destination) ?? destination;
            if (!Directory.Exists(resolvedDestination))
            {
                Directory.CreateDirectory(resolvedDestination);
            }

            // Search the catalog
            using var catalogService = new WindowsUpdateCatalogService();
            var criteria = new WindowsUpdateSearchCriteria
            {
                Query = query!,
                Architecture = Architecture,
                MaxResults = 10,
                PageSize = 10,
                IncludeSuperseded = false
            };

            LoggingService.WriteProgress(this, ActivityName,
                "Searching Microsoft Update Catalog",
                $"Query: {query} ({Architecture})", 0);

            var searchResult = catalogService.SearchUpdates(criteria, includeDownloadUrls: true, cmdlet: this);
            if (!searchResult.Success || searchResult.Updates.Count == 0)
            {
                WriteWarning($"Catalog search returned no results for '{query}': {searchResult.ErrorMessage ?? "no updates found"}");
                return new List<string>();
            }

            // Download each update
            var downloaded = new List<string>();
            foreach (var update in searchResult.Updates)
            {
                var downloadUrl = update.DownloadUrls.FirstOrDefault();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    continue;
                }

                var fileName = NetworkService.GetSuggestedFilename(downloadUrl) ?? $"{update.KBNumber}_{update.UpdateId}.msu";
                var destinationFile = Path.Combine(resolvedDestination, fileName);

                if (File.Exists(destinationFile))
                {
                    LoggingService.WriteVerbose(this, ComponentName, $"Update already downloaded: {destinationFile}");
                    downloaded.Add(destinationFile);
                    continue;
                }

                LoggingService.WriteProgress(this, ActivityName,
                    "Downloading updates from Microsoft Update Catalog",
                    $"{update.Title}", -1);

                if (NetworkService.DownloadFile(downloadUrl, destinationFile, this))
                {
                    downloaded.Add(destinationFile);
                }
                else
                {
                    WriteWarning($"Failed to download {update.Title}");
                }
            }

            return downloaded;
        }

        /// <summary>
        /// Discovers the latest KB article for the configured operating system
        /// </summary>
        private string? DiscoverLatestKB()
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                var releaseService = new WindowsReleaseHistoryService(httpClient, this, ContinueOnError.IsPresent);
                var history = releaseService.GetWindowsReleaseHistory();

                var release = history.FirstOrDefault(r =>
                    FormatUtilityService.ContainsIgnoreCase(r.OperatingSystem, OperatingSystem));

                return release?.LatestRelease?.KBArticle;
            }
            catch (Exception ex)
            {
                WriteWarning($"Failed to discover latest KB: {ex.Message}");
                return null;
            }
        }

        private void TryCleanupMountDirectory(string mountPath)
        {
            try
            {
                if (Directory.Exists(mountPath))
                {
                    Directory.Delete(mountPath, true);
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ComponentName, $"Failed to clean up mount directory {mountPath}: {ex.Message}");
            }
        }

        private void TryUnmountDiscard(WindowsImageService imageService, string mountPath)
        {
            try
            {
                if (Directory.Exists(mountPath))
                {
                    imageService.UnmountImage(mountPath, commitChanges: false);
                    TryCleanupMountDirectory(mountPath);
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, ComponentName, $"Failed to discard image after failure: {ex.Message}");
            }
        }
    }
}
