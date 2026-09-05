using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
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
                bool success = nativeService.ApplyRegistryOperations(mountedImage.MountPath.FullName, operations, callbacks);

                if (success)
                {
                    result.SuccessfulOperations.AddRange(operations);
                    callbacks.Verbose?.Invoke($"Successfully applied all {operations.Length} registry operations to {mountedImage.ImageName}");
                }
                else
                {
                    // ApplyRegistryOperations returns false when at least one operation failed;
                    // without per-operation results we conservatively mark all as failed.
                    result.FailedOperations.AddRange(operations);

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
    }
}
