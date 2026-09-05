using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Creates a checkpoint of a mounted Windows image's current on-disk state
    /// </summary>
    [Cmdlet(VerbsData.Checkpoint, "WindowsImage")]
    [OutputType(typeof(ImageCheckpointInfo))]
    public class CheckpointWindowsImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Checkpoint-WindowsImage";

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows image to checkpoint")]
        [ValidateNotNull]
        public MountedWindowsImage MountedImage { get; set; } = null!;

        [Parameter(HelpMessage = "Optional label for this checkpoint")]
        public string? Label { get; set; }

        protected override void ProcessRecord()
        {
            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));

            try
            {
                var checkpoint = service.Create(MountedImage, Label);
                WriteObject(checkpoint);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "CheckpointFailed", ErrorCategory.WriteError, MountedImage));
            }
        }
    }

    /// <summary>
    /// Lists checkpoints, optionally for a specific mounted image
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageCheckpoint")]
    [OutputType(typeof(ImageCheckpointInfo[]))]
    public class GetWindowsImageCheckpointCmdlet : PSCmdlet
    {
        [Parameter(HelpMessage = "Only list checkpoints for this mounted image")]
        public MountedWindowsImage? MountedImage { get; set; }

        protected override void ProcessRecord()
        {
            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));
            var checkpoints = service.List(MountedImage?.MountId);
            WriteObject(checkpoints.ToArray());
        }
    }

    /// <summary>
    /// Restores a mounted Windows image's directory to a previously taken checkpoint
    /// </summary>
    [Cmdlet(VerbsData.Restore, "WindowsImageCheckpoint", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class RestoreWindowsImageCheckpointCmdlet : PSCmdlet
    {
        private const string ComponentName = "Restore-WindowsImageCheckpoint";
        private readonly List<ImageCheckpointInfo> _allCheckpoints = new List<ImageCheckpointInfo>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Checkpoint(s) to restore")]
        [ValidateNotNull]
        public ImageCheckpointInfo[] Checkpoint { get; set; } = Array.Empty<ImageCheckpointInfo>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Mounted image to restore into")]
        [ValidateNotNull]
        public MountedWindowsImage MountedImage { get; set; } = null!;

        [Parameter(HelpMessage = "Continue processing other checkpoints if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        [Parameter(HelpMessage = "Delete the checkpoint after successfully restoring it")]
        public SwitchParameter RemoveAfterRestore { get; set; }

        protected override void ProcessRecord()
        {
            _allCheckpoints.AddRange(Checkpoint);
        }

        protected override void EndProcessing()
        {
            if (_allCheckpoints.Count == 0)
            {
                LoggingService.WriteWarning(this, "No checkpoints provided to restore");
                return;
            }

            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));

            foreach (var checkpoint in _allCheckpoints)
            {
                var target = MountedImage.MountPath?.FullName ?? MountedImage.ImageName;
                if (!ShouldProcess(target, $"Restore checkpoint {(checkpoint.Label ?? checkpoint.CheckpointId)}"))
                {
                    continue;
                }

                try
                {
                    service.Restore(checkpoint, MountedImage);

                    if (RemoveAfterRestore.IsPresent)
                    {
                        service.Delete(checkpoint);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to restore checkpoint {checkpoint.CheckpointId}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}
