using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Changes the edition of a mounted (offline) Windows image via DISM edition servicing
    /// (the API equivalent of `DISM /Image:&lt;path&gt; /Set-Edition:&lt;edition&gt; [/ProductKey:&lt;key&gt;]`
    /// and `/Set-Edition:ServerEdition` for server SKUs.)
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "WindowsImageEdition", SupportsShouldProcess = true)]
    [OutputType(typeof(WindowsImageEditionResult))]
    public class SetWindowsImageEditionCmdlet : PSCmdlet
    {
        private const string ComponentName = "Set-WindowsImageEdition";

        /// <summary>
        /// Mounted (offline) image directory whose edition will change
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "Edition",
            ValueFromPipeline = true,
            HelpMessage = "Mounted image directory whose edition will change")]
        [Parameter(
            Mandatory = true,
            Position = 0,
            ParameterSetName = "ServerEdition",
            ValueFromPipeline = true,
            HelpMessage = "Mounted image directory whose edition will change")]
        [ValidateNotNull]
        public DirectoryInfo ImagePath { get; set; } = null!;

        /// <summary>
        /// Target edition name (e.g. "Professional", "Enterprise")
        /// </summary>
        [Parameter(
            Mandatory = true,
            ParameterSetName = "Edition",
            HelpMessage = "Target edition name (e.g. 'Professional', 'Enterprise')")]
        [ValidateNotNullOrEmpty]
        public string Edition { get; set; } = string.Empty;

        /// <summary>
        /// Optional product key for the target edition (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)
        /// </summary>
        [Parameter(
            Mandatory = false,
            ParameterSetName = "Edition",
            HelpMessage = "Product key for the target edition (XXXXX-XXXXX-XXXXX-XXXXX-XXXXX)")]
        public string ProductKey { get; set; } = string.Empty;

        /// <summary>
        /// Use the server SKU path (DISM /Set-Edition:ServerEdition). Mutually exclusive with -Edition/-ProductKey.
        /// </summary>
        [Parameter(
            Mandatory = true,
            ParameterSetName = "ServerEdition",
            HelpMessage = "Use the server SKU edition-change path (Set-Edition:ServerEdition)")]
        public SwitchParameter ServerEdition { get; set; }

        /// <summary>
        /// Emit the WindowsImageEditionResult object (before/after editions, status)
        /// </summary>
        [Parameter(
            Mandatory = false,
            HelpMessage = "Emit the WindowsImageEditionResult object (before/after editions, status)")]
        public SwitchParameter PassThru { get; set; }

        /// <summary>
        /// Changes the edition of the mounted image
        /// </summary>
        protected override void EndProcessing()
        {
            var mountPath = ImagePath.FullName;

            if (!ImagePath.Exists)
            {
                var errorMessage = $"Image path does not exist: {mountPath}";
                LoggingService.WriteError(this, ComponentName, errorMessage);
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException(errorMessage),
                    "ImagePathNotFound",
                    ErrorCategory.ObjectNotFound,
                    ImagePath.FullName));
                return;
            }

            string editionId;
            try
            {
                WindowsImageEditionService.ValidateEditionParameters(Edition, ProductKey, ServerEdition.IsPresent);
                editionId = WindowsImageEditionService.ResolveEditionId(Edition, ServerEdition.IsPresent);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(
                    ex,
                    "InvalidEditionParameters",
                    ErrorCategory.InvalidArgument,
                    Edition));
                return;
            }

            WindowsImageEditionResult? result = null;
            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, "Set Windows Image Edition",
                $"{mountPath} to '{editionId}'");

            try
            {
                using var imageService = WindowsImageService.ForCmdlet(this);
                imageService.Initialize();

                var editionService = new WindowsImageEditionService(ModuleCallbacks.FromCmdlet(this));

                // Read the current edition before ShouldProcess so the confirmation/WhatIf message
                // can show the real before -> after change without mutating anything.
                string? currentEdition = null;
                try
                {
                    currentEdition = editionService.GetCurrentEdition(mountPath);
                }
                catch (Exception readEx)
                {
                    LoggingService.WriteWarning(this, ComponentName,
                        $"Could not read the current edition of {mountPath}: {readEx.Message}");
                }

                var action = currentEdition != null
                    ? $"change image edition from '{currentEdition}' to '{editionId}'"
                    : $"change image edition to '{editionId}'";

                if (!ShouldProcess(mountPath, action))
                {
                    LoggingService.WriteVerbose(this, ComponentName, $"Edition change declined for {mountPath}: {action}");

                    if (PassThru.IsPresent)
                    {
                        result = WindowsImageEditionService.BuildResult(
                            ImagePath, editionId, ServerEdition.IsPresent, ProductKey,
                            currentEdition ?? string.Empty, afterEdition: null,
                            applied: false, declined: true, isSuccessful: false,
                            errorMessage: null, availableTargetEditions: null,
                            DateTime.UtcNow, TimeSpan.Zero);
                        WriteObject(result);
                    }

                    LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Set Windows Image Edition",
                        operationStartTime, "Declined (WhatIf or confirmation declined)");
                    return;
                }

                var progressCallback = ProgressService.CreateProgressCallback(
                    this,
                    "Setting Windows Image Edition",
                    $"Setting edition '{editionId}'",
                    currentIndex: 1,
                    totalCount: 1);

                result = editionService.SetImageEdition(mountPath, Edition, ProductKey, ServerEdition.IsPresent, progressCallback);

                if (PassThru.IsPresent)
                {
                    WriteObject(result);
                }

                var summary = result.IsSuccessful
                    ? result.Applied
                        ? $"Edition changed to '{result.AfterEdition ?? editionId}'"
                        : $"Image is already edition '{result.CurrentEdition}'"
                    : $"Edition change failed: {result.ErrorMessage}";
                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Set Windows Image Edition",
                    operationStartTime, summary);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to set image edition for {mountPath}: {ex.Message}", ex);

                if (PassThru.IsPresent)
                {
                    result = WindowsImageEditionService.BuildResult(
                        ImagePath, editionId, ServerEdition.IsPresent, ProductKey,
                        string.Empty, afterEdition: null, applied: false, declined: false,
                        isSuccessful: false, errorMessage: ex.Message,
                        availableTargetEditions: null, DateTime.UtcNow, TimeSpan.Zero);
                    WriteObject(result);
                }
                else
                {
                    ThrowTerminatingError(new ErrorRecord(
                        ex,
                        "SetEditionFailed",
                        ErrorCategory.InvalidOperation,
                        ImagePath.FullName));
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Set Windows Image Edition",
                    operationStartTime, $"Failed: {ex.Message}");
            }
        }
    }
}