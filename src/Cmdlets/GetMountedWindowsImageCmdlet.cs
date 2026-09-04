using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Re-discovers mounted Windows images registered by previous cmdlet runs, including mounts
    /// from other PowerShell sessions
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "MountedWindowsImage")]
    [OutputType(typeof(MountedWindowsImage[]))]
    public class GetMountedWindowsImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-MountedWindowsImage";

        /// <summary>
        /// Regex pattern to filter by image name
        /// </summary>
        [Parameter(Mandatory = false, Position = 0, HelpMessage = "Regex pattern to filter by image name")]
        [ValidateNotNullOrEmpty]
        public string? Filter { get; set; }

        /// <summary>
        /// Remove entries whose mount directories no longer exist
        /// </summary>
        [Parameter(HelpMessage = "Remove entries whose mount directories no longer exist")]
        public SwitchParameter Prune { get; set; }

        protected override void ProcessRecord()
        {
            try
            {
                if (Prune.IsPresent)
                {
                    var pruned = MountSessionService.Prune();
                    LoggingService.WriteVerbose(this, ComponentName, $"Pruned {pruned} stale mount entries");
                }

                var mounts = MountSessionService.GetActive();

                System.Text.RegularExpressions.Regex? filter = null;
                if (!string.IsNullOrEmpty(Filter))
                {
                    filter = new System.Text.RegularExpressions.Regex(Filter, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                foreach (var mount in mounts)
                {
                    if (filter != null && !filter.IsMatch(mount.ImageName ?? string.Empty))
                    {
                        continue;
                    }

                    WriteObject(mount);
                }
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "GetMountedWindowsImageFailed", ErrorCategory.ReadError, ComponentName));
            }
        }
    }
}
