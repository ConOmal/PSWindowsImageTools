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

    /// <summary>
    /// Removes a driver package from a mounted Windows image
    /// </summary>
    [Cmdlet(VerbsCommon.Remove, "WindowsImageDriver", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class RemoveWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Remove-WindowsImageDriver";
        private readonly List<WindowsImageDriverInfo> _allDrivers = new List<WindowsImageDriverInfo>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Driver(s) to remove, from Get-WindowsImageDriver")]
        [ValidateNotNull]
        public WindowsImageDriverInfo[] Driver { get; set; } = Array.Empty<WindowsImageDriverInfo>();

        [Parameter(HelpMessage = "Continue processing other drivers if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allDrivers.AddRange(Driver);
        }

        protected override void EndProcessing()
        {
            if (_allDrivers.Count == 0)
            {
                LoggingService.WriteWarning(this, "No drivers provided for removal");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);

            foreach (var driver in _allDrivers)
            {
                if (string.IsNullOrEmpty(driver.MountPath))
                {
                    LoggingService.WriteWarning(this, $"Driver {driver.PublishedName} has no mount path; skipping");
                    continue;
                }

                if (!ShouldProcess($"{driver.PublishedName} ({driver.OriginalFileName}) on {driver.MountPath}", "Remove driver"))
                {
                    continue;
                }

                try
                {
                    imageService.RemoveDriver(driver.MountPath, driver.PublishedName);
                    LoggingService.WriteVerbose(this, $"Removed driver {driver.PublishedName} from {driver.MountPath}");
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to remove driver {driver.PublishedName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}
