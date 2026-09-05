using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Per-entry verdict of a security baseline compliance report:
    /// value matches the baseline, value is present but wrong, or value/key is absent
    /// </summary>
    public enum WindowsImageBaselineComplianceState
    {
        /// <summary>
        /// The value is present and matches the expected value
        /// </summary>
        Compliant,

        /// <summary>
        /// The value is present but does not match the expected value
        /// </summary>
        NonCompliant,

        /// <summary>
        /// The key or value is absent (or the whole hive file is absent)
        /// </summary>
        NotPresent
    }

    /// <summary>
    /// Per-entry verdict of a security baseline apply (Set) result
    /// </summary>
    public enum WindowsImageBaselineApplyState
    {
        /// <summary>
        /// The expected value was written to the image
        /// </summary>
        Applied,

        /// <summary>
        /// The image already satisfied the entry; no write was needed
        /// </summary>
        AlreadyApplied,

        /// <summary>
        /// The entry could not be written (shared batch error)
        /// </summary>
        Failed,

        /// <summary>
        /// The entry was not attempted (e.g. the hive file is missing)
        /// </summary>
        Skipped
    }

    /// <summary>
    /// One curated security baseline entry: a registry value an offline image must
    /// contain, with its expected value and rationale
    /// </summary>
    public class WindowsImageSecurityBaselineEntry
    {
        /// <summary>
        /// Hive containing the value (HKLM\SOFTWARE, HKLM\SYSTEM or HKU\DefaultUser)
        /// </summary>
        public string Hive { get; set; } = string.Empty;

        /// <summary>
        /// Key path relative to the hive root
        /// (e.g. Microsoft\Windows\CurrentVersion\Policies\System)
        /// </summary>
        public string KeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Registry value name
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Expected value data as a normalized string (decimal for DWord)
        /// </summary>
        public string ExpectedValue { get; set; } = string.Empty;

        /// <summary>
        /// Registry value kind of the expected value (the curated baseline uses
        /// DWord and String only)
        /// </summary>
        public RegistryValueKind ValueType { get; set; } = RegistryValueKind.DWord;

        /// <summary>
        /// Why this entry is part of the baseline (shown in docs and reports)
        /// </summary>
        public string Rationale { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Hive}\\{KeyPath}\\{ValueName} = {ExpectedValue} ({ValueType})";
        }
    }

    /// <summary>
    /// One compliance observation of a mounted image against a baseline entry,
    /// from Get-WindowsImageSecurityBaseline
    /// </summary>
    public class WindowsImageSecurityBaselineObservation
    {
        /// <summary>
        /// Name of the image that was observed
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

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
        /// Expected registry value kind
        /// </summary>
        public RegistryValueKind ValueType { get; set; } = RegistryValueKind.DWord;

        /// <summary>
        /// Why this entry is part of the baseline
        /// </summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>
        /// Compliance verdict for this entry
        /// </summary>
        public WindowsImageBaselineComplianceState State { get; set; } = WindowsImageBaselineComplianceState.NotPresent;

        /// <summary>
        /// Observed value data (normalized string); empty when the value is not present
        /// </summary>
        public string ObservedValue { get; set; } = string.Empty;

        /// <summary>
        /// Observed registry value type as reported by the hive parser
        /// (e.g. RegDword, RegSz); empty when the value is not present
        /// </summary>
        public string ObservedValueType { get; set; } = string.Empty;

        public override string ToString()
        {
            var observed = State == WindowsImageBaselineComplianceState.NotPresent
                ? "(not present)"
                : $"{ObservedValue} [{ObservedValueType}]";
            return $"{Hive}\\{KeyPath}\\{ValueName}: {State} (expected '{ExpectedValue}', observed {observed})";
        }
    }

    /// <summary>
    /// Compliance report of one mounted image against the security baseline,
    /// from Get-WindowsImageSecurityBaseline
    /// </summary>
    public class WindowsImageSecurityBaselineReport
    {
        /// <summary>
        /// Name of the image that was observed
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// One observation per baseline entry, in baseline order
        /// </summary>
        public List<WindowsImageSecurityBaselineObservation> Entries { get; set; } = new List<WindowsImageSecurityBaselineObservation>();

        /// <summary>
        /// Number of baseline entries observed
        /// </summary>
        public int TotalEntries => Entries.Count;

        /// <summary>
        /// Entries whose observed value matches the baseline
        /// </summary>
        public int CompliantCount => Entries.Count(e => e.State == WindowsImageBaselineComplianceState.Compliant);

        /// <summary>
        /// Entries whose observed value differs from the baseline
        /// </summary>
        public int NonCompliantCount => Entries.Count(e => e.State == WindowsImageBaselineComplianceState.NonCompliant);

        /// <summary>
        /// Entries whose key or value is absent
        /// </summary>
        public int NotPresentCount => Entries.Count(e => e.State == WindowsImageBaselineComplianceState.NotPresent);

        /// <summary>
        /// True when every entry is compliant
        /// </summary>
        public bool IsCompliant => TotalEntries > 0 && NonCompliantCount == 0 && NotPresentCount == 0;

        public override string ToString()
        {
            var verdict = IsCompliant ? "compliant" : "not compliant";
            return $"Security baseline for {ImageName}: {verdict} ({CompliantCount}/{TotalEntries} compliant, {NonCompliantCount} non-compliant, {NotPresentCount} not present)";
        }
    }

    /// <summary>
    /// Per-entry outcome of applying the security baseline to a mounted image,
    /// from Set-WindowsImageSecurityBaseline
    /// </summary>
    public class WindowsImageSecurityBaselineApplyEntry
    {
        /// <summary>
        /// Name of the image that was modified
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

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
        /// Expected value data that was (or was not) applied
        /// </summary>
        public string ExpectedValue { get; set; } = string.Empty;

        /// <summary>
        /// Apply outcome for this entry
        /// </summary>
        public WindowsImageBaselineApplyState State { get; set; } = WindowsImageBaselineApplyState.Skipped;

        /// <summary>
        /// Human-readable detail (e.g. why the entry was skipped or failed)
        /// </summary>
        public string Detail { get; set; } = string.Empty;

        public override string ToString()
        {
            var detail = string.IsNullOrEmpty(Detail) ? string.Empty : $" ({Detail})";
            return $"{Hive}\\{KeyPath}\\{ValueName}: {State}{detail}";
        }
    }

    /// <summary>
    /// Result of applying the security baseline to one mounted image,
    /// from Set-WindowsImageSecurityBaseline (one result per image)
    /// </summary>
    public class WindowsImageSecurityBaselineApplyResult
    {
        /// <summary>
        /// Name of the image that was modified
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// One apply entry per baseline entry, in baseline order
        /// </summary>
        public List<WindowsImageSecurityBaselineApplyEntry> Results { get; set; } = new List<WindowsImageSecurityBaselineApplyEntry>();

        /// <summary>
        /// Whether the baseline was applied without failure
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Shared error message when the apply batch failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Number of baseline entries reported
        /// </summary>
        public int TotalCount => Results.Count;

        /// <summary>
        /// Entries that were written
        /// </summary>
        public int AppliedCount => Results.Count(r => r.State == WindowsImageBaselineApplyState.Applied);

        /// <summary>
        /// Entries already satisfied (not written)
        /// </summary>
        public int AlreadyAppliedCount => Results.Count(r => r.State == WindowsImageBaselineApplyState.AlreadyApplied);

        /// <summary>
        /// Entries that failed to write
        /// </summary>
        public int FailedCount => Results.Count(r => r.State == WindowsImageBaselineApplyState.Failed);

        /// <summary>
        /// Entries that were not attempted
        /// </summary>
        public int SkippedCount => Results.Count(r => r.State == WindowsImageBaselineApplyState.Skipped);

        public override string ToString()
        {
            var status = Success ? "succeeded" : $"failed: {ErrorMessage}";
            return $"Security baseline for {ImageName}: {status} ({AppliedCount} applied, {AlreadyAppliedCount} already compliant, {SkippedCount} skipped, {FailedCount} failed)";
        }
    }
}
