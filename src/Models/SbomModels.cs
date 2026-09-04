using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Software Bill of Materials for a Windows image, built from a captured ImageSnapshot
    /// </summary>
    public class SbomReport
    {
        public string WindowsVersion { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<SnapshotItem> Packages { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Drivers { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Features { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Capabilities { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Applications { get; set; } = new List<SnapshotItem>();

        public override string ToString() =>
            $"{ImageName}: {Packages.Count} packages, {Drivers.Count} drivers, {Applications.Count} applications";
    }
}
