using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    /// <summary>
    /// Targeted tests for the refactored WindowsUpdateCatalogService surface:
    /// ModuleCallbacks ctor injection (no PSCmdlet), callback routing, and the
    /// offline parsing pipeline driven through a stubbed HttpClient.
    /// </summary>
    public class WindowsUpdateCatalogServiceTests
    {
        private const string SearchResultsHtml = @"<html><body><form>
<input type=""hidden"" name=""__VIEWSTATE"" id=""__VIEWSTATE"" value=""viewstate-abc"" />
<input type=""hidden"" name=""__VIEWSTATEGENERATOR"" id=""__VIEWSTATEGENERATOR"" value=""gen-123"" />
<input type=""hidden"" name=""__EVENTVALIDATION"" id=""__EVENTVALIDATION"" value=""ev-456"" />
<span id=""ctl00_catalogBody_noResultText""></span>
<table id=""ctl00_catalogBody_updateMatches"">
<tr id=""headerRow""><th>Title</th><th>Products</th><th>Classification</th><th>Last Updated</th><th>Version</th><th>Size</th></tr>
<tr id=""a1b2c3d4-0000-0000-0000-000000000001_R0"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=a1b2c3d4-0000-0000-0000-000000000001"">2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5044285)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>9/1/2026</td>
<td>10.0.26100.2000</td>
<td><span style='display:none'>123456789</span>117.7 MB</td>
</tr>
<tr id=""e5f6a7b8-0000-0000-0000-000000000002_R1"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=e5f6a7b8-0000-0000-0000-000000000002"">2026-09 .NET Framework 4.8.1 Cumulative Update for Windows 11 for x64 (KB5044032)</a></td>
<td>Windows 11</td>
<td>Updates</td>
<td>8/28/2026</td>
<td>4.8.1</td>
<td><span style='display:none'>98765432</span>94.2 MB</td>
</tr>
</table>
</form></body></html>";

        private const string NoResultsHtml = @"<html><body><form>
<input type=""hidden"" name=""__VIEWSTATE"" id=""__VIEWSTATE"" value=""viewstate-abc"" />
<span id=""ctl00_catalogBody_noResultText"">No results found for your search.</span>
</form></body></html>";

        private const string DownloadDialogHtml = @"<html><body><script>
var downloadInformation = [{ size: 123456789, url: 'https://download.windowsupdate.com/d/msdownload/update/software/updt/2026/09/foo.cab' }];
</script></body></html>";

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public int RequestCount { get; private set; }

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                return Task.FromResult(_responder(request));
            }
        }

        private static WindowsUpdateCatalogService CreateService(
            StubHttpMessageHandler handler,
            out List<string> verbose,
            out List<string> warnings,
            out List<(Exception Exception, string Message)> errors)
        {
            var verboseList = new List<string>();
            var warningsList = new List<string>();
            var errorsList = new List<(Exception, string)>();

            var callbacks = new ModuleCallbacks
            {
                Verbose = verboseList.Add,
                Warning = warningsList.Add,
                Error = (ex, message) => errorsList.Add((ex, message))
            };

            verbose = verboseList;
            warnings = warningsList;
            errors = errorsList;

            return new WindowsUpdateCatalogService(callbacks, new HttpClient(handler));
        }

        [Fact]
        public void Constructor_Parameterless_Works()
        {
            using var service = new WindowsUpdateCatalogService();
        }

        [Fact]
        public void Constructor_NullCallbacks_DefaultsToSilent()
        {
            using var service = new WindowsUpdateCatalogService(null!);
        }

        [Fact]
        public void SearchUpdates_ParsesResultsFromStubHtml()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SearchResultsHtml)
            });
            var service = CreateService(handler, out var verbose, out _, out _);

            var criteria = new WindowsUpdateSearchCriteria
            {
                Query = "KB5044285",
                SortBy = string.Empty,
                SortDirection = string.Empty
            };

            var result = service.SearchUpdates(criteria);

            Assert.True(result.Success);
            Assert.Equal(2, result.Updates.Count);

            var first = result.Updates[0];
            Assert.Equal("a1b2c3d4-0000-0000-0000-000000000001", first.UpdateId);
            Assert.Contains("KB5044285", first.Title);
            Assert.Equal("KB5044285", first.KBNumber);
            Assert.Equal("Security Updates", first.Classification);
            Assert.Equal("10.0.26100.2000", first.Version);
            Assert.Equal(123456789, first.SizeInBytes);
            Assert.Equal("x64", first.Architecture);
            Assert.Equal(new DateTime(2026, 9, 1), first.LastUpdated);
            Assert.Equal(new[] { "Windows 11", "Windows 11 Version 24H2" }, first.ProductsList);

            var second = result.Updates[1];
            Assert.Equal("e5f6a7b8-0000-0000-0000-000000000002", second.UpdateId);
            Assert.Equal("KB5044032", second.KBNumber);
            Assert.Equal(98765432, second.SizeInBytes);

            // Callback routing: operation start/complete verbose messages must be emitted
            Assert.Contains(verbose, m => m.Contains("Starting Windows Update Catalog Search"));
            Assert.Contains(verbose, m => m.Contains("Completed Windows Update Catalog Search"));
        }

        [Fact]
        public void SearchUpdates_AppliesDefaultSorting_WithExtraRequest()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SearchResultsHtml)
            });
            var service = CreateService(handler, out _, out _, out _);

            // Default criteria: SortBy=LastUpdated, SortDirection=Descending -> ApplySorting runs
            var result = service.SearchUpdates(new WindowsUpdateSearchCriteria { Query = "KB5044285" });

            Assert.True(result.Success);
            Assert.Equal(2, result.Updates.Count);
            Assert.True(handler.RequestCount >= 2, $"Expected GET + sort POST, got {handler.RequestCount} requests");
        }

        [Fact]
        public void SearchUpdates_NoResults_ReturnsEmptySuccess()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(NoResultsHtml)
            });
            var service = CreateService(handler, out _, out _, out _);

            var result = service.SearchUpdates(new WindowsUpdateSearchCriteria
            {
                Query = "nonexistent-update",
                SortBy = string.Empty,
                SortDirection = string.Empty
            });

            Assert.True(result.Success);
            Assert.Empty(result.Updates);
        }

        [Fact]
        public void SearchUpdates_HttpFailure_ReturnsFailureAndInvokesErrorCallback()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var service = CreateService(handler, out _, out _, out var errors);

            var result = service.SearchUpdates(new WindowsUpdateSearchCriteria
            {
                Query = "KB5044285",
                SortBy = string.Empty,
                SortDirection = string.Empty
            });

            Assert.False(result.Success);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
            Assert.NotEmpty(errors);
            Assert.Contains(errors, e => e.Message.Contains("Catalog request failed"));
        }

        [Fact]
        public void GetDownloadUrls_ExtractsUrlsFromStubHtml()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DownloadDialogHtml)
            });
            var service = CreateService(handler, out _, out _, out _);

            var urls = service.GetDownloadUrls("a1b2c3d4-0000-0000-0000-000000000001");

            var url = Assert.Single(urls);
            Assert.Equal("https://download.windowsupdate.com/d/msdownload/update/software/updt/2026/09/foo.cab", url.OriginalString);
        }

        [Fact]
        public void GetDownloadUrls_RequestFailure_ReturnsEmptyAndInvokesErrorCallback()
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var service = CreateService(handler, out _, out _, out var errors);

            var urls = service.GetDownloadUrls("a1b2c3d4-0000-0000-0000-000000000001");

            Assert.Empty(urls);
            // The no-exception error path wraps the message in an InvalidOperationException
            Assert.Contains(errors, e => e.Message.Contains("Failed to get download URLs"));
        }
    }
}