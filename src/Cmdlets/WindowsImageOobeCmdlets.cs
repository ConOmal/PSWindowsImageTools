using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Reports the Out-of-Box Experience (OOBE) configuration of one or more
    /// mounted Windows images (SkipMachineOOBE, SkipUserOOBE, SkipPrivacyExperience,
    /// ProtectYourPC, BypassNRO, HideOnlineAccountScreens, HideWirelessSetupInOOBE)
    /// from each image's offline SOFTWARE hive
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageOOBE")]
    [OutputType(typeof(WindowsImageOobeSetting[]))]
    public class GetWindowsImageOobeCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageOOBE";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to query")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Continue processing other images if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted images provided for OOBE enumeration");
                return;
            }

            var service = new WindowsImageOobeService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<WindowsImageOobeSetting>();

            foreach (var mountedImage in _allMountedImages)
            {
                var mountPath = mountedImage.MountPath?.FullName ?? string.Empty;

                if (string.IsNullOrEmpty(mountPath))
                {
                    LoggingService.WriteError(this, ComponentName, $"Image {mountedImage.ImageName} has no mount path; skipping");
                    if (!ContinueOnError.IsPresent)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException($"Image {mountedImage.ImageName} has no mount path."),
                            "ImageNotMounted",
                            ErrorCategory.InvalidOperation,
                            mountedImage.ImageName));
                    }

                    continue;
                }

                try
                {
                    using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this));
                    results.AddRange(service.GetSettings(reader, mountedImage.ImageName, mountPath));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to enumerate OOBE settings for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(results.ToArray());
        }
    }

    /// <summary>
    /// Applies Out-of-Box Experience (OOBE) settings to one or more mounted Windows
    /// images' offline SOFTWARE hives. Writes are delegated to the existing
    /// hive-mounted native registry path. Setting switches are tri-state: not
    /// specified leaves the value untouched, specified writes 1, and specified with
    /// :$false writes 0.
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "WindowsImageOOBE", SupportsShouldProcess = true)]
    [OutputType(typeof(WindowsImageOobeOperationResult[]))]
    public class SetWindowsImageOobeCmdlet : PSCmdlet
    {
        private const string ComponentName = "Set-WindowsImageOOBE";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to modify")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "SkipMachineOOBE value: omit to leave untouched, specify to write 1, or use -SkipMachineOOBE:$false to write 0 (legacy switch, informational on Windows 10/11 images)")]
        public SwitchParameter SkipMachineOOBE { get; set; }

        [Parameter(HelpMessage = "SkipUserOOBE value: omit to leave untouched, specify to write 1, or use -SkipUserOOBE:$false to write 0 (legacy switch, informational on Windows 10/11 images)")]
        public SwitchParameter SkipUserOOBE { get; set; }

        [Parameter(HelpMessage = "SkipPrivacyExperience value: omit to leave untouched, specify to write 1, or use -SkipPrivacyExperience:$false to write 0")]
        public SwitchParameter SkipPrivacyExperience { get; set; }

        [Parameter(HelpMessage = "BypassNRO value (Windows 11, allows OOBE without a network connection): omit to leave untouched, specify to write 1, or use -BypassNRO:$false to write 0")]
        public SwitchParameter BypassNRO { get; set; }

        [Parameter(HelpMessage = "HideOnlineAccountScreens value: omit to leave untouched, specify to write 1, or use -HideOnlineAccountScreens:$false to write 0")]
        public SwitchParameter HideOnlineAccountScreens { get; set; }

        [Parameter(HelpMessage = "HideWirelessSetupInOOBE value: omit to leave untouched, specify to write 1, or use -HideWirelessSetupInOOBE:$false to write 0")]
        public SwitchParameter HideWirelessSetupInOOBE { get; set; }

        [Parameter(HelpMessage = "ProtectYourPC express-settings choice (Recommended = 1, ImportantOnly = 2, NotInProgram = 3); omit to leave untouched")]
        public WindowsImageOobeProtectYourPc? ProtectYourPC { get; set; }

        [Parameter(HelpMessage = "Documented OOBE value names to remove from the OOBE key (e.g. BypassNRO)")]
        [ValidateNotNullOrEmpty]
        public string[]? Remove { get; set; }

        [Parameter(HelpMessage = "Continue processing other images if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted images provided for OOBE configuration");
                return;
            }

            var changes = CollectChanges();

            try
            {
                WindowsImageOobeService.ValidateChanges(changes);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "InvalidOobeConfiguration", ErrorCategory.InvalidArgument, ex.Message));
                return;
            }

            var service = new WindowsImageOobeService(ModuleCallbacks.FromCmdlet(this));
            var operationName = WindowsImageOobeService.DescribeSetChange(changes);
            var operations = WindowsImageOobeService.BuildSetOperations(changes);

            foreach (var mountedImage in _allMountedImages)
            {
                var mountPath = mountedImage.MountPath?.FullName ?? string.Empty;

                if (string.IsNullOrEmpty(mountPath))
                {
                    LoggingService.WriteError(this, ComponentName, $"Image {mountedImage.ImageName} has no mount path; skipping");
                    if (!ContinueOnError.IsPresent)
                    {
                        ThrowTerminatingError(new ErrorRecord(
                            new InvalidOperationException($"Image {mountedImage.ImageName} has no mount path."),
                            "ImageNotMounted",
                            ErrorCategory.InvalidOperation,
                            mountedImage.ImageName));
                    }

                    continue;
                }

                var target = $"OOBE settings on {mountPath}";

                if (!ShouldProcess(target, operationName))
                {
                    continue;
                }

                var startTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, operationName, target);

                var success = false;
                string? errorMessage;

                try
                {
                    var applied = new NativeRegistryService().ApplyRegistryOperations(mountPath, operations.ToArray(), this);
                    success = applied;
                    errorMessage = applied ? null : $"One or more registry operations could not be applied to {mountPath}.";
                }
                catch (Exception ex)
                {
                    success = false;
                    errorMessage = ex.Message;
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, operationName, startTime,
                    success ? "succeeded" : $"failed: {errorMessage}");

                WriteObject(WindowsImageOobeService.BuildSetResult(
                    mountedImage.ImageName, operationName, success, errorMessage));

                if (!success)
                {
                    if (ContinueOnError.IsPresent)
                    {
                        WriteWarning($"Failed to apply OOBE settings on {mountedImage.ImageName}: {errorMessage}");
                    }
                    else
                    {
                        var failure = new InvalidOperationException(
                            $"Failed to apply OOBE settings on {mountedImage.ImageName}: {errorMessage}");
                        LoggingService.WriteError(this, ComponentName, failure.Message, failure);
                        throw failure;
                    }
                }
            }
        }

        /// <summary>
        /// Collects the requested changes from the tri-state switches, ProtectYourPC and -Remove
        /// </summary>
        private List<WindowsImageOobeChange> CollectChanges()
        {
            var changes = new List<WindowsImageOobeChange>();

            AddSwitchChange(changes, SkipMachineOOBE, "SkipMachineOOBE");
            AddSwitchChange(changes, SkipUserOOBE, "SkipUserOOBE");
            AddSwitchChange(changes, SkipPrivacyExperience, "SkipPrivacyExperience");
            AddSwitchChange(changes, BypassNRO, "BypassNRO");
            AddSwitchChange(changes, HideOnlineAccountScreens, "HideOnlineAccountScreens");
            AddSwitchChange(changes, HideWirelessSetupInOOBE, "HideWirelessSetupInOOBE");

            if (ProtectYourPC.HasValue)
            {
                changes.Add(new WindowsImageOobeChange
                {
                    ValueName = "ProtectYourPC",
                    Value = WindowsImageOobeService.ToProtectYourPcValue(ProtectYourPC.Value)
                });
            }

            if (Remove != null)
            {
                foreach (var valueName in Remove)
                {
                    changes.Add(new WindowsImageOobeChange
                    {
                        ValueName = valueName ?? string.Empty,
                        Value = null
                    });
                }
            }

            return changes;
        }

        /// <summary>
        /// Adds a write change for a tri-state switch (present = 1, present :$false = 0)
        /// </summary>
        private static void AddSwitchChange(List<WindowsImageOobeChange> changes, SwitchParameter parameter, string valueName)
        {
            if (!parameter.IsPresent)
            {
                return;
            }

            changes.Add(new WindowsImageOobeChange
            {
                ValueName = valueName,
                Value = parameter.ToBool() ? 1 : 0
            });
        }
    }
}
