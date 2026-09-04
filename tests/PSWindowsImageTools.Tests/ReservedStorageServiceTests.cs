using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ReservedStorageServiceTests
    {
        [Theory]
        [InlineData(@"C:\Mount", "/Image:\"C:\\Mount\" /Get-ReservedStorageState")]
        [InlineData(@"C:\Mount Dir", "/Image:\"C:\\Mount Dir\" /Get-ReservedStorageState")]
        public void BuildGetReservedStorageStateArguments_ReturnsExpectedDismArgs(string imagePath, string expected)
        {
            var args = ReservedStorageService.BuildGetReservedStorageStateArguments(imagePath);
            Assert.Equal(expected, args);
        }

        [Theory]
        [InlineData(true, @"C:\Mount", "/Image:\"C:\\Mount\" /Set-ReservedStorageState:Enabled")]
        [InlineData(false, @"C:\Mount", "/Image:\"C:\\Mount\" /Set-ReservedStorageState:Disabled")]
        [InlineData(true, @"C:\Mount Dir", "/Image:\"C:\\Mount Dir\" /Set-ReservedStorageState:Enabled")]
        public void BuildSetReservedStorageStateArguments_ReturnsExpectedDismArgs(bool enable, string imagePath, string expected)
        {
            var args = ReservedStorageService.BuildSetReservedStorageStateArguments(imagePath, enable);
            Assert.Equal(expected, args);
        }

        [Fact]
        public void ParseReservedStorageState_EnabledOutput_ReturnsEnabled()
        {
            var output = "Deployment Image Servicing and Management tool\n" +
                         "Version: 10.0.19041.1\n\n" +
                         "Image Version: 10.0.19041.1\n\n" +
                         "Reserved Storage is: Enabled\n\n" +
                         "The operation completed successfully.\n";

            Assert.Equal(ReservedStorageState.Enabled, ReservedStorageService.ParseReservedStorageState(output));
        }

        [Fact]
        public void ParseReservedStorageState_DisabledOutput_ReturnsDisabled()
        {
            var output = "Reserved Storage is: Disabled";

            Assert.Equal(ReservedStorageState.Disabled, ReservedStorageService.ParseReservedStorageState(output));
        }

        [Fact]
        public void ParseReservedStorageState_CaseInsensitive_ReturnsState()
        {
            Assert.Equal(ReservedStorageState.Enabled, ReservedStorageService.ParseReservedStorageState("reserved storage is: enabled"));
            Assert.Equal(ReservedStorageState.Disabled, ReservedStorageService.ParseReservedStorageState("RESERVED STORAGE IS: DISABLED"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseReservedStorageState_NullOrBlank_ReturnsNull(string? output)
        {
            Assert.Null(ReservedStorageService.ParseReservedStorageState(output));
        }

        [Fact]
        public void ParseReservedStorageState_UnknownValue_ReturnsNull()
        {
            Assert.Null(ReservedStorageService.ParseReservedStorageState("Reserved Storage is: Unknown"));
            Assert.Null(ReservedStorageService.ParseReservedStorageState("Deployment Image Servicing and Management tool"));
        }

        [Theory]
        [InlineData("Reserved Storage Size: 1024", 1024L)]
        [InlineData("Reserved Storage Size: 1 KB", 1024L)]
        [InlineData("Reserved Storage Size: 5 MB", 5L * 1024 * 1024)]
        [InlineData("Reserved Storage Size: 7 GB", 7L * 1024 * 1024 * 1024)]
        [InlineData("Total Reserved Size 4.5 MB", (long)(4.5 * 1024 * 1024))]
        public void ParseReservedStorageSizeBytes_ParsesSizes(string line, long expectedBytes)
        {
            var parsed = ReservedStorageService.ParseReservedStorageSizeBytes(line);
            Assert.NotNull(parsed);
            Assert.Equal(expectedBytes, parsed!.Value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Reserved Storage is: Enabled")]
        [InlineData("Deployment Image Servicing and Management tool\nVersion: 10.0.19041.1")]
        public void ParseReservedStorageSizeBytes_NoSizeLine_ReturnsNull(string? output)
        {
            Assert.Null(ReservedStorageService.ParseReservedStorageSizeBytes(output));
        }

        [Fact]
        public void ExtractErrorMessage_ReturnsLastErrorLine()
        {
            var output = "Deployment Image Servicing and Management tool\n" +
                         "Error: 87\n" +
                         "The parameter is incorrect.\n" +
                         "Error: 50\n" +
                         "DISM does not support servicing Windows images.\n";

            var message = ReservedStorageService.ExtractErrorMessage(output, 50);

            Assert.Contains("Error: 50", message);
            Assert.Contains("exit code 50", message);
        }

        [Fact]
        public void ExtractErrorMessage_NoErrorLine_ReturnsLastNonEmptyLine()
        {
            var message = ReservedStorageService.ExtractErrorMessage("something went wrong here", 3);
            Assert.Equal("something went wrong here (exit code 3)", message);
        }

        [Theory]
        [InlineData(null, 5)]
        [InlineData("", 5)]
        [InlineData("   ", 5)]
        public void ExtractErrorMessage_EmptyOutput_ReturnsExitCodeOnly(string? output, int exitCode)
        {
            Assert.Equal($"dism.exe exited with code {exitCode}", ReservedStorageService.ExtractErrorMessage(output, exitCode));
        }
    }
}