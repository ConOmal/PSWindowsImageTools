using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Resolved, time-limited direct download link for an official Windows ISO
    /// </summary>
    public class WindowsISODownloadInfo
    {
        /// <summary>
        /// Time-limited direct CDN download URL
        /// </summary>
        public Uri Url { get; set; } = null!;

        /// <summary>
        /// Suggested local file name for the download
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Requested edition (e.g. "Windows 11")
        /// </summary>
        public string Edition { get; set; } = string.Empty;

        /// <summary>
        /// Requested architecture (e.g. "x64", "arm64")
        /// </summary>
        public string Architecture { get; set; } = string.Empty;

        /// <summary>
        /// Requested language SKU (e.g. "English International")
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Returns a human-readable summary
        /// </summary>
        public override string ToString()
        {
            return $"{Edition} {Architecture} ({Language})";
        }
    }
}
