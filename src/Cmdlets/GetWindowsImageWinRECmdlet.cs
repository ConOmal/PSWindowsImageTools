using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Reports on the embedded WinRE image (Windows\System32\Recovery\Winre.wim) inside a mounted Windows image
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageWinRE")]
    [OutputType(typeof(WinREIntelligenceReport))]
    public class GetWindowsImageWinRECmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageWinRE";

        /// <summary>
        /// Path to the mounted Windows image directory to inspect
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Path to the mounted Windows image directory to inspect for an embedded WinRE image")]
        [ValidateNotNull]
        public DirectoryInfo ImagePath { get; set; } = null!;

        /// <summary>
        /// Also read the embedded WinRE WIM's XML metadata for a best-effort first-image display name
        /// </summary>
        [Parameter(
            Mandatory = false,
            HelpMessage = "Also read the embedded WinRE WIM's XML metadata for a best-effort first-image display name")]
        public SwitchParameter Detailed { get; set; }

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            if (ImagePath == null || !ImagePath.Exists)
            {
                var errorMessage = $"Image path does not exist: {ImagePath?.FullName}";
                var errorRecord = new ErrorRecord(
                    new DirectoryNotFoundException(errorMessage),
                    "ImagePathNotFound",
                    ErrorCategory.ObjectNotFound,
                    ImagePath);
                WriteError(errorRecord);
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(
                this,
                ComponentName,
                "Inspect embedded WinRE image",
                $"Image path: {ImagePath.FullName} (Detailed: {Detailed.IsPresent})");

            try
            {
                var service = new WinREIntelligenceService(ModuleCallbacks.FromCmdlet(this));
                var report = service.Inspect(ImagePath.FullName, Detailed.IsPresent);

                WriteObject(report);

                LoggingService.LogOperationCompleteWithTimestamp(
                    this,
                    ComponentName,
                    "Embedded WinRE inspection",
                    operationStartTime,
                    report.ToString());
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to inspect embedded WinRE image: {ex.Message}", ex);
                throw;
            }
        }
    }
}