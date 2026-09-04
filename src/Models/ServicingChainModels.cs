using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// The role a servicing package plays in the update chain
    /// </summary>
    public enum ServicingPackageRole
    {
        ServicingStackUpdate,
        CumulativeUpdate,
        SafeOSUpdate,
        DotNetUpdate,
        Other
    }

    /// <summary>
    /// How confident the classification of a package's role is. Verified = confirmed real
    /// naming convention (SSU/LCU); Heuristic = best-effort pattern match, may be wrong.
    /// </summary>
    public enum ClassificationConfidence
    {
        Verified,
        Heuristic
    }

    /// <summary>
    /// A single classified servicing package
    /// </summary>
    public class ServicingPackageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public ServicingPackageRole Role { get; set; }
        public ClassificationConfidence Confidence { get; set; }
        public int Build { get; set; }
        public int Revision { get; set; }
        public DateTime? InstallTime { get; set; }

        public override string ToString() => $"{Role} ({Confidence}): {PackageName} [{Build}.{Revision}]";
    }

    /// <summary>
    /// Servicing chain analysis for a mounted Windows image: classified update packages and
    /// whether the SSU/LCU pairing looks consistent
    /// </summary>
    public class ServicingChainReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<ServicingPackageInfo> Packages { get; set; } = new List<ServicingPackageInfo>();
        public ServicingPackageInfo? ServicingStackUpdate { get; set; }
        public ServicingPackageInfo? CumulativeUpdate { get; set; }
        public bool OrderingValid { get; set; } = true;
        public List<string> Issues { get; set; } = new List<string>();

        public override string ToString() =>
            $"{ImageName}: {Packages.Count} servicing package(s), OrderingValid={OrderingValid}";
    }
}
