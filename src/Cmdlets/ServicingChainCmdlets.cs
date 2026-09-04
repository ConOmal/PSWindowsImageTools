using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Analyzes the servicing chain (SSU/LCU classification and version consistency) of one or
    /// more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageServicingChain")]
    [OutputType(typeof(ServicingChainReport[]))]
    public class GetWindowsImageServicingChainCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageServicingChain";
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
                LoggingService.WriteWarning(this, "No mounted images provided for servicing chain analysis");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var servicingChainService = new ServicingChainService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ServicingChainReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    results.Add(servicingChainService.Analyze(mountedImage, imageService));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to analyze servicing chain for {mountedImage.ImageName}: {ex.Message}", ex);
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
    /// Tests whether one or more mounted Windows images have a version-consistent SSU/LCU
    /// servicing chain
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Test, "WindowsImageServicing")]
    [OutputType(typeof(bool))]
    [OutputType(typeof(ServicingChainReport))]
    public class TestWindowsImageServicingCmdlet : PSCmdlet
    {
        private const string ComponentName = "Test-WindowsImageServicing";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to test")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Return the full ServicingChainReport instead of just a boolean")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for servicing test");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var servicingChainService = new ServicingChainService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var report = servicingChainService.Analyze(mountedImage, imageService);
                    if (Detailed.IsPresent)
                    {
                        WriteObject(report);
                    }
                    else
                    {
                        WriteObject(report.OrderingValid);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to test servicing for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}