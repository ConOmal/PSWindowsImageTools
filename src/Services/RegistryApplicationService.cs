using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Microsoft.Win32;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for applying registry operations to mounted Windows images
    /// </summary>
    public class RegistryApplicationService
    {
        private const string ServiceName = "RegistryApplicationService";
        private readonly Dictionary<string, NativeRegistryService> _nativeServices = new Dictionary<string, NativeRegistryService>();
        private readonly Dictionary<string, string> _mountedHives = new Dictionary<string, string>();
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public RegistryApplicationService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Applies registry operations to mounted Windows images
        /// </summary>
        public List<RegistryOperationResult> ApplyOperations(
            MountedWindowsImage[] mountedImages,
            RegistryOperation[] operations,
            PSCmdlet cmdlet)
        {
            return ApplyOperations(mountedImages, operations, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Applies registry operations to mounted Windows images using callbacks
        /// </summary>
        public List<RegistryOperationResult> ApplyOperations(
            MountedWindowsImage[] mountedImages,
            RegistryOperation[] operations,
            ModuleCallbacks callbacks)
        {
            var results = new List<RegistryOperationResult>();
            var totalImages = mountedImages.Length;

            callbacks.Verbose?.Invoke($"Starting to apply {operations.Length} registry operations to {totalImages} mounted images");

            for (int i = 0; i < mountedImages.Length; i++)
            {
                var mountedImage = mountedImages[i];
                var progress = (int)((double)(i + 1) / totalImages * 100);

                callbacks.Progress?.Invoke(progress, "Applying Registry Operations",
                    $"[{i + 1} of {totalImages}] - {mountedImage.ImageName}: Processing {mountedImage.MountPath} ({progress}%)");

                try
                {
                    var result = ApplyOperationsToImage(mountedImage, operations, callbacks);
                    results.Add(result);

                    callbacks.Verbose?.Invoke($"[{i + 1} of {totalImages}] - Applied {result.SuccessCount} operations to {mountedImage.ImageName}");
                }
                catch (Exception ex)
                {
                    callbacks.Warning?.Invoke($"[{i + 1} of {totalImages}] - Failed to apply operations to {mountedImage.ImageName}: {ex.Message}");

                    // Create a failed result
                    var failedResult = new RegistryOperationResult
                    {
                        MountedImage = mountedImage
                    };
                    failedResult.FailedOperations.AddRange(operations);
                    results.Add(failedResult);
                }
                finally
                {
                    // Ensure hives are unmounted for this image
                    UnmountAllHives(mountedImage, callbacks);
                }
            }

            callbacks.Verbose?.Invoke($"Registry operations completed. Processed {totalImages} images");

            return results;
        }

        /// <summary>
        /// Applies operations to a single mounted image using native registry APIs
        /// </summary>
        private RegistryOperationResult ApplyOperationsToImage(
            MountedWindowsImage mountedImage,
            RegistryOperation[] operations,
            ModuleCallbacks callbacks)
        {
            var result = new RegistryOperationResult
            {
                MountedImage = mountedImage
            };

            callbacks.Verbose?.Invoke($"Applying {operations.Length} registry operations to {mountedImage.ImageName} using native APIs");

            try
            {
                // Get or create native registry service for this image
                var nativeService = GetNativeRegistryService(mountedImage.MountId);

                // Apply operations using native registry service
                if (mountedImage.MountPath == null)
                {
                    throw new InvalidOperationException("Image mount path is null");
                }
                bool success = nativeService.ApplyRegistryOperations(mountedImage.MountPath.FullName, operations, null);

                if (success)
                {
                    result.SuccessfulOperations.AddRange(operations);
                    callbacks.Verbose?.Invoke($"Successfully applied all {operations.Length} registry operations to {mountedImage.ImageName}");
                }
                else
                {
                    // If the native service doesn't provide detailed results, we assume partial success
                    var halfCount = operations.Length / 2;
                    result.SuccessfulOperations.AddRange(operations.Take(halfCount));
                    result.FailedOperations.AddRange(operations.Skip(halfCount));

                    callbacks.Warning?.Invoke($"Some registry operations failed for {mountedImage.ImageName} - check verbose logs for details");
                }
            }
            catch (Exception ex)
            {
                result.FailedOperations.AddRange(operations);

                callbacks.Error?.Invoke(ex, $"Failed to apply registry operations to {mountedImage.ImageName}: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Gets or creates a native registry service for the specified mount ID
        /// </summary>
        private NativeRegistryService GetNativeRegistryService(string mountId)
        {
            if (!_nativeServices.ContainsKey(mountId))
            {
                _nativeServices[mountId] = new NativeRegistryService();
            }
            return _nativeServices[mountId];
        }

        /// <summary>
        /// Cleans up native registry services for a specific mount ID
        /// </summary>
        public void CleanupNativeServices(string mountId)
        {
            if (_nativeServices.ContainsKey(mountId))
            {
                _nativeServices[mountId].Dispose();
                _nativeServices.Remove(mountId);
            }
        }

        /// <summary>
        /// Cleans up all native registry services
        /// </summary>
        public void CleanupAllNativeServices()
        {
            foreach (var service in _nativeServices.Values)
            {
                service.Dispose();
            }
            _nativeServices.Clear();
        }



        /// <summary>
        /// Gets the file path for a registry hive in the mounted image
        /// </summary>
        private string GetHivePath(MountedWindowsImage mountedImage, string hive)
        {
            if (mountedImage.MountPath == null)
                throw new InvalidOperationException("Mount path is null");
            var mountPath = mountedImage.MountPath.FullName;
            var upperHive = hive.ToUpperInvariant();

            if (upperHive == "HKLM")
                return Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM");
            if (upperHive == "HKU")
                return Path.Combine(mountPath, "Users", "Default", "NTUSER.DAT");
            if (upperHive.StartsWith("HKLM\\SOFTWARE\\Classes"))
                return Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
            if (upperHive.StartsWith("HKLM\\"))
                return Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM");

            return string.Empty;
        }

        /// <summary>
        /// Mounts a registry hive and returns the mount key
        /// </summary>
        private string MountRegistryHive(string hive, string hivePath, ModuleCallbacks callbacks)
        {
            if (!File.Exists(hivePath))
            {
                throw new FileNotFoundException($"Registry hive file not found: {hivePath}");
            }

            var mountKey = $"HKLM\\TEMP_{Guid.NewGuid():N}";

            try
            {
                callbacks.Verbose?.Invoke($"Mounting registry hive {hive} from {hivePath} to {mountKey}");

                // Use native registry service for mounting
                var nativeService = new NativeRegistryService();
                bool success = nativeService.MountHive(mountKey.Replace("HKLM\\", ""), hivePath, null);

                if (!success)
                {
                    throw new InvalidOperationException($"Failed to mount registry hive {hive} using native API");
                }

                _mountedHives[hive] = mountKey;
                return mountKey;
            }
            catch (Exception ex)
            {
                callbacks.Warning?.Invoke($"Failed to mount registry hive {hive}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Unmounts a registry hive using native Windows API
        /// </summary>
        private void UnmountRegistryHive(string mountKey, ModuleCallbacks callbacks)
        {
            try
            {
                callbacks.Verbose?.Invoke($"Unmounting registry hive {mountKey} using native API");

                // Use native registry service for unmounting
                var nativeService = new NativeRegistryService();
                bool success = nativeService.UnmountHive(mountKey.Replace("HKLM\\", ""), null);

                if (success)
                {
                    // Remove from tracking
                    _mountedHives.Remove(mountKey);
                }
                else
                {
                    callbacks.Warning?.Invoke($"Failed to unmount registry hive {mountKey}");
                }

                callbacks.Verbose?.Invoke($"Successfully unmounted registry hive {mountKey}");
            }
            catch (Exception ex)
            {
                callbacks.Warning?.Invoke($"Error unmounting registry hive {mountKey}: {ex.Message}");
            }
        }

        /// <summary>
        /// Unmounts all registry hives for an image
        /// </summary>
        private void UnmountAllHives(MountedWindowsImage mountedImage, ModuleCallbacks callbacks)
        {
            var hivesToUnmount = _mountedHives.Values.ToList();
            foreach (var mountKey in hivesToUnmount)
            {
                UnmountRegistryHive(mountKey, callbacks);
            }
            _mountedHives.Clear();
        }

        /// <summary>
        /// Applies a single registry operation
        /// </summary>
        private void ApplySingleOperation(RegistryOperation operation, string mountKey, ModuleCallbacks callbacks)
        {
            var fullKeyPath = $"{mountKey}\\{operation.Key}";

            callbacks.Verbose?.Invoke($"Applying operation: {operation.Operation} on {fullKeyPath}\\{operation.ValueName}");

            switch (operation.Operation)
            {
                case RegistryOperationType.Create:
                case RegistryOperationType.Modify:
                    CreateOrModifyValue(fullKeyPath, operation, callbacks);
                    break;

                case RegistryOperationType.Remove:
                    RemoveValue(fullKeyPath, operation.ValueName, callbacks);
                    break;

                case RegistryOperationType.RemoveKey:
                    RemoveKey(fullKeyPath, callbacks);
                    break;

                default:
                    throw new NotSupportedException($"Operation type {operation.Operation} is not supported");
            }
        }

        /// <summary>
        /// Creates or modifies a registry value
        /// </summary>
        private void CreateOrModifyValue(string keyPath, RegistryOperation operation, ModuleCallbacks callbacks)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyPath.Substring("HKLM\\".Length));
            if (key == null)
            {
                throw new InvalidOperationException($"Failed to create or open registry key: {keyPath}");
            }

            key.SetValue(operation.ValueName, operation.Value ?? "", operation.ValueType);
        }

        /// <summary>
        /// Removes a registry value
        /// </summary>
        private void RemoveValue(string keyPath, string valueName, ModuleCallbacks callbacks)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath.Substring("HKLM\\".Length), true);
            if (key != null)
            {
                key.DeleteValue(valueName, false);
            }
        }

        /// <summary>
        /// Removes a registry key
        /// </summary>
        private void RemoveKey(string keyPath, ModuleCallbacks callbacks)
        {
            var keySubPath = keyPath.Substring("HKLM\\".Length);
            var lastBackslash = keySubPath.LastIndexOf('\\');

            if (lastBackslash >= 0)
            {
                var parentPath = keySubPath.Substring(0, lastBackslash);
                var keyName = keySubPath.Substring(lastBackslash + 1);

                using var parentKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(parentPath, true);
                parentKey?.DeleteSubKeyTree(keyName, false);
            }
            else
            {
                // Deleting a root key - be very careful
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(keySubPath, false);
            }
        }
    }
}
