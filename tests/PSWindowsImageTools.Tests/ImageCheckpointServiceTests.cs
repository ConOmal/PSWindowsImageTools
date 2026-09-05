using System;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ImageCheckpointServiceTests : IDisposable
    {
        private readonly string _mountDir;
        private readonly string _checkpointRoot;

        public ImageCheckpointServiceTests()
        {
            _mountDir = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Mount-" + Guid.NewGuid().ToString("N"));
            _checkpointRoot = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Checkpoints-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_mountDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_mountDir)) Directory.Delete(_mountDir, true);
            if (Directory.Exists(_checkpointRoot)) Directory.Delete(_checkpointRoot, true);
        }

        private MountedWindowsImage MakeMountedImage()
        {
            return new MountedWindowsImage
            {
                MountId = "test-mount-id",
                ImageName = "Test Image",
                MountPath = new DirectoryInfo(_mountDir),
                Status = MountStatus.Mounted,
                IsReadOnly = false
            };
        }

        [Fact]
        public void Create_CopiesFilesToCheckpointDirectory()
        {
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "original content");

            var service = new ImageCheckpointService(_checkpointRoot);
            var checkpoint = service.Create(MakeMountedImage(), "before-change");

            Assert.Equal("before-change", checkpoint.Label);
            Assert.Equal("test-mount-id", checkpoint.MountId);
            Assert.True(checkpoint.CheckpointPath.Exists);

            var copiedFile = Path.Combine(checkpoint.CheckpointPath.FullName, "marker.txt");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("original content", File.ReadAllText(copiedFile));
        }

        [Fact]
        public void Create_ComputesNonZeroSizeBytesForNonEmptyDirectory()
        {
            File.WriteAllBytes(Path.Combine(_mountDir, "data.bin"), new byte[1024]);

            var service = new ImageCheckpointService(_checkpointRoot);
            var checkpoint = service.Create(MakeMountedImage(), null);

            Assert.True(checkpoint.SizeBytes >= 1024);
        }

        [Fact]
        public void List_ReturnsCreatedCheckpoints_FilteredByMountId()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var image1 = MakeMountedImage();
            image1.MountId = "mount-1";
            var image2 = MakeMountedImage();
            image2.MountId = "mount-2";

            service.Create(image1, "cp1");
            service.Create(image2, "cp2");

            var all = service.List(null);
            Assert.Equal(2, all.Count);

            var filtered = service.List("mount-1");
            Assert.Single(filtered);
            Assert.Equal("cp1", filtered[0].Label);
        }

        [Fact]
        public void Restore_RevertsModifiedFileToCheckpointContent()
        {
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "original content");

            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "before-edit");

            // Simulate a servicing edit after the checkpoint
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "modified content");
            File.WriteAllText(Path.Combine(_mountDir, "new-file.txt"), "should be removed on restore");

            service.Restore(checkpoint, mountedImage);

            Assert.Equal("original content", File.ReadAllText(Path.Combine(_mountDir, "marker.txt")));
            Assert.False(File.Exists(Path.Combine(_mountDir, "new-file.txt")));
        }

        [Fact]
        public void Restore_ReadOnlyMount_ThrowsInvalidOperationException()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "cp");

            mountedImage.IsReadOnly = true;

            Assert.Throws<InvalidOperationException>(() => service.Restore(checkpoint, mountedImage));
        }

        [Fact]
        public void Restore_UnmountedImage_ThrowsInvalidOperationException()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "cp");

            mountedImage.Status = MountStatus.Unmounted;

            Assert.Throws<InvalidOperationException>(() => service.Restore(checkpoint, mountedImage));
        }

        [Fact]
        public void Delete_RemovesCheckpointDirectoryAndIndexEntry()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "to-delete");

            Assert.True(checkpoint.CheckpointPath.Exists);

            service.Delete(checkpoint);

            Assert.False(Directory.Exists(checkpoint.CheckpointPath.FullName));
            Assert.Empty(service.List(mountedImage.MountId));
        }
    }
}
