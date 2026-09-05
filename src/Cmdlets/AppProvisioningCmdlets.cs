using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Lists provisioned AppX packages in one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageProvisionedApp")]
    [OutputType(typeof(ProvisionedAppInfo[]))]
    public class GetWindowsImageProvisionedAppCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageProvisionedApp";
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
                LoggingService.WriteWarning(this, "No mounted images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var apps = appProvisioningService.GetProvisionedApps(mountedImage, imageService);
                    WriteObject(apps.ToArray());
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to get provisioned apps for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Provisions a new AppX package into one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsImageProvisionedApp", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class AddWindowsImageProvisionedAppCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsImageProvisionedApp";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to provision the app into")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to the .appx/.appxbundle/.msix package file")]
        [ValidateNotNull]
        public FileInfo PackagePath { get; set; } = null!;

        [Parameter(HelpMessage = "Paths to any dependency packages the app requires")]
        public FileInfo[]? DependencyPackagePath { get; set; }

        [Parameter(HelpMessage = "Path to the app's license file, if required")]
        public FileInfo? LicensePath { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));
            var dependencyPackages = DependencyPackagePath?.ToList();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, $"Provision app {PackagePath.Name}"))
                {
                    continue;
                }

                try
                {
                    appProvisioningService.AddProvisionedApp(mountedImage, imageService, PackagePath, dependencyPackages, LicensePath);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to provision app for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates a WinGet Configuration artifact for first-boot application (WinGet cannot
    /// target an offline mounted image directly)
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImageWinGetConfiguration")]
    [OutputType(typeof(WinGetConfigurationExportResult))]
    public class ExportWindowsImageWinGetConfigurationCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImageWinGetConfiguration";
        private readonly List<WinGetConfigurationEntry> _allPackages = new List<WinGetConfigurationEntry>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Desired package entries")]
        [ValidateNotNull]
        public WinGetConfigurationEntry[] Package { get; set; } = Array.Empty<WinGetConfigurationEntry>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination directory for the generated configuration files")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        protected override void ProcessRecord()
        {
            _allPackages.AddRange(Package);
        }

        protected override void EndProcessing()
        {
            if (_allPackages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No packages provided for WinGet configuration export");
            }

            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                var result = appProvisioningService.ExportWinGetConfiguration(_allPackages, DestinationPath);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "ExportWinGetConfigurationFailed", ErrorCategory.WriteError, DestinationPath));
            }
        }
    }
}
