using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A package found in a Windows image
    /// </summary>
    public class WindowsImagePackage
    {
        /// <summary>
        /// Name of the image the package belongs to
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image the package belongs to
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Path where the image is mounted
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// Name of the package
        /// </summary>
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// State of the package
        /// </summary>
        public string PackageState { get; set; } = string.Empty;

        /// <summary>
        /// Release type of the package
        /// </summary>
        public string ReleaseType { get; set; } = string.Empty;

        /// <summary>
        /// When the package was installed
        /// </summary>
        public DateTime? InstallTime { get; set; }

        public override string ToString()
        {
            return $"{PackageName} ({PackageState})";
        }
    }

    /// <summary>
    /// A Windows feature found in a Windows image
    /// </summary>
    public class WindowsImageFeature
    {
        /// <summary>
        /// Name of the image the feature belongs to
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image the feature belongs to
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Path where the image is mounted
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// Name of the feature
        /// </summary>
        public string FeatureName { get; set; } = string.Empty;

        /// <summary>
        /// State of the feature
        /// </summary>
        public string State { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{FeatureName} ({State})";
        }
    }

    /// <summary>
    /// A capability (Feature on Demand) found in a Windows image
    /// </summary>
    public class WindowsImageCapability
    {
        /// <summary>
        /// Name of the image the capability belongs to
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image the capability belongs to
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Path where the image is mounted
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// Name of the capability
        /// </summary>
        public string CapabilityName { get; set; } = string.Empty;

        /// <summary>
        /// State of the capability
        /// </summary>
        public string State { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{CapabilityName} ({State})";
        }
    }

    /// <summary>
    /// Result of a single package/feature/capability operation on a mounted image
    /// </summary>
    public class ImageOperationResult
    {
        /// <summary>
        /// Name of the image the operation targeted
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image the operation targeted
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Path where the image is mounted
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// The item the operation applied to (package name, feature name, or capability name)
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// The operation that was performed
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// Whether the operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message when the operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        public override string ToString()
        {
            var status = Success ? "SUCCESS" : $"FAILED: {ErrorMessage}";
            return $"{Operation} {Target} on {ImageName}: {status}";
        }
    }
}
