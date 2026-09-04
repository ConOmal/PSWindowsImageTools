using System;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class FormatUtilityServiceTests
    {
        [Theory]
        [InlineData("10.0.19045.3086", 10, 0, 19045, 3086)]
        [InlineData("Version: 10.0.19045.3086", 10, 0, 19045, 3086)]
        [InlineData("22621.3155", 10, 0, 22621, 3155)]
        [InlineData("22631", 10, 0, 22631, 0)]
        public void ParseVersion_ParsesWindowsFormats(string input, int major, int minor, int build, int revision)
        {
            var result = FormatUtilityService.ParseVersion(input);

            Assert.NotNull(result);
            Assert.Equal(new Version(major, minor, build, revision), result);
        }

        [Fact]
        public void ParseVersion_ThreePartKeepsRevisionUnset()
        {
            var result = FormatUtilityService.ParseVersion("10.0.22621");

            Assert.NotNull(result);
            Assert.Equal(22621, result!.Build);
            Assert.Equal(-1, result.Revision);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a version")]
        [InlineData("v:")]
        public void ParseVersion_ReturnsNullForInvalidInput(string? input)
        {
            Assert.Null(FormatUtilityService.ParseVersion(input!));
        }

        [Fact]
        public void TryParseVersion_HandlesPrefixesAndBrackets()
        {
            var success = FormatUtilityService.TryParseVersion("(build 10.0.19045.1)", out var result);

            Assert.True(success);
            Assert.Equal(new Version(10, 0, 19045, 1), result);
        }

        [Fact]
        public void ParseDate_ParsesIsoDate()
        {
            var result = FormatUtilityService.ParseDate("2024-01-09");

            Assert.NotNull(result);
            // AssumeUniversal style interprets date-only strings as UTC midnight; compare the UTC instant
            Assert.Equal(new DateTime(2024, 1, 9, 0, 0, 0, DateTimeKind.Utc), result!.Value.ToUniversalTime());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("definitely not a date")]
        public void ParseDate_ReturnsNullForInvalidInput(string? input)
        {
            Assert.Null(FormatUtilityService.ParseDate(input!));
        }

        [Fact]
        public void ParseDate_CleansPrefixes()
        {
            var result = FormatUtilityService.ParseDate("Released: 2024-01-09");

            Assert.NotNull(result);
            Assert.Equal(new DateTime(2024, 1, 9, 0, 0, 0, DateTimeKind.Utc), result!.Value.ToUniversalTime());
        }

        [Fact]
        public void ExtractKBArticles_FindsCaseInsensitiveMatches()
        {
            var result = FormatUtilityService.ExtractKBArticles("Includes KB5034123 and kb5034124 plus KB 5034125");

            Assert.Equal(new[] { "KB5034123", "KB5034124", "KB5034125" }, result);
        }

        [Fact]
        public void ExtractKBArticles_Deduplicates()
        {
            var result = FormatUtilityService.ExtractKBArticles("KB123456 KB123456 kb123456");

            Assert.Single(result);
            Assert.Equal("KB123456", result[0]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ExtractKBArticles_ReturnsEmptyForNullOrEmpty(string? input)
        {
            Assert.Empty(FormatUtilityService.ExtractKBArticles(input!));
        }

        [Theory]
        [InlineData("KB 5034123", "KB5034123")]
        [InlineData("kb5034123", "KB5034123")]
        [InlineData("5034123", "KB5034123")]
        [InlineData("abc", "")]
        [InlineData("", "")]
        public void NormalizeKBArticle_NormalizesInput(string input, string expected)
        {
            Assert.Equal(expected, FormatUtilityService.NormalizeKBArticle(input));
        }

        [Fact]
        public void FormatList_SingleItem()
        {
            Assert.Equal("Alpha", FormatUtilityService.FormatList(new[] { "Alpha" }));
        }

        [Fact]
        public void FormatList_TwoItemsUsesLastSeparator()
        {
            Assert.Equal("Alpha and Beta", FormatUtilityService.FormatList(new[] { "Alpha", "Beta" }));
        }

        [Fact]
        public void FormatList_ThreeItemsUsesBothSeparators()
        {
            Assert.Equal("Alpha, Beta and Gamma", FormatUtilityService.FormatList(new[] { "Alpha", "Beta", "Gamma" }));
        }

        [Fact]
        public void FormatList_SkipsWhitespaceItems()
        {
            Assert.Equal("Alpha and Beta", FormatUtilityService.FormatList(new[] { "Alpha", "  ", null!, "Beta" }));
        }

        [Fact]
        public void FormatListWithLimit_TruncatesWithMoreText()
        {
            var result = FormatUtilityService.FormatListWithLimit(new[] { "A", "B", "C", "D" }, maxItems: 2);

            Assert.Equal("A, B, and 2 more", result);
        }

        [Fact]
        public void FormatListWithLimit_NoLimitFormatsNormally()
        {
            var result = FormatUtilityService.FormatListWithLimit(new[] { "A", "B" }, maxItems: 0);

            Assert.Equal("A and B", result);
        }

        [Theory]
        [InlineData("Microsoft Windows 11 Pro", "Windows 11 Pro")]
        [InlineData("windows  10", "Windows 10")]
        [InlineData("Windows Server   2022", "Windows Server 2022")]
        public void NormalizeOperatingSystemName_NormalizesVariants(string input, string expected)
        {
            Assert.Equal(expected, FormatUtilityService.NormalizeOperatingSystemName(input));
        }

        [Theory]
        [InlineData("22h2", "22H2")]
        [InlineData("v22H2", "22H2")]
        [InlineData("Version 21H2", "21H2")]
        public void NormalizeReleaseId_NormalizesVariants(string input, string expected)
        {
            Assert.Equal(expected, FormatUtilityService.NormalizeReleaseId(input));
        }

        [Fact]
        public void ContainsIgnoreCase_IsCaseInsensitive()
        {
            Assert.True(FormatUtilityService.ContainsIgnoreCase("Windows 11 Enterprise", "ENTERPRISE"));
            Assert.False(FormatUtilityService.ContainsIgnoreCase("Windows 11 Pro", "Enterprise"));
        }

        [Fact]
        public void ContainsIgnoreCase_HandlesNulls()
        {
            Assert.False(FormatUtilityService.ContainsIgnoreCase(null!, "x"));
            Assert.False(FormatUtilityService.ContainsIgnoreCase("x", null!));
        }

        [Fact]
        public void GetValueIgnoreCase_FallsBackToCaseInsensitiveMatch()
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>
            {
                ["DisplayVersion"] = "10.0"
            };

            Assert.Equal("10.0", FormatUtilityService.GetValueIgnoreCase(dict, "displayversion"));
            Assert.Equal("fallback", FormatUtilityService.GetValueIgnoreCase(dict, "missing", "fallback"));
        }

        [Fact]
        public void FormatCollectionSummary_Pluralizes()
        {
            Assert.Equal("No images", FormatUtilityService.FormatCollectionSummary<string>(null!, "image"));
            Assert.Equal("No images", FormatUtilityService.FormatCollectionSummary(Array.Empty<string>(), "image"));
            Assert.Equal("1 image", FormatUtilityService.FormatCollectionSummary(new[] { "a" }, "image"));
            Assert.Equal("3 images", FormatUtilityService.FormatCollectionSummary(new[] { "a", "b", "c" }, "image"));
        }

        [Fact]
        public void FormatDuration_UsesIntelligentUnits()
        {
            Assert.Equal("1.0 seconds", FormatUtilityService.FormatDuration(TimeSpan.FromSeconds(1)));
            Assert.Equal("1.5 minutes", FormatUtilityService.FormatDuration(TimeSpan.FromSeconds(90)));
            Assert.Equal("2.0 hours", FormatUtilityService.FormatDuration(TimeSpan.FromHours(2)));
            Assert.Equal("1.5 days", FormatUtilityService.FormatDuration(TimeSpan.FromHours(36)));
        }
    }
}
