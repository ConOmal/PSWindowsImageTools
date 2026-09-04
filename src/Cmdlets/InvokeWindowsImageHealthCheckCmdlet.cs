using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Runs a composite health check against one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "WindowsImageHealthCheck", SupportsShouldProcess = true)]
    [OutputType(typeof(HealthCheckReport[]))]
    public class InvokeWindowsImageHealthCheckCmdlet : PSCmdlet
    {
        private const string ComponentName = "Invoke-WindowsImageHealthCheck";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to check")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Attempt to repair detected corruption via DISM RestoreHealth")]
        public SwitchParameter RestoreHealth { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided for health check");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var healthCheckService = new WindowsImageHealthCheckService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<HealthCheckReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                var effectiveRestoreHealth = RestoreHealth.IsPresent;
                if (effectiveRestoreHealth)
                {
                    var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                    if (!ShouldProcess(target, "Restore image health (repair corruption)"))
                    {
                        effectiveRestoreHealth = false;
                    }
                }

                try
                {
                    results.Add(healthCheckService.Run(mountedImage, imageService, effectiveRestoreHealth));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to health-check {mountedImage.ImageName}: {ex.Message}", ex);
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
