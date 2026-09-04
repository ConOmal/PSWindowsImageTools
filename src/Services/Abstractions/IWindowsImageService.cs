using System;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Unified Windows image operations: query (managed DISM), mount/unmount (native DISM with
    /// progress callbacks), and export (native WIM API). Owns a single DISM API lifecycle.
    /// </summary>
    public interface IWindowsImageService : IDisposable
    {
        /// <summary>
        /// Initializes the DISM API (idempotent)
        /// </summary>
        void Initialize();

        /// <summary>
        /// Enumerates images in a WIM/ESD file
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns>List of basic image information</returns>
        System.Collections.Generic.List<WindowsImageInfo> GetImageInfo(string imagePath);

        /// <summary>
        /// Mounts an image. Throws on failure with the underlying DISM error.
        /// </summary>
        /// <param name="imageFilePath">Path to the WIM/ESD file</param>
        /// <param name="mountPath">Mount directory</param>
        /// <param name="imageIndex">Image index within the file</param>
        /// <param name="readOnly">Mount read-only (default: false)</param>
        /// <param name="progressCallback">Optional progress callback: percent (-1 indeterminate), status</param>
        void MountImage(string imageFilePath, string mountPath, uint imageIndex, bool readOnly = false, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Unmounts an image. Throws on failure with the underlying DISM error.
        /// </summary>
        /// <param name="mountPath">Mount directory</param>
        /// <param name="commitChanges">Commit changes (save) or discard</param>
        /// <param name="progressCallback">Optional progress callback: percent (-1 indeterminate), status</param>
        void UnmountImage(string mountPath, bool commitChanges = false, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Mounts an image, reads advanced registry information, and unmounts (unless skipDismount).
        /// </summary>
        /// <param name="imagePath">Path to the WIM/ESD file</param>
        /// <param name="imageIndex">Image index to mount</param>
        /// <param name="mountPath">Mount directory</param>
        /// <param name="skipDismount">Keep the image mounted and return mount info</param>
        /// <param name="readWrite">Mount read-write</param>
        /// <param name="progressCallback">Optional mount progress callback</param>
        /// <returns>Advanced info plus mount info when skipDismount is true</returns>
        (WindowsImageAdvancedInfo AdvancedInfo, MountedWindowsImage? MountedImage) GetAdvancedImageInfo(
            string imagePath, int imageIndex, string mountPath, bool skipDismount = false, bool readWrite = false,
            Action<int, string>? progressCallback = null);

        /// <summary>
        /// Exports an image from one WIM/ESD file to another WIM file
        /// </summary>
        /// <param name="sourcePath">Source WIM/ESD file path</param>
        /// <param name="destinationPath">Destination WIM file path</param>
        /// <param name="sourceIndex">Source image index</param>
        /// <param name="compressionType">Compression type for the output</param>
        /// <param name="progressCallback">Optional progress callback</param>
        /// <returns>True when the export succeeded</returns>
        bool ExportImage(string sourcePath, string destinationPath, int sourceIndex, string compressionType, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Lists packages in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <returns>Package information</returns>
        System.Collections.Generic.List<Microsoft.Dism.DismPackage> GetPackages(string mountPath);

        /// <summary>
        /// Lists Windows features in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <returns>Feature information</returns>
        System.Collections.Generic.List<Microsoft.Dism.DismFeature> GetFeatures(string mountPath);

        /// <summary>
        /// Lists capabilities (Features on Demand) in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <returns>Capability information</returns>
        System.Collections.Generic.List<Microsoft.Dism.DismCapability> GetCapabilities(string mountPath);

        /// <summary>
        /// Lists provisioned AppX packages in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <returns>Provisioned AppX package information</returns>
        System.Collections.Generic.List<Microsoft.Dism.DismAppxPackage> GetProvisionedAppxPackages(string mountPath);

        /// <summary>
        /// Adds a package (.cab/.msu) to a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="packagePath">Path to the package file</param>
        /// <param name="ignoreCheck">Skip applicability checks</param>
        /// <param name="preventPending">Prevent installation if there are pending operations</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void AddPackage(string mountPath, string packagePath, bool ignoreCheck = false, bool preventPending = false, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Removes a package by name from a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="packageName">Name of the package to remove</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void RemovePackageByName(string mountPath, string packageName, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Enables a Windows feature in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="featureName">Name of the feature to enable</param>
        /// <param name="enableAll">Enable all parent features</param>
        /// <param name="sourcePaths">Optional source paths for feature payload</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void EnableFeature(string mountPath, string featureName, bool enableAll = false, System.Collections.Generic.List<string>? sourcePaths = null, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Disables a Windows feature in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="featureName">Name of the feature to disable</param>
        /// <param name="removePayload">Remove the feature payload</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void DisableFeature(string mountPath, string featureName, bool removePayload = false, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Adds a capability (Feature on Demand) to a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="capabilityName">Name of the capability to add</param>
        /// <param name="limitAccess">Prevent Windows Update as a source</param>
        /// <param name="sourcePaths">Optional source paths for the capability payload</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void AddCapability(string mountPath, string capabilityName, bool limitAccess = false, System.Collections.Generic.List<string>? sourcePaths = null, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Removes a capability from a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="capabilityName">Name of the capability to remove</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void RemoveCapability(string mountPath, string capabilityName, Action<int, string>? progressCallback = null);

        /// <summary>
        /// Removes a provisioned AppX package from a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="packageName">Name of the AppX package to remove</param>
        void RemoveProvisionedAppxPackage(string mountPath, string packageName);

        /// <summary>
        /// Adds all drivers from a directory (optionally recursive) to a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="driverDirectory">Directory containing INF drivers</param>
        /// <param name="forceUnsigned">Force installation of unsigned drivers</param>
        /// <param name="recursive">Search subdirectories recursively</param>
        /// <param name="progressCallback">Optional progress callback</param>
        void AddDriversFromDirectory(string mountPath, string driverDirectory, bool forceUnsigned = false, bool recursive = true, Action<int, string>? progressCallback = null);
    }
}
