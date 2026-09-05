using System;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class BootImageServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public BootImageServiceTests()
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

        [Fact]
        public void Locate_BootWimPresent_ReturnsBootImageInfo()
        {
            var sourcesDir = Path.Combine(_tempDirectory, "sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllBytes(Path.Combine(sourcesDir, "boot.wim"), new byte[] { 0x00 });

            var result = new BootImageService().Locate(new DirectoryInfo(_tempDirectory));

            Assert.NotNull(result);
            Assert.Equal("boot.wim", result!.Path.Name);
            Assert.Equal(_tempDirectory, result.SourceMediaRoot);
        }

        [Fact]
        public void Locate_NoBootWim_ReturnsNull()
        {
            var result = new BootImageService().Locate(new DirectoryInfo(_tempDirectory));

            Assert.Null(result);
        }
    }
}
