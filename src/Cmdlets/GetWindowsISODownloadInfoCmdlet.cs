using System;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Resolves a time-limited direct download URL for the latest official Windows 11 ISO
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsISODownloadInfo")]
    [OutputType(typeof(WindowsISODownloadInfo))]
    public class GetWindowsISODownloadInfoCmdlet : PSCmdlet
    {
        /// <summary>
        /// Windows edition to resolve (only "Windows 11" is currently supported, matching Microsoft's public download page)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string Edition { get; set; } = "Windows 11";

        /// <summary>
        /// Target architecture
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("x64", "arm64")]
        public string Architecture { get; set; } = "x64";

        /// <summary>
        /// Language SKU, as labeled on Microsoft's download page (e.g. "English International")
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string Language { get; set; } = "English International";

        private const string ComponentName = "GetWindowsISODownloadInfo";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Resolve Windows ISO download link", $"{Edition} {Architecture} ({Language})");

            try
            {
                using var downloadService = new WindowsISODownloadService();
                var info = downloadService.GetDownloadInfo(Edition, Architecture, Language, this);

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Resolve Windows ISO download link", operationStartTime,
                    $"Resolved: {info.FileName}");

                WriteObject(info);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to resolve Windows ISO download link: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "GetWindowsISODownloadInfoFailed", ErrorCategory.NotSpecified, null));
            }
        }
    }
}
