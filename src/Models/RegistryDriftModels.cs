using System.Collections.Generic;
using System.Linq;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Capture mode for a registry drift key definition:
    /// direct values of the key, or sorted direct child subkey names as a signature
    /// </summary>
    public enum RegistryKeyCaptureMode
    {
        /// <summary>
        /// Capture the direct value-name/value pairs of the key
        /// </summary>
        Values,

        /// <summary>
        /// Capture the sorted direct child subkey names of the key as a signature
        /// </summary>
        SubKeyNames
    }

    /// <summary>
    /// One captured registry value of an image snapshot
    /// </summary>
    public class RegistrySnapshotValue
    {
        /// <summary>
        /// Hive the value was captured from (HKLM\SOFTWARE or HKLM\SYSTEM)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Key path relative to the hive root (e.g. Microsoft\Windows\CurrentVersion\Run)
        /// </summary>
        public string KeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Value name ((Default) for the default value; a child subkey name for SubKeyNames captures)
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Friendly registry value type (REG_*) or "SubKey" for name-signature entries
        /// </summary>
        public string ValueType { get; set; } = string.Empty;

        /// <summary>
        /// Normalized value data (empty for name-signature entries)
        /// </summary>
        public string ValueData { get; set; } = string.Empty;

        /// <summary>
        /// Stable diff identity: Hive\KeyPath\ValueName
        /// </summary>
        public string FullPath => $"{Hive}\\{KeyPath}\\{ValueName}";

        public override string ToString()
        {
            return string.IsNullOrEmpty(ValueData) ? FullPath : $"{FullPath} = {ValueData}";
        }
    }

    /// <summary>
    /// Definition of a registry key captured as part of a drift snapshot
    /// </summary>
    public class RegistryDriftKeyDefinition
    {
        /// <summary>
        /// Hive containing the key (HKLM\SOFTWARE or HKLM\SYSTEM)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Key path relative to the hive root
        /// </summary>
        public string KeyPath { get; set; } = string.Empty;

        /// <summary>
        /// What to capture from this key
        /// </summary>
        public RegistryKeyCaptureMode Mode { get; set; } = RegistryKeyCaptureMode.Values;

        /// <summary>
        /// Why the key is captured (shown in the module's docs)
        /// </summary>
        public string Description { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Hive}\\{KeyPath}";
        }
    }

    /// <summary>
    /// Difference between a reference and a current value for the same full path
    /// </summary>
    public class RegistryValueChange
    {
        /// <summary>
        /// Hive the value lives in (HKLM\SOFTWARE or HKLM\SYSTEM)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Key path relative to the hive root
        /// </summary>
        public string KeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Value name ((Default) for the default value)
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Current (difference) value type
        /// </summary>
        public string ValueType { get; set; } = string.Empty;

        /// <summary>
        /// Reference (before) value data
        /// </summary>
        public string PreviousData { get; set; } = string.Empty;

        /// <summary>
        /// Current (after) value data
        /// </summary>
        public string CurrentData { get; set; } = string.Empty;

        /// <summary>
        /// Stable diff identity: Hive\KeyPath\ValueName
        /// </summary>
        public string FullPath => $"{Hive}\\{KeyPath}\\{ValueName}";

        public override string ToString()
        {
            return $"{FullPath}: '{PreviousData}' -> '{CurrentData}'";
        }
    }

    /// <summary>
    /// Difference between two snapshots for one hive
    /// </summary>
    public class RegistryHiveDifference
    {
        /// <summary>
        /// Hive this difference applies to (HKLM\SOFTWARE or HKLM\SYSTEM)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Values present in the difference snapshot but not in the reference
        /// </summary>
        public List<RegistrySnapshotValue> Added { get; set; } = new List<RegistrySnapshotValue>();

        /// <summary>
        /// Values present in the reference but not in the difference snapshot
        /// </summary>
        public List<RegistrySnapshotValue> Removed { get; set; } = new List<RegistrySnapshotValue>();

        /// <summary>
        /// Values present in both but with different data or type
        /// </summary>
        public List<RegistryValueChange> Changed { get; set; } = new List<RegistryValueChange>();

        /// <summary>
        /// Total differences in this hive
        /// </summary>
        public int Count => Added.Count + Removed.Count + Changed.Count;

        public override string ToString()
        {
            return $"{Hive}: +{Added.Count} -{Removed.Count} ~{Changed.Count}";
        }
    }

    /// <summary>
    /// Registry drift between two image snapshots, per hive
    /// </summary>
    public class RegistryDriftResult
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
        /// Number of registry values captured on the reference side
        /// </summary>
        public int ReferenceValueCount { get; set; }

        /// <summary>
        /// Number of registry values captured on the difference side
        /// </summary>
        public int DifferenceValueCount { get; set; }

        /// <summary>
        /// Per-hive differences (empty when both snapshots have no registry data or no differences)
        /// </summary>
        public List<RegistryHiveDifference> Hives { get; set; } = new List<RegistryHiveDifference>();

        /// <summary>
        /// Whether either side captured any registry values (false for pre-registry snapshots)
        /// </summary>
        public bool HasRegistryData => ReferenceValueCount > 0 || DifferenceValueCount > 0;

        /// <summary>
        /// Total differences across all hives
        /// </summary>
        public int TotalDifferences => Hives.Sum(h => h.Count);

        /// <summary>
        /// Whether the snapshots are registry-identical (no data on either side counts as identical)
        /// </summary>
        public bool AreIdentical => TotalDifferences == 0;

        public override string ToString()
        {
            return AreIdentical
                ? $"Registry drift between '{ReferenceName}' and '{DifferenceName}': identical"
                : $"Registry drift between '{ReferenceName}' and '{DifferenceName}': {TotalDifferences} differences";
        }
    }
}