using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Extracts a Windows ISO's contents to a working folder, ready for Get-WindowsImageList/New-WindowsImageISO
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsISO")]
    [OutputType(typeof(WindowsInstallationMedia))]
    public class ExportWindowsISOCmdlet : PSCmdlet
    {
        /// <summary>
        /// Path to the Windows ISO file
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, HelpMessage = "Path to the Windows ISO file")]
        [ValidateNotNull]
        public FileInfo IsoPath { get; set; } = null!;

        /// <summary>
        /// Destination folder to extract the ISO contents to
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination folder to extract the ISO contents to")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        private const string ComponentName = "ExportWindowsISO";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!IsoPath.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"ISO file not found: {IsoPath.FullName}", IsoPath.FullName),
                    "IsoFileNotFound",
                    ErrorCategory.ObjectNotFound,
                    IsoPath.FullName));
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Export Windows ISO", $"{IsoPath.FullName} -> {DestinationPath.FullName}");

            try
            {
                var progressCallback = ProgressService.CreateProgressCallback(
                    this, "Extracting Windows ISO", IsoPath.Name, 1, 1);

                var extractionService = new WindowsISOExtractionService();
                var media = extractionService.ExtractIso(IsoPath, DestinationPath, this, progressCallback);

                LoggingService.CompleteProgress(this, "Extracting Windows ISO");

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Export Windows ISO", operationStartTime,
                    $"Extracted to {DestinationPath.FullName}");

                WriteObject(media);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to export ISO: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "ExportWindowsISOFailed", ErrorCategory.NotSpecified, IsoPath.FullName));
            }
        }
    }
}
