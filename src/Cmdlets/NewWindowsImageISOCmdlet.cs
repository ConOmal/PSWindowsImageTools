using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Boot mode for ISO creation
    /// </summary>
    public enum ISOBootMode
    {
        /// <summary>
        /// UEFI boot only (EFI boot image)
        /// </summary>
        UEFI,

        /// <summary>
        /// BIOS boot only (El Torito boot image)
        /// </summary>
        BIOS,

        /// <summary>
        /// Both UEFI and BIOS boot
        /// </summary>
        Both
    }

    /// <summary>
    /// Creates a bootable ISO from a Windows setup folder using oscdimg (Windows ADK)
    /// </summary>
    [Cmdlet(VerbsCommon.New, "WindowsImageISO")]
    [OutputType(typeof(ISOCreationResult))]
    public class NewWindowsImageISOCmdlet : PSCmdlet
    {
        private const string ComponentName = "New-WindowsImageISO";

        /// <summary>
        /// Path to the Windows setup folder (containing boot/, efi/, sources/)
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            HelpMessage = "Path to the Windows setup folder (containing boot/, efi/, sources/)")]
        [ValidateNotNullOrEmpty]
        public string SourcePath { get; set; } = null!;

        /// <summary>
        /// Path for the output ISO file
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Path for the output ISO file")]
        [ValidateNotNullOrEmpty]
        public string OutputIsoPath { get; set; } = null!;

        /// <summary>
        /// Volume label for the ISO
        /// </summary>
        [Parameter(HelpMessage = "Volume label for the ISO")]
        [ValidateNotNullOrEmpty]
        public string VolumeLabel { get; set; } = "Windows";

        /// <summary>
        /// Boot mode: UEFI, BIOS, or Both
        /// </summary>
        [Parameter(HelpMessage = "Boot mode: UEFI, BIOS, or Both")]
        [ValidateSet("UEFI", "BIOS", "Both")]
        public string BootMode { get; set; } = "Both";

        /// <summary>
        /// Overwrite the output ISO if it exists
        /// </summary>
        [Parameter(HelpMessage = "Overwrite the output ISO if it exists")]
        public SwitchParameter Force { get; set; }

        protected override void ProcessRecord()
        {
            var startTime = DateTime.UtcNow;

            try
            {
                var resolvedSourcePath = GetUnresolvedProviderPathFromPSPath(SourcePath) ?? SourcePath;
                var resolvedOutputPath = GetUnresolvedProviderPathFromPSPath(OutputIsoPath) ?? OutputIsoPath;

                if (!Directory.Exists(resolvedSourcePath))
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new DirectoryNotFoundException($"Source folder not found: {resolvedSourcePath}"),
                        "SourceFolderNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedSourcePath));
                    return;
                }

                if (File.Exists(resolvedOutputPath) && !Force.IsPresent)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new IOException($"Output ISO already exists: {resolvedOutputPath}. Use -Force to overwrite."),
                        "OutputFileExists",
                        ErrorCategory.ResourceExists,
                        resolvedOutputPath));
                    return;
                }

                var parsedBootMode = BootMode switch
                {
                    "UEFI" => global::PSWindowsImageTools.Services.BootMode.UEFI,
                    "BIOS" => global::PSWindowsImageTools.Services.BootMode.BIOS,
                    _ => global::PSWindowsImageTools.Services.BootMode.Both
                };

                var result = new ISOCreationResult
                {
                    SourcePath = resolvedSourcePath,
                    OutputIsoPath = resolvedOutputPath,
                    VolumeLabel = VolumeLabel,
                    BootMode = BootMode
                };

                LoggingService.WriteProgress(this, "Creating Windows ISO",
                    $"Creating bootable ISO ({BootMode})",
                    $"Source: {resolvedSourcePath}", 0);

                using var isoService = new ISOService();
                var success = isoService.CreateBootableISO(
                    resolvedSourcePath,
                    resolvedOutputPath,
                    VolumeLabel,
                    parsedBootMode,
                    progressCallback: (percent, status) =>
                    {
                        LoggingService.WriteProgress(this, "Creating Windows ISO", status, $"{percent}%", percent);
                    },
                    cmdlet: this);

                result.Success = success;
                result.Duration = DateTime.UtcNow - startTime;

                if (success && File.Exists(resolvedOutputPath))
                {
                    result.OutputSize = new FileInfo(resolvedOutputPath).Length;
                }

                LoggingService.CompleteProgress(this, "Creating Windows ISO");

                if (success)
                {
                    LoggingService.LogOperationComplete(this, ComponentName, result.Duration,
                        $"ISO created: {resolvedOutputPath} ({result.OutputSize / 1024 / 1024} MB)");
                }
                else
                {
                    result.ErrorMessage = "ISO creation failed. Install the Windows ADK (Install-ADK -IncludeDeploymentTools) so oscdimg is available.";
                    LoggingService.WriteWarning(this, ComponentName, result.ErrorMessage);
                }

                WriteObject(result);
            }
            catch (Exception ex)
            {
                LoggingService.LogOperationFailure(this, ComponentName, ex);
                ThrowTerminatingError(new ErrorRecord(ex, "ISOCreationFailed", ErrorCategory.OperationStopped, SourcePath));
            }
        }
    }
}
