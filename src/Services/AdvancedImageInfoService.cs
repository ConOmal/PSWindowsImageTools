using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Dedicated service for gathering advanced image information from mounted images
    /// Orchestrates different specialized services for reading registry and system information
    /// Does NOT handle mounting - expects images to already be mounted
    /// </summary>
    public class AdvancedImageInfoService : IDisposable
    {
        private const string ServiceName = "AdvancedImageInfoService";
        private bool _disposed = false;

        /// <summary>
        /// Gets advanced registry information from an already-mounted image
        /// Reads Windows version info, installed software, and Windows Update configuration
        /// Does NOT handle mounting/unmounting - expects the image to already be mounted at mountPath
        /// </summary>
        /// <param name="mountPath">Path where the image is already mounted</param>
        /// <param name="cmdlet">Cmdlet for logging</param>
        /// <returns>Advanced image information collected from the mounted image</returns>
        public WindowsImageAdvancedInfo GetAdvancedImageInfo(string mountPath, PSCmdlet cmdlet)
        {
            return GetAdvancedImageInfo(mountPath, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Gets advanced registry information from an already-mounted image using callbacks
        /// </summary>
        /// <param name="mountPath">Path where the image is already mounted</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>Advanced image information collected from the mounted image</returns>
        public WindowsImageAdvancedInfo GetAdvancedImageInfo(string mountPath, ModuleCallbacks callbacks)
        {
            var advancedInfo = new WindowsImageAdvancedInfo();

            try
            {
                callbacks.Verbose?.Invoke($"Reading advanced registry information from mounted image at: {mountPath}");

                // Verify the mount path exists and contains a mounted Windows image
                if (!Directory.Exists(mountPath))
                {
                    throw new DirectoryNotFoundException($"Mount path does not exist: {mountPath}");
                }

                var windowsDir = Path.Combine(mountPath, "Windows");
                if (!Directory.Exists(windowsDir))
                {
                    throw new InvalidOperationException($"No Windows directory found at mount path. Ensure the image is properly mounted at: {mountPath}");
                }

                // Read registry information using RegistryHiveReader
                using var registryReader = new RegistryHiveReader(callbacks);
                var softwareHivePath = RegistryHiveReader.GetSoftwareHivePath(mountPath);

                // Get Windows version info
                var versionInfo = registryReader.GetWindowsVersionInfo(softwareHivePath);
                foreach (var kvp in versionInfo)
                {
                    advancedInfo.CurrentVersion[kvp.Key] = kvp.Value;
                }

                // Get installed software as proper Software objects
                advancedInfo.Software = registryReader.GetInstalledSoftware(softwareHivePath);

                // Get Windows Update configuration
                var wuConfig = registryReader.GetWindowsUpdateConfiguration(softwareHivePath);
                foreach (var kvp in wuConfig)
                {
                    advancedInfo.WindowsUpdate[kvp.Key] = kvp.Value;
                }

                callbacks.Verbose?.Invoke("Successfully collected advanced registry information from mounted image");
            }
            catch (Exception ex)
            {
                callbacks.Error?.Invoke(ex, $"Failed to get advanced registry information: {ex.Message}");
                throw;
            }

            return advancedInfo;
        }



        /// <summary>
        /// Disposes the advanced image info service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
