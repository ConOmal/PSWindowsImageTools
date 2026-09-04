using System;
using System.IO;
using System.Net;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class NetworkServiceResumeTests : IDisposable
    {
        private readonly string _tempRoot;

        public NetworkServiceResumeTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "NetworkServiceResumeTests_" + Guid.NewGuid().ToString("N"));
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
        public void ComputeResumeStartPosition_ResumeFalse_ReturnsZero()
        {
            var path = Path.Combine(_tempRoot, "file.iso");
            File.WriteAllText(path, "some existing partial content");

            Assert.Equal(0, NetworkService.ComputeResumeStartPosition(path, resume: false));
        }

        [Fact]
        public void ComputeResumeStartPosition_FileMissing_ReturnsZero()
        {
            var path = Path.Combine(_tempRoot, "missing.iso");

            Assert.Equal(0, NetworkService.ComputeResumeStartPosition(path, resume: true));
        }

        [Fact]
        public void ComputeResumeStartPosition_ResumeTrueExistingFile_ReturnsFileLength()
        {
            var path = Path.Combine(_tempRoot, "file.iso");
            var content = "some existing partial content";
            File.WriteAllText(path, content);

            var result = NetworkService.ComputeResumeStartPosition(path, resume: true);

            Assert.Equal(new FileInfo(path).Length, result);
            Assert.True(result > 0);
        }

        [Fact]
        public void ShouldRestartFromScratch_PartialContentWithRangeRequested_ReturnsFalse()
        {
            Assert.False(NetworkService.ShouldRestartFromScratch(HttpStatusCode.PartialContent, requestedStartPosition: 1024));
        }

        [Fact]
        public void ShouldRestartFromScratch_OkStatusWithRangeRequested_ReturnsTrue()
        {
            Assert.True(NetworkService.ShouldRestartFromScratch(HttpStatusCode.OK, requestedStartPosition: 1024));
        }

        [Fact]
        public void ShouldRestartFromScratch_NoRangeRequested_ReturnsFalse()
        {
            Assert.False(NetworkService.ShouldRestartFromScratch(HttpStatusCode.OK, requestedStartPosition: 0));
        }
    }
}
