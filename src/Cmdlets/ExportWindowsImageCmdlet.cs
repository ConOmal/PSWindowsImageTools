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
    /// Exports images from a WIM/ESD file to a new WIM file using the native WIM API
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImage")]
    [OutputType(typeof(WindowsImageExportResult))]
    public class ExportWindowsImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImage";

        /// <summary>
        /// Path to the source WIM/ESD file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            HelpMessage = "Path to the source WIM/ESD file")]
        [ValidateNotNullOrEmpty]
        public string SourcePath { get; set; } = null!;

        /// <summary>
        /// Path for the destination WIM file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Path for the destination WIM file")]
        [ValidateNotNullOrEmpty]
        public string DestinationPath { get; set; } = null!;

        /// <summary>
        /// Source image index to export (0 = export all images)
        /// </summary>
        [Parameter(HelpMessage = "Source image index to export (0 = export all images)")]
        [ValidateRange(0, int.MaxValue)]
        public int SourceIndex { get; set; } = 0;

        /// <summary>
        /// Source image name to export (overrides SourceIndex)
        /// </summary>
        [Parameter(HelpMessage = "Source image name to export (overrides SourceIndex)")]
        [ValidateNotNullOrEmpty]
        public string? SourceName { get; set; }

        /// <summary>
        /// Name to set on the exported image(s)
        /// </summary>
        [Parameter(HelpMessage = "Name to set on the exported image(s)")]
        [ValidateNotNullOrEmpty]
        public string? DestinationName { get; set; }

        /// <summary>
        /// Description to set on the exported image(s)
        /// </summary>
        [Parameter(HelpMessage = "Description to set on the exported image(s)")]
        [ValidateNotNullOrEmpty]
        public string? DestinationDescription { get; set; }

        /// <summary>
        /// Compression type for the destination WIM (None, Fast, Max, Recovery)
        /// </summary>
        [Parameter(HelpMessage = "Compression type for the destination WIM (None, Fast, Max, Recovery)")]
        [ValidateSet("None", "Fast", "Max", "Recovery")]
        public string CompressionType { get; set; } = "Max";

        /// <summary>
        /// Desired maximum size of each split part (in MB). If omitted, the export will be a single file.
        /// </summary>
        [Parameter(HelpMessage = "Maximum size of each split part in MB (optional)")]
        [ValidateRange(1, long.MaxValue)]
        public long? SplitSize { get; set; }


        /// <summary>
        /// Verify file integrity during export
        /// </summary>
        [Parameter(HelpMessage = "Verify file integrity during export")]
        public SwitchParameter CheckIntegrity { get; set; }

        /// <summary>
        /// Set the exported image(s) as bootable
        /// </summary>
        [Parameter(HelpMessage = "Set the exported image(s) as bootable")]
        public SwitchParameter SetBootable { get; set; }

        /// <summary>
        /// Overwrite the destination file if it exists
        /// </summary>
        [Parameter(HelpMessage = "Overwrite the destination file if it exists")]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Continue exporting remaining images when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue exporting remaining images when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            var startTime = DateTime.UtcNow;
            var results = new List<WindowsImageExportResult>();

            try
            {
                var resolvedSourcePath = GetUnresolvedProviderPathFromPSPath(SourcePath) ?? SourcePath;
                var resolvedDestinationPath = GetUnresolvedProviderPathFromPSPath(DestinationPath) ?? DestinationPath;

                if (!File.Exists(resolvedSourcePath))
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new FileNotFoundException($"Source image file not found: {resolvedSourcePath}"),
                        "SourceFileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedSourcePath));
                    return;
                }

                if (File.Exists(resolvedDestinationPath) && !Force.IsPresent)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new IOException($"Destination file already exists: {resolvedDestinationPath}. Use -Force to overwrite."),
                        "DestinationFileExists",
                        ErrorCategory.ResourceExists,
                        resolvedDestinationPath));
                    return;
                }

                // Determine which indices to export
                using var imageService = WindowsImageService.ForCmdlet(this);
                var images = imageService.GetImageInfo(resolvedSourcePath);

                var indices = new List<int>();

                if (!string.IsNullOrEmpty(SourceName))
                {
                    var matched = images.Find(i => string.Equals(i.Name, SourceName, StringComparison.OrdinalIgnoreCase));
                    if (matched == null)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException($"No image named '{SourceName}' found in {resolvedSourcePath}"),
                            "SourceNameNotFound",
                            ErrorCategory.ObjectNotFound,
                            SourceName));
                        return;
                    }

                    indices.Add(matched.Index);
                }
                else if (SourceIndex > 0)
                {
                    if (SourceIndex > images.Count)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException($"Source index {SourceIndex} is out of range; file contains {images.Count} images"),
                            "SourceIndexOutOfRange",
                            ErrorCategory.InvalidArgument,
                            SourceIndex));
                        return;
                    }

                    indices.Add(SourceIndex);
                }
                else
                {
                    // Export all images
                    indices.AddRange(images.Select(i => i.Index));
                }

                LoggingService.WriteProgress(this, "Exporting Windows Images",
                    $"Exporting {indices.Count} image(s) to {Path.GetFileName(resolvedDestinationPath)}",
                    $"Source: {resolvedSourcePath}", 0);

                using var wimExportService = new WimExportService();

                for (int i = 0; i < indices.Count; i++)
                {
                    var index = indices[i];
                    var percent = (int)((double)i / indices.Count * 100);

                    LoggingService.WriteProgress(this, "Exporting Windows Images",
                        $"[{i + 1} of {indices.Count}] - Exporting image {index}",
                        $"({percent}%)", percent);

                    var result = new WindowsImageExportResult
                    {
                        SourcePath = resolvedSourcePath,
                        DestinationPath = resolvedDestinationPath,
                        SourceIndex = index
                    };

                    try
                    {
                        var exportStartTime = DateTime.UtcNow;

                        var success = wimExportService.ExportImage(
                            sourceImagePath: resolvedSourcePath,
                            destinationImagePath: resolvedDestinationPath,
                            sourceIndex: (uint)index,
                            destinationName: DestinationName,
                            compressionType: CompressionType,
                            checkIntegrity: CheckIntegrity.IsPresent,
                            setBootable: SetBootable.IsPresent,
                            scratchDirectory: Path.GetTempPath(),
                            progressCallback: (percent, status) =>
                            {
                                LoggingService.WriteProgress(this, "Exporting Windows Images",
                                    $"[{i + 1} of {indices.Count}] - Exporting image {index}",
                                    $"{percent}%", percent);
                            },
                            cmdlet: this,
                            destinationDescription: DestinationDescription,
                            splitSize: SplitSize);

                        result.Success = success;
                        result.Duration = DateTime.UtcNow - exportStartTime;

                        if (!success && !ContinueOnError.IsPresent)
                        {
                            results.Add(result);
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(
                                new InvalidOperationException($"Failed to export image index {index}"),
                                "ExportFailed",
                                ErrorCategory.OperationStopped,
                                index));
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;
                        result.Duration = DateTime.UtcNow - startTime;

                        if (!ContinueOnError.IsPresent)
                        {
                            results.Add(result);
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "ExportFailed", ErrorCategory.OperationStopped, index));
                            return;
                        }

                        WriteWarning($"Failed to export image {index}: {ex.Message}");
                    }

                    results.Add(result);
                    WriteObject(result);
                }

                LoggingService.CompleteProgress(this, "Exporting Windows Images");

                var duration = DateTime.UtcNow - startTime;
                var successCount = results.Count(r => r.Success);                LoggingService.LogOperationComplete(this, ComponentName, duration,
                    $"Exported {successCount} of {indices.Count} images to {resolvedDestinationPath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogOperationFailure(this, ComponentName, ex);
                ThrowTerminatingError(new ErrorRecord(ex, "ExportWindowsImageFailed", ErrorCategory.OperationStopped, SourcePath));
            }
        }
    }
}
