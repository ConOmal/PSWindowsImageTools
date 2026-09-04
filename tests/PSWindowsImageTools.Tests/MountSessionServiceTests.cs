using System;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    /// <summary>
    /// Regression tests for the cross-session mount registry. Guard against the Newtonsoft
    /// DirectoryInfo serialization bug that silently emptied the registry.
    /// </summary>
    public class MountSessionServiceTests : IDisposable
    {
        public MountSessionServiceTests()
        {
            // Isolate every test from the shared state file
            if (File.Exists(MountSessionService.StateFilePath))
            {
                File.Delete(MountSessionService.StateFilePath);
            }
        }

        public void Dispose()
        {
            if (File.Exists(MountSessionService.StateFilePath))
            {
                File.Delete(MountSessionService.StateFilePath);
            }
        }

        private static MountedWindowsImage MakeImage(string mountPath, string mountId = "id-1")
        {
            return new MountedWindowsImage
            {
                MountId = mountId,
                SourceImagePath = @"C:\Images\install.wim",
                ImageIndex = 1,
                ImageName = "Windows 11 Pro",
                Edition = "Professional",
                Architecture = "x64",
                MountPath = new DirectoryInfo(mountPath),
                IsReadOnly = false
            };
        }

        [Fact]
        public void Register_CreatesStateFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                MountSessionService.Register(MakeImage(dir));

                Assert.True(File.Exists(MountSessionService.StateFilePath), "state file must be written");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Register_ThenGetActive_RoundTripsAllFields()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var original = MakeImage(dir);
                MountSessionService.Register(original);

                var active = MountSessionService.GetActive();

                var restored = Assert.Single(active);
                Assert.Equal(original.MountId, restored.MountId);
                Assert.Equal(original.ImageName, restored.ImageName);
                Assert.Equal(original.Edition, restored.Edition);
                Assert.Equal(original.Architecture, restored.Architecture);
                Assert.Equal(original.ImageIndex, restored.ImageIndex);
                Assert.Equal(original.SourceImagePath, restored.SourceImagePath);
                Assert.False(restored.IsReadOnly);
                Assert.Equal(dir, restored.MountPath!.FullName, ignoreCase: true);
                Assert.Equal(MountStatus.Mounted, restored.Status);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void GetActive_PrunesEntriesWhoseDirectoryVanished()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            MountSessionService.Register(MakeImage(dir, "alive"));

            // Register a second entry whose directory never existed
            MountSessionService.Register(MakeImage(dir + "-gone", "dead"));

            var active = MountSessionService.GetActive();

            Assert.Single(active);
            Assert.Equal("alive", active[0].MountId);
        }

        [Fact]
        public void Unregister_RemovesEntry()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            MountSessionService.Register(MakeImage(dir));
            MountSessionService.Unregister(dir);

            Assert.Empty(MountSessionService.GetActive());
        }

        [Fact]
        public void Register_ReplacesExistingEntryForSamePath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            MountSessionService.Register(MakeImage(dir, "first"));
            MountSessionService.Register(MakeImage(dir, "second"));

            var active = MountSessionService.GetActive();
            Assert.Single(active);
            Assert.Equal("second", active[0].MountId);
        }

        [Fact]
        public void Register_IgnoresNullMountPath()
        {
            MountSessionService.Register(new MountedWindowsImage { MountId = "no-path" });

            Assert.False(File.Exists(MountSessionService.StateFilePath));
        }

        [Fact]
        public void Prune_RemovesDeadEntriesAndReportsCount()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PSWIT-MSS-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            MountSessionService.Register(MakeImage(dir, "alive"));
            MountSessionService.Register(MakeImage(dir + "-gone", "dead"));

            var pruned = MountSessionService.Prune();

            Assert.Equal(1, pruned);
            Assert.Single(MountSessionService.GetActive());
        }
    }
}
