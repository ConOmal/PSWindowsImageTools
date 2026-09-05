using System;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;
using PSWindowsImageTools.Services.Native;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// WIM Export Service based on Microsoft's actual Export-WindowsImage implementation
    /// Uses native WIM API calls exactly like Microsoft does
    /// </summary>
    public class WimExportService : IDisposable
    {
        private const string ServiceName = "WimExportService";
        private bool _disposed = false;

        /// <summary>
        /// Exports an image using the same method as Microsoft's Export-WindowsImage cmdlet
        /// </summary>
        /// <param name="sourceImagePath">Source WIM/ESD file path</param>
        /// <param name="destinationImagePath">Destination WIM file path</param>
        /// <param name="sourceIndex">Source image index</param>
        /// <param name="sourceName">Optional source image name</param>
        /// <param name="destinationName">Optional destination image name</param>
        /// <param name="compressionType">Compression type (None, Fast, Max, Recovery)</param>
        /// <param name="checkIntegrity">Verify file integrity</param>
        /// <param name="setBootable">Set as bootable image</param>
        /// <param name="scratchDirectory">Temporary directory for operations</param>
        /// <param name="progressCallback">Progress reporting callback</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if export succeeded</returns>
        public bool ExportImage(
            string sourceImagePath,
            string destinationImagePath,
            uint sourceIndex,
            string? destinationName = null,
            string compressionType = "Max",
            bool checkIntegrity = false,
            bool setBootable = false,
            string? scratchDirectory = null,
            Action<int, string>? progressCallback = null,
            PSCmdlet? cmdlet = null,
            string? destinationDescription = null,
            long? splitSize = null)
        {
            // Validate parameters
            if (string.IsNullOrEmpty(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"Source image file not found: {sourceImagePath}");
                return false;
            }

            if (string.IsNullOrEmpty(destinationImagePath))
            {
                LoggingService.WriteError(cmdlet, ServiceName, "Destination image path cannot be empty");
                return false;
            }

            IntPtr sourceWimHandle = IntPtr.Zero;
            IntPtr destinationWimHandle = IntPtr.Zero;
            IntPtr sourceImageHandle = IntPtr.Zero;
            IntPtr destinationImageHandle = IntPtr.Zero;
            bool inPlaceExport = false;

            try
            {
                var exportStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                    "WIM Export", $"Index {sourceIndex} from {Path.GetFileName(sourceImagePath)} to {Path.GetFileName(destinationImagePath)}");

                // Check if source and destination are the same (in-place export)
                if (string.Equals(Path.GetFullPath(sourceImagePath), Path.GetFullPath(destinationImagePath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    inPlaceExport = true;
                    LoggingService.WriteVerbose(cmdlet, ServiceName, "Performing in-place export");
                }

                // Open source WIM file
                uint sourceAccess = WimNativeApi.GENERIC_READ;
                if (inPlaceExport)
                    sourceAccess |= 0x40000000; // Add write access for in-place

                sourceWimHandle = WimNativeApi.WIMCreateFile(
                    sourceImagePath,
                    sourceAccess,
                    WimNativeApi.OPEN_EXISTING,
                    WimNativeApi.GetWimCreateFlags(checkIntegrity, false) | WimNativeApi.WIM_FLAG_SHARE_WRITE,
                    0,
                    out uint sourceCreationResult);

                if (sourceWimHandle == IntPtr.Zero)
                {
                    var error = WimNativeApi.GetLastErrorAsHResult();
                    LoggingService.WriteError(cmdlet, ServiceName, $"Failed to open source WIM file. Error: 0x{error:X8}");
                    return false;
                }

                // Set scratch directory if provided
                if (!string.IsNullOrEmpty(scratchDirectory))
                {
                    if (!WimNativeApi.WIMSetTemporaryPath(sourceWimHandle, scratchDirectory!))
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, "Failed to set scratch directory");
                    }
                }

                // Resolve source image index if name was provided (not needed here, caller passes index)
                uint actualSourceIndex = sourceIndex;

                // Load source image
                sourceImageHandle = WimNativeApi.WIMLoadImage(sourceWimHandle, actualSourceIndex);
                if (sourceImageHandle == IntPtr.Zero)
                {
                    var error = WimNativeApi.GetLastErrorAsHResult();
                    LoggingService.WriteError(cmdlet, ServiceName, $"Failed to load source image {actualSourceIndex}. Error: 0x{error:X8}");
                    return false;
                }

                // Handle destination WIM
                if (inPlaceExport)
                {
                    destinationWimHandle = sourceWimHandle;
                }
                else
                {
                    // Create destination directory if needed
                    var destinationDir = Path.GetDirectoryName(destinationImagePath);
                    if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                        LoggingService.WriteVerbose(cmdlet, ServiceName, $"Created destination directory: {destinationDir}");
                    }

                    // Determine compression type
                    uint compression = WimNativeApi.ParseCompressionType(compressionType);

                    // Create destination WIM file
                    uint destFlags = WimNativeApi.GetWimCreateFlags(checkIntegrity, false);
                    if (compression == WimNativeApi.WIM_COMPRESS_LZMS)
                        destFlags |= 0x20000000; // Chunked flag for LZMS

                    destinationWimHandle = WimNativeApi.WIMCreateFile(
                        destinationImagePath,
                        WimNativeApi.GENERIC_READ | WimNativeApi.GENERIC_WRITE,
                        WimNativeApi.CREATE_ALWAYS,
                        destFlags,
                        compression,
                        out uint destCreationResult);

                    if (destinationWimHandle == IntPtr.Zero)
                    {
                        var error = WimNativeApi.GetLastErrorAsHResult();
                        LoggingService.WriteError(cmdlet, ServiceName, $"Failed to create destination WIM file. Error: 0x{error:X8}");
                        return false;
                    }

                    // Set scratch directory for destination
                    if (!string.IsNullOrEmpty(scratchDirectory))
                    {
                        WimNativeApi.WIMSetTemporaryPath(destinationWimHandle, scratchDirectory!);
                    }
                }

                // Register progress callback
                WimNativeApi.WimCallback? nativeCallback = null;
                if (progressCallback != null)
                {
                    nativeCallback = (messageId, wParam, lParam, userData) =>
                    {
                        if (messageId == 0x9448) // WIM_MSG_PROGRESS
                        {
                            var current = (uint)wParam.ToInt32();
                            var total = (uint)lParam.ToInt32();
                            if (total > 0)
                            {
                                var percentage = (int)((current * 100) / total);
                                progressCallback(percentage, $"Exporting image: {percentage}%");
                            }
                        }
                        return 0;
                    };

                    var callbackResult = WimNativeApi.WIMRegisterMessageCallback(destinationWimHandle, nativeCallback, IntPtr.Zero);
                    if (callbackResult == uint.MaxValue)
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, "Failed to register progress callback");
                    }
                }

                // Perform the actual export
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Performing WIM export operation");
                uint exportFlags = WimNativeApi.GetWimExportFlags();
                bool exportResult = WimNativeApi.WIMExportImage(sourceImageHandle, destinationWimHandle, exportFlags);

                if (!exportResult)
                {
                    var error = WimNativeApi.GetLastErrorAsHResult();
                    LoggingService.WriteError(cmdlet, ServiceName, $"WIM export operation failed. Error: 0x{error:X8}");
                    return false;
                }

                // Unregister callback
                if (nativeCallback != null)
                {
                    WimNativeApi.WIMUnregisterMessageCallback(destinationWimHandle, nativeCallback);
                }

                // Post‑export split handling
                if (splitSize.HasValue && splitSize.Value > 0)
                {
                    try
                    {
                        var chunkSizeBytes = splitSize.Value * 1024 * 1024;
                        var fileInfo = new FileInfo(destinationImagePath);
                        using var sourceStream = new FileStream(destinationImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        int partNumber = 1;
                        byte[] buffer = new byte[81920]; // 80 KB buffer
                        while (sourceStream.Position < fileInfo.Length)
                        {
                            var partPath = Path.Combine(fileInfo.DirectoryName ?? "", $"{Path.GetFileNameWithoutExtension(fileInfo.Name)}.part{partNumber:D3}{fileInfo.Extension}");
                            using var partStream = new FileStream(partPath, FileMode.Create, FileAccess.Write);
                            long bytesRemaining = Math.Min(chunkSizeBytes, fileInfo.Length - sourceStream.Position);
                            while (bytesRemaining > 0)
                            {
                                int read = sourceStream.Read(buffer, 0, (int)Math.Min(buffer.Length, bytesRemaining));
                                if (read <= 0) break;
                                partStream.Write(buffer, 0, read);
                                bytesRemaining -= read;
                            }
                            partNumber++;
                        }
                        // Optionally delete the original large file if split successful
                        try
                        {
                            File.Delete(destinationImagePath);
                        }
                        catch { /* ignore */ }
                    }
                    catch (Exception splitEx)
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to split exported WIM file: {splitEx.Message}");
                    }
                }

                // Determine the index of the newly exported image (always appended last)
                uint exportedImageIndex = GetWimImageCount(destinationImagePath, cmdlet);
                if (exportedImageIndex == 0)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, "Could not determine exported image count; skipping post-export operations");
                }
                else
                {
                    // Set bootable flag if requested
                    if (setBootable)
                    {
                        if (WimNativeApi.WIMSetBootImage(destinationWimHandle, exportedImageIndex))
                        {
                            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Set image {exportedImageIndex} as bootable");
                        }
                        else
                        {
                            LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to set image {exportedImageIndex} as bootable");
                        }
                    }

                    // Set destination name/description if provided
                    if (!string.IsNullOrEmpty(destinationName) || !string.IsNullOrEmpty(destinationDescription))
                    {
                        if (SetExportedImageName(destinationWimHandle, exportedImageIndex, destinationName, destinationDescription, cmdlet))
                        {
                            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Updated destination image {exportedImageIndex} name/description");
                        }
                    }
                }

                LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, "WIM Export", exportStartTime,
                    $"Index {sourceIndex} from {Path.GetFileName(sourceImagePath)} to {Path.GetFileName(destinationImagePath)}");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"WIM export failed: {ex.Message}", ex);
                return false;
            }
            finally
            {
                // Clean up handles in reverse order
                if (destinationImageHandle != IntPtr.Zero)
                    WimNativeApi.WIMCloseHandle(destinationImageHandle);

                if (sourceImageHandle != IntPtr.Zero)
                    WimNativeApi.WIMCloseHandle(sourceImageHandle);

                if (destinationWimHandle != IntPtr.Zero && !inPlaceExport)
                    WimNativeApi.WIMCloseHandle(destinationWimHandle);

                if (sourceWimHandle != IntPtr.Zero)
                    WimNativeApi.WIMCloseHandle(sourceWimHandle);
            }
        }

        /// <summary>
        /// Resolves a WIM image index by image name using the DISM API
        /// </summary>
        /// <param name="imagePath">Path to the WIM/ESD file</param>
        /// <param name="imageName">Image name to find (case-insensitive)</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Image index, or null when not found</returns>
        private static uint? GetWimIndexByName(string imagePath, string imageName, PSCmdlet? cmdlet)
        {
            try
            {
                var imageInfo = Microsoft.Dism.DismApi.GetImageInfo(imagePath);

                foreach (var image in imageInfo)
                {
                    if (string.Equals(image.ImageName, imageName, StringComparison.OrdinalIgnoreCase))
                    {
                        return (uint)image.ImageIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to resolve image name '{imageName}': {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Counts images in a WIM/ESD file using the DISM API
        /// </summary>
        /// <param name="imagePath">Path to the WIM/ESD file</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Image count, or 0 when the count could not be determined</returns>
        private static uint GetWimImageCount(string imagePath, PSCmdlet? cmdlet)
        {
            try
            {
                return (uint)Microsoft.Dism.DismApi.GetImageInfo(imagePath).Count;
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to count images in {imagePath}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Sets the name and/or description of an exported image via the native WIM API
        /// </summary>
        /// <param name="destinationWimHandle">Open destination WIM handle</param>
        /// <param name="imageIndex">Index of the exported image</param>
        /// <param name="destinationName">New image name, or null to keep the existing name</param>
        /// <param name="destinationDescription">New description, or null to keep the existing description</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True when all requested updates succeeded</returns>
        private static bool SetExportedImageName(IntPtr destinationWimHandle, uint imageIndex, string? destinationName, string? destinationDescription, PSCmdlet? cmdlet)
        {
            var success = true;

            if (!string.IsNullOrEmpty(destinationName))
            {
                if (WimNativeApi.WIMSetImageName(destinationWimHandle, imageIndex, destinationName!))
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName, $"Set destination name: {destinationName}");
                }
                else
                {
                    var error = WimNativeApi.GetLastErrorAsHResult();
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to set destination name. Error: 0x{error:X8}");
                    success = false;
                }
            }

            if (!string.IsNullOrEmpty(destinationDescription))
            {
                if (WimNativeApi.WIMSetImageDescription(destinationWimHandle, imageIndex, destinationDescription!))
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName, $"Set destination description: {destinationDescription}");
                }
                else
                {
                    var error = WimNativeApi.GetLastErrorAsHResult();
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to set destination description. Error: 0x{error:X8}");
                    success = false;
                }
            }

            return success;
        }

        /// <summary>
        /// Disposes the WIM export service
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
