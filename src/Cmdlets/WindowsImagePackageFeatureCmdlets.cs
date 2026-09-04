using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Lists packages in mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImagePackageList")]
    [OutputType(typeof(WindowsImagePackage[]))]
    public class GetWindowsImagePackageListCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImagePackageList";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to list packages from
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to list packages from")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Regex pattern to filter package names
        /// </summary>
        [Parameter(Mandatory = false, HelpMessage = "Regex pattern to filter package names")]
        [ValidateNotNullOrEmpty]
        public string? Filter { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            var filter = string.IsNullOrEmpty(Filter) ? null : new Regex(Filter, RegexOptions.IgnoreCase);

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                try
                {
                    var packages = imageService.GetPackages(mountedImage.MountPath.FullName);
                    var results = packages.Select(package => new WindowsImagePackage
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        PackageName = package.PackageName ?? string.Empty,
                        PackageState = package.PackageState.ToString(),
                        ReleaseType = package.ReleaseType.ToString(),
                        InstallTime = package.InstallTime == default ? (DateTime?)null : package.InstallTime
                    });

                    foreach (var result in results)
                    {
                        if (filter == null || filter.IsMatch(result.PackageName))
                        {
                            WriteObject(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteWarning($"Failed to list packages on {mountedImage.ImageName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Lists Windows features in mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageFeatureList")]
    [OutputType(typeof(WindowsImageFeature[]))]
    public class GetWindowsImageFeatureListCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageFeatureList";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to list features from
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to list features from")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Regex pattern to filter feature names
        /// </summary>
        [Parameter(Mandatory = false, HelpMessage = "Regex pattern to filter feature names")]
        [ValidateNotNullOrEmpty]
        public string? Filter { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            var filter = string.IsNullOrEmpty(Filter) ? null : new Regex(Filter, RegexOptions.IgnoreCase);

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                try
                {
                    var features = imageService.GetFeatures(mountedImage.MountPath.FullName);

                    foreach (var feature in features)
                    {
                        if (filter != null && !filter.IsMatch(feature.FeatureName ?? string.Empty))
                        {
                            continue;
                        }

                        WriteObject(new WindowsImageFeature
                        {
                            ImageName = mountedImage.ImageName,
                            ImageIndex = mountedImage.ImageIndex,
                            MountPath = mountedImage.MountPath.FullName,
                            FeatureName = feature.FeatureName ?? string.Empty,
                            State = feature.State.ToString()
                        });
                    }
                }
                catch (Exception ex)
                {
                    WriteWarning($"Failed to list features on {mountedImage.ImageName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Adds a package (.cab/.msu) to mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsImagePackage")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class AddWindowsImagePackageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsImagePackage";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to add packages to
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to add packages to")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Paths to package files (.cab or .msu)
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Paths to package files (.cab or .msu)")]
        [ValidateNotNullOrEmpty]
        public string[] PackagePath { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Skip applicability checks
        /// </summary>
        [Parameter(HelpMessage = "Skip applicability checks")]
        public SwitchParameter IgnoreCheck { get; set; }

        /// <summary>
        /// Prevent installation if there are pending operations
        /// </summary>
        [Parameter(HelpMessage = "Prevent installation if there are pending operations")]
        public SwitchParameter PreventPending { get; set; }

        /// <summary>
        /// Continue processing remaining packages when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue processing remaining packages when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                foreach (var packagePath in PackagePath)
                {
                    var resolvedPath = GetUnresolvedProviderPathFromPSPath(packagePath) ?? packagePath;

                    if (!File.Exists(resolvedPath))
                    {
                        WriteError(new ErrorRecord(
                            new FileNotFoundException($"Package file not found: {packagePath}"),
                            "PackageFileNotFound",
                            ErrorCategory.ObjectNotFound,
                            packagePath));
                        continue;
                    }

                    var result = new ImageOperationResult
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        Target = resolvedPath,
                        Operation = "AddPackage"
                    };

                    try
                    {
                        imageService.AddPackage(mountedImage.MountPath.FullName, resolvedPath, IgnoreCheck.IsPresent, PreventPending.IsPresent);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;

                        if (!ContinueOnError.IsPresent)
                        {
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "AddPackageFailed", ErrorCategory.OperationStopped, resolvedPath));
                        }
                        else
                        {
                            WriteWarning($"Failed to add {resolvedPath} to {mountedImage.ImageName}: {ex.Message}");
                        }
                    }

                    WriteObject(result);
                }
            }
        }
    }

    /// <summary>
    /// Enables a Windows feature in mounted Windows images
    /// </summary>
    [Cmdlet(VerbsLifecycle.Enable, "WindowsImageFeature")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class EnableWindowsImageFeatureCmdlet : PSCmdlet
    {
        private const string ComponentName = "Enable-WindowsFeature";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to enable features in
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to enable features in")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Names of the features to enable
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Names of the features to enable")]
        [ValidateNotNullOrEmpty]
        public string[] FeatureName { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Enable all parent features
        /// </summary>
        [Parameter(HelpMessage = "Enable all parent features")]
        public SwitchParameter EnableAll { get; set; }

        /// <summary>
        /// Optional source paths for feature payload
        /// </summary>
        [Parameter(HelpMessage = "Optional source paths for feature payload")]
        [ValidateNotNullOrEmpty]
        public string[]? SourcePath { get; set; }

        /// <summary>
        /// Continue processing remaining features when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue processing remaining features when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            var sourcePaths = SourcePath?
                .Select(p => GetUnresolvedProviderPathFromPSPath(p) ?? p)
                .ToList();

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                foreach (var featureName in FeatureName)
                {
                    var result = new ImageOperationResult
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        Target = featureName,
                        Operation = "EnableFeature"
                    };

                    try
                    {
                        imageService.EnableFeature(mountedImage.MountPath.FullName, featureName, EnableAll.IsPresent, sourcePaths);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;

                        if (!ContinueOnError.IsPresent)
                        {
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "EnableFeatureFailed", ErrorCategory.OperationStopped, featureName));
                        }
                        else
                        {
                            WriteWarning($"Failed to enable {featureName} on {mountedImage.ImageName}: {ex.Message}");
                        }
                    }

                    WriteObject(result);
                }
            }
        }
    }

    /// <summary>
    /// Disables a Windows feature in mounted Windows images
    /// </summary>
    [Cmdlet(VerbsLifecycle.Disable, "WindowsImageFeature")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class DisableWindowsImageFeatureCmdlet : PSCmdlet
    {
        private const string ComponentName = "Disable-WindowsFeature";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to disable features in
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to disable features in")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Names of the features to disable
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Names of the features to disable")]
        [ValidateNotNullOrEmpty]
        public string[] FeatureName { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Remove the feature payload
        /// </summary>
        [Parameter(HelpMessage = "Remove the feature payload")]
        public SwitchParameter RemovePayload { get; set; }

        /// <summary>
        /// Continue processing remaining features when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue processing remaining features when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                foreach (var featureName in FeatureName)
                {
                    var result = new ImageOperationResult
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        Target = featureName,
                        Operation = "DisableFeature"
                    };

                    try
                    {
                        imageService.DisableFeature(mountedImage.MountPath.FullName, featureName, RemovePayload.IsPresent);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;

                        if (!ContinueOnError.IsPresent)
                        {
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "DisableFeatureFailed", ErrorCategory.OperationStopped, featureName));
                        }
                        else
                        {
                            WriteWarning($"Failed to disable {featureName} on {mountedImage.ImageName}: {ex.Message}");
                        }
                    }

                    WriteObject(result);
                }
            }
        }
    }

    /// <summary>
    /// Adds a capability (Feature on Demand) to mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsImageCapability")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class AddWindowsImageCapabilityCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsImageCapability";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to add capabilities to
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to add capabilities to")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Names of the capabilities to add
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Names of the capabilities to add (e.g., 'Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0')")]
        [ValidateNotNullOrEmpty]
        public string[] CapabilityName { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Prevent Windows Update as a source
        /// </summary>
        [Parameter(HelpMessage = "Prevent Windows Update as a source")]
        public SwitchParameter LimitAccess { get; set; }

        /// <summary>
        /// Optional source paths for the capability payload
        /// </summary>
        [Parameter(HelpMessage = "Optional source paths for the capability payload")]
        [ValidateNotNullOrEmpty]
        public string[]? SourcePath { get; set; }

        /// <summary>
        /// Continue processing remaining capabilities when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue processing remaining capabilities when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);
            var sourcePaths = SourcePath?
                .Select(p => GetUnresolvedProviderPathFromPSPath(p) ?? p)
                .ToList();

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                foreach (var capabilityName in CapabilityName)
                {
                    var result = new ImageOperationResult
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        Target = capabilityName,
                        Operation = "AddCapability"
                    };

                    try
                    {
                        imageService.AddCapability(mountedImage.MountPath.FullName, capabilityName, LimitAccess.IsPresent, sourcePaths);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;

                        if (!ContinueOnError.IsPresent)
                        {
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "AddCapabilityFailed", ErrorCategory.OperationStopped, capabilityName));
                        }
                        else
                        {
                            WriteWarning($"Failed to add {capabilityName} on {mountedImage.ImageName}: {ex.Message}");
                        }
                    }

                    WriteObject(result);
                }
            }
        }
    }

    /// <summary>
    /// Removes a capability from mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Remove, "WindowsImageCapability")]
    [OutputType(typeof(ImageOperationResult[]))]
    public class RemoveWindowsImageCapabilityCmdlet : PSCmdlet
    {
        private const string ComponentName = "Remove-WindowsImageCapability";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        /// <summary>
        /// Mounted Windows images to remove capabilities from
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipeline = true,
            HelpMessage = "Mounted Windows images to remove capabilities from")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        /// <summary>
        /// Names of the capabilities to remove
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 1,
            HelpMessage = "Names of the capabilities to remove")]
        [ValidateNotNullOrEmpty]
        public string[] CapabilityName { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Continue processing remaining capabilities when one fails
        /// </summary>
        [Parameter(HelpMessage = "Continue processing remaining capabilities when one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            using var imageService = WindowsImageService.ForCmdlet(this);

            foreach (var mountedImage in _allMountedImages)
            {
                if (mountedImage.MountPath == null)
                {
                    WriteWarning($"Mount path is null for {mountedImage.ImageName}; skipping");
                    continue;
                }

                foreach (var capabilityName in CapabilityName)
                {
                    var result = new ImageOperationResult
                    {
                        ImageName = mountedImage.ImageName,
                        ImageIndex = mountedImage.ImageIndex,
                        MountPath = mountedImage.MountPath.FullName,
                        Target = capabilityName,
                        Operation = "RemoveCapability"
                    };

                    try
                    {
                        imageService.RemoveCapability(mountedImage.MountPath.FullName, capabilityName);
                        result.Success = true;
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.ErrorMessage = ex.Message;

                        if (!ContinueOnError.IsPresent)
                        {
                            WriteObject(result);
                            ThrowTerminatingError(new ErrorRecord(ex, "RemoveCapabilityFailed", ErrorCategory.OperationStopped, capabilityName));
                        }
                        else
                        {
                            WriteWarning($"Failed to remove {capabilityName} on {mountedImage.ImageName}: {ex.Message}");
                        }
                    }

                    WriteObject(result);
                }
            }
        }
    }
}
