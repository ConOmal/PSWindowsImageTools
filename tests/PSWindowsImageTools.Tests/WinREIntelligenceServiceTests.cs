using System;
using System.IO;
using System.Text;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WinREIntelligenceServiceTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly WinREIntelligenceService _service;

        public WinREIntelligenceServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WinREIntelligenceServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _service = new WinREIntelligenceService();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void ResolveWinREPath_ReturnsCanonicalNestedPath()
        {
            var path = WinREIntelligenceService.ResolveWinREPath(_tempRoot);

            Assert.Equal(Path.Combine(_tempRoot, "Windows", "System32", "Recovery", "Winre.wim"), path);
        }

        [Theory]
        [InlineData(0L, 0.0)]
        [InlineData(1048576L, 1.0)]
        [InlineData(367001600L, 350.0)]
        [InlineData(1572864L, 1.5)]
        [InlineData(123456789L, 117.74)]
        public void BytesToMB_ConvertsAndRounds(long bytes, double expected)
        {
            Assert.Equal(expected, WinREIntelligenceService.BytesToMB(bytes), 2);
        }

        [Fact]
        public void TryParseWimHeader_NullOrTooShort_ReturnsNull()
        {
            Assert.Null(WinREIntelligenceService.TryParseWimHeader(null!));
            Assert.Null(WinREIntelligenceService.TryParseWimHeader(new byte[0]));
            Assert.Null(WinREIntelligenceService.TryParseWimHeader(new byte[207]));
        }

        [Fact]
        public void TryParseWimHeader_BadSignature_ReturnsNull()
        {
            var buffer = new byte[208];
            buffer[0] = (byte)'N';
            buffer[1] = (byte)'O';
            buffer[2] = (byte)'P';
            buffer[3] = (byte)'E';

            Assert.Null(WinREIntelligenceService.TryParseWimHeader(buffer));
        }

        [Fact]
        public void TryParseWimHeader_RoundTripsFields()
        {
            var guid = Guid.NewGuid();
            var header = WinREIntelligenceService.TryParseWimHeader(BuildSyntheticHeader(guid: guid));

            Assert.NotNull(header);
            Assert.True(header!.IsValid);
            Assert.Equal((uint)208, header.HeaderSize);
            Assert.Equal((uint)0x000D0000, header.Version);
            Assert.Equal(13, header.VersionMajor);
            Assert.Equal(0, header.VersionMinor);
            Assert.Equal("13.0", header.VersionText);
            Assert.Equal((uint)2, header.CompressionType);
            Assert.Equal("XPRESS", header.CompressionTypeName);
            Assert.Equal(guid, header.WimGuid);
            Assert.Equal(1, header.PartNumber);
            Assert.Equal(1, header.NumberOfParts);
            Assert.Equal(1, header.ImageCount);
            Assert.Equal(0, header.BootIndex);
            Assert.Equal(4096, header.MetadataOffset);
            Assert.Equal(8192, header.TotalBytes);
        }

        [Fact]
        public void TryParseWimHeader_NewerVersionFifteen()
        {
            var header = WinREIntelligenceService.TryParseWimHeader(BuildSyntheticHeader(version: 0x000F0000));

            Assert.NotNull(header);
            Assert.Equal("15.0", header!.VersionText);
        }

        [Theory]
        [InlineData(1u, "LZX")]
        [InlineData(2u, "XPRESS")]
        [InlineData(3u, "LZMS")]
        [InlineData(9u, "Unknown (9)")]
        public void MapCompressionType_MapsKnownAndUnknown(uint type, string expected)
        {
            Assert.Equal(expected, WinREIntelligenceService.MapCompressionType(type));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_Utf16WithBom()
        {
            var xml = "<WIM><IMAGE><DISPLAYNAME>Windows Recovery Environment</DISPLAYNAME></IMAGE></WIM>";
            var payload = Encoding.Unicode.GetBytes(xml);
            var bytes = new byte[2 + payload.Length];
            bytes[0] = 0xFF;
            bytes[1] = 0xFE;
            Array.Copy(payload, 0, bytes, 2, payload.Length);

            Assert.Equal("Windows Recovery Environment", WinREIntelligenceService.TryExtractXmlImageDisplayName(bytes));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_Utf8WithoutBom()
        {
            var bytes = Encoding.UTF8.GetBytes("<WIM><IMAGE><DISPLAYNAME>WinRE x64</DISPLAYNAME></IMAGE></WIM>");

            Assert.Equal("WinRE x64", WinREIntelligenceService.TryExtractXmlImageDisplayName(bytes));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_MissingElement_ReturnsNull()
        {
            var bytes = Encoding.UTF8.GetBytes("<WIM><IMAGE><NAME>No Display Name</NAME></IMAGE></WIM>");

            Assert.Null(WinREIntelligenceService.TryExtractXmlImageDisplayName(bytes));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_EmptyElement_ReturnsNull()
        {
            var bytes = Encoding.UTF8.GetBytes("<WIM><IMAGE><DISPLAYNAME> </DISPLAYNAME></IMAGE></WIM>");

            Assert.Null(WinREIntelligenceService.TryExtractXmlImageDisplayName(bytes));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_UnescapesEntities()
        {
            var bytes = Encoding.UTF8.GetBytes("<WIM><IMAGE><DISPLAYNAME>Rock &amp; Roll / &lt;Custom&gt;</DISPLAYNAME></IMAGE></WIM>");

            Assert.Equal("Rock & Roll / <Custom>", WinREIntelligenceService.TryExtractXmlImageDisplayName(bytes));
        }

        [Fact]
        public void TryExtractXmlImageDisplayName_NullOrEmpty_ReturnsNull()
        {
            Assert.Null(WinREIntelligenceService.TryExtractXmlImageDisplayName(null!));
            Assert.Null(WinREIntelligenceService.TryExtractXmlImageDisplayName(new byte[0]));
        }

        [Fact]
        public void Inspect_AbsentWim_ReturnsNegativeReport()
        {
            var report = _service.Inspect(_tempRoot, detailed: false);

            Assert.Equal(_tempRoot, report.ImagePath);
            Assert.False(report.WinREPresent);
            Assert.Equal(WinREIntelligenceService.ResolveWinREPath(_tempRoot), report.WinREPath);
            Assert.False(report.WimHeaderParsed);
        }

        [Fact]
        public void Inspect_ReportsFileMetadata()
        {
            var wimPath = CreateSyntheticWimWithXml(out _);
            var lastWrite = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(wimPath, lastWrite);

            var report = _service.Inspect(_tempRoot, detailed: false);

            Assert.True(report.WinREPresent);
            Assert.Equal(wimPath, report.WinREPath);
            Assert.Equal(new FileInfo(wimPath).Length, report.SizeBytes);
            Assert.Equal(WinREIntelligenceService.BytesToMB(report.SizeBytes), report.SizeMB, 2);
            Assert.Equal(lastWrite, report.LastModifiedUtc);
        }

        [Fact]
        public void Inspect_WithoutDetailed_ParsesHeaderButLeavesXmlDisplayNameNull()
        {
            CreateSyntheticWimWithXml(out var guid);

            var report = _service.Inspect(_tempRoot, detailed: false);

            Assert.True(report.WimHeaderParsed);
            Assert.NotNull(report.WimHeader);
            Assert.Equal(guid, report.WimHeader!.WimGuid);
            Assert.Equal("13.0", report.WimVersion);
            Assert.Equal(1, report.ImageCount);
            Assert.Equal("XPRESS", report.CompressionType);
            Assert.Null(report.XmlImageDisplayName);
        }

        [Fact]
        public void Inspect_WithDetailed_ReadsXmlDisplayName()
        {
            CreateSyntheticWimWithXml(out _);

            var report = _service.Inspect(_tempRoot, detailed: true);

            Assert.True(report.WimHeaderParsed);
            Assert.Equal("Windows Recovery Environment", report.XmlImageDisplayName);
        }

        [Fact]
        public void Inspect_NonWimPayload_DegradesGracefully()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            File.WriteAllText(Path.Combine(recoveryDir, "Winre.wim"), "not-a-real-wim");

            var report = _service.Inspect(_tempRoot, detailed: true);

            Assert.True(report.WinREPresent);
            Assert.False(report.WimHeaderParsed);
            Assert.Null(report.WimHeader);
            Assert.Null(report.XmlImageDisplayName);
            Assert.Equal(14, report.SizeBytes);
        }

        [Fact]
        public void Inspect_MissingMountPath_Throws()
        {
            var missing = Path.Combine(_tempRoot, "does-not-exist");

            Assert.Throws<DirectoryNotFoundException>(() => _service.Inspect(missing, detailed: false));
        }

        [Fact]
        public void Inspect_EmptyMountPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => _service.Inspect(string.Empty, detailed: false));
        }

        private string CreateSyntheticWimWithXml(out Guid wimGuid)
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            var wimPath = Path.Combine(recoveryDir, "Winre.wim");

            const long metadataOffset = 4096;
            wimGuid = Guid.NewGuid();

            var xml = "<WIM><IMAGE><DISPLAYNAME>Windows Recovery Environment</DISPLAYNAME></IMAGE></WIM>";
            var payload = Encoding.Unicode.GetBytes(xml);
            var xmlBytes = new byte[2 + payload.Length];
            xmlBytes[0] = 0xFF;
            xmlBytes[1] = 0xFE;
            Array.Copy(payload, 0, xmlBytes, 2, payload.Length);

            var totalBytes = metadataOffset + xmlBytes.Length;
            var header = BuildSyntheticHeader(
                guid: wimGuid,
                metadataOffset: (ulong)metadataOffset,
                totalBytes: (ulong)totalBytes);

            using (var fs = File.Create(wimPath))
            {
                fs.Write(header, 0, header.Length);
                fs.Seek(metadataOffset, SeekOrigin.Begin);
                fs.Write(xmlBytes, 0, xmlBytes.Length);
            }

            return wimPath;
        }

        private static byte[] BuildSyntheticHeader(
            uint version = 0x000D0000,
            Guid? guid = null,
            ulong metadataOffset = 4096,
            ulong totalBytes = 8192)
        {
            var bytes = new byte[208];
            var signature = new byte[] { (byte)'M', (byte)'S', (byte)'W', (byte)'I', (byte)'M', 0, 0, 0 };
            Array.Copy(signature, 0, bytes, 0, 8);

            WriteUInt32(bytes, 8, 208);
            WriteUInt32(bytes, 12, version);
            WriteUInt32(bytes, 16, 0);
            WriteUInt32(bytes, 20, 2);

            var guidBytes = (guid ?? Guid.NewGuid()).ToByteArray();
            Array.Copy(guidBytes, 0, bytes, 31, 16);

            WriteUInt16(bytes, 47, 1);
            WriteUInt16(bytes, 49, 1);
            WriteUInt64(bytes, 51, 1);
            WriteUInt64(bytes, 59, 0);
            WriteUInt64(bytes, 75, metadataOffset);
            WriteUInt64(bytes, 83, totalBytes);

            return bytes;
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUInt64(byte[] buffer, int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                buffer[offset + i] = (byte)((value >> (8 * i)) & 0xFF);
            }
        }
    }
}