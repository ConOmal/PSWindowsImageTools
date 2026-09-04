using System;
using System.IO;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Represents a downloaded Windows ISO file
    /// </summary>
    public class WindowsISOFile
    {
        /// <summary>
        /// Local file where the ISO is stored
        /// </summary>
        public FileInfo LocalFile { get; set; } = null!;

        /// <summary>
        /// Whether the ISO has been successfully downloaded
        /// </summary>
        public bool IsDownloaded { get; set; }

        /// <summary>
        /// Whether the ISO file has been verified (hash calculated)
        /// </summary>
        public bool IsVerified { get; set; }

        /// <summary>
        /// SHA256 hash of the downloaded file, if verified
        /// </summary>
        public string? Hash { get; set; }

        /// <summary>
        /// When the ISO was downloaded
        /// </summary>
        public DateTime DownloadedAt { get; set; }

        /// <summary>
        /// Size of the downloaded file in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// The URL the ISO was downloaded from
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;

        /// <summary>
        /// The download info this file was resolved from, if any (absent when downloaded via a manual -Url)
        /// </summary>
        public WindowsISODownloadInfo? SourceDownloadInfo { get; set; }

        /// <summary>
        /// Any error message if the download failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets a human-readable file size string
        /// </summary>
        public string FileSizeFormatted
        {
            get
            {
                if (FileSize == 0) return "Unknown";

                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = FileSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        /// <summary>
        /// Returns a string representation of the ISO file
        /// </summary>
        public override string ToString()
        {
            var status = IsDownloaded ? (IsVerified ? "Verified" : "Downloaded") : "Not Downloaded";
            return $"{LocalFile?.Name ?? DownloadUrl} ({status}, {FileSizeFormatted})";
        }
    }
}
