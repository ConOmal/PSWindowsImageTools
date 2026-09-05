using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Reports the scheduled tasks registered in one or more mounted Windows images'
    /// offline SOFTWARE hives (Schedule\TaskCache): task path, GUID, state where
    /// readable, and raw cache-entry values under -Detailed. Strictly read-only —
    /// the undocumented Tasks-key definition blob is not parsed.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageScheduledTask")]
    [OutputType(typeof(WindowsImageScheduledTaskInfo[]))]
    public class GetWindowsImageScheduledTaskCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageScheduledTask";
        private const string OperationName = "Scheduled task inventory";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to query")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Position = 1, HelpMessage = "Task path to filter by (exact path, or a regular expression pattern)")]
        public string Path { get; set; } = string.Empty;

        [Parameter(HelpMessage = "Include the raw registry values of each task cache entry")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for scheduled task inventory");
                return;
            }

            var service = new ScheduledTasksService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<WindowsImageScheduledTaskInfo>();
            var totalCount = _allMountedImages.Count;

            for (var index = 0; index < totalCount; index++)
            {
                var mountedImage = _allMountedImages[index];
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

                var progressCallback = ProgressService.CreateProgressCallback(
                    this,
                    "Reading scheduled tasks",
                    mountedImage.ImageName,
                    currentIndex: index + 1,
                    totalCount: totalCount);

                var startTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, OperationName, mountPath);

                try
                {
                    using var reader = new RegistryHiveReader(ModuleCallbacks.FromCmdlet(this));
                    results.AddRange(service.GetScheduledTasks(reader, mountedImage.ImageName, mountPath, Path, Detailed.IsPresent, progressCallback));

                    LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, OperationName, startTime, "succeeded");
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to enumerate scheduled tasks for {mountedImage.ImageName}: {ex.Message}", ex);
                    LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, OperationName, startTime, $"failed: {ex.Message}");
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(results.ToArray());
        }
    }
}
