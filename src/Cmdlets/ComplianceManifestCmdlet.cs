using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Exports a compliance manifest: one JSON audit artifact combining an image snapshot's
    /// inventory summary, an optional security baseline evaluation and an optional servicing
    /// chain evaluation, plus tool provenance (tool version, timestamps, image identity).
    /// Read-only regarding images — no DISM, no mounting.
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImageComplianceManifest")]
    [OutputType(typeof(WindowsImageComplianceManifest))]
    public class ExportWindowsImageComplianceManifestCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImageComplianceManifest";

        /// <summary>
        /// Snapshot from Get-WindowsImageSnapshot
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Snapshot from Get-WindowsImageSnapshot")]
        [ValidateNotNull]
        public ImageSnapshot Snapshot { get; set; } = null!;

        /// <summary>
        /// Optional security baseline compliance report from Get-WindowsImageSecurityBaseline
        /// </summary>
        [Parameter(HelpMessage = "Optional security baseline compliance report from Get-WindowsImageSecurityBaseline")]
        [ValidateNotNull]
        public WindowsImageSecurityBaselineReport? BaselineReport { get; set; }

        /// <summary>
        /// Optional servicing chain report from Get-WindowsImageServicingChain
        /// </summary>
        [Parameter(HelpMessage = "Optional servicing chain report from Get-WindowsImageServicingChain")]
        [ValidateNotNull]
        public ServicingChainReport? ServicingChainReport { get; set; }

        /// <summary>
        /// Destination JSON file path for the compliance manifest
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination JSON file path for the compliance manifest")]
        [ValidateNotNullOrEmpty]
        public string DestinationPath { get; set; } = null!;

        /// <summary>
        /// Overwrite the destination file if it exists
        /// </summary>
        [Parameter(HelpMessage = "Overwrite the destination file if it exists")]
        public SwitchParameter Force { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                var resolvedPath = GetUnresolvedProviderPathFromPSPath(DestinationPath) ?? DestinationPath;

                if (File.Exists(resolvedPath) && !Force.IsPresent)
                {
                    WriteError(new ErrorRecord(
                        new InvalidOperationException($"File already exists: {resolvedPath}. Use -Force to overwrite."),
                        "FileExists",
                        ErrorCategory.ResourceExists,
                        resolvedPath));
                    return;
                }

                var directory = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    LoggingService.WriteVerbose(this, ComponentName, $"Created directory: {directory}");
                }

                LoggingService.WriteVerbose(this, ComponentName, $"Building compliance manifest for {Snapshot.ImageName}");

                var service = new ComplianceManifestService(ModuleCallbacks.FromCmdlet(this));
                var manifest = service.BuildManifest(Snapshot, BaselineReport, ServicingChainReport);
                ComplianceManifestService.SaveManifest(manifest, resolvedPath);

                LoggingService.WriteVerbose(this, ComponentName, $"Compliance manifest exported: {resolvedPath}");
                WriteObject(manifest);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "ExportComplianceManifestFailed", ErrorCategory.NotSpecified, Snapshot));
            }
        }
    }
}
