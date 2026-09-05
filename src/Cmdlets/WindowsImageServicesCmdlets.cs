using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Enumerates the services configured in one or more mounted Windows images
    /// (start type, display name, image path, description, delayed auto start)
    /// from each image's offline SYSTEM hive
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageService")]
    [OutputType(typeof(WindowsImageServiceInfo[]))]
    public class GetWindowsImageServiceCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageService";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to query")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Position = 1, HelpMessage = "Service name to filter by (exact name, or a regular expression pattern)")]
        public string Name { get; set; } = string.Empty;

        [Parameter(HelpMessage = "Include the raw registry values of each service key")]
        public SwitchParameter Detailed { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided for service enumeration");
                return;
            }

            var service = new WindowsImageServicesService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<WindowsImageServiceInfo>();

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
                    results.AddRange(service.GetServices(reader, mountedImage.ImageName, mountPath, Name, Detailed.IsPresent));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to enumerate services for {mountedImage.ImageName}: {ex.Message}", ex);
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
    /// Changes the start type (and optionally enables delayed auto start) of a
    /// service in one or more mounted Windows images' offline SYSTEM hives.
    /// Writes are delegated to the existing hive-mounted native registry path.
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "WindowsImageService", SupportsShouldProcess = true)]
    [OutputType(typeof(WindowsImageServiceOperationResult[]))]
    public class SetWindowsImageServiceCmdlet : PSCmdlet
    {
        private const string ComponentName = "Set-WindowsImageService";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to modify")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Name of the service to configure")]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; } = string.Empty;

        [Parameter(HelpMessage = "New start type (Boot, System, Automatic, Manual, Disabled). Boot/System are for driver services only.")]
        public WindowsImageServiceStartType? StartType { get; set; }

        [Parameter(HelpMessage = "Enable DelayedAutoStart (DWORD 1). Only valid with -StartType Automatic.")]
        public SwitchParameter DelayedAutoStart { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided for service configuration");
                return;
            }

            try
            {
                WindowsImageServicesService.ValidateSetParameters(StartType, DelayedAutoStart.IsPresent);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "InvalidServiceConfiguration", ErrorCategory.InvalidArgument, Name));
                return;
            }

            if (!WindowsImageServicesService.IsValidServiceName(Name))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException($"Service name '{Name}' is not valid (must be a non-empty key name without path separators)."),
                    "InvalidServiceName",
                    ErrorCategory.InvalidArgument,
                    Name));
                return;
            }

            var service = new WindowsImageServicesService(ModuleCallbacks.FromCmdlet(this));
            var operationName = WindowsImageServicesService.DescribeSetChange(StartType, DelayedAutoStart.IsPresent);

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

                using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this));

                try
                {
                    if (!service.ServiceExists(reader, mountPath, Name))
                    {
                        throw new InvalidOperationException(
                            $"Service '{Name}' was not found in the image's SYSTEM hive at {mountPath}.");
                    }

                    var operations = WindowsImageServicesService.BuildSetOperations(Name, StartType, DelayedAutoStart.IsPresent);
                    var target = $"{Name} on {mountPath}";

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

                    WriteObject(WindowsImageServicesService.BuildSetResult(
                        mountedImage.ImageName, Name, StartType, DelayedAutoStart.IsPresent, success, errorMessage));

                    if (!success)
                    {
                        if (ContinueOnError.IsPresent)
                        {
                            WriteWarning($"Failed to {operationName.ToLowerInvariant()} for '{Name}' on {mountedImage.ImageName}: {errorMessage}");
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Failed to {operationName.ToLowerInvariant()} for '{Name}' on {mountedImage.ImageName}: {errorMessage}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to set service '{Name}' on {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}