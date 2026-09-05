using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A provisioned AppX package in a mounted Windows image
    /// </summary>
    public class ProvisionedAppInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;

        public override string ToString() => $"{DisplayName} ({PackageName})";
    }

    /// <summary>
    /// One desired package entry for a WinGet Configuration export
    /// </summary>
    public class WinGetConfigurationEntry
    {
        public string PackageIdentifier { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Source { get; set; } = "winget";

        public override string ToString() => $"{PackageIdentifier} ({Source})";
    }

    /// <summary>
    /// Result of exporting a WinGet Configuration artifact for first-boot application
    /// </summary>
    public class WinGetConfigurationExportResult
    {
        public System.IO.FileInfo ConfigPath { get; set; } = null!;
        public System.IO.FileInfo ScheduledTaskPath { get; set; } = null!;
        public System.Collections.Generic.List<WinGetConfigurationEntry> Packages { get; set; } = new System.Collections.Generic.List<WinGetConfigurationEntry>();

        public override string ToString() => $"{Packages.Count} package(s) -> {ConfigPath.FullName}";
    }
}
