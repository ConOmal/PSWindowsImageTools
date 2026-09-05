using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// One capability (Feature on Demand) package discovered in a FoD payload source
    /// directory. All capability metadata is parsed from the .cab file name per the
    /// documented convention — it is filename-derived, not read from inside the cab.
    /// </summary>
    public class CapabilityRepositoryEntry
    {
        /// <summary>
        /// Name of the .cab file (with extension)
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Full path of the .cab file
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Capability name parsed from the file name (the part after the
        /// Microsoft-Windows- prefix, before the first ~ separator)
        /// </summary>
        public string CapabilityName { get; set; } = string.Empty;

        /// <summary>
        /// Publisher token parsed from the file name (opaque build-revision identifier)
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Architecture parsed from the file name (e.g. amd64, x86, arm64);
        /// 'neutral' when the segment is empty
        /// </summary>
        public string Architecture { get; set; } = string.Empty;

        /// <summary>
        /// Language parsed from the file name (e.g. en-us); 'neutral' when the
        /// segment is empty (language-neutral package)
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Version parsed from the file name (e.g. 10.0.26100.1); empty when the
        /// file name carries no version segment
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Size of the .cab file in bytes
        /// </summary>
        public long FileSize { get; set; }

        public override string ToString()
        {
            return $"{CapabilityName} ({Architecture}, {Language}, {Version})";
        }
    }

    /// <summary>
    /// Summary of every capability package in a FoD payload source directory that
    /// shares one capability name (produced by the -GroupByName switch)
    /// </summary>
    public class CapabilityRepositoryGroup
    {
        /// <summary>
        /// Capability name shared by the grouped packages
        /// </summary>
        public string CapabilityName { get; set; } = string.Empty;

        /// <summary>
        /// Number of .cab packages grouped under this capability name
        /// </summary>
        public int PackageCount { get; set; }

        /// <summary>
        /// Distinct architectures of the grouped packages (sorted)
        /// </summary>
        public List<string> Architectures { get; set; } = new List<string>();

        /// <summary>
        /// Distinct languages of the grouped packages (sorted)
        /// </summary>
        public List<string> Languages { get; set; } = new List<string>();

        /// <summary>
        /// Distinct versions of the grouped packages (sorted)
        /// </summary>
        public List<string> Versions { get; set; } = new List<string>();

        /// <summary>
        /// Combined size of the grouped .cab files in bytes
        /// </summary>
        public long TotalSize { get; set; }

        public override string ToString()
        {
            return $"{CapabilityName} ({PackageCount} package(s))";
        }
    }
}
