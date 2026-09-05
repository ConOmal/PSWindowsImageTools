using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Rolled-up compliance verdict of a Windows image compliance manifest:
    /// Unknown when no security baseline report was supplied, otherwise the
    /// baseline's own verdict
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum WindowsImageComplianceStatus
    {
        /// <summary>
        /// No security baseline report was included in the manifest
        /// </summary>
        Unknown,

        /// <summary>
        /// Every observed baseline entry was compliant
        /// </summary>
        Compliant,

        /// <summary>
        /// At least one baseline entry is non-compliant or not present
        /// </summary>
        NonCompliant
    }

    /// <summary>
    /// Image identity recorded in a compliance manifest, copied from the source snapshot
    /// </summary>
    public class ComplianceManifestImageIdentity
    {
        /// <summary>
        /// Name of the image
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Index of the image within its WIM/ESD
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// Source WIM/ESD path
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Mount directory the snapshot was captured from (null for file-based snapshots)
        /// </summary>
        public string? MountPath { get; set; }

        /// <summary>
        /// When the snapshot was captured (UTC)
        /// </summary>
        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() =>
            $"[{ImageIndex}] {ImageName} from {ImagePath}";
    }

    /// <summary>
    /// Aggregate per-category item counts of a snapshot. Counts only — the item
    /// lists stay in the snapshot JSON / SBOM (the generic-inventory non-goal).
    /// </summary>
    public class ComplianceManifestInventorySummary
    {
        /// <summary>
        /// DISM package count
        /// </summary>
        public int Packages { get; set; }

        /// <summary>
        /// Windows feature count
        /// </summary>
        public int Features { get; set; }

        /// <summary>
        /// Capability (Features on Demand) count
        /// </summary>
        public int Capabilities { get; set; }

        /// <summary>
        /// Provisioned AppX package count
        /// </summary>
        public int AppxPackages { get; set; }

        /// <summary>
        /// Installed software entry count
        /// </summary>
        public int Software { get; set; }

        /// <summary>
        /// Driver package count
        /// </summary>
        public int Drivers { get; set; }

        /// <summary>
        /// Captured registry drift value count
        /// </summary>
        public int Registry { get; set; }

        /// <summary>
        /// Total captured items across all categories
        /// </summary>
        public int TotalItems { get; set; }

        public override string ToString() =>
            $"{TotalItems} items ({Packages} packages, {Features} features, {Drivers} drivers)";
    }

    /// <summary>
    /// One flattened, string-typed baseline observation in a compliance manifest
    /// </summary>
    public class ComplianceManifestBaselineEntry
    {
        /// <summary>
        /// Hive containing the value (HKLM\SOFTWARE, HKLM\SYSTEM or HKU\DefaultUser)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Key path relative to the hive root
        /// </summary>
        public string KeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Registry value name
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Expected value data as a normalized string
        /// </summary>
        public string ExpectedValue { get; set; } = string.Empty;

        /// <summary>
        /// Expected registry value kind (e.g. DWord, String)
        /// </summary>
        public string ValueType { get; set; } = string.Empty;

        /// <summary>
        /// Why this entry is part of the baseline
        /// </summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>
        /// Compliance verdict (Compliant, NonCompliant or NotPresent)
        /// </summary>
        public string State { get; set; } = string.Empty;

        /// <summary>
        /// Observed value data (normalized string); empty when the value is not present
        /// </summary>
        public string ObservedValue { get; set; } = string.Empty;

        /// <summary>
        /// Observed registry value type as reported by the hive parser
        /// </summary>
        public string ObservedValueType { get; set; } = string.Empty;

        public override string ToString() =>
            $"{Hive}\\{KeyPath}\\{ValueName}: {State} (expected '{ExpectedValue}')";
    }

    /// <summary>
    /// Security baseline policy evaluation embedded in a compliance manifest,
    /// projected from WindowsImageSecurityBaselineReport
    /// </summary>
    public class ComplianceManifestBaselineSection
    {
        /// <summary>
        /// Name of the image the baseline was evaluated against
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Mount path the baseline evaluation ran against
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// True when every observed entry was compliant
        /// </summary>
        public bool IsCompliant { get; set; }

        /// <summary>
        /// Number of baseline entries observed
        /// </summary>
        public int TotalEntries { get; set; }

        /// <summary>
        /// Entries whose observed value matches the baseline
        /// </summary>
        public int CompliantCount { get; set; }

        /// <summary>
        /// Entries whose observed value differs from the baseline
        /// </summary>
        public int NonCompliantCount { get; set; }

        /// <summary>
        /// Entries whose key or value is absent
        /// </summary>
        public int NotPresentCount { get; set; }

        /// <summary>
        /// Flattened per-entry observations, in baseline order
        /// </summary>
        public List<ComplianceManifestBaselineEntry> Entries { get; set; } = new List<ComplianceManifestBaselineEntry>();

        public override string ToString() =>
            IsCompliant
                ? $"Security baseline: compliant ({CompliantCount}/{TotalEntries})"
                : $"Security baseline: not compliant ({CompliantCount}/{TotalEntries} compliant, {NonCompliantCount} non-compliant, {NotPresentCount} not present)";
    }

    /// <summary>
    /// Servicing chain verdict embedded in a compliance manifest,
    /// projected from ServicingChainReport
    /// </summary>
    public class ComplianceManifestServicingSection
    {
        /// <summary>
        /// Name of the image the servicing chain was analyzed from
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Source image path of the servicing analysis
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// When the servicing chain report was generated (UTC)
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Number of classified servicing packages
        /// </summary>
        public int PackageCount { get; set; }

        /// <summary>
        /// Servicing stack update summary (package ToString), null when unclassified
        /// </summary>
        public string? ServicingStackUpdate { get; set; }

        /// <summary>
        /// Cumulative update summary (package ToString), null when unclassified
        /// </summary>
        public string? CumulativeUpdate { get; set; }

        /// <summary>
        /// Whether the SSU/LCU ordering looks consistent
        /// </summary>
        public bool OrderingValid { get; set; }

        /// <summary>
        /// Servicing chain issues reported by the analysis
        /// </summary>
        public List<string> Issues { get; set; } = new List<string>();

        public override string ToString() =>
            $"Servicing chain: {PackageCount} package(s), OrderingValid={OrderingValid}, {Issues.Count} issue(s)";
    }

    /// <summary>
    /// Audit artifact combining an image snapshot's inventory summary with optional
    /// security-baseline and servicing-chain evaluations plus tool provenance.
    /// This is the policy-evaluation + provenance document — item lists stay in the
    /// snapshot JSON and SBOM (the generic-inventory non-goal).
    /// </summary>
    public class WindowsImageComplianceManifest
    {
        /// <summary>
        /// Manifest schema version
        /// </summary>
        public string ManifestVersion { get; set; } = "1.0";

        /// <summary>
        /// When the manifest was generated (UTC)
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Producing tool name
        /// </summary>
        public string ToolName { get; set; } = "PSWindowsImageTools";

        /// <summary>
        /// Producing tool (assembly) version
        /// </summary>
        public string ToolVersion { get; set; } = string.Empty;

        /// <summary>
        /// Identity of the image the manifest describes
        /// </summary>
        public ComplianceManifestImageIdentity Image { get; set; } = new ComplianceManifestImageIdentity();

        /// <summary>
        /// Aggregate inventory counts of the source snapshot
        /// </summary>
        public ComplianceManifestInventorySummary Inventory { get; set; } = new ComplianceManifestInventorySummary();

        /// <summary>
        /// Rolled-up compliance verdict (Unknown without a baseline report)
        /// </summary>
        public WindowsImageComplianceStatus OverallStatus { get; set; } = WindowsImageComplianceStatus.Unknown;

        /// <summary>
        /// Security baseline evaluation, null when no report was supplied
        /// </summary>
        public ComplianceManifestBaselineSection? SecurityBaseline { get; set; }

        /// <summary>
        /// Servicing chain evaluation, null when no report was supplied
        /// </summary>
        public ComplianceManifestServicingSection? ServicingChain { get; set; }

        /// <summary>
        /// Whether a security baseline evaluation is embedded
        /// </summary>
        public bool HasSecurityBaseline => SecurityBaseline != null;

        /// <summary>
        /// Whether a servicing chain evaluation is embedded
        /// </summary>
        public bool HasServicingChain => ServicingChain != null;

        public override string ToString() =>
            $"Compliance manifest for {Image.ImageName}: {OverallStatus} ({Inventory.TotalItems} items, baseline: {HasSecurityBaseline}, servicing: {HasServicingChain})";
    }
}
