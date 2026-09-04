using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsImageDriverServiceTests
    {
        private static WindowsImageDriverInfo MakeDriver(
            string published, string original, string provider, string version, bool inBox = false)
        {
            return new WindowsImageDriverInfo
            {
                PublishedName = published,
                OriginalFileName = original,
                ProviderName = provider,
                Version = version,
                InBox = inBox
            };
        }

        [Fact]
        public void Compare_DetectsAdded()
        {
            var reference = new List<WindowsImageDriverInfo>();
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Added);
            Assert.Equal("oem1.inf", result.Added[0].PublishedName);
            Assert.Empty(result.Removed);
        }

        [Fact]
        public void Compare_DetectsRemoved()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo>();

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Removed);
            Assert.Empty(result.Added);
        }

        [Fact]
        public void Compare_DetectsSuperseded_SameOriginalFileNameAndProvider_HigherVersion()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem2.inf", "net.inf", "Acme", "2.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Superseded);
            Assert.Equal("oem2.inf", result.Superseded[0].PublishedName);
        }

        [Fact]
        public void Compare_DetectsDuplicateOem_SameOriginalFileNameAndProvider_SamePublishedNameSet_DifferentEntries()
        {
            var driverA = MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0");
            var driverB = MakeDriver("oem2.inf", "net.inf", "Acme", "1.0.0.0");
            var current = new List<WindowsImageDriverInfo> { driverA, driverB };

            var result = new WindowsImageDriverService().Compare(new List<WindowsImageDriverInfo>(), current);

            Assert.Equal(2, result.DuplicateOem.Count);
        }

        [Fact]
        public void Compare_IdenticalLists_ReportsNoDifferences()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Superseded);
            Assert.Empty(result.DuplicateOem);
        }

        [Fact]
        public void Compare_DuplicateBlankPublishedNames_DoesNotThrow()
        {
            // Two inbox drivers with blank PublishedName should not crash with ArgumentException
            var current = new List<WindowsImageDriverInfo>
            {
                MakeDriver("", "net.inf", "Acme", "1.0.0.0", inBox: true),
                MakeDriver("", "storage.inf", "Acme", "2.0.0.0", inBox: true)
            };

            var result = new WindowsImageDriverService().Compare(new List<WindowsImageDriverInfo>(), current);

            Assert.Equal(2, result.Added.Count);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Superseded);
        }

        [Fact]
        public void Compare_SupersededCheck_IsExistentialNotMaxVersion()
        {
            // Reference has both v3.0 and v1.0 of the same driver; current has v2.0
            // Existential check: v2.0 > v1.0, so it's marked Superseded even though v3.0 > v2.0
            var reference = new List<WindowsImageDriverInfo>
            {
                MakeDriver("oem1.inf", "net.inf", "Acme", "3.0.0.0"),  // Newer version
                MakeDriver("oem2.inf", "net.inf", "Acme", "1.0.0.0")   // Older version
            };
            var current = new List<WindowsImageDriverInfo>
            {
                MakeDriver("oem3.inf", "net.inf", "Acme", "2.0.0.0")   // Middle version
            };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Added);
            Assert.Single(result.Superseded);
            Assert.Equal("oem3.inf", result.Superseded[0].PublishedName);
        }

        [Theory]
        [InlineData(@"C:\Mount", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme\net.cat", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme")]
        [InlineData(@"C:\Mount", @"Windows\System32\DriverStore\FileRepository\net_acme\net.cat", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme")]
        public void ResolveDriverSourceDirectory_HandlesAbsoluteAndRelativeCatalogPaths(string mountPath, string catalogFile, string expected)
        {
            var resolved = WindowsImageDriverService.ResolveDriverSourceDirectory(mountPath, catalogFile);
            Assert.Equal(expected, resolved, ignoreCase: true);
        }

        [Fact]
        public void ResolveDriverSourceDirectory_NullCatalogFile_ReturnsNull()
        {
            Assert.Null(WindowsImageDriverService.ResolveDriverSourceDirectory(@"C:\Mount", null));
        }

        [Fact]
        public void Export_CopiesDriverFilesToDestination()
        {
            var mountPath = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            var driverFolder = Path.Combine(mountPath, "Windows", "System32", "DriverStore", "FileRepository", "net_acme");
            var destination = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Dest-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(driverFolder);
            File.WriteAllText(Path.Combine(driverFolder, "net.inf"), "; fake inf");
            File.WriteAllText(Path.Combine(driverFolder, "net.cat"), "fake catalog");

            try
            {
                var driver = new WindowsImageDriverInfo
                {
                    PublishedName = "oem1.inf",
                    MountPath = mountPath,
                    CatalogFile = Path.Combine(driverFolder, "net.cat")
                };

                new WindowsImageDriverService().Export(driver, new DirectoryInfo(destination));

                var copiedInf = Path.Combine(destination, "net_acme", "net.inf");
                Assert.True(File.Exists(copiedInf));
            }
            finally
            {
                if (Directory.Exists(mountPath)) Directory.Delete(mountPath, true);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
            }
        }
    }
}
