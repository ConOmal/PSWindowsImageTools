using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ComplianceManifestServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public ComplianceManifestServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        private static ImageSnapshot MakeSnapshot(string name = "Windows 11 Pro", DateTime? capturedAt = null)
        {
            return new ImageSnapshot
            {
                ImageName = name,
                ImageIndex = 1,
                ImagePath = @"C:\media\install.wim",
                MountPath = @"C:\mount\win11",
                CapturedAt = capturedAt ?? new DateTime(2026, 9, 4, 11, 30, 0, DateTimeKind.Utc),
                Packages = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Package-A", State = "Installed" },
                    new SnapshotItem { Name = "Package-B", State = "Installed" }
                },
                Features = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Feature-1", State = "Enabled" },
                    new SnapshotItem { Name = "Feature-2", State = "Disabled" }
                },
                Capabilities = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Cap.X~~~~0.0.1.0", State = "Installed" }
                },
                AppxPackages = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "App-1_abc", Detail = "App One" }
                },
                Software = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Tool", State = "1.0.0", Detail = "Vendor" }
                },
                Drivers = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "net.inf", State = "Acme", Detail = "1.0.0.0" }
                },
                Registry = new List<RegistrySnapshotValue>
                {
                    new RegistrySnapshotValue
                    {
                        Hive = "HKLM\\SOFTWARE",
                        KeyPath = "Microsoft\\Windows\\CurrentVersion\\Run",
                        ValueName = "Agent",
                        ValueType = "REG_SZ",
                        ValueData = "C:\\agent.exe"
                    }
                }
            };
        }

        private static WindowsImageSecurityBaselineReport MakeBaselineReport(
            string imageName = "Windows 11 Pro",
            bool fullyCompliant = false)
        {
            var report = new WindowsImageSecurityBaselineReport
            {
                ImageName = imageName,
                MountPath = @"C:\mount\win11"
            };

            report.Entries.Add(new WindowsImageSecurityBaselineObservation
            {
                ImageName = imageName,
                MountPath = @"C:\mount\win11",
                Hive = "HKLM\\SOFTWARE",
                KeyPath = "Microsoft\\Windows\\CurrentVersion\\Policies\\System",
                ValueName = "EnableLUA",
                ExpectedValue = "1",
                ValueType = RegistryValueKind.DWord,
                Rationale = "UAC must stay enabled",
                State = WindowsImageBaselineComplianceState.Compliant,
                ObservedValue = "1",
                ObservedValueType = "RegDword"
            });

            if (fullyCompliant)
            {
                return report;
            }

            report.Entries.Add(new WindowsImageSecurityBaselineObservation
            {
                ImageName = imageName,
                MountPath = @"C:\mount\win11",
                Hive = "HKLM\\SOFTWARE",
                KeyPath = "Microsoft\\Windows\\CurrentVersion\\Policies\\System",
                ValueName = "ConsentPromptBehaviorAdmin",
                ExpectedValue = "5",
                ValueType = RegistryValueKind.DWord,
                Rationale = "Prompt for credentials on elevation",
                State = WindowsImageBaselineComplianceState.NonCompliant,
                ObservedValue = "0",
                ObservedValueType = "RegDword"
            });

            report.Entries.Add(new WindowsImageSecurityBaselineObservation
            {
                ImageName = imageName,
                MountPath = @"C:\mount\win11",
                Hive = "HKLM\\SOFTWARE",
                KeyPath = "Policies\\Microsoft\\Windows\\WindowsUpdate",
                ValueName = "WUServer",
                ExpectedValue = "http://wsus.corp",
                ValueType = RegistryValueKind.String,
                Rationale = "WSUS server must be configured",
                State = WindowsImageBaselineComplianceState.NotPresent,
                ObservedValue = string.Empty,
                ObservedValueType = string.Empty
            });

            return report;
        }

        private static ServicingChainReport MakeServicingReport(
            string imageName = "Windows 11 Pro",
            bool withClassifiedPackages = true)
        {
            var report = new ServicingChainReport
            {
                ImageName = imageName,
                ImagePath = @"C:\media\install.wim",
                GeneratedAt = new DateTime(2026, 9, 4, 11, 45, 0, DateTimeKind.Utc),
                OrderingValid = true
            };

            if (withClassifiedPackages)
            {
                var ssu = new ServicingPackageInfo
                {
                    PackageName = "ServicingStackUpdate",
                    Role = ServicingPackageRole.ServicingStackUpdate,
                    Confidence = ClassificationConfidence.Verified,
                    Build = 22621,
                    Revision = 1000
                };
                var lcu = new ServicingPackageInfo
                {
                    PackageName = "CumulativeUpdate",
                    Role = ServicingPackageRole.CumulativeUpdate,
                    Confidence = ClassificationConfidence.Verified,
                    Build = 22621,
                    Revision = 3400
                };
                report.Packages.Add(ssu);
                report.Packages.Add(lcu);
                report.ServicingStackUpdate = ssu;
                report.CumulativeUpdate = lcu;
            }
            else
            {
                report.OrderingValid = false;
                report.Issues.Add("LCU older than SSU");
            }

            return report;
        }

        [Fact]
        public void BuildManifest_SnapshotOnly_PopulatesProvenanceAndLeavesSectionsNull()
        {
            var capturedAt = new DateTime(2026, 9, 4, 11, 30, 0, DateTimeKind.Utc);
            var snapshot = MakeSnapshot(capturedAt: capturedAt);

            var manifest = new ComplianceManifestService().BuildManifest(snapshot);

            Assert.Equal(ComplianceManifestService.CurrentManifestVersion, manifest.ManifestVersion);
            Assert.Equal("1.0", manifest.ManifestVersion);
            Assert.Equal("PSWindowsImageTools", manifest.ToolName);
            Assert.False(string.IsNullOrEmpty(manifest.ToolVersion));
            Assert.Equal(capturedAt, manifest.Image.CapturedAt);
            Assert.Equal("Windows 11 Pro", manifest.Image.ImageName);
            Assert.Equal(1, manifest.Image.ImageIndex);
            Assert.Equal(@"C:\media\install.wim", manifest.Image.ImagePath);
            Assert.Equal(@"C:\mount\win11", manifest.Image.MountPath);
            Assert.Null(manifest.SecurityBaseline);
            Assert.Null(manifest.ServicingChain);
            Assert.False(manifest.HasSecurityBaseline);
            Assert.False(manifest.HasServicingChain);
            Assert.Equal(WindowsImageComplianceStatus.Unknown, manifest.OverallStatus);
            Assert.True(manifest.GeneratedAt != default);
        }

        [Fact]
        public void BuildManifest_InventorySummary_MapsCategoryCounts()
        {
            var manifest = new ComplianceManifestService().BuildManifest(MakeSnapshot());

            Assert.Equal(2, manifest.Inventory.Packages);
            Assert.Equal(2, manifest.Inventory.Features);
            Assert.Equal(1, manifest.Inventory.Capabilities);
            Assert.Equal(1, manifest.Inventory.AppxPackages);
            Assert.Equal(1, manifest.Inventory.Software);
            Assert.Equal(1, manifest.Inventory.Drivers);
            Assert.Equal(1, manifest.Inventory.Registry);
            Assert.Equal(9, manifest.Inventory.TotalItems);
        }

        [Fact]
        public void BuildManifest_WithBaselineReport_MapsSectionCountsAndEntries()
        {
            var snapshot = MakeSnapshot();
            var report = MakeBaselineReport();

            var manifest = new ComplianceManifestService().BuildManifest(snapshot, report);

            Assert.True(manifest.HasSecurityBaseline);
            Assert.NotNull(manifest.SecurityBaseline);
            var section = manifest.SecurityBaseline!;
            Assert.Equal("Windows 11 Pro", section.ImageName);
            Assert.Equal(@"C:\mount\win11", section.MountPath);
            Assert.False(section.IsCompliant);
            Assert.Equal(3, section.TotalEntries);
            Assert.Equal(1, section.CompliantCount);
            Assert.Equal(1, section.NonCompliantCount);
            Assert.Equal(1, section.NotPresentCount);
            Assert.Equal(3, section.Entries.Count);

            var first = section.Entries[0];
            Assert.Equal("HKLM\\SOFTWARE", first.Hive);
            Assert.Equal("EnableLUA", first.ValueName);
            Assert.Equal("1", first.ExpectedValue);
            Assert.Equal("DWord", first.ValueType);
            Assert.Equal("UAC must stay enabled", first.Rationale);
            Assert.Equal("Compliant", first.State);
            Assert.Equal("1", first.ObservedValue);
            Assert.Equal("RegDword", first.ObservedValueType);

            Assert.Equal("NonCompliant", section.Entries[1].State);
            Assert.Equal("NotPresent", section.Entries[2].State);
        }

        [Fact]
        public void BuildManifest_WithServicingChainReport_MapsSection()
        {
            var snapshot = MakeSnapshot();
            var report = MakeServicingReport();

            var manifest = new ComplianceManifestService().BuildManifest(snapshot, servicingChainReport: report);

            Assert.True(manifest.HasServicingChain);
            Assert.NotNull(manifest.ServicingChain);
            var section = manifest.ServicingChain!;
            Assert.Equal("Windows 11 Pro", section.ImageName);
            Assert.Equal(@"C:\media\install.wim", section.ImagePath);
            Assert.Equal(new DateTime(2026, 9, 4, 11, 45, 0, DateTimeKind.Utc), section.GeneratedAt);
            Assert.Equal(2, section.PackageCount);
            Assert.StartsWith("ServicingStackUpdate (Verified):", section.ServicingStackUpdate ?? string.Empty, StringComparison.Ordinal);
            Assert.StartsWith("CumulativeUpdate (Verified):", section.CumulativeUpdate ?? string.Empty, StringComparison.Ordinal);
            Assert.True(section.OrderingValid);
            Assert.Empty(section.Issues);
        }

        [Fact]
        public void BuildManifest_ServicingWithoutClassifiedPackages_KeepsSummariesNullAndCarriesIssues()
        {
            var report = MakeServicingReport(withClassifiedPackages: false);

            var manifest = new ComplianceManifestService().BuildManifest(MakeSnapshot(), servicingChainReport: report);

            Assert.NotNull(manifest.ServicingChain);
            var section = manifest.ServicingChain!;
            Assert.Null(section.ServicingStackUpdate);
            Assert.Null(section.CumulativeUpdate);
            Assert.False(section.OrderingValid);
            Assert.Equal(new List<string> { "LCU older than SSU" }, section.Issues);
        }

        [Fact]
        public void BuildManifest_BothSections_SetsOverallStatusAndFlags()
        {
            var snapshot = MakeSnapshot();

            var manifest = new ComplianceManifestService().BuildManifest(
                snapshot,
                MakeBaselineReport(fullyCompliant: true),
                MakeServicingReport());

            Assert.True(manifest.HasSecurityBaseline);
            Assert.True(manifest.HasServicingChain);
            Assert.Equal(WindowsImageComplianceStatus.Compliant, manifest.OverallStatus);
            Assert.Equal(1, manifest.SecurityBaseline!.CompliantCount);
            Assert.Equal(1, manifest.SecurityBaseline.TotalEntries);
        }

        [Theory]
        [InlineData(true, WindowsImageComplianceStatus.Compliant)]
        [InlineData(false, WindowsImageComplianceStatus.NonCompliant)]
        public void ResolveOverallStatus_FollowsBaselineVerdict(bool isCompliant, WindowsImageComplianceStatus expected)
        {
            var report = MakeBaselineReport(fullyCompliant: isCompliant);

            Assert.Equal(expected, ComplianceManifestService.ResolveOverallStatus(report));
        }

        [Fact]
        public void ResolveOverallStatus_NullReport_IsUnknown()
        {
            Assert.Equal(WindowsImageComplianceStatus.Unknown, ComplianceManifestService.ResolveOverallStatus(null));
        }

        [Fact]
        public void BuildManifest_ImageNameMismatch_KeepsSectionAndWarns()
        {
            var warnings = new List<string>();
            var callbacks = new ModuleCallbacks { Warning = message => warnings.Add(message) };
            var service = new ComplianceManifestService(callbacks);

            var manifest = service.BuildManifest(
                MakeSnapshot("Windows 11 Pro"),
                MakeBaselineReport("Windows 11 Enterprise"),
                MakeServicingReport("Windows 11 Enterprise"));

            Assert.Equal(2, warnings.Count);
            Assert.Contains("Windows 11 Enterprise", warnings[0], StringComparison.Ordinal);
            Assert.Contains("Windows 11 Pro", warnings[0], StringComparison.Ordinal);
            Assert.NotNull(manifest.SecurityBaseline);
            Assert.NotNull(manifest.ServicingChain);
            Assert.Equal("Windows 11 Enterprise", manifest.SecurityBaseline!.ImageName);
        }

        [Fact]
        public void BuildManifest_MatchingImageNames_ProducesNoWarnings()
        {
            var warnings = new List<string>();
            var callbacks = new ModuleCallbacks { Warning = message => warnings.Add(message) };
            var service = new ComplianceManifestService(callbacks);

            service.BuildManifest(MakeSnapshot(), MakeBaselineReport(), MakeServicingReport());

            Assert.Empty(warnings);
        }

        [Fact]
        public void BuildManifest_NullSnapshot_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ComplianceManifestService().BuildManifest(null!));
        }

        [Fact]
        public void ResolveToolVersion_ReturnsNonEmptyVersion()
        {
            var version = ComplianceManifestService.ResolveToolVersion();

            Assert.False(string.IsNullOrEmpty(version));
            Assert.Contains(".", version, StringComparison.Ordinal);
        }

        [Fact]
        public void SaveLoadManifest_RoundTripPreservesDocument()
        {
            var manifestPath = Path.Combine(_tempDirectory, "manifest.json");
            var manifest = new ComplianceManifestService().BuildManifest(
                MakeSnapshot(),
                MakeBaselineReport(),
                MakeServicingReport());
            manifest.OverallStatus = manifest.SecurityBaseline!.IsCompliant
                ? WindowsImageComplianceStatus.Compliant
                : WindowsImageComplianceStatus.NonCompliant;

            ComplianceManifestService.SaveManifest(manifest, manifestPath);

            Assert.True(File.Exists(manifestPath));
            var json = File.ReadAllText(manifestPath);
            Assert.Contains("\"OverallStatus\": \"NonCompliant\"", json, StringComparison.Ordinal);

            var loaded = ComplianceManifestService.LoadManifest(manifestPath);

            Assert.Equal("1.0", loaded.ManifestVersion);
            Assert.Equal(WindowsImageComplianceStatus.NonCompliant, loaded.OverallStatus);
            Assert.Equal("Windows 11 Pro", loaded.Image.ImageName);
            Assert.Equal(9, loaded.Inventory.TotalItems);
            Assert.True(loaded.HasSecurityBaseline);
            Assert.True(loaded.HasServicingChain);
            Assert.Equal(3, loaded.SecurityBaseline!.TotalEntries);
            Assert.Equal(1, loaded.SecurityBaseline.NonCompliantCount);
            Assert.Equal("NonCompliant", loaded.SecurityBaseline.Entries[1].State);
            Assert.Equal("DWord", loaded.SecurityBaseline.Entries[0].ValueType);
            Assert.Equal(2, loaded.ServicingChain!.PackageCount);
            Assert.True(loaded.ServicingChain.OrderingValid);
            Assert.NotNull(loaded.ServicingChain.CumulativeUpdate);
            Assert.Equal(manifest.Image.CapturedAt, loaded.Image.CapturedAt);
        }

        [Fact]
        public void SaveManifest_OverwritesExistingFile()
        {
            var manifestPath = Path.Combine(_tempDirectory, "manifest.json");
            File.WriteAllText(manifestPath, "stale content");

            ComplianceManifestService.SaveManifest(
                new ComplianceManifestService().BuildManifest(MakeSnapshot()),
                manifestPath);

            var loaded = ComplianceManifestService.LoadManifest(manifestPath);
            Assert.Equal("Windows 11 Pro", loaded.Image.ImageName);
        }

        [Fact]
        public void LoadManifest_MissingFile_ThrowsFileNotFoundException()
        {
            Assert.Throws<FileNotFoundException>(() =>
                ComplianceManifestService.LoadManifest(Path.Combine(_tempDirectory, "missing.json")));
        }

        [Fact]
        public void LoadManifest_EmptyFile_ThrowsInvalidOperationException()
        {
            var manifestPath = Path.Combine(_tempDirectory, "empty.json");
            File.WriteAllText(manifestPath, "null");

            Assert.Throws<InvalidOperationException>(() => ComplianceManifestService.LoadManifest(manifestPath));
        }
    }
}
