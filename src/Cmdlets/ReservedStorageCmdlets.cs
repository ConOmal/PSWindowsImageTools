using System;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Reports the reserved-storage state of a mounted Windows image
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageReservedStorage")]
    [OutputType(typeof(WindowsImageReservedStorage))]
    public class GetWindowsImageReservedStorageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageReservedStorage";

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Path to the mounted Windows image directory")]
        [ValidateNotNullOrEmpty]
        public string ImagePath { get; set; } = string.Empty;

        protected override void EndProcessing()
        {
            var resolvedPath = GetUnresolvedProviderPathFromPSPath(ImagePath) ?? ImagePath;
            var service = new ReservedStorageService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                var result = service.GetState(resolvedPath, this);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to query reserved storage state for {resolvedPath}: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "GetReservedStorageStateFailed", ErrorCategory.OperationStopped, resolvedPath));
            }
        }
    }

    /// <summary>
    /// Enables or disables reserved storage in a mounted Windows image
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "WindowsImageReservedStorage", SupportsShouldProcess = true)]
    [OutputType(typeof(ReservedStorageOperationResult))]
    public class SetWindowsImageReservedStorageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Set-WindowsImageReservedStorage";

        [Parameter(Mandatory = true, Position = 0, HelpMessage = "Path to the mounted Windows image directory")]
        [ValidateNotNullOrEmpty]
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Enable reserved storage in the image
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "Enable", HelpMessage = "Enable reserved storage in the image")]
        public SwitchParameter Enable { get; set; }

        /// <summary>
        /// Disable reserved storage in the image
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "Disable", HelpMessage = "Disable reserved storage in the image")]
        public SwitchParameter Disable { get; set; }

        protected override void EndProcessing()
        {
            var resolvedPath = GetUnresolvedProviderPathFromPSPath(ImagePath) ?? ImagePath;

            if (!Enable.IsPresent && !Disable.IsPresent)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException("Specify either -Enable or -Disable to set the reserved storage state."),
                    "MissingReservedStorageState",
                    ErrorCategory.InvalidArgument,
                    resolvedPath));
                return;
            }

            var enable = Enable.IsPresent;
            var operationVerb = enable ? "Enable" : "Disable";

            if (!ShouldProcess($"{operationVerb} reserved storage on {resolvedPath}", "Set reserved storage state"))
            {
                return;
            }

            var service = new ReservedStorageService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                var result = service.SetState(resolvedPath, enable, this);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to set reserved storage state for {resolvedPath}: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "SetReservedStorageStateFailed", ErrorCategory.OperationStopped, resolvedPath));
            }
        }
    }
}