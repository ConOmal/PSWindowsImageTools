using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Lists driver packages present in one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageDriver")]
    [OutputType(typeof(WindowsImageDriverInfo[]))]
    public class GetWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageDriver";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to enumerate drivers from")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Include inbox (Windows-provided) drivers, not just third-party")]
        public SwitchParameter All { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted images provided for driver enumeration");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var driverService = new WindowsImageDriverService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var drivers = driverService.GetDrivers(mountedImage, imageService, All.IsPresent);
                    WriteObject(drivers.ToArray());
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to get drivers for {mountedImage.ImageName}: {ex.Message}", ex);
                }
            }
        }
    }
}
