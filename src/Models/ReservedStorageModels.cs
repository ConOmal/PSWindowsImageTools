using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Reserved-storage state of a Windows image as reported by DISM
    /// </summary>
    public enum ReservedStorageState
    {
        /// <summary>
        /// Reserved storage is enabled (space is reserved for servicing operations)
        /// </summary>
        Enabled,

        /// <summary>
        /// Reserved storage is disabled (no space is reserved)
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Reserved-storage state of a mounted Windows image, from Get-WindowsImageReservedStorage
    /// </summary>
    public class WindowsImageReservedStorage
    {
        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Reserved-storage state of the image
        /// </summary>
        public ReservedStorageState State { get; set; }

        /// <summary>
        /// Human-readable state ("Enabled" or "Disabled")
        /// </summary>
        public string StateText => State.ToString();

        /// <summary>
        /// Reserved-storage size in bytes when DISM reports one; null otherwise.
        /// Current DISM /Get-ReservedStorageState output reports state only, so
        /// this is normally null and is surfaced defensively if a size line appears.
        /// </summary>
        public long? SizeBytes { get; set; }

        /// <summary>
        /// Reserved-storage size in MB when SizeBytes is available; null otherwise
        /// </summary>
        public double? SizeMB => SizeBytes.HasValue ? Math.Round(SizeBytes.Value / 1024.0 / 1024.0, 2) : (double?)null;

        /// <summary>
        /// Returns a string representation of the reserved-storage state
        /// </summary>
        public override string ToString()
        {
            return $"{StateText} at {ImagePath}";
        }
    }

    /// <summary>
    /// Result of enabling or disabling reserved storage in a mounted Windows image,
    /// from Set-WindowsImageReservedStorage
    /// </summary>
    public class ReservedStorageOperationResult
    {
        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// The operation that was performed (EnableReservedStorage or DisableReservedStorage)
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// The reserved-storage state that was requested
        /// </summary>
        public ReservedStorageState RequestedState { get; set; }

        /// <summary>
        /// Whether the operation succeeded
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Exit code of the underlying dism.exe invocation
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// Error message when the operation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Returns a string representation of the operation result
        /// </summary>
        public override string ToString()
        {
            var status = Success ? "succeeded" : $"failed: {ErrorMessage}";
            return $"{Operation} on {ImagePath}: {status} (exit {ExitCode})";
        }
    }
}