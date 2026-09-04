using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ComponentStoreServiceTests
    {
        [Fact]
        public void GetDirectorySizeMB_SumsFileSizesRecursively()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(tempDir, "nested");
            Directory.CreateDirectory(nested);
            try
            {
                File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[1024 * 1024]);
                File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[1024 * 1024]);

                var sizeMb = ComponentStoreService.GetDirectorySizeMB(tempDir);

                Assert.Equal(2.0, sizeMb, precision: 1);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void GetDirectorySizeMB_MissingDirectory_ReturnsZero()
        {
            var missing = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Missing-" + Guid.NewGuid().ToString("N"));
            Assert.Equal(0, ComponentStoreService.GetDirectorySizeMB(missing));
        }

        [Fact]
        public void ClassifyPackages_CountsInstalledSupersededAndPending()
        {
            var report = new ComponentStoreReport();
            var packages = new List<(string Name, DismPackageFeatureState State)>
            {
                ("Package-A", DismPackageFeatureState.Installed),
                ("Package-B", DismPackageFeatureState.Superseded),
                ("Package-C", DismPackageFeatureState.InstallPending),
                ("Package-D", DismPackageFeatureState.UninstallPending),
                ("Package-E", DismPackageFeatureState.Installed),
            };

            ComponentStoreService.ClassifyPackages(packages, report);

            Assert.Equal(5, report.TotalPackages);
            Assert.Equal(2, report.InstalledPackages);
            Assert.Equal(1, report.SupersededPackages);
            Assert.Equal(2, report.PendingPackages);
            Assert.Equal(new[] { "Package-B" }, report.SupersededPackageNames);
        }

        [Fact]
        public void ClassifyPackages_EmptyList_NoCountsIncremented()
        {
            var report = new ComponentStoreReport();
            ComponentStoreService.ClassifyPackages(new List<(string Name, DismPackageFeatureState State)>(), report);

            Assert.Equal(0, report.TotalPackages);
            Assert.Equal(0, report.InstalledPackages);
            Assert.Equal(0, report.SupersededPackages);
            Assert.Equal(0, report.PendingPackages);
            Assert.Empty(report.SupersededPackageNames);
        }

        [Fact]
        public void ClassifyPackages_AccumulatesOntoPrePopulatedReport()
        {
            var report = new ComponentStoreReport
            {
                TotalPackages = 2,
                InstalledPackages = 2
            };
            var packages = new List<(string Name, DismPackageFeatureState State)>
            {
                ("Package-X", DismPackageFeatureState.Superseded)
            };

            ComponentStoreService.ClassifyPackages(packages, report);

            Assert.Equal(3, report.TotalPackages);
            Assert.Equal(2, report.InstalledPackages);
            Assert.Equal(1, report.SupersededPackages);
            Assert.Equal(new[] { "Package-X" }, report.SupersededPackageNames);
        }

        [Theory]
        [InlineData(false, "/Image:\"C:\\Mount\" /Cleanup-Image /StartComponentCleanup")]
        [InlineData(true, "/Image:\"C:\\Mount\" /Cleanup-Image /StartComponentCleanup /ResetBase")]
        public void BuildCleanupArguments_ReturnsExpectedDismArgs(bool resetBase, string expected)
        {
            var args = ComponentStoreService.BuildCleanupArguments(@"C:\Mount", resetBase);
            Assert.Equal(expected, args);
        }
    }
}
