using System;
using System.IO;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsInstallationMediaTests : IDisposable
    {
        private readonly string _tempRoot;

        public WindowsInstallationMediaTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsInstallationMediaTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void FromRoot_ResolvesExistingWimFiles()
        {
            var sourcesDir = Path.Combine(_tempRoot, "sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllText(Path.Combine(sourcesDir, "install.wim"), "fake");
            File.WriteAllText(Path.Combine(sourcesDir, "boot.wim"), "fake");

            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.NotNull(media.InstallWim);
            Assert.NotNull(media.BootWim);
            Assert.Null(media.InstallEsd);
            Assert.Equal(Path.Combine(sourcesDir, "install.wim"), media.InstallWim!.FullName);
        }

        [Fact]
        public void FromRoot_ResolvesInstallEsdWhenPresent()
        {
            var sourcesDir = Path.Combine(_tempRoot, "sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllText(Path.Combine(sourcesDir, "install.esd"), "fake");

            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.NotNull(media.InstallEsd);
            Assert.Null(media.InstallWim);
        }

        [Fact]
        public void FromRoot_ReturnsNullForMissingFiles()
        {
            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.Null(media.InstallWim);
            Assert.Null(media.InstallEsd);
            Assert.Null(media.BootWim);
        }

        [Fact]
        public void ToString_ReturnsRootPath()
        {
            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.Equal(_tempRoot, media.ToString());
        }
    }
}
