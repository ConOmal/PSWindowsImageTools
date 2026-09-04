using System;
using System.Collections.Generic;
using System.Linq;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A single item captured in an image snapshot (package, feature, capability, AppX, or software)
    /// </summary>
    public class SnapshotItem
    {
        /// <summary>
        /// Primary identifier (package name, feature name, capability name, or software display name)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// State (DISM state) or detail (version, display name)
        /// </summary>
        public string? State { get; set; }

        /// <summary>
        /// Additional detail (e.g., software publisher)
        /// </summary>
        public string? Detail { get; set; }

        public override string ToString()
        {
            return State == null ? Name : $"{Name} ({State})";
        }
    }

    /// <summary>
    /// Point-in-time snapshot of a Windows image's inventory
    /// </summary>
    public class ImageSnapshot
    {
        /// <summary>
        /// Name of the snapshotted image
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the snapshotted image
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Source WIM/ESD path
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Mount directory the snapshot was captured from
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// When the snapshot was captured
        /// </summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// DISM packages
        /// </summary>
        public List<SnapshotItem> Packages { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Windows features
        /// </summary>
        public List<SnapshotItem> Features { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Capabilities (Features on Demand)
        /// </summary>
        public List<SnapshotItem> Capabilities { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Provisioned AppX packages
        /// </summary>
        public List<SnapshotItem> AppxPackages { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Installed software (from the offline SOFTWARE hive)
        /// </summary>
        public List<SnapshotItem> Software { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Driver packages present in the image
        /// </summary>
        public List<SnapshotItem> Drivers { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Total captured items
        /// </summary>
        public int TotalItems => Packages.Count + Features.Count + Capabilities.Count + AppxPackages.Count + Software.Count + Drivers.Count;

        public override string ToString()
        {
            return $"[{ImageIndex}] {ImageName}: {TotalItems} items captured {CapturedAt:yyyy-MM-dd HH:mm}UTC";
        }
    }

    /// <summary>
    /// Differences for one inventory category
    /// </summary>
    public class CategoryDifference
    {
        /// <summary>
        /// Category name (Packages, Features, Capabilities, AppxPackages, Software)
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Items present in the difference snapshot but not in the reference
        /// </summary>
        public List<SnapshotItem> Added { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Items present in the reference but not in the difference snapshot
        /// </summary>
        public List<SnapshotItem> Removed { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Items present in both but with different state/detail
        /// </summary>
        public List<SnapshotItem> Changed { get; set; } = new List<SnapshotItem>();

        /// <summary>
        /// Total differences in this category
        /// </summary>
        public int Count => Added.Count + Removed.Count + Changed.Count;

        public override string ToString()
        {
            return $"{Category}: +{Added.Count} -{Removed.Count} ~{Changed.Count}";
        }
    }

    /// <summary>
    /// Result of comparing two image snapshots
    /// </summary>
    public class ImageComparisonResult
    {
        /// <summary>
        /// Name of the reference (before) image
        /// </summary>
        public string ReferenceName { get; set; } = string.Empty;

        /// <summary>
        /// Name of the difference (after) image
        /// </summary>
        public string DifferenceName { get; set; } = string.Empty;

        /// <summary>
        /// Per-category differences
        /// </summary>
        public List<CategoryDifference> Categories { get; set; } = new List<CategoryDifference>();

        /// <summary>
        /// Total differences across all categories
        /// </summary>
        public int TotalDifferences => Categories.Sum(c => c.Count);

        /// <summary>
        /// Whether the snapshots are identical
        /// </summary>
        public bool AreIdentical => TotalDifferences == 0;

        public override string ToString()
        {
            return AreIdentical
                ? $"'{ReferenceName}' vs '{DifferenceName}': identical"
                : $"'{ReferenceName}' vs '{DifferenceName}': {TotalDifferences} differences";
        }
    }
}
