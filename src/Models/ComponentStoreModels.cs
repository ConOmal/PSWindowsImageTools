using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Component-store (WinSxS) analysis for a mounted Windows image
    /// </summary>
    public class ComponentStoreReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public double WinSxSSizeMB { get; set; }
        public int TotalPackages { get; set; }
        public int InstalledPackages { get; set; }
        public int SupersededPackages { get; set; }
        public int PendingPackages { get; set; }
        public List<string> SupersededPackageNames { get; set; } = new List<string>();
        public List<string> Issues { get; set; } = new List<string>();

        public override string ToString() =>
            $"{ImageName}: {TotalPackages} packages, {SupersededPackages} superseded, WinSxS {WinSxSSizeMB:F1} MB";
    }

    /// <summary>
    /// Result of a component-store cleanup (StartComponentCleanup / ResetBase) operation
    /// </summary>
    public class ComponentStoreCleanupResult
    {
        public ComponentStoreReport Before { get; set; } = new ComponentStoreReport();
        public ComponentStoreReport? After { get; set; }
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success => ExitCode == 0;

        public override string ToString() =>
            $"{Before.ImageName}: cleanup {(Success ? "succeeded" : "failed")} (exit {ExitCode})";
    }
}
