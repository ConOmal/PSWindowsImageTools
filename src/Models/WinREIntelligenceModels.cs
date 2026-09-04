using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Intelligence report for the embedded WinRE image inside a mounted Windows image
    /// </summary>
    public class WinREIntelligenceReport
    {
        /// <summary>
        /// Mounted Windows image directory that was inspected
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Whether an embedded WinRE image was found at Windows\System32\Recovery\Winre.wim
        /// </summary>
        public bool WinREPresent { get; set; }

        /// <summary>
        /// Full path to the embedded WinRE image (resolved even when absent)
        /// </summary>
        public string WinREPath { get; set; } = string.Empty;

        /// <summary>
        /// Size of the embedded WinRE image in bytes (0 when absent)
        /// </summary>
        public long SizeBytes { get; set; }

        /// <summary>
        /// Size of the embedded WinRE image in megabytes (rounded to 2 decimals)
        /// </summary>
        public double SizeMB { get; set; }

        /// <summary>
        /// Last write time of the embedded WinRE image, in UTC
        /// </summary>
        public DateTime LastModifiedUtc { get; set; }

        /// <summary>
        /// Whether the WIM file header was successfully parsed
        /// </summary>
        public bool WimHeaderParsed { get; set; }

        /// <summary>
        /// WIM format version as a string (e.g. "13.0"), when the header parsed
        /// </summary>
        public string WimVersion { get; set; } = string.Empty;

        /// <summary>
        /// Number of images inside the embedded WinRE WIM (from the header), when the header parsed
        /// </summary>
        public long ImageCount { get; set; }

        /// <summary>
        /// WIM compression type name (e.g. "LZX", "XPRESS", "LZMS"), when the header parsed
        /// </summary>
        public string CompressionType { get; set; } = string.Empty;

        /// <summary>
        /// Parsed WIM file header details, or null when the header could not be read
        /// </summary>
        public WimHeaderInfo? WimHeader { get; set; }

        /// <summary>
        /// Display name of the first image inside the embedded WinRE WIM, recovered from the
        /// raw XML metadata (populated only when inspecting with -Detailed)
        /// </summary>
        public string? XmlImageDisplayName { get; set; }

        /// <summary>
        /// Returns a string representation of the report
        /// </summary>
        public override string ToString()
        {
            if (!WinREPresent)
            {
                return $"{ImagePath}: no embedded WinRE image";
            }

            var version = string.IsNullOrEmpty(WimVersion) ? "unknown version" : $"WIM {WimVersion}";
            return $"{ImagePath}: WinRE {SizeMB:F1} MB, {version}, {ImageCount} image(s), last modified {LastModifiedUtc:u}";
        }
    }

    /// <summary>
    /// Parsed fields from the fixed 208-byte WIM file header (pure, no DISM)
    /// </summary>
    public class WimHeaderInfo
    {
        /// <summary>
        /// Whether the byte buffer was long enough and carried the MSWIM signature
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Header size as declared by the file (expected 208)
        /// </summary>
        public uint HeaderSize { get; set; }

        /// <summary>
        /// Raw version DWORD from the header
        /// </summary>
        public uint Version { get; set; }

        /// <summary>
        /// Version major component (high 16 bits of Version)
        /// </summary>
        public int VersionMajor { get; set; }

        /// <summary>
        /// Version minor component (low 16 bits of Version)
        /// </summary>
        public int VersionMinor { get; set; }

        /// <summary>
        /// Formatted version string ("{Major}.{Minor}", e.g. "13.0")
        /// </summary>
        public string VersionText { get; set; } = string.Empty;

        /// <summary>
        /// Raw flags DWORD from the header
        /// </summary>
        public uint Flags { get; set; }

        /// <summary>
        /// Raw compression type value (1=LZX, 2=XPRESS, 3=LZMS)
        /// </summary>
        public uint CompressionType { get; set; }

        /// <summary>
        /// Friendly compression type name, or "Unknown (n)"
        /// </summary>
        public string CompressionTypeName { get; set; } = string.Empty;

        /// <summary>
        /// WIM identifier GUID from the header
        /// </summary>
        public Guid WimGuid { get; set; }

        /// <summary>
        /// 1-based part number of this WIM within a split set
        /// </summary>
        public int PartNumber { get; set; }

        /// <summary>
        /// Total number of parts in the split WIM set
        /// </summary>
        public int NumberOfParts { get; set; }

        /// <summary>
        /// Number of images stored in this WIM file
        /// </summary>
        public long ImageCount { get; set; }

        /// <summary>
        /// 1-based index of the bootable image, or 0 when none is marked bootable
        /// </summary>
        public long BootIndex { get; set; }

        /// <summary>
        /// File offset of the XML metadata (used for the -Detailed display-name read)
        /// </summary>
        public long MetadataOffset { get; set; }

        /// <summary>
        /// Total size of the WIM file as declared by the header
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Returns a string representation of the header
        /// </summary>
        public override string ToString() =>
            $"WIM {VersionText}, {CompressionTypeName} compression, {ImageCount} image(s), boot index {BootIndex}, part {PartNumber}/{NumberOfParts}";
    }
}