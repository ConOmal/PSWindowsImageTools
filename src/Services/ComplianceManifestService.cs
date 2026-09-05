using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Assembles compliance manifests from existing evaluation outputs: an image
    /// snapshot's inventory summary, an optional security baseline report and an
    /// optional servicing chain report, plus tool provenance. Pure assembly logic —
    /// no DISM, no hive reads, no network; the only I/O is SaveManifest/LoadManifest.
    /// </summary>
    public class ComplianceManifestService
    {
        private const string ServiceName = "ComplianceManifestService";
        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Current manifest schema version
        /// </summary>
        public const string CurrentManifestVersion = "1.0";

        public ComplianceManifestService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Builds a compliance manifest from a snapshot and optional evaluation reports
        /// </summary>
        /// <param name="snapshot">Inventory snapshot from Get-WindowsImageSnapshot</param>
        /// <param name="baselineReport">Optional security baseline report from Get-WindowsImageSecurityBaseline</param>
        /// <param name="servicingChainReport">Optional servicing chain report from Get-WindowsImageServicingChain</param>
        /// <returns>Compliance manifest</returns>
        public WindowsImageComplianceManifest BuildManifest(
            ImageSnapshot snapshot,
            WindowsImageSecurityBaselineReport? baselineReport = null,
            ServicingChainReport? servicingChainReport = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var manifest = new WindowsImageComplianceManifest
            {
                ManifestVersion = CurrentManifestVersion,
                GeneratedAt = DateTime.UtcNow,
                ToolName = "PSWindowsImageTools",
                ToolVersion = ResolveToolVersion(),
                Image = BuildImageIdentity(snapshot),
                Inventory = BuildInventorySummary(snapshot),
                OverallStatus = ResolveOverallStatus(baselineReport),
                SecurityBaseline = baselineReport == null ? null : BuildBaselineSection(baselineReport),
                ServicingChain = servicingChainReport == null ? null : BuildServicingSection(servicingChainReport)
            };

            if (baselineReport != null &&
                !string.Equals(baselineReport.ImageName, snapshot.ImageName, StringComparison.OrdinalIgnoreCase))
            {
                _callbacks.Warning?.Invoke(
                    $"Security baseline report image '{baselineReport.ImageName}' does not match snapshot image '{snapshot.ImageName}'; embedding as-is");
            }

            if (servicingChainReport != null &&
                !string.Equals(servicingChainReport.ImageName, snapshot.ImageName, StringComparison.OrdinalIgnoreCase))
            {
                _callbacks.Warning?.Invoke(
                    $"Servicing chain report image '{servicingChainReport.ImageName}' does not match snapshot image '{snapshot.ImageName}'; embedding as-is");
            }

            _callbacks.Verbose?.Invoke($"[{ServiceName}] Built compliance manifest for {snapshot.ImageName}: {manifest.OverallStatus}");
            return manifest;
        }

        /// <summary>
        /// Builds the image identity section from a snapshot. Pure.
        /// </summary>
        internal static ComplianceManifestImageIdentity BuildImageIdentity(ImageSnapshot snapshot)
        {
            return new ComplianceManifestImageIdentity
            {
                ImageName = snapshot.ImageName,
                ImageIndex = snapshot.ImageIndex,
                ImagePath = snapshot.ImagePath,
                MountPath = snapshot.MountPath,
                CapturedAt = snapshot.CapturedAt
            };
        }

        /// <summary>
        /// Builds the inventory summary section from a snapshot. Pure.
        /// </summary>
        internal static ComplianceManifestInventorySummary BuildInventorySummary(ImageSnapshot snapshot)
        {
            return new ComplianceManifestInventorySummary
            {
                Packages = snapshot.Packages.Count,
                Features = snapshot.Features.Count,
                Capabilities = snapshot.Capabilities.Count,
                AppxPackages = snapshot.AppxPackages.Count,
                Software = snapshot.Software.Count,
                Drivers = snapshot.Drivers.Count,
                Registry = snapshot.Registry.Count,
                TotalItems = snapshot.TotalItems
            };
        }

        /// <summary>
        /// Builds the security baseline section from a compliance report. Pure.
        /// </summary>
        internal static ComplianceManifestBaselineSection BuildBaselineSection(WindowsImageSecurityBaselineReport report)
        {
            var section = new ComplianceManifestBaselineSection
            {
                ImageName = report.ImageName,
                MountPath = report.MountPath,
                IsCompliant = report.IsCompliant,
                TotalEntries = report.TotalEntries,
                CompliantCount = report.CompliantCount,
                NonCompliantCount = report.NonCompliantCount,
                NotPresentCount = report.NotPresentCount
            };

            foreach (var observation in report.Entries)
            {
                section.Entries.Add(AppendBaselineEntry(observation));
            }

            return section;
        }

        /// <summary>
        /// Projects one baseline observation into its manifest form. Pure.
        /// </summary>
        internal static ComplianceManifestBaselineEntry AppendBaselineEntry(WindowsImageSecurityBaselineObservation observation)
        {
            return new ComplianceManifestBaselineEntry
            {
                Hive = observation.Hive,
                KeyPath = observation.KeyPath,
                ValueName = observation.ValueName,
                ExpectedValue = observation.ExpectedValue,
                ValueType = observation.ValueType.ToString(),
                Rationale = observation.Rationale,
                State = observation.State.ToString(),
                ObservedValue = observation.ObservedValue,
                ObservedValueType = observation.ObservedValueType
            };
        }

        /// <summary>
        /// Builds the servicing chain section from a servicing chain report. Pure.
        /// </summary>
        internal static ComplianceManifestServicingSection BuildServicingSection(ServicingChainReport report)
        {
            return new ComplianceManifestServicingSection
            {
                ImageName = report.ImageName,
                ImagePath = report.ImagePath,
                GeneratedAt = report.GeneratedAt,
                PackageCount = report.Packages.Count,
                ServicingStackUpdate = report.ServicingStackUpdate?.ToString(),
                CumulativeUpdate = report.CumulativeUpdate?.ToString(),
                OrderingValid = report.OrderingValid,
                Issues = new List<string>(report.Issues)
            };
        }

        /// <summary>
        /// Resolves the rolled-up compliance status from an optional baseline report. Pure.
        /// </summary>
        internal static WindowsImageComplianceStatus ResolveOverallStatus(WindowsImageSecurityBaselineReport? baselineReport)
        {
            if (baselineReport == null)
            {
                return WindowsImageComplianceStatus.Unknown;
            }

            return baselineReport.IsCompliant
                ? WindowsImageComplianceStatus.Compliant
                : WindowsImageComplianceStatus.NonCompliant;
        }

        /// <summary>
        /// Resolves the producing tool version from the assembly. Pure.
        /// </summary>
        internal static string ResolveToolVersion()
        {
            var version = typeof(ComplianceManifestService).Assembly.GetName().Version;
            return version == null ? "unknown" : version.ToString();
        }

        /// <summary>
        /// Saves a compliance manifest to a JSON file
        /// </summary>
        /// <param name="manifest">Manifest to save</param>
        /// <param name="manifestPath">Destination path</param>
        public static void SaveManifest(WindowsImageComplianceManifest manifest, string manifestPath)
        {
            var json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
            File.WriteAllText(manifestPath, json);
        }

        /// <summary>
        /// Loads a compliance manifest from a JSON file
        /// </summary>
        /// <param name="manifestPath">Path to the manifest JSON file</param>
        /// <returns>Loaded manifest</returns>
        public static WindowsImageComplianceManifest LoadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"Compliance manifest file not found: {manifestPath}");
            }

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonConvert.DeserializeObject<WindowsImageComplianceManifest>(json);

            return manifest ?? throw new InvalidOperationException($"Compliance manifest file is empty or invalid: {manifestPath}");
        }
    }
}
