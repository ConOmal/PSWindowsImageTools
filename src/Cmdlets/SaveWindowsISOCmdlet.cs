using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Downloads a Windows ISO, resolved via Get-WindowsISODownloadInfo or supplied directly with -Url
    /// </summary>
    [Cmdlet(VerbsData.Save, "WindowsISO")]
    [OutputType(typeof(WindowsISOFile))]
    public class SaveWindowsISOCmdlet : PSCmdlet
    {
        /// <summary>
        /// Download info from Get-WindowsISODownloadInfo
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "FromDownloadInfo")]
        [ValidateNotNull]
        public WindowsISODownloadInfo InputObject { get; set; } = null!;

        /// <summary>
        /// A manually obtained ISO URL, used as a bypass if Get-WindowsISODownloadInfo's automated flow fails
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "FromUrl")]
        [ValidateNotNull]
        public Uri Url { get; set; } = null!;

        /// <summary>
        /// Local path to save the ISO to
        /// </summary>
        [Parameter(Mandatory = true, Position = 1)]
        [ValidateNotNull]
        public FileInfo DestinationPath { get; set; } = null!;

        /// <summary>
        /// Re-download even if the destination file already exists
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Resume an existing partial download
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Resume { get; set; }

        /// <summary>
        /// Calculate a SHA256 hash of the downloaded file after completion
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Verify { get; set; }

        private const string ComponentName = "SaveWindowsISO";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            var isFromUrl = ParameterSetName == "FromUrl";
            var downloadUrl = isFromUrl ? Url : InputObject.Url;
            var sourceInfo = isFromUrl ? null : InputObject;

            if (DestinationPath.Exists && !Force.IsPresent && !Resume.IsPresent)
            {
                LoggingService.WriteVerbose(this, ComponentName, $"File already exists, skipping: {DestinationPath.FullName}");
                WriteObject(BuildResult(sourceInfo, downloadUrl, success: true));
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Download Windows ISO", $"{downloadUrl} -> {DestinationPath.FullName}");

            try
            {
                var progressCallback = ProgressService.CreateDownloadProgressCallback(
                    this, "Downloading Windows ISO", DestinationPath.Name, 1, 1);

                var success = NetworkService.DownloadFileWithResume(downloadUrl.OriginalString, DestinationPath.FullName, Resume.IsPresent, this, progressCallback);

                var result = BuildResult(sourceInfo, downloadUrl, success);

                if (!success)
                {
                    result.ErrorMessage = $"Failed to download ISO from {downloadUrl}";
                    ThrowTerminatingError(new ErrorRecord(
                        new InvalidOperationException(result.ErrorMessage),
                        "SaveWindowsISOFailed",
                        ErrorCategory.NotSpecified,
                        downloadUrl));
                    return;
                }

                if (Verify.IsPresent)
                {
                    result.Hash = NetworkService.CalculateFileHash(DestinationPath.FullName);
                    result.IsVerified = true;
                    LoggingService.WriteVerbose(this, ComponentName, $"Verified {DestinationPath.Name}: SHA256 = {result.Hash}");
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Download Windows ISO", operationStartTime,
                    $"Downloaded {DestinationPath.FullName}");

                WriteObject(result);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to download ISO: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "SaveWindowsISOFailed", ErrorCategory.NotSpecified, downloadUrl));
            }
        }

        /// <summary>
        /// Builds a WindowsISOFile result reflecting the current state of DestinationPath
        /// </summary>
        private WindowsISOFile BuildResult(WindowsISODownloadInfo? sourceInfo, Uri downloadUrl, bool success)
        {
            DestinationPath.Refresh();

            return new WindowsISOFile
            {
                LocalFile = DestinationPath,
                IsDownloaded = success && DestinationPath.Exists,
                FileSize = DestinationPath.Exists ? DestinationPath.Length : 0,
                DownloadedAt = DestinationPath.Exists ? DestinationPath.LastWriteTimeUtc : DateTime.MinValue,
                DownloadUrl = downloadUrl.OriginalString,
                SourceDownloadInfo = sourceInfo
            };
        }
    }
}
