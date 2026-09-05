using System;
using System.Collections.Generic;
using System.Linq;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Severity of a single Unattend XML validation issue. Ordered by magnitude
    /// so a minimum-severity filter is a single comparison: Warning = 0 &lt;
    /// Error = 1 (Error is the more severe value, so filtering at Warning
    /// reports everything and filtering at Error reports errors only).
    /// </summary>
    public enum UnattendValidationSeverity
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// A single problem found while validating an Unattend XML configuration.
    /// </summary>
    public class UnattendValidationIssue
    {
        /// <summary>
        /// Issue severity: Error makes the report invalid, Warning does not
        /// </summary>
        public UnattendValidationSeverity Severity { get; set; } = UnattendValidationSeverity.Error;

        /// <summary>
        /// Configuration pass the element lives in (windowsPE, offlineServicing,
        /// generalize, specialize, oobeSystem), or empty when not applicable
        /// </summary>
        public string Pass { get; set; } = string.Empty;

        /// <summary>
        /// Readable element path of the offending element, e.g.
        /// /unattend/settings[@pass='specialize']/component[@name='Microsoft-Windows-Shell-Setup']/CopyProfile
        /// </summary>
        public string ElementPath { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the problem
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Stable machine-readable rule identifier, e.g. Run-DuplicateOrder
        /// </summary>
        public string RuleId { get; set; } = string.Empty;

        public override string ToString() => $"[{Severity}] {Pass}: {Message} ({ElementPath})";
    }

    /// <summary>
    /// Result of validating an Unattend XML configuration file
    /// </summary>
    public class UnattendValidationReport
    {
        /// <summary>
        /// Path of the validated file (may be empty for in-memory documents)
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Issues found (post severity-filter). IsValid is always computed over
        /// the complete, unfiltered issue set.
        /// </summary>
        public List<UnattendValidationIssue> Issues { get; set; } = new List<UnattendValidationIssue>();

        /// <summary>
        /// True when the document has no Error-severity issues over the complete
        /// (unfiltered) issue set
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Number of reported Error-severity issues (post severity-filter)
        /// </summary>
        public int ErrorCount => Issues.Count(i => i.Severity == UnattendValidationSeverity.Error);

        /// <summary>
        /// Number of reported Warning-severity issues (post severity-filter)
        /// </summary>
        public int WarningCount => Issues.Count(i => i.Severity == UnattendValidationSeverity.Warning);

        /// <summary>
        /// When the validation ran
        /// </summary>
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;

        public override string ToString() =>
            $"{FilePath}: IsValid={IsValid} ({ErrorCount} errors, {WarningCount} warnings)";
    }
}
