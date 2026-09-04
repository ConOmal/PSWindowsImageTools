using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ImageComparisonServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public ImageComparisonServiceTests()
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

        private static ImageSnapshot MakeSnapshot(string name, Action<ImageSnapshot>? customize = null)
        {
            var snapshot = new ImageSnapshot
            {
                ImageName = name,
                ImageIndex = 1,
                Packages = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Package-A", State = "Installed" },
                    new SnapshotItem { Name = "Package-B", State = "Installed" }
                },
                Features = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "Feature-1", State = "Enabled" }
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
                        KeyPath = @"Microsoft\Windows\CurrentVersion\Run",
                        ValueName = "Tool",
                        ValueType = "REG_SZ",
                        ValueData = "1.0.0"
                    }
                },
            };

            customize?.Invoke(snapshot);
            return snapshot;
        }

        [Fact]
        public void Compare_IdenticalSnapshots_ReportsNoDifferences()
        {
            var result = new ImageComparisonService().Compare(MakeSnapshot("A"), MakeSnapshot("B"));

            Assert.True(result.AreIdentical);
            Assert.Equal(0, result.TotalDifferences);
        }

        [Fact]
        public void Compare_DetectsAddedAndRemoved()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s =>
            {
                s.Packages.Add(new SnapshotItem { Name = "Package-C", State = "Installed" });
                s.Packages.RemoveAll(i => i.Name == "Package-A");
            });

            var result = new ImageComparisonService().Compare(reference, difference);

            var packages = result.Categories.Single(c => c.Category == "Packages");
            Assert.Single(packages.Added);
            Assert.Equal("Package-C", packages.Added[0].Name);
            Assert.Single(packages.Removed);
            Assert.Equal("Package-A", packages.Removed[0].Name);
            Assert.False(result.AreIdentical);
        }

        [Fact]
        public void Compare_DetectsChangedState()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s =>
            {
                s.Features.Clear();
                s.Features.Add(new SnapshotItem { Name = "Feature-1", State = "Disabled" });
            });

            var result = new ImageComparisonService().Compare(reference, difference);

            var features = result.Categories.Single(c => c.Category == "Features");
            Assert.Single(features.Changed);
            Assert.Equal("Feature-1", features.Changed[0].Name);
            Assert.Empty(features.Added);
            Assert.Empty(features.Removed);
        }

        [Fact]
        public void Compare_IsCaseInsensitiveOnNames()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s =>
            {
                s.Packages.Clear();
                s.Packages.Add(new SnapshotItem { Name = "PACKAGE-A", State = "Installed" });
                s.Packages.Add(new SnapshotItem { Name = "package-b", State = "Installed" });
            });

            var result = new ImageComparisonService().Compare(reference, difference);

            Assert.True(result.AreIdentical);
        }

        [Fact]
        public void SaveAndLoadSnapshot_RoundTrips()
        {
            var snapshot = MakeSnapshot("RoundTrip");
            var path = Path.Combine(_tempDirectory, "snapshot.json");

            ImageComparisonService.SaveSnapshot(snapshot, path);
            var loaded = ImageComparisonService.LoadSnapshot(path);

            Assert.Equal("RoundTrip", loaded.ImageName);
            Assert.Equal(2, loaded.Packages.Count);
            Assert.Equal("Tool", loaded.Software.Single().Name);
            Assert.Single(loaded.Registry);
            Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\Tool", loaded.Registry.Single().FullPath);
        }

        [Fact]
        public void LoadSnapshot_MissingFile_Throws()
        {
            Assert.Throws<FileNotFoundException>(() =>
                ImageComparisonService.LoadSnapshot(Path.Combine(_tempDirectory, "missing.json")));
        }

        [Fact]
        public void Compare_IncludesDriversCategory()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s => s.Drivers.Add(new SnapshotItem { Name = "gpu.inf", State = "Vendor", Detail = "2.0.0.0" }));

            var result = new ImageComparisonService().Compare(reference, difference);

            var driversDiff = result.Categories.Single(c => c.Category == "Drivers");
            Assert.Single(driversDiff.Added);
            Assert.Equal("gpu.inf", driversDiff.Added[0].Name);
        }

        [Fact]
        public void Compare_DetectsRegistryDriftPerHive()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s =>
            {
                s.Registry.RemoveAll(v => v.ValueName == "Tool");
                s.Registry.Add(new RegistrySnapshotValue
                {
                    Hive = "HKLM\\SOFTWARE",
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "ConsentPromptBehaviorAdmin",
                    ValueType = "REG_DWORD",
                    ValueData = "1"
                });
                s.Registry.Add(Val("HKLM\\SOFTWARE", @"Microsoft\Windows\CurrentVersion\Run", "Tool", "REG_SZ", "2.0.0"));
            });

            var result = new ImageComparisonService().Compare(reference, difference);

            Assert.NotNull(result.RegistryDrift);
            Assert.False(result.AreIdentical);

            var registry = result.Categories.Single(c => c.Category == "Registry");
            Assert.Single(registry.Added);
            Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\ConsentPromptBehaviorAdmin", registry.Added[0].Name);
            Assert.Single(registry.Changed);
            Assert.Empty(registry.Removed);

            var drift = result.RegistryDrift;
            Assert.Single(drift.Hives);
            Assert.Equal("HKLM\\SOFTWARE", drift.Hives[0].Hive);
            Assert.Single(drift.Hives[0].Added);
            Assert.Single(drift.Hives[0].Changed);
            Assert.Equal("Tool", drift.Hives[0].Changed[0].ValueName);
            Assert.Equal("1.0.0", drift.Hives[0].Changed[0].PreviousData);
            Assert.Equal("2.0.0", drift.Hives[0].Changed[0].CurrentData);
            Assert.Equal(2, drift.TotalDifferences);
            Assert.Equal(2, result.TotalDifferences);
        }

        private static RegistrySnapshotValue Val(string hive, string keyPath, string valueName, string valueType = "REG_SZ", string valueData = "")
        {
            return new RegistrySnapshotValue
            {
                Hive = hive,
                KeyPath = keyPath,
                ValueName = valueName,
                ValueType = valueType,
                ValueData = valueData
            };
        }
    }
}
