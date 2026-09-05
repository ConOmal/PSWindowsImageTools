using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class CapabilityRepositoryServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public CapabilityRepositoryServiceTests()
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
        public void ParseCabFileName_ConformingName_ParsesAllFields()
        {
            var filePath = "C:\\FoD\\Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab";

            var entry = CapabilityRepositoryService.ParseCabFileName(filePath);

            Assert.NotNull(entry);
            Assert.Equal("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab", entry!.FileName);
            Assert.Equal(filePath, entry.FilePath);
            Assert.Equal("Rsat.ActiveDirectory.DS-LDS.Tools", entry.CapabilityName);
            Assert.Equal("31bf3856ad364e35", entry.Token);
            Assert.Equal("amd64", entry.Architecture);
            Assert.Equal("neutral", entry.Language);
            Assert.Equal(string.Empty, entry.Version);
        }

        [Fact]
        public void ParseCabFileName_LanguageAndVersion_ParsedVerbatim()
        {
            var entry = CapabilityRepositoryService.ParseCabFileName(
                "C:\\FoD\\Microsoft-Windows-LanguageFeatures-Basic-en-us~31bf3856ad364e35~amd64~en-us~10.0.26100.1.cab");

            Assert.NotNull(entry);
            Assert.Equal("LanguageFeatures-Basic-en-us", entry!.CapabilityName);
            Assert.Equal("amd64", entry.Architecture);
            Assert.Equal("en-us", entry.Language);
            Assert.Equal("10.0.26100.1", entry.Version);
        }

        [Fact]
        public void ParseCabFileName_EmptyArchitectureSegment_ReportsNeutral()
        {
            var entry = CapabilityRepositoryService.ParseCabFileName(
                "C:\\FoD\\Microsoft-Windows-App.Steps.Core~31bf3856ad364e35~~en-us~1.0.0.0.cab");

            Assert.NotNull(entry);
            Assert.Equal("App.Steps.Core", entry!.CapabilityName);
            Assert.Equal("neutral", entry.Architecture);
            Assert.Equal("en-us", entry.Language);
        }

        [Theory]
        [InlineData("MICROSOFT-WINDOWS-Rsat.Tools~31bf3856ad364e35~amd64~en-us~1.0.0.0.CAB")]
        [InlineData("microsoft-windows-Rsat.Tools~31bf3856ad364e35~amd64~en-us~1.0.0.0.cab")]
        public void ParseCabFileName_CaseInsensitivePrefixAndExtension_Parses(string fileName)
        {
            var entry = CapabilityRepositoryService.ParseCabFileName(Path.Combine("C:\\FoD", fileName));

            Assert.NotNull(entry);
            Assert.Equal("Rsat.Tools", entry!.CapabilityName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("C:\\FoD\\Microsoft-Windows-Rsat.Tools.cab")]
        [InlineData("C:\\FoD\\Contoso-Tool~31bf3856ad364e35~amd64~en-us~1.0.0.0.cab")]
        [InlineData("C:\\FoD\\Microsoft-Windows-Rsat.Tools~31bf3856ad364e35~amd64~en-us~1.0.0.0~extra.cab")]
        [InlineData("C:\\FoD\\Microsoft-Windows-Rsat.Tools~31bf3856ad364e35~amd64.cab")]
        [InlineData("C:\\FoD\\Microsoft-Windows-~31bf3856ad364e35~amd64~en-us~1.0.0.0.cab")]
        [InlineData("C:\\FoD\\Microsoft-Windows-Rsat.Tools~31bf3856ad364e35~amd64~en-us~1.0.0.0.txt")]
        public void ParseCabFileName_NonConforming_ReturnsNull(string? filePath)
        {
            var entry = CapabilityRepositoryService.ParseCabFileName(filePath!);

            Assert.Null(entry);
        }

        [Fact]
        public void MatchesFilters_NoFilters_MatchesAll()
        {
            var entry = MakeEntry("Rsat.ActiveDirectory.DS-LDS.Tools", "amd64", "neutral");

            Assert.True(CapabilityRepositoryService.MatchesFilters(entry, null, null, null));
            Assert.True(CapabilityRepositoryService.MatchesFilters(entry, string.Empty, string.Empty, string.Empty));
        }

        [Theory]
        [InlineData("^Rsat\\.", true)]
        [InlineData("OpenSSH", false)]
        public void MatchesFilters_NameRegex_Matches(string nameFilter, bool expected)
        {
            var entry = MakeEntry("Rsat.ActiveDirectory.DS-LDS.Tools", "amd64", "neutral");

            Assert.Equal(expected, CapabilityRepositoryService.MatchesFilters(entry, nameFilter, null, null));
        }

        [Fact]
        public void MatchesFilters_ArchitectureAndLanguage_CaseInsensitive()
        {
            var entry = MakeEntry("LanguageFeatures-Basic-en-us", "amd64", "en-us");

            Assert.True(CapabilityRepositoryService.MatchesFilters(entry, null, "AMD64", "EN-US"));
            Assert.False(CapabilityRepositoryService.MatchesFilters(entry, null, "x86", null));
            Assert.False(CapabilityRepositoryService.MatchesFilters(entry, null, null, "de-de"));
        }

        [Fact]
        public void MatchesFilters_NeutralTokens_Match()
        {
            var entry = MakeEntry("Rsat.ActiveDirectory.DS-LDS.Tools", "amd64", "neutral");

            Assert.True(CapabilityRepositoryService.MatchesFilters(entry, null, null, "neutral"));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("  ", true)]
        [InlineData("^Rsat\\.", true)]
        [InlineData("(", false)]
        [InlineData("[a-", false)]
        public void IsValidRegexPattern_ValidatesPattern(string? pattern, bool expected)
        {
            Assert.Equal(expected, CapabilityRepositoryService.IsValidRegexPattern(pattern));
        }

        [Fact]
        public void GroupEntries_GroupsByNameCaseInsensitively_WithSortedMembers()
        {
            var entries = new List<CapabilityRepositoryEntry>
            {
                MakeEntry("Rsat.ActiveDirectory.DS-LDS.Tools", "x86", "neutral", 100),
                MakeEntry("rsat.activedirectory.ds-lds.tools", "amd64", "neutral", 200),
                MakeEntry("OpenSSH.Client~~~~0.0.1.0", "amd64", "en-us", 50)
            };

            var groups = CapabilityRepositoryService.GroupEntries(entries);

            Assert.Equal(2, groups.Count);
            Assert.Equal("OpenSSH.Client~~~~0.0.1.0", groups[0].CapabilityName);
            Assert.Equal(1, groups[0].PackageCount);
            Assert.Equal("Rsat.ActiveDirectory.DS-LDS.Tools", groups[1].CapabilityName);
            Assert.Equal(2, groups[1].PackageCount);
            Assert.Equal(new[] { "amd64", "x86" }, groups[1].Architectures);
            Assert.Equal(new[] { "neutral" }, groups[1].Languages);
            Assert.Equal(300L, groups[1].TotalSize);
        }

        [Fact]
        public void GroupEntries_EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(CapabilityRepositoryService.GroupEntries(new List<CapabilityRepositoryEntry>()));
        }

        [Fact]
        public void IndexRepository_IndexesConformingCabsOnly_WithSizesAndSortOrder()
        {
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~x86~~.cab");
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab");
            WriteCab("Microsoft-Windows-LanguageFeatures-Basic-en-us~31bf3856ad364e35~amd64~en-us~10.0.26100.1.cab");
            File.WriteAllBytes(Path.Combine(_tempDirectory, "Microsoft-Windows-Notepad-System-FoD-Package~31bf3856ad364e35~amd64~en-US~10.0.26100.1.cab"), new byte[10]);
            WriteCab("FoDMetadata_Client.cab");
            File.WriteAllText(Path.Combine(_tempDirectory, "readme.txt"), "not a cab");

            var entries = new CapabilityRepositoryService().IndexRepository(new DirectoryInfo(_tempDirectory), null, null, null, ModuleCallbacks.Silent);

            Assert.Equal(4, entries.Count);
            Assert.Equal(
                new[]
                {
                    "LanguageFeatures-Basic-en-us",
                    "Notepad-System-FoD-Package",
                    "Rsat.ActiveDirectory.DS-LDS.Tools",
                    "Rsat.ActiveDirectory.DS-LDS.Tools"
                },
                entries.Select(entry => entry.CapabilityName).ToArray());
            Assert.Equal("amd64", entries[2].Architecture);
            Assert.Equal("x86", entries[3].Architecture);
            Assert.Equal(string.Empty, entries[2].Version);
            Assert.Equal("10.0.26100.1", entries[0].Version);

            var notepad = entries.Single(entry => entry.CapabilityName == "Notepad-System-FoD-Package");
            Assert.Equal(10L, notepad.FileSize);
            Assert.Equal(Path.Combine(_tempDirectory, notepad.FileName), notepad.FilePath);
        }

        [Fact]
        public void IndexRepository_AppliesFilters()
        {
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~x86~~.cab");
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab");
            WriteCab("Microsoft-Windows-LanguageFeatures-Basic-en-us~31bf3856ad364e35~amd64~en-us~10.0.26100.1.cab");

            var service = new CapabilityRepositoryService();

            var rsatX86 = service.IndexRepository(new DirectoryInfo(_tempDirectory), "^Rsat\\.", "x86", null, ModuleCallbacks.Silent);
            Assert.Single(rsatX86);
            Assert.Equal("x86", rsatX86[0].Architecture);

            var neutral = service.IndexRepository(new DirectoryInfo(_tempDirectory), null, null, "neutral", ModuleCallbacks.Silent);
            Assert.Equal(2, neutral.Count);

            var none = service.IndexRepository(new DirectoryInfo(_tempDirectory), "OpenSSH", null, null, ModuleCallbacks.Silent);
            Assert.Empty(none);
        }

        [Fact]
        public void IndexRepository_GroupsEndToEnd()
        {
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~x86~~.cab");
            WriteCab("Microsoft-Windows-Rsat.ActiveDirectory.DS-LDS.Tools~31bf3856ad364e35~amd64~~.cab");
            WriteCab("Microsoft-Windows-OpenSSH.Client~31bf3856ad364e35~amd64~en-us~0.0.1.0.cab");

            var entries = new CapabilityRepositoryService().IndexRepository(new DirectoryInfo(_tempDirectory), null, null, null, ModuleCallbacks.Silent);
            var groups = CapabilityRepositoryService.GroupEntries(entries);

            Assert.Equal(2, groups.Count);
            Assert.Equal("OpenSSH.Client", groups[0].CapabilityName);
            Assert.Equal("Rsat.ActiveDirectory.DS-LDS.Tools", groups[1].CapabilityName);
            Assert.Equal(2, groups[1].PackageCount);
            Assert.Equal(new[] { "amd64", "x86" }, groups[1].Architectures);
            Assert.Equal(new[] { "neutral" }, groups[1].Languages);
        }

        [Fact]
        public void IndexRepository_MissingDirectory_ReturnsEmptyWithoutThrowing()
        {
            var missing = new DirectoryInfo(Path.Combine(_tempDirectory, "does-not-exist"));

            var entries = new CapabilityRepositoryService().IndexRepository(missing, null, null, null, ModuleCallbacks.Silent);

            Assert.Empty(entries);
        }

        private static CapabilityRepositoryEntry MakeEntry(string capabilityName, string architecture, string language, long fileSize = 0)
        {
            return new CapabilityRepositoryEntry
            {
                FileName = "Microsoft-Windows-" + capabilityName + "~31bf3856ad364e35~" + architecture + "~" + language + "~1.0.0.0.cab",
                FilePath = "C:\\FoD\\Microsoft-Windows-" + capabilityName + ".cab",
                CapabilityName = capabilityName,
                Token = "31bf3856ad364e35",
                Architecture = architecture,
                Language = language,
                Version = "1.0.0.0",
                FileSize = fileSize
            };
        }

        private void WriteCab(string fileName)
        {
            File.WriteAllBytes(Path.Combine(_tempDirectory, fileName), Array.Empty<byte>());
        }
    }
}
