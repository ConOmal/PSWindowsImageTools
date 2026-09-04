using System;
using System.Collections.Generic;
using Microsoft.Dism;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A driver package present inside a mounted (offline) Windows image, distinct from
    /// INFDriverInfo which represents loose .inf files on disk before injection.
    /// </summary>
    public class WindowsImageDriverInfo
    {
        public string PublishedName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ClassDescription { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Version { get; set; } = string.Empty;
        public bool BootCritical { get; set; }
        public bool InBox { get; set; }
        public DismDriverSignature DriverSignature { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public string? CatalogFile { get; set; }

        public override string ToString() => $"{PublishedName} ({OriginalFileName}) v{Version} by {ProviderName}";
    }

    /// <summary>
    /// Result of comparing driver packages between two mounted images
    /// </summary>
    public class DriverComparisonResult
    {
        public string ReferenceName { get; set; } = string.Empty;
        public string CurrentName { get; set; } = string.Empty;
        public List<WindowsImageDriverInfo> Added { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> Removed { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> Superseded { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> DuplicateOem { get; set; } = new List<WindowsImageDriverInfo>();

        public override string ToString() =>
            $"'{ReferenceName}' vs '{CurrentName}': +{Added.Count} -{Removed.Count} superseded:{Superseded.Count} duplicates:{DuplicateOem.Count}";
    }
}
