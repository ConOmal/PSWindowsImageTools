using System;
using System.Collections.Generic;
using System.IO;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsISOExtractionServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public WindowsISOExtractionServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsISOExtractionServiceTests_" + Guid.NewGuid().ToString("N"));
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
        public void CopyDirectoryTree_CopiesNestedFilesAndClearsReadOnly()
        {
            var source = Path.Combine(_tempRoot, "source");
            var destination = Path.Combine(_tempRoot, "destination");
            Directory.CreateDirectory(Path.Combine(source, "sources"));
            Directory.CreateDirectory(Path.Combine(source, "boot"));

            var installWim = Path.Combine(source, "sources", "install.wim");
            File.WriteAllText(installWim, "install-wim-content");
            File.SetAttributes(installWim, FileAttributes.ReadOnly);

            File.WriteAllText(Path.Combine(source, "boot", "bootmgr"), "bootmgr-content");

            WindowsISOExtractionService.CopyDirectoryTree(source, destination);

            var copiedInstallWim = Path.Combine(destination, "sources", "install.wim");
            var copiedBootmgr = Path.Combine(destination, "boot", "bootmgr");

            Assert.True(File.Exists(copiedInstallWim));
            Assert.Equal("install-wim-content", File.ReadAllText(copiedInstallWim));
            Assert.False(File.GetAttributes(copiedInstallWim).HasFlag(FileAttributes.ReadOnly));

            Assert.True(File.Exists(copiedBootmgr));
            Assert.Equal("bootmgr-content", File.ReadAllText(copiedBootmgr));

            File.SetAttributes(installWim, FileAttributes.Normal);
        }

        [Fact]
        public void CopyDirectoryTree_ReportsCompletionProgress()
        {
            var source = Path.Combine(_tempRoot, "source2");
            var destination = Path.Combine(_tempRoot, "destination2");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "b.txt"), "b");

            var reportedPercentages = new List<int>();
            WindowsISOExtractionService.CopyDirectoryTree(source, destination, (percentage, status) => reportedPercentages.Add(percentage));

            Assert.Equal(100, reportedPercentages[reportedPercentages.Count - 1]);
        }
    }
}
