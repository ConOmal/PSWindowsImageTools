using System;
using System.Collections.Generic;
using System.Linq;

namespace PSWindowsImageTools.Models
{
    public enum HealthStatus
    {
        Healthy,
        Warning,
        Unhealthy
    }

    /// <summary>
    /// A single health finding for an offline Windows image
    /// </summary>
    public class HealthFinding
    {
        /// <summary>
        /// One of: Corruption, MissingRegistryHive, OrphanedOrSupersededPackage, DriverIssue, PendingOperation
        /// </summary>
        public string Category { get; set; } = string.Empty;
        public HealthStatus Severity { get; set; }
        public string Message { get; set; } = string.Empty;

        public override string ToString() => $"[{Severity}] {Category}: {Message}";
    }

    /// <summary>
    /// Composite health assessment of an offline Windows image
    /// </summary>
    public class HealthCheckReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<HealthFinding> Findings { get; set; } = new List<HealthFinding>();

        /// <summary>
        /// Unhealthy if any Corruption finding exists; Warning if any other finding exists; else Healthy
        /// </summary>
        public HealthStatus OverallHealth =>
            Findings.Any(f => f.Category == "Corruption")
                ? HealthStatus.Unhealthy
                : Findings.Count > 0
                    ? HealthStatus.Warning
                    : HealthStatus.Healthy;

        public override string ToString() => $"{ImageName}: {OverallHealth} ({Findings.Count} findings)";
    }
}
