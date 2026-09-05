using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services.Native;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Unified Windows image service: managed DISM for queries, native DISM for mount/unmount with
    /// real progress callbacks, native WIM API for export. One DISM API lifecycle per instance.
    /// Mount/unmount failures throw InvalidOperationException carrying the underlying DISM error.
    /// </summary>
    public class WindowsImageService : IWindowsImageService
    {
        private const string ServiceName = "WindowsImageService";
        private const int HresultImageInUse = unchecked((int)0xC142010C);
        private readonly ModuleCallbacks _callbacks;
        private bool _dismInitialized;
        private bool _disposed;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public WindowsImageService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Creates the service routing output to a PowerShell cmdlet
        /// </summary>
        /// <param name="cmdlet">Cmdlet instance, or null for silent callbacks</param>
        public static WindowsImageService ForCmdlet(System.Management.Automation.PSCmdlet? cmdlet)
        {
            return new WindowsImageService(ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <inheritdoc />
        public void Initialize()
        {
            if (!_dismInitialized)
            {
                try
                {
                    DismApi.Initialize(DismLogLevel.LogErrors);
                    _dismInitialized = true;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to initialize DISM API. HRESULT: 0x{ex.HResult:X8}", ex);
                }
            }
        }

        /// <inheritdoc />
        public List<WindowsImageInfo> GetImageInfo(string imagePath)
        {
            Initialize();
            var imageInfoList = new List<WindowsImageInfo>();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting image information from: {imagePath}");

                var imageInfoCollection = DismApi.GetImageInfo(imagePath);

                foreach (var dismImageInfo in imageInfoCollection)
                {
                    var imageInfo = new WindowsImageInfo
                    {
                        Index = (int)dismImageInfo.ImageIndex,
                        Name = dismImageInfo.ImageName ?? string.Empty,
                        Description = dismImageInfo.ImageDescription ?? string.Empty,
                        Size = (long)dismImageInfo.ImageSize,
                        Architecture = ConvertArchitectureToDisplayString(dismImageInfo.Architecture.ToString()),
                        ProductType = dismImageInfo.ProductType ?? string.Empty,
                        InstallationType = dismImageInfo.InstallationType ?? string.Empty,
                        Edition = dismImageInfo.EditionId ?? string.Empty,
                        Version = FormatUtilityService.ParseVersion(dismImageInfo.ProductVersion?.ToString() ?? string.Empty),
                        Build = dismImageInfo.ProductVersion?.Build.ToString() ?? string.Empty,
                        ServicePackLevel = dismImageInfo.SpLevel.ToString(),
                        DefaultLanguage = dismImageInfo.DefaultLanguage?.Name ?? string.Empty,
                        Languages = dismImageInfo.Languages?.Select(l => l.Name).ToList() ?? new List<string>(),
                        CreatedTime = DateTime.UtcNow, // DISM API doesn't provide creation time
                        ModifiedTime = DateTime.UtcNow, // DISM API doesn't provide modification time
                        SourcePath = imagePath,
                        SystemRoot = dismImageInfo.SystemRoot ?? string.Empty,
                        ProductSuite = dismImageInfo.ProductSuite ?? string.Empty,
                        SourceHash = string.Empty // Defer hash calculation until explicitly requested
                    };

                    imageInfoList.Add(imageInfo);
                }

                _callbacks.Verbose?.Invoke($"Found {imageInfoList.Count} images in file");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get image information: {ex.Message}");
                throw;
            }

            return imageInfoList;
        }

        /// <inheritdoc />
        public void MountImage(string imageFilePath, string mountPath, uint imageIndex, bool readOnly = false, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Mounting image {imageIndex} from {imageFilePath} to {mountPath} (ReadOnly: {readOnly})");

                if (!Directory.Exists(mountPath))
                {
                    Directory.CreateDirectory(mountPath);
                    _callbacks.Verbose?.Invoke($"Created mount directory: {mountPath}");
                }

                uint mountFlags = readOnly ? 1u : 0u; // DISM_MOUNT_READONLY = 1, DISM_MOUNT_READWRITE = 0

                DismNativeApi.DismMountImage(
                    imageFilePath,
                    mountPath,
                    imageIndex,
                    null, // imageName
                    DismNativeApi.ImageIdentifier.ImageIndex,
                    mountFlags,
                    IntPtr.Zero, // cancelEvent
                    WrapProgress(progressCallback, "Mounting image"),
                    IntPtr.Zero); // userData

                _callbacks.Verbose?.Invoke("Image mounted successfully using native API");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to mount image {imageIndex} from {imageFilePath}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void UnmountImage(string mountPath, bool commitChanges = false, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Unmounting image from {mountPath} (CommitChanges: {commitChanges})");

                try
                {
                    // Microsoft.Dism's wrapper handles the native call correctly; the raw
                    // DismNativeApi.DismUnmountImage P/Invoke consistently fails with 0xC142010C
                    // (WIM provider "could not commit changes during unmount") even for
                    // read-only discard unmounts that the DISM CLI performs without issue.
                    Microsoft.Dism.DismApi.UnmountImage(
                        mountPath,
                        commitChanges,
                        WrapDismProgress(progressCallback, "Unmounting image"));
                }
                catch (DismException ex) when (ex.HResult == HresultImageInUse)
                {
                    // Image is still in use - allow handles to release and retry once with force discard
                    _callbacks.Verbose?.Invoke("Image in use (0xC142010C), waiting and attempting force discard...");

                    System.Threading.Thread.Sleep(500);

                    Microsoft.Dism.DismApi.UnmountImage(mountPath, false);
                }

                _callbacks.Verbose?.Invoke("Image unmounted successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to unmount image from {mountPath}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public (WindowsImageAdvancedInfo AdvancedInfo, MountedWindowsImage? MountedImage) GetAdvancedImageInfo(
            string imagePath, int imageIndex, string mountPath, bool skipDismount = false, bool readWrite = false,
            Action<int, string>? progressCallback = null)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                throw new ArgumentException("Image path cannot be null or empty", nameof(imagePath));
            }

            if (string.IsNullOrWhiteSpace(mountPath))
            {
                throw new ArgumentException("Mount path cannot be null or empty", nameof(mountPath));
            }

            var advancedInfo = new WindowsImageAdvancedInfo();
            MountedWindowsImage? mountedImage = null;

            try
            {
                _callbacks.Verbose?.Invoke($"Mounting image {imageIndex} from {imagePath} to {mountPath} for advanced information collection");

                MountImage(imagePath, mountPath, (uint)imageIndex, readOnly: !readWrite, progressCallback: progressCallback);

                _callbacks.Verbose?.Invoke($"Image {imageIndex} successfully mounted to {mountPath}");

                // Create mounted image info if we're keeping it mounted
                if (skipDismount)
                {
                    mountedImage = new MountedWindowsImage
                    {
                        MountId = Guid.NewGuid().ToString(),
                        SourceImagePath = imagePath,
                        ImageIndex = imageIndex,
                        MountPath = new DirectoryInfo(mountPath),
                        Status = MountStatus.Mounted,
                        IsReadOnly = !readWrite,
                        MountedAt = DateTime.UtcNow
                    };

                    // Register for re-discovery across sessions
                    MountSessionService.Register(mountedImage);

                    _callbacks.Verbose?.Invoke($"Image will remain mounted for use with other cmdlets (MountId: {mountedImage.MountId})");
                }

                try
                {
                    // Read registry information from the mounted image
                    using var advancedInfoService = new AdvancedImageInfoService();
                    advancedInfo = advancedInfoService.GetAdvancedImageInfo(mountPath, _callbacks);
                }
                finally
                {
                    // Only unmount if not skipping dismount
                    if (!skipDismount)
                    {
                        try
                        {
                            UnmountImage(mountPath, false);
                            CleanupMountDirectory(mountPath);
                        }
                        catch (Exception ex)
                        {
                            _callbacks.Warning?.Invoke($"Failed to unmount image: {ex.Message}");
                        }
                    }
                }

                return (advancedInfo, mountedImage);
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get advanced image information: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public bool ExportImage(string sourcePath, string destinationPath, int sourceIndex, string compressionType, Action<int, string>? progressCallback = null)
        {
            try
            {
                _callbacks.Verbose?.Invoke($"Exporting image index {sourceIndex} from {Path.GetFileName(sourcePath)} to {Path.GetFileName(destinationPath)}");

                using var wimExportService = new WimExportService();

                var result = wimExportService.ExportImage(
                    sourceImagePath: sourcePath,
                    destinationImagePath: destinationPath,
                    sourceIndex: (uint)sourceIndex,
                    compressionType: compressionType,
                    checkIntegrity: false,
                    setBootable: false,
                    scratchDirectory: System.IO.Path.GetTempPath(),
                    progressCallback: progressCallback,
                    cmdlet: null);

                if (!result)
                {
                    _callbacks.Error?.Invoke(new InvalidOperationException("Image export failed"), "Image export failed");
                }

                return result;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Export operation failed: {ex.Message}");
                return false;
            }
        }

        /// <inheritdoc />
        public List<DismPackage> GetPackages(string mountPath)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting packages from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var packages = DismApi.GetPackages(session).ToList();

                _callbacks.Verbose?.Invoke($"Found {packages.Count} packages");
                return packages;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get packages: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public List<DismFeature> GetFeatures(string mountPath)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting features from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var features = DismApi.GetFeatures(session).ToList();

                _callbacks.Verbose?.Invoke($"Found {features.Count} features");
                return features;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get features: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public List<DismCapability> GetCapabilities(string mountPath)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting capabilities from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var capabilities = DismApi.GetCapabilities(session).ToList();

                _callbacks.Verbose?.Invoke($"Found {capabilities.Count} capabilities");
                return capabilities;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get capabilities: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public List<DismAppxPackage> GetProvisionedAppxPackages(string mountPath)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting provisioned AppX packages from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var appxPackages = DismApi.GetProvisionedAppxPackages(session).ToList();

                _callbacks.Verbose?.Invoke($"Found {appxPackages.Count} provisioned AppX packages");
                return appxPackages;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get provisioned AppX packages: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public void AddPackage(string mountPath, string packagePath, bool ignoreCheck = false, bool preventPending = false, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Adding package {packagePath} to mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.AddPackage(session, packagePath, ignoreCheck, preventPending, WrapNativeProgress(progressCallback));

                _callbacks.Verbose?.Invoke($"Package {packagePath} added successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add package {packagePath}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void RemovePackageByName(string mountPath, string packageName, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Removing package {packageName} from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.RemovePackageByName(session, packageName, WrapNativeProgress(progressCallback));

                _callbacks.Verbose?.Invoke($"Package {packageName} removed successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove package {packageName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void EnableFeature(string mountPath, string featureName, bool enableAll = false, List<string>? sourcePaths = null, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Enabling feature {featureName} in mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.EnableFeature(session, featureName, false, enableAll, sourcePaths ?? new List<string>(), WrapNativeProgress(progressCallback));

                _callbacks.Verbose?.Invoke($"Feature {featureName} enabled successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to enable feature {featureName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void DisableFeature(string mountPath, string featureName, bool removePayload = false, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Disabling feature {featureName} in mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.DisableFeature(session, featureName, null!, removePayload, WrapNativeProgress(progressCallback));

                _callbacks.Verbose?.Invoke($"Feature {featureName} disabled successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to disable feature {featureName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void AddCapability(string mountPath, string capabilityName, bool limitAccess = false, List<string>? sourcePaths = null, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Adding capability {capabilityName} in mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.AddCapability(session, capabilityName, limitAccess, sourcePaths ?? new List<string>(), WrapNativeProgress(progressCallback), null!);

                _callbacks.Verbose?.Invoke($"Capability {capabilityName} added successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add capability {capabilityName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void RemoveCapability(string mountPath, string capabilityName, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Removing capability {capabilityName} from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.RemoveCapability(session, capabilityName, WrapNativeProgress(progressCallback), null!);

                _callbacks.Verbose?.Invoke($"Capability {capabilityName} removed successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove capability {capabilityName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void RemoveProvisionedAppxPackage(string mountPath, string packageName)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Removing provisioned AppX package {packageName} from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.RemoveProvisionedAppxPackage(session, packageName);

                _callbacks.Verbose?.Invoke($"Provisioned AppX package {packageName} removed successfully");
            }
            catch (Exception ex)
            {
                    throw new InvalidOperationException($"Failed to remove provisioned AppX package {packageName}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public void AddProvisionedAppxPackage(string mountPath, string appPath, List<string> dependencyPackages, string? licensePath = null, string? customDataPath = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Provisioning AppX package {appPath} into mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.AddProvisionedAppxPackage(session, appPath, dependencyPackages, licensePath ?? string.Empty, customDataPath ?? string.Empty);

                _callbacks.Verbose?.Invoke($"AppX package {appPath} provisioned successfully");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to provision AppX package {appPath}: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public void AddDriversFromDirectory(string mountPath, string driverDirectory, bool forceUnsigned = false, bool recursive = true, Action<int, string>? progressCallback = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Adding drivers from {driverDirectory} to mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.AddDriversEx(session, driverDirectory, forceUnsigned, recursive);

                _callbacks.Verbose?.Invoke($"Drivers from {driverDirectory} added successfully");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add drivers from {driverDirectory}: {ex.Message}", ex);
            }
        }

        /// <inheritdoc />
        public List<DismDriverPackage> GetDrivers(string mountPath, bool allDrivers = false)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting drivers from mounted image at {mountPath} (allDrivers: {allDrivers})");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var drivers = DismApi.GetDrivers(session, allDrivers).ToList();

                _callbacks.Verbose?.Invoke($"Found {drivers.Count} drivers");
                return drivers;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get drivers: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public void RemoveDriver(string mountPath, string publishedName)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Removing driver {publishedName} from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.RemoveDriver(session, publishedName);

                _callbacks.Verbose?.Invoke($"Driver {publishedName} removed successfully");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to remove driver {publishedName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Wraps a percent/status callback into a Microsoft.Dism progress callback that never throws
        /// </summary>
        private static DismProgressCallback? WrapNativeProgress(Action<int, string>? progressCallback)
        {
            if (progressCallback == null)
            {
                return null;
            }

            return progress =>
            {
                try
                {
                    progressCallback(progress.Current, $"{progress.Current}%");
                }
                catch
                {
                    // Never throw from the native callback thread
                }
            };
        }

        /// <summary>
        /// Wraps a percent/status callback into a native callback that never throws on the native thread
        /// </summary>
        private static DismNativeApi.ProgressCallback? WrapProgress(Action<int, string>? progressCallback, string operation)
        {
            if (progressCallback == null)
            {
                return null;
            }

            return (current, total, userData) =>
            {
                try
                {
                    if (total > 0)
                    {
                        var percentage = (int)((current * 100) / total);
                        progressCallback(percentage, $"{operation}: {percentage}%");
                    }
                    else
                    {
                        progressCallback(-1, $"{operation}...");
                    }
                }
                catch
                {
                    // Never throw exceptions from native callback thread
                }
            };
        }

        /// <summary>
        /// Wraps a percent/status callback into a Microsoft.Dism progress callback that never throws on the native thread
        /// </summary>
        private static Microsoft.Dism.DismProgressCallback? WrapDismProgress(Action<int, string>? progressCallback, string operation)
        {
            if (progressCallback == null)
            {
                return null;
            }

            return progress =>
            {
                try
                {
                    if (progress.Total > 0)
                    {
                        var percentage = (int)((progress.Current * 100) / progress.Total);
                        progressCallback(percentage, $"{operation}: {percentage}%");
                    }
                    else
                    {
                        progressCallback(-1, $"{operation}...");
                    }
                }
                catch
                {
                    // Never throw exceptions from native callback thread
                }
            };
        }

        /// <summary>
        /// Cleans up mount directory and parent GUID folder after dismount
        /// </summary>
        private void CleanupMountDirectory(string mountPath)
        {
            try
            {
                if (Directory.Exists(mountPath))
                {
                    // Remove the mount directory (e.g., .../GUID/1)
                    Directory.Delete(mountPath, true);
                    _callbacks.Verbose?.Invoke($"Cleaned up mount directory: {mountPath}");

                    // Get the parent GUID directory (e.g., .../GUID)
                    var parentDir = Directory.GetParent(mountPath);
                    if (parentDir != null && parentDir.Exists && IsDirectoryEmptyOrContainsOnlyEmptyDirectories(parentDir.FullName))
                    {
                        Directory.Delete(parentDir.FullName, true);
                        _callbacks.Verbose?.Invoke($"Cleaned up GUID directory: {parentDir.FullName}");
                    }
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to clean up mount directory {mountPath}: {ex.Message}");
            }
        }

        private static bool IsDirectoryEmptyOrContainsOnlyEmptyDirectories(string directoryPath)
        {
            try
            {
                return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories).Length == 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ConvertArchitectureToDisplayString(string dismArchitecture)
        {
            return dismArchitecture?.ToUpperInvariant() switch
            {
                "AMD64" => "x64",
                "X86" => "x86",
                "ARM" => "ARM",
                "ARM64" => "ARM64",
                "IA64" => "IA64",
                _ => dismArchitecture ?? "Unknown"
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_dismInitialized)
                {
                    try { DismApi.Shutdown(); } catch { /* best effort */ }
                    _dismInitialized = false;
                }

                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
