using System;
using System.IO;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WinREImageServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public WinREImageServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WinREImageServiceTests_" + Guid.NewGuid().ToString("N"));
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
        public void TryGetEmbeddedWinREPath_ReturnsFalseWhenMissing()
        {
            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);

            Assert.False(found);
            Assert.Equal(Path.Combine(_tempRoot, "Windows", "System32", "Recovery", "Winre.wim"), path);
        }

        [Fact]
        public void TryGetEmbeddedWinREPath_ReturnsTrueWhenPresent()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            File.WriteAllText(Path.Combine(recoveryDir, "Winre.wim"), "fake-wim-content");

            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);

            Assert.True(found);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void ExtractEmbeddedWinRE_ThrowsWhenMissing()
        {
            var destination = Path.Combine(_tempRoot, "extracted.wim");

            Assert.Throws<FileNotFoundException>(() => WinREImageService.ExtractEmbeddedWinRE(_tempRoot, destination));
        }

        [Fact]
        public void ExtractEmbeddedWinRE_CopiesFileOutAndClearsReadOnly()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            var sourcePath = Path.Combine(recoveryDir, "Winre.wim");
            File.WriteAllText(sourcePath, "fake-wim-content");
            File.SetAttributes(sourcePath, FileAttributes.ReadOnly);

            var destination = Path.Combine(_tempRoot, "extracted.wim");
            WinREImageService.ExtractEmbeddedWinRE(_tempRoot, destination);

            Assert.True(File.Exists(destination));
            Assert.Equal("fake-wim-content", File.ReadAllText(destination));
            Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));

            File.SetAttributes(sourcePath, FileAttributes.Normal);
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_ThrowsWhenSourceMissing()
        {
            Assert.Throws<FileNotFoundException>(() => WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, Path.Combine(_tempRoot, "missing.wim")));
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_CopiesFileIntoNestedPath()
        {
            var updatedSource = Path.Combine(_tempRoot, "updated.wim");
            File.WriteAllText(updatedSource, "updated-content");

            WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, updatedSource);

            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);
            Assert.True(found);
            Assert.Equal("updated-content", File.ReadAllText(path));
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_OverwritesReadOnlyExisting()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            var existingPath = Path.Combine(recoveryDir, "Winre.wim");
            File.WriteAllText(existingPath, "old-content");
            File.SetAttributes(existingPath, FileAttributes.ReadOnly);

            var updatedSource = Path.Combine(_tempRoot, "updated.wim");
            File.WriteAllText(updatedSource, "new-content");

            WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, updatedSource);

            Assert.Equal("new-content", File.ReadAllText(existingPath));
        }
    }
}
