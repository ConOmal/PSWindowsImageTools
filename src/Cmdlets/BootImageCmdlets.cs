using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Locates boot.wim under an extracted Windows installation media root and reports the
    /// images it contains
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsBootImage")]
    [OutputType(typeof(BootImageInfo))]
    public class GetWindowsBootImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsBootImage";

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Root directory of extracted Windows installation media")]
        [ValidateNotNull]
        public DirectoryInfo MediaRoot { get; set; } = null!;

        protected override void ProcessRecord()
        {
            if (!MediaRoot.Exists)
            {
                LoggingService.WriteWarning(this, $"Media root does not exist: {MediaRoot.FullName}");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));

            var result = bootImageService.Locate(MediaRoot, imageService);

            if (result == null)
            {
                LoggingService.WriteWarning(this, $"No boot.wim found under {MediaRoot.FullName}");
                return;
            }

            WriteObject(result);
        }
    }

    /// <summary>
    /// Injects drivers into one or more mounted boot.wim images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsBootDriver", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class AddWindowsBootDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsBootDriver";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted boot images to add drivers to")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Directory containing driver INF files")]
        [ValidateNotNull]
        public DirectoryInfo DriverPath { get; set; } = null!;

        [Parameter(HelpMessage = "Allow installation of unsigned drivers")]
        public SwitchParameter ForceUnsigned { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted boot images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, "Add boot drivers"))
                {
                    continue;
                }

                try
                {
                    bootImageService.AddDriver(mountedImage, imageService, DriverPath, ForceUnsigned.IsPresent);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to add drivers to {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs component cleanup against one or more mounted boot.wim images
    /// </summary>
    [Cmdlet(VerbsCommon.Optimize, "WindowsBootImage", SupportsShouldProcess = true)]
    [OutputType(typeof(ComponentStoreCleanupResult[]))]
    public class OptimizeWindowsBootImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Optimize-WindowsBootImage";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted boot images to optimize")]
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
                LoggingService.WriteWarning(this, "No mounted boot images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreCleanupResult>();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, "Optimize boot image component store"))
                {
                    continue;
                }

                try
                {
                    results.Add(bootImageService.Optimize(mountedImage, imageService, this));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to optimize {mountedImage.ImageName}: {ex.Message}", ex);
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
