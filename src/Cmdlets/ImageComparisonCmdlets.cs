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
    /// Captures an inventory snapshot of a mounted Windows image (packages, features,
    /// capabilities, provisioned AppX, installed software). Snapshots can be exported to JSON
    /// for later comparison with Compare-WindowsImage.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageSnapshot")]
    [OutputType(typeof(ImageSnapshot[]))]
    public class GetWindowsImageSnapshotCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageSnapshot";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to snapshot
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to snapshot")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Optional directory to export snapshots as JSON files
        /// </summary>
        [Parameter(HelpMessage = "Optional directory to export snapshots as JSON files")]
        [ValidateNotNullOrEmpty]
        public string? ExportPath { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            string? resolvedExportPath = null;

            if (ExportPath != null)
            {
                resolvedExportPath = GetUnresolvedProviderPathFromPSPath(ExportPath) ?? ExportPath;
                if (!Directory.Exists(resolvedExportPath))
                {
                    Directory.CreateDirectory(resolvedExportPath);
                }
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var comparisonService = new ImageComparisonService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                try
                {
                    var snapshot = comparisonService.CaptureSnapshot(mountedImage, imageService);

                    if (resolvedExportPath != null)
                    {
                        var fileName = $"snapshot_{SanitizeFileName(mountedImage.ImageName)}_{snapshot.CapturedAt:yyyyMMdd_HHmmss}.json";
                        var snapshotFile = Path.Combine(resolvedExportPath, fileName);
                        ImageComparisonService.SaveSnapshot(snapshot, snapshotFile);
                        LoggingService.WriteVerbose(this, ComponentName, $"Snapshot exported: {snapshotFile}");
                    }

                    WriteObject(snapshot);
                }
                catch (Exception ex)
                {
                    WriteWarning($"Failed to snapshot {mountedImage.ImageName}: {ex.Message}");
                }
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }

    /// <summary>
    /// Compares two Windows image snapshots to surface what changed. Accepts two mounted images,
    /// two snapshot JSON files, or a mix of both.
    /// </summary>
    [Cmdlet(VerbsData.Compare, "WindowsImage")]
    [OutputType(typeof(ImageComparisonResult))]
    public class CompareWindowsImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Compare-WindowsImage";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to compare (exactly two required; first = reference/before,
        /// second = difference/after)
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ByMountedImages",
            ValueFromPipeline = true,
            HelpMessage = "Two mounted images: first is the reference (before), second the difference (after)")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Reference (before) snapshot JSON file
        /// </summary>
        [Parameter(
            Mandatory = true,
            ParameterSetName = "BySnapshotFiles",
            HelpMessage = "Reference (before) snapshot JSON file")]
        [ValidateNotNullOrEmpty]
        public string ReferencePath { get; set; } = null!;

        /// <summary>
        /// Difference (after) snapshot JSON file
        /// </summary>
        [Parameter(
            Mandatory = true,
            ParameterSetName = "BySnapshotFiles",
            Position = 1,
            HelpMessage = "Difference (after) snapshot JSON file")]
        [ValidateNotNullOrEmpty]
        public string DifferencePath { get; set; } = null!;

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            var comparisonService = new ImageComparisonService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                ImageSnapshot reference;
                ImageSnapshot difference;

                if (ParameterSetName == "BySnapshotFiles")
                {
                    var resolvedReferencePath = GetUnresolvedProviderPathFromPSPath(ReferencePath) ?? ReferencePath;
                    var resolvedDifferencePath = GetUnresolvedProviderPathFromPSPath(DifferencePath) ?? DifferencePath;

                    reference = ImageComparisonService.LoadSnapshot(resolvedReferencePath);
                    difference = ImageComparisonService.LoadSnapshot(resolvedDifferencePath);
                }
                else
                {
                    if (_allMountedImages.Count < 2)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException(
                                "Compare-WindowsImage requires exactly two mounted images. " +
                                "Capture snapshots with Get-WindowsImageSnapshot for point-in-time comparisons."),
                            "InsufficientImages",
                            ErrorCategory.InvalidArgument,
                            _allMountedImages.Count));
                        return;
                    }

                    if (_allMountedImages.Count > 2)
                    {
                        WriteWarning($"{_allMountedImages.Count} mounted images received; using the first two");
                    }

                    var first = _allMountedImages[0];
                    var second = _allMountedImages[1];

                    if (first.MountPath == null || second.MountPath == null)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException("Mount path is null for one of the images"),
                            "NullMountPath",
                            ErrorCategory.InvalidArgument,
                            null));
                        return;
                    }

                    LoggingService.WriteProgress(this, "Comparing Windows Images",
                        "Capturing snapshots",
                        $"{first.ImageName} (reference) vs {second.ImageName} (difference)", 0);

                    reference = comparisonService.CaptureSnapshot(first, imageService);
                    difference = comparisonService.CaptureSnapshot(second, imageService);
                }

                var result = comparisonService.Compare(reference, difference);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "CompareWindowsImageFailed", ErrorCategory.OperationStopped, ComponentName));
            }
        }
    }
}
