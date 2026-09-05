using System.Collections.Generic;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class AppProvisioningServiceTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public AppProvisioningServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void ExportWinGetConfiguration_WritesYamlWithSchemaHeaderAndPackages()
        {
            var packages = new List<WinGetConfigurationEntry>
            {
                new WinGetConfigurationEntry { PackageIdentifier = "Microsoft.PowerToys", Version = "0.87.0", Source = "winget" },
                new WinGetConfigurationEntry { PackageIdentifier = "7zip.7zip", Source = "winget" }
            };

            var result = new AppProvisioningService().ExportWinGetConfiguration(packages, new DirectoryInfo(_tempDirectory));

            Assert.True(result.ConfigPath.Exists);
            var yaml = File.ReadAllText(result.ConfigPath.FullName);
            Assert.Contains("yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2", yaml);
            Assert.Contains("Microsoft.WinGet.DSC/WinGetPackage", yaml);
            Assert.Contains("Microsoft.PowerToys", yaml);
            Assert.Contains("7zip.7zip", yaml);
            Assert.Equal(2, result.Packages.Count);
        }

        [Fact]
        public void ExportWinGetConfiguration_WritesWellFormedScheduledTaskXml()
        {
            var packages = new List<WinGetConfigurationEntry>
            {
                new WinGetConfigurationEntry { PackageIdentifier = "7zip.7zip", Source = "winget" }
            };

            var result = new AppProvisioningService().ExportWinGetConfiguration(packages, new DirectoryInfo(_tempDirectory));

            Assert.True(result.ScheduledTaskPath.Exists);
            // Must parse as well-formed XML — throws if malformed
            var doc = new System.Xml.XmlDocument();
            doc.Load(result.ScheduledTaskPath.FullName);
            Assert.Equal("Task", doc.DocumentElement!.Name);
        }

        [Fact]
        public void ExportWinGetConfiguration_EmptyPackageList_StillWritesValidFiles()
        {
            var result = new AppProvisioningService().ExportWinGetConfiguration(new List<WinGetConfigurationEntry>(), new DirectoryInfo(_tempDirectory));

            Assert.True(result.ConfigPath.Exists);
            Assert.Empty(result.Packages);
        }
    }
}
