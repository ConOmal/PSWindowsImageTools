using System;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsISODownloadUrlBuilderTests
    {
        [Theory]
        [InlineData("Windows 11", "x64", "3321")]
        [InlineData("Windows 11", "X64", "3321")]
        [InlineData("Windows 11", "arm64", "3324")]
        [InlineData("Windows 11", "ARM64", "3324")]
        [InlineData("windows 11", "x64", "3321")]
        public void ResolveProductEditionId_ReturnsKnownIds(string edition, string architecture, string expected)
        {
            Assert.Equal(expected, WindowsISODownloadUrlBuilder.ResolveProductEditionId(edition, architecture));
        }

        [Fact]
        public void ResolveProductEditionId_ThrowsForUnsupportedEdition()
        {
            Assert.Throws<ArgumentException>(() => WindowsISODownloadUrlBuilder.ResolveProductEditionId("Windows 10", "x64"));
        }

        [Fact]
        public void ResolveProductEditionId_ThrowsForUnsupportedArchitecture()
        {
            Assert.Throws<ArgumentException>(() => WindowsISODownloadUrlBuilder.ResolveProductEditionId("Windows 11", "x86"));
        }

        [Fact]
        public void BuildSessionRegistrationUrl_ContainsOrgIdAndSessionId()
        {
            var url = WindowsISODownloadUrlBuilder.BuildSessionRegistrationUrl("abc-123");

            Assert.Contains("org_id=y6jn8c31", url);
            Assert.Contains("session_id=abc-123", url);
        }

        [Fact]
        public void BuildBotChallengeScriptUrl_ContainsFixedInstanceId()
        {
            var url = WindowsISODownloadUrlBuilder.BuildBotChallengeScriptUrl("abc-123");

            Assert.Contains("instanceId=560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175", url);
            Assert.Contains("session_id=abc-123", url);
        }

        [Fact]
        public void BuildSkuLookupUrl_ContainsProfileAndProductEditionId()
        {
            var url = WindowsISODownloadUrlBuilder.BuildSkuLookupUrl("3321", "abc-123");

            Assert.Contains("profile=606624d44113", url);
            Assert.Contains("ProductEditionId=3321", url);
            Assert.Contains("sessionID=abc-123", url);
        }

        [Fact]
        public void BuildDownloadLinksUrl_ContainsSkuId()
        {
            var url = WindowsISODownloadUrlBuilder.BuildDownloadLinksUrl("47", "abc-123");

            Assert.Contains("SKU=47", url);
            Assert.Contains("sessionID=abc-123", url);
        }

        [Fact]
        public void ExtractBotChallengeTokens_ValidScript_ReturnsTokenAndTicks()
        {
            var script = "document.write('<img src=\"x?w=1A2B3C\">'); var x = \"...rticks=\"+123456;";

            var (token, ticks) = WindowsISODownloadUrlBuilder.ExtractBotChallengeTokens(script);

            Assert.Equal("1A2B3C", token);
            Assert.Equal("123456", ticks);
        }

        [Fact]
        public void ExtractBotChallengeTokens_MissingToken_Throws()
        {
            var script = "var x = \"...rticks=\"+123456;";

            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.ExtractBotChallengeTokens(script));
        }

        [Fact]
        public void ExtractBotChallengeTokens_MissingTicks_Throws()
        {
            var script = "document.write('<img src=\"x?w=1A2B3C\">');";

            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.ExtractBotChallengeTokens(script));
        }

        [Fact]
        public void SelectSkuId_FindsMatchingLanguage()
        {
            var json = "{\"Skus\":[{\"Id\":\"1\",\"Language\":\"Arabic\"},{\"Id\":\"47\",\"Language\":\"English International\"}]}";

            Assert.Equal("47", WindowsISODownloadUrlBuilder.SelectSkuId(json, "English International"));
        }

        [Fact]
        public void SelectSkuId_IsCaseInsensitiveOnLanguage()
        {
            var json = "{\"Skus\":[{\"Id\":\"47\",\"Language\":\"English International\"}]}";

            Assert.Equal("47", WindowsISODownloadUrlBuilder.SelectSkuId(json, "english international"));
        }

        [Fact]
        public void SelectSkuId_ThrowsWhenLanguageNotFound()
        {
            var json = "{\"Skus\":[{\"Id\":\"1\",\"Language\":\"Arabic\"}]}";

            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.SelectSkuId(json, "English International"));
        }

        [Fact]
        public void SelectSkuId_ThrowsWhenSkusMissing()
        {
            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.SelectSkuId("{}", "English International"));
        }

        [Fact]
        public void SelectDownloadUri_ReturnsFirstUri()
        {
            var json = "{\"ProductDownloadOptions\":[{\"DownloadType\":1,\"Uri\":\"https://example.com/x.iso\"}]}";

            Assert.Equal("https://example.com/x.iso", WindowsISODownloadUrlBuilder.SelectDownloadUri(json));
        }

        [Fact]
        public void SelectDownloadUri_ThrowsWhenEmpty()
        {
            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.SelectDownloadUri("{\"ProductDownloadOptions\":[]}"));
        }

        [Fact]
        public void SelectDownloadUri_ThrowsWhenMissing()
        {
            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadUrlBuilder.SelectDownloadUri("{}"));
        }

        [Fact]
        public void ThrowIfRejected_ThrowsOnSentinelRejection()
        {
            var body = "{\"errors\":[\"Sentinel marked this request as rejected.\"]}";

            var ex = Assert.Throws<WindowsISODiscoveryRejectedException>(() => WindowsISODownloadUrlBuilder.ThrowIfRejected(body));
            Assert.Contains("Save-WindowsISO -Url", ex.Message);
        }

        [Fact]
        public void ThrowIfRejected_ThrowsOnGenericBlock()
        {
            var body = "We are unable to complete your request at this time.";

            Assert.Throws<WindowsISODiscoveryRejectedException>(() => WindowsISODownloadUrlBuilder.ThrowIfRejected(body));
        }

        [Fact]
        public void ThrowIfRejected_DoesNothingForNormalResponse()
        {
            WindowsISODownloadUrlBuilder.ThrowIfRejected("{\"Skus\":[]}");
        }

        [Fact]
        public void ThrowIfRejected_DoesNothingForNullOrEmpty()
        {
            WindowsISODownloadUrlBuilder.ThrowIfRejected(string.Empty);
            WindowsISODownloadUrlBuilder.ThrowIfRejected(null!);
        }
    }
}
