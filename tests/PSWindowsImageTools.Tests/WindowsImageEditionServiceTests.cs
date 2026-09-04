using System;
using System.Collections.Generic;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsImageEditionServiceTests
    {
        [Theory]
        [InlineData(null, null, false, true)]                 // no edition, server: still requires edition -> fail
        [InlineData("Professional", null, true, true)]        // server + edition: fail
        [InlineData(null, "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", true, true)] // server + key: fail
        [InlineData("Professional", null, false, false)]      // valid client
        [InlineData("Professional", "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", false, false)] // valid client + key
        public void ValidateEditionParameters_Combinations(string? edition, string? productKey, bool serverEdition, bool throws)
        {
            if (throws)
            {
                Assert.Throws<ArgumentException>(() =>
                    WindowsImageEditionService.ValidateEditionParameters(edition, productKey, serverEdition));
            }
            else
            {
                WindowsImageEditionService.ValidateEditionParameters(edition, productKey, serverEdition);
            }
        }

        [Fact]
        public void ValidateEditionParameters_ServerEditionAllowsNoEditionAndNoKey()
        {
            Assert.Throws<ArgumentException>(() =>
                WindowsImageEditionService.ValidateEditionParameters("Professional", null, true));
        }

        [Fact]
        public void ValidateEditionParameters_ServerEditionRequiresNoEditionArgument()
        {
            // With serverEdition true the server path is selected; edition must be empty
            Assert.Throws<ArgumentException>(() =>
                WindowsImageEditionService.ValidateEditionParameters("Professional", null, true));
        }

        [Fact]
        public void ValidateEditionParameters_RejectsProductKeyWithServerEdition()
        {
            Assert.Throws<ArgumentException>(() =>
                WindowsImageEditionService.ValidateEditionParameters(null, "XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateEditionParameters_RejectsMissingEditionForClientPath(string? edition)
        {
            Assert.Throws<ArgumentException>(() =>
                WindowsImageEditionService.ValidateEditionParameters(edition, null, false));
        }

        [Theory]
        [InlineData("XXXXX-XXXXX-XXXXX-XXXXX-XXXXX", true)]
        [InlineData("ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", true)]
        [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXY", true)]        // flat 25
        [InlineData("", false)]
        [InlineData("short", false)]
        [InlineData("XXXXX-XXXXX-XXXXX-XXXXX-AAAA", false)]   // 4x5+4
        [InlineData("XXXXX-XXXXX-XXXXX-XXXXX-XXXXXX", false)] // group of 6
        [InlineData("XXXXX-XXXXXX-XXXXX-XXXXX-XXXXX", false)] // middle group of 6
        [InlineData("ABCDE-FGHIJ-KLMNO-PQRST", false)]        // 4 groups
        [InlineData("ABC DE-FGHIJ-KLMNO-PQRST-UVWXY", false)] // space not alphanumeric
        [InlineData("AAAAA-AAAAA-AAAAA-AAAAA-AA AA", false)]  // space
        public void IsValidProductKeyFormat_Validates(string key, bool expected)
        {
            Assert.Equal(expected, WindowsImageEditionService.IsValidProductKeyFormat(key));
        }

        [Theory]
        [InlineData("XXXXX-XXXXX-XXXXX-XXXXX-ABCDE", "XXXXX-XXXXX-XXXXX-XXXXX-ABCDE")]
        [InlineData("ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", "XXXXX-XXXXX-XXXXX-XXXXX-UVWXY")]
        [InlineData("UVWXY12345", "XXXXX-XXXXX-XXXXX-XXXXX-12345")]
        [InlineData(null, "")]
        [InlineData("", "")]
        public void MaskProductKey_MasksAllButTail(string? key, string expected)
        {
            Assert.Equal(expected, WindowsImageEditionService.MaskProductKey(key));
        }

        [Theory]
        [InlineData("Professional", "Professional", true)]
        [InlineData("professional", "Professional", true)]
        [InlineData("PROFESSIONAL", "Professional", true)]
        [InlineData("Professional", "Enterprise", false)]
        [InlineData("", "Professional", false)]
        [InlineData("Professional", "", false)]
        [InlineData("Professional", null, false)]
        public void EditionsMatch_ComparesCaseInsensitively(string current, string? requested, bool expected)
        {
            Assert.Equal(expected, WindowsImageEditionService.EditionsMatch(current, requested));
        }

        [Fact]
        public void IsEditionSupported_NullTargetListIsSupported()
        {
            Assert.True(WindowsImageEditionService.IsEditionSupported("Professional", null));
        }

        [Fact]
        public void IsEditionSupported_MatchingTargetIsSupported()
        {
            var targets = new List<string> { "Home", "Professional", "Enterprise" };
            Assert.True(WindowsImageEditionService.IsEditionSupported("professional", targets));
        }

        [Fact]
        public void IsEditionSupported_MissingTargetIsNotSupported()
        {
            var targets = new List<string> { "Home", "Professional" };
            Assert.False(WindowsImageEditionService.IsEditionSupported("Education", targets));
        }

        [Theory]
        [InlineData("Professional", null, false, "DismSetEdition('Professional')")]
        [InlineData("Professional", "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", false,
            "DismSetEditionAndProductKey('Professional', productKeyMasked='XXXXX-XXXXX-XXXXX-XXXXX-UVWXY')")]
        [InlineData(null, null, true, "DismSetEdition('ServerEdition')")]
        public void DescribeSetEditionCall_DescribesTheDismInvocation(string? edition, string? productKey, bool serverEdition, string expected)
        {
            var editionId = WindowsImageEditionService.ResolveEditionId(edition, serverEdition);
            Assert.Equal(expected, WindowsImageEditionService.DescribeSetEditionCall(editionId, productKey, serverEdition));
        }

        [Theory]
        [InlineData(" Professional ", "Professional")]
        [InlineData("Professional", "Professional")]
        public void NormalizeEditionName_TrimsAndKeepsName(string edition, string expected)
        {
            Assert.Equal(expected, WindowsImageEditionService.NormalizeEditionName(edition));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Professional\\Home")]
        [InlineData("Professional/Home")]
        public void NormalizeEditionName_RejectsBlankOrPathLike(string? edition)
        {
            Assert.Throws<ArgumentException>(() => WindowsImageEditionService.NormalizeEditionName(edition));
        }

        [Theory]
        [InlineData("Professional", false, "Professional")]
        [InlineData(" Professional ", false, "Professional")]
        [InlineData(null, true, WindowsImageEditionService.ServerEditionId)]
        public void ResolveEditionId_SelectsDismId(string? edition, bool serverEdition, string expected)
        {
            Assert.Equal(expected, WindowsImageEditionService.ResolveEditionId(edition, serverEdition));
        }

        [Fact]
        public void BuildResult_MapsSuccessfulChange()
        {
            var completed = DateTime.UtcNow;
            var duration = TimeSpan.FromSeconds(42);

            var result = WindowsImageEditionService.BuildResult(
                new DirectoryInfo(@"C:\Mount"), "Professional", false,
                "ABCDE-FGHIJ-KLMNO-PQRST-UVWXY", "Home", "Professional",
                applied: true, declined: false, isSuccessful: true, errorMessage: null,
                new List<string> { "Professional", "Enterprise" }, completed, duration);

            Assert.Equal(@"C:\Mount", result.ImagePath?.FullName);
            Assert.Equal("Home", result.CurrentEdition);
            Assert.Equal("Professional", result.RequestedEdition);
            Assert.Equal("Professional", result.AfterEdition);
            Assert.False(result.IsServerEdition);
            Assert.True(result.ProductKeyProvided);
            Assert.Equal("XXXXX-XXXXX-XXXXX-XXXXX-UVWXY", result.ProductKeyMasked);
            Assert.True(result.Applied);
            Assert.False(result.Declined);
            Assert.True(result.IsSuccessful);
            Assert.Null(result.ErrorMessage);
            Assert.Contains("Professional", result.AvailableTargetEditions);
            Assert.Equal(completed, result.CompletedAt);
            Assert.Equal(duration, result.Duration);
            Assert.True(result.EditionChanged);
            Assert.Equal("changed", result.Status);
        }

        [Fact]
        public void BuildResult_MarksAlreadyMatchingAsNoChange()
        {
            var result = WindowsImageEditionService.BuildResult(
                new DirectoryInfo(@"C:\Mount"), "Professional", false, null,
                "Professional", "Professional",
                applied: true, declined: false, isSuccessful: true, errorMessage: null,
                null, DateTime.UtcNow, TimeSpan.Zero);

            Assert.False(result.EditionChanged);
            Assert.Equal("unchanged", result.Status);
        }

        [Fact]
        public void BuildResult_MarksDeclinedAsNoChange()
        {
            var result = WindowsImageEditionService.BuildResult(
                new DirectoryInfo(@"C:\Mount"), "Professional", false, null,
                "Home", null, applied: false, declined: true, isSuccessful: false,
                errorMessage: null, null, DateTime.UtcNow, TimeSpan.Zero);

            Assert.True(result.Declined);
            Assert.False(result.EditionChanged);
            Assert.Equal("declined", result.Status);
        }

        [Fact]
        public void BuildResult_MarksFailure()
        {
            var result = WindowsImageEditionService.BuildResult(
                new DirectoryInfo(@"C:\Mount"), "Professional", false, null,
                string.Empty, null, applied: false, declined: false, isSuccessful: false,
                errorMessage: "boom", null, DateTime.UtcNow, TimeSpan.Zero);

            Assert.False(result.IsSuccessful);
            Assert.Equal("boom", result.ErrorMessage);
            Assert.Equal("failed", result.Status);
        }

        [Fact]
        public void BuildResult_ServerEditionPathSetsFlagAndMaskedKey()
        {
            var result = WindowsImageEditionService.BuildResult(
                new DirectoryInfo(@"C:\Mount"), WindowsImageEditionService.ServerEditionId, true, null,
                "ServerStandard", null, applied: true, declined: false, isSuccessful: true,
                errorMessage: null, null, DateTime.UtcNow, TimeSpan.Zero);

            Assert.True(result.IsServerEdition);
            Assert.Equal("ServerEdition", result.RequestedEdition);
            Assert.False(result.ProductKeyProvided);
            Assert.Equal(string.Empty, result.ProductKeyMasked);
        }
    }
}