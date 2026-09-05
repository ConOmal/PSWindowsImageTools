using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Reports compliance of one or more mounted Windows images against the curated
    /// security baseline (per-entry current value vs expected value, plus an overall
    /// verdict), reading each image's offline SOFTWARE, SYSTEM and default-user
    /// hives in memory
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageSecurityBaseline")]
    [OutputType(typeof(WindowsImageSecurityBaselineReport[]))]
    public class GetWindowsImageSecurityBaselineCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageSecurityBaseline";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to check against the security baseline")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for security baseline compliance check");
                return;
            }

            var service = new SecurityBaselineService(ModuleCallbacks.FromCmdlet(this));
            var reports = new List<WindowsImageSecurityBaselineReport>();

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
                    reports.Add(service.GetBaselineCompliance(reader, mountedImage.ImageName, mountPath));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to check the security baseline for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(reports.ToArray());
        }
    }

    /// <summary>
    /// Applies the curated security baseline to one or more mounted Windows images:
    /// entries that are already compliant are skipped, the rest are written to the
    /// offline SOFTWARE, SYSTEM and default-user hives via the hive-mounted native
    /// registry path
    /// </summary>
    [Cmdlet(VerbsCommon.Set, "WindowsImageSecurityBaseline", SupportsShouldProcess = true)]
    [OutputType(typeof(WindowsImageSecurityBaselineApplyResult[]))]
    public class SetWindowsImageSecurityBaselineCmdlet : PSCmdlet
    {
        private const string ComponentName = "Set-WindowsImageSecurityBaseline";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to bring to the security baseline")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for security baseline application");
                return;
            }

            var service = new SecurityBaselineService(ModuleCallbacks.FromCmdlet(this));
            var baseline = SecurityBaselineService.GetBaselineEntries();
            var operationName = "Apply security baseline";

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
                    // In-memory pre-flight: classify every entry before any hive is mounted
                    WindowsImageSecurityBaselineReport compliance;
                    using (var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this)))
                    {
                        compliance = service.GetBaselineCompliance(reader, mountedImage.ImageName, mountPath, baseline);
                    }

                    var pending = new List<WindowsImageSecurityBaselineEntry>();
                    var alreadyCompliant = new List<WindowsImageSecurityBaselineEntry>();
                    var missingHiveEntries = new List<WindowsImageSecurityBaselineEntry>();
                    var missingHiveHives = new List<string>();

                    // Lookup by entry identity (not position) so the report order can never
                    // mis-align the partitioning
                    var stateByPath = new Dictionary<string, WindowsImageBaselineComplianceState>(StringComparer.OrdinalIgnoreCase);
                    foreach (var observation in compliance.Entries)
                    {
                        stateByPath[$"{observation.Hive}\\{observation.KeyPath}\\{observation.ValueName}"] = observation.State;
                    }

                    foreach (var entry in baseline)
                    {
                        var hiveFile = SecurityBaselineService.ResolveHivePath(mountPath, entry.Hive);

                        if (!System.IO.File.Exists(hiveFile))
                        {
                            missingHiveEntries.Add(entry);
                            if (!missingHiveHives.Contains(entry.Hive))
                            {
                                missingHiveHives.Add(entry.Hive);
                            }

                            continue;
                        }

                        if (stateByPath.TryGetValue($"{entry.Hive}\\{entry.KeyPath}\\{entry.ValueName}", out var state) &&
                            state == WindowsImageBaselineComplianceState.Compliant)
                        {
                            alreadyCompliant.Add(entry);
                        }
                        else
                        {
                            pending.Add(entry);
                        }
                    }

                    var rows = SecurityBaselineService.BuildApplyRows(
                        mountedImage.ImageName,
                        Array.Empty<WindowsImageSecurityBaselineEntry>(),
                        WindowsImageBaselineApplyState.Applied,
                        null,
                        alreadyCompliant,
                        WindowsImageBaselineApplyState.AlreadyApplied,
                        "Already compliant");

                    if (missingHiveEntries.Count > 0)
                    {
                        rows.AddRange(SecurityBaselineService.BuildApplyRows(
                            mountedImage.ImageName,
                            missingHiveEntries,
                            WindowsImageBaselineApplyState.Skipped,
                            "Hive file not found: " + string.Join(", ", missingHiveHives),
                            Array.Empty<WindowsImageSecurityBaselineEntry>(),
                            WindowsImageBaselineApplyState.Skipped,
                            null));
                    }

                    if (pending.Count == 0)
                    {
                        LoggingService.WriteVerbose(this, ComponentName,
                            $"Image {mountedImage.ImageName} already satisfies the security baseline; no hive was mounted");
                        WriteObject(SecurityBaselineService.BuildApplyResult(
                            mountedImage.ImageName, mountPath, rows, true, null));
                        continue;
                    }

                    if (!ShouldProcess(
                            SecurityBaselineService.DescribeApplyTarget(mountedImage.ImageName, mountPath),
                            SecurityBaselineService.DescribeApplyAction(pending.Count, alreadyCompliant.Count, mountedImage.ImageName)))
                    {
                        continue;
                    }

                    var operations = SecurityBaselineService.BuildApplyOperations(pending);
                    var startTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, operationName,
                        $"{pending.Count} entries on {mountPath}");

                    var success = false;
                    string? errorMessage;

                    try
                    {
                        var applied = new NativeRegistryService().ApplyRegistryOperations(mountPath, operations.ToArray(), this);
                        success = applied;
                        errorMessage = applied ? null : $"One or more security baseline entries could not be applied to {mountPath}.";
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        errorMessage = ex.Message;
                    }

                    LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, operationName, startTime,
                        success ? "succeeded" : $"failed: {errorMessage}");

                    rows.AddRange(SecurityBaselineService.BuildApplyRows(
                        mountedImage.ImageName,
                        pending,
                        success ? WindowsImageBaselineApplyState.Applied : WindowsImageBaselineApplyState.Failed,
                        success ? null : errorMessage,
                        Array.Empty<WindowsImageSecurityBaselineEntry>(),
                        WindowsImageBaselineApplyState.Skipped,
                        null));

                    WriteObject(SecurityBaselineService.BuildApplyResult(
                        mountedImage.ImageName, mountPath, rows, success, errorMessage));

                    if (!success)
                    {
                        if (!ContinueOnError.IsPresent)
                        {
                            throw new InvalidOperationException(
                                $"Failed to apply the security baseline to {mountedImage.ImageName}: {errorMessage}");
                        }

                        WriteWarning($"Failed to apply the security baseline to {mountedImage.ImageName}: {errorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to apply the security baseline to {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}
