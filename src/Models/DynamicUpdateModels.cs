using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Windows media Dynamic Update types, ordered by the apply sequence used by
    /// Invoke-MediaDynamicUpdate (Servicing Stack, SafeOS, Cumulative, Setup)
    /// </summary>
    public enum DynamicUpdateType
    {
        /// <summary>
        /// Servicing Stack Update (SSU)
        /// </summary>
        ServicingStack,

        /// <summary>
        /// Safe OS Dynamic Update (updates the recovery environment)
        /// </summary>
        SafeOS,

        /// <summary>
        /// Latest Cumulative Update (LCU)
        /// </summary>
        Cumulative,

        /// <summary>
        /// Setup Dynamic Update (updated setup binaries for the media)
        /// </summary>
        Setup
    }

    /// <summary>
    /// Represents a Dynamic Update discovered in the Microsoft Update Catalog
    /// for a Windows build. Output of Get-WindowsDynamicUpdate; one instance per
    /// discovered update (by default: the latest one per Dynamic Update type)
    /// </summary>
    public class WindowsDynamicUpdate
    {
        /// <summary>
        /// The Dynamic Update type this catalog result was classified as
        /// </summary>
        public DynamicUpdateType UpdateType { get; set; }

        /// <summary>
        /// The Windows build number discovery was performed for (e.g., 26100)
        /// </summary>
        public int Build { get; set; }

        /// <summary>
        /// The OS label resolved from the build and used as the catalog title
        /// fragment (e.g., "Windows 11 Version 24H2", "Windows Server 2025")
        /// </summary>
        public string OSLabel { get; set; } = string.Empty;

        /// <summary>
        /// Knowledge Base article number (e.g., "KB5044285")
        /// </summary>
        public string KBNumber { get; set; } = string.Empty;

        /// <summary>
        /// Title of the update as returned by the catalog
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Catalog update identifier (used by Get-WindowsUpdateDownloadUrl plumbing)
        /// </summary>
        public string UpdateId { get; set; } = string.Empty;

        /// <summary>
        /// Target architecture, normalized (e.g., "x64", "x86", "ARM64")
        /// </summary>
        public string Architecture { get; set; } = string.Empty;

        /// <summary>
        /// Package version reported by the catalog (e.g., "10.0.26100.2314")
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Catalog classification (e.g., "Security Updates", "Updates")
        /// </summary>
        public string Classification { get; set; } = string.Empty;

        /// <summary>
        /// When the catalog entry was last modified
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Size of the update package in bytes
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Resolved download URL for the update package; null when resolution
        /// failed (a warning is emitted in that case)
        /// </summary>
        public Uri? DownloadUrl { get; set; }

        /// <summary>
        /// Returns a string representation of the Dynamic Update
        /// </summary>
        public override string ToString()
        {
            return $"{KBNumber} - {Title} ({UpdateType})";
        }

        /// <summary>
        /// Gets a human-readable size string (mirrors WindowsUpdateCatalogResult)
        /// </summary>
        public string SizeFormatted
        {
            get
            {
                if (Size == 0) return "Unknown";

                string[] sizes = { "B", "KB", "MB", "GB" };
                double len = Size;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }
    }
}
