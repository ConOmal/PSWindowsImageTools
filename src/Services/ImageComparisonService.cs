using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Captures inventory snapshots of mounted images (packages, features, capabilities,
    /// provisioned AppX, installed software) and compares snapshots to surface what changed.
    /// </summary>
    public class ImageComparisonService
    {
        private const string ServiceName = "ImageComparisonService";
        private readonly ModuleCallbacks _callbacks;

        public ImageComparisonService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Captures an inventory snapshot of a mounted image
        /// </summary>
        /// <param name="mountedImage">Mounted image to snapshot</param>
        /// <param name="imageService">Unified image service for DISM queries</param>
        /// <returns>Inventory snapshot</returns>
        public ImageSnapshot CaptureSnapshot(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            _callbacks.Verbose?.Invoke($"Capturing snapshot of [{mountedImage.ImageIndex}] {mountedImage.ImageName} from {mountPath}");

            var snapshot = new ImageSnapshot
            {
                ImageName = mountedImage.ImageName,
                ImageIndex = mountedImage.ImageIndex,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath,
                CapturedAt = DateTime.UtcNow
            };

            try
            {
                foreach (var package in imageService.GetPackages(mountPath))
                {
                    snapshot.Packages.Add(new SnapshotItem
                    {
                        Name = package.PackageName ?? string.Empty,
                        State = package.PackageState.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture packages: {ex.Message}");
            }

            try
            {
                foreach (var feature in imageService.GetFeatures(mountPath))
                {
                    snapshot.Features.Add(new SnapshotItem
                    {
                        Name = feature.FeatureName ?? string.Empty,
                        State = feature.State.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture features: {ex.Message}");
            }

            try
            {
                foreach (var capability in imageService.GetCapabilities(mountPath))
                {
                    snapshot.Capabilities.Add(new SnapshotItem
                    {
                        Name = capability.Name ?? string.Empty,
                        State = capability.State.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture capabilities: {ex.Message}");
            }

            try
            {
                foreach (var appx in imageService.GetProvisionedAppxPackages(mountPath))
                {
                    snapshot.AppxPackages.Add(new SnapshotItem
                    {
                        Name = appx.PackageName ?? string.Empty,
                        Detail = appx.DisplayName
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture provisioned AppX packages: {ex.Message}");
            }

            try
            {
                using var registryReader = new RegistryHiveReader(_callbacks);
                var softwareHivePath = RegistryHiveReader.GetSoftwareHivePath(mountPath);

                foreach (var software in registryReader.GetInstalledSoftware(softwareHivePath))
                {
                    snapshot.Software.Add(new SnapshotItem
                    {
                        Name = software.DisplayName,
                        State = software.DisplayVersion?.ToString(),
                        Detail = software.Publisher
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture installed software: {ex.Message}");
            }

            _callbacks.Verbose?.Invoke($"Snapshot captured: {snapshot.TotalItems} items");
            return snapshot;
        }

        /// <summary>
        /// Saves a snapshot to a JSON file
        /// </summary>
        /// <param name="snapshot">Snapshot to save</param>
        /// <param name="snapshotPath">Destination path</param>
        public static void SaveSnapshot(ImageSnapshot snapshot, string snapshotPath)
        {
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            File.WriteAllText(snapshotPath, json);
        }

        /// <summary>
        /// Loads a snapshot from a JSON file
        /// </summary>
        /// <param name="snapshotPath">Path to the snapshot JSON file</param>
        /// <returns>Loaded snapshot</returns>
        public static ImageSnapshot LoadSnapshot(string snapshotPath)
        {
            if (!File.Exists(snapshotPath))
            {
                throw new FileNotFoundException($"Snapshot file not found: {snapshotPath}");
            }

            var json = File.ReadAllText(snapshotPath);
            var snapshot = JsonConvert.DeserializeObject<ImageSnapshot>(json);

            return snapshot ?? throw new InvalidOperationException($"Snapshot file is empty or invalid: {snapshotPath}");
        }

        /// <summary>
        /// Compares two snapshots and reports additions, removals, and changes per category
        /// </summary>
        /// <param name="reference">Reference (before) snapshot</param>
        /// <param name="difference">Difference (after) snapshot</param>
        /// <returns>Comparison result</returns>
        public ImageComparisonResult Compare(ImageSnapshot reference, ImageSnapshot difference)
        {
            _callbacks.Verbose?.Invoke($"Comparing '{reference.ImageName}' vs '{difference.ImageName}'");

            var result = new ImageComparisonResult
            {
                ReferenceName = reference.ImageName,
                DifferenceName = difference.ImageName
            };

            result.Categories.Add(CompareCategory("Packages", reference.Packages, difference.Packages));
            result.Categories.Add(CompareCategory("Features", reference.Features, difference.Features));
            result.Categories.Add(CompareCategory("Capabilities", reference.Capabilities, difference.Capabilities));
            result.Categories.Add(CompareCategory("AppxPackages", reference.AppxPackages, difference.AppxPackages));
            result.Categories.Add(CompareCategory("Software", reference.Software, difference.Software));

            return result;
        }

        /// <summary>
        /// Compares one category of snapshot items
        /// </summary>
        private static CategoryDifference CompareCategory(string category, List<SnapshotItem> reference, List<SnapshotItem> difference)
        {
            var diff = new CategoryDifference { Category = category };

            var referenceByName = reference
                .Where(i => !string.IsNullOrEmpty(i.Name))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var differenceByName = difference
                .Where(i => !string.IsNullOrEmpty(i.Name))
                .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var name in differenceByName.Keys)
            {
                if (!referenceByName.ContainsKey(name))
                {
                    diff.Added.Add(differenceByName[name]);
                }
                else if (!ItemsEqual(referenceByName[name], differenceByName[name]))
                {
                    diff.Changed.Add(differenceByName[name]);
                }
            }

            foreach (var name in referenceByName.Keys)
            {
                if (!differenceByName.ContainsKey(name))
                {
                    diff.Removed.Add(referenceByName[name]);
                }
            }

            diff.Added.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            diff.Removed.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            diff.Changed.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            return diff;
        }

        private static bool ItemsEqual(SnapshotItem a, SnapshotItem b)
        {
            return string.Equals(a.State, b.State, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Detail, b.Detail, StringComparison.OrdinalIgnoreCase);
        }
    }
}
