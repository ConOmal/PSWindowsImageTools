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
    /// Analyzes the WinSxS component store of one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageComponentStore")]
    [OutputType(typeof(ComponentStoreReport[]))]
    public class GetWindowsImageComponentStoreCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageComponentStore";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to analyze")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for component store analysis");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var componentStoreService = new ComponentStoreService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    results.Add(componentStoreService.Analyze(mountedImage, imageService));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to analyze {mountedImage.ImageName}: {ex.Message}", ex);
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
    /// Runs component cleanup (and optionally ResetBase) against one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Optimize, "WindowsImageComponentStore", SupportsShouldProcess = true)]
    [OutputType(typeof(ComponentStoreCleanupResult[]))]
    public class OptimizeWindowsImageComponentStoreCmdlet : PSCmdlet
    {
        private const string ComponentName = "Optimize-WindowsImageComponentStore";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to clean up")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Also reset the component store base (makes prior updates non-removable)")]
        public SwitchParameter ResetBase { get; set; }

        [Parameter(HelpMessage = "Timeout in minutes for the cleanup operation")]
        [ValidateRange(1, 600)]
        public int TimeoutMinutes { get; set; } = 90;

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
                LoggingService.WriteWarning(this, "No mounted images provided for component store optimization");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var componentStoreService = new ComponentStoreService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreCleanupResult>();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                var action = ResetBase.IsPresent ? "Component cleanup + ResetBase" : "Component cleanup";

                if (!ShouldProcess(target, action))
                {
                    continue;
                }

                try
                {
                    results.Add(componentStoreService.Cleanup(mountedImage, imageService, ResetBase.IsPresent, this, TimeoutMinutes));
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
