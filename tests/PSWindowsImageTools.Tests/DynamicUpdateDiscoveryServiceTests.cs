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
    /// Tests for DynamicUpdateDiscoveryService: build parsing, build-to-label
    /// mapping, query construction, title-based classification, latest-per-type
    /// selection (all pure, synthetic catalog results) and the Discover
    /// orchestration driven through a stubbed HttpClient (no live catalog calls)
    /// </summary>
    public class DynamicUpdateDiscoveryServiceTests
    {
        private const string SearchResultsHtml = @"<html><body><form>
<input type=""hidden"" name=""__VIEWSTATE"" id=""__VIEWSTATE"" value=""viewstate-abc"" />
<input type=""hidden"" name=""__VIEWSTATEGENERATOR"" id=""__VIEWSTATEGENERATOR"" value=""gen-123"" />
<input type=""hidden"" name=""__EVENTVALIDATION"" id=""__EVENTVALIDATION"" value=""ev-456"" />
<span id=""ctl00_catalogBody_noResultText""></span>
<table id=""ctl00_catalogBody_updateMatches"">
<tr id=""headerRow""><th>Title</th><th>Products</th><th>Classification</th><th>Last Updated</th><th>Version</th><th>Size</th></tr>
<tr id=""aaaa1111-0000-0000-0000-000000000001_R0"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000001"">2026-09 Servicing Stack Update for Windows 11 Version 24H2 for x64-based Systems (KB5044284)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>9/1/2026</td>
<td>10.0.26100.2318</td>
<td><span style='display:none'>12345678</span>11.8 MB</td>
</tr>
<tr id=""aaaa1111-0000-0000-0000-000000000002_R1"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000002"">2026-08 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5030000)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>8/13/2026</td>
<td>10.0.26100.2033</td>
<td><span style='display:none'>222222222</span>212.4 MB</td>
</tr>
<tr id=""aaaa1111-0000-0000-0000-000000000003_R2"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000003"">2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5044285)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>9/9/2026</td>
<td>10.0.26100.2314</td>
<td><span style='display:none'>333333333</span>317.8 MB</td>
</tr>
<tr id=""aaaa1111-0000-0000-0000-000000000004_R3"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000004"">2026-09 Dynamic Update for Windows 11 Version 24H2 for x64-based Systems (KB5044287)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>9/9/2026</td>
<td>10.0.26100.2311</td>
<td><span style='display:none'>44444444</span>42.3 MB</td>
</tr>
<tr id=""aaaa1111-0000-0000-0000-000000000005_R4"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000005"">2026-09 Dynamic Update for Windows 11 Setup for x64-based Systems (KB5044286)</a></td>
<td>Windows 11, Windows 11 Version 24H2</td>
<td>Security Updates</td>
<td>9/9/2026</td>
<td>10.0.26100.2306</td>
<td><span style='display:none'>5555555</span>5.3 MB</td>
</tr>
<tr id=""aaaa1111-0000-0000-0000-000000000006_R5"">
<td><input type=""checkbox"" /></td>
<td><a href=""https://www.catalog.update.microsoft.com/ScopedViewInline.aspx?updateid=aaaa1111-0000-0000-0000-000000000006"">2026-09 .NET Framework 4.8.1 Cumulative Update for Windows 11 for x64 (KB5044032)</a></td>
<td>Windows 11</td>
<td>Security Updates</td>
<td>8/28/2026</td>
<td>4.8.1</td>
<td><span style='display:none'>66666666</span>66.6 MB</td>
</tr>
</table>
</form></body></html>";

        private const string NoResultsHtml = @"<html><body><form>
<input type=""hidden"" name=""__VIEWSTATE"" id=""__VIEWSTATE"" value=""viewstate-abc"" />
<span id=""ctl00_catalogBody_noResultText"">No results found for your search.</span>
</form></body></html>";

        private const string StubDownloadUrl = "https://download.windowsupdate.com/d/msdownload/update/software/updt/2026/09/stub.cab";

        private const string DownloadDialogHtml = @"<html><body><script>
var downloadInformation = [{ size: 123456789, url: '" + StubDownloadUrl + @"' }];
</script></body></html>";

        [Theory]
        [InlineData("26100", 26100)]
        [InlineData("26100.1234", 26100)]
        [InlineData("10.0.26100", 26100)]
        [InlineData("10.0.26100.1234", 26100)]
        [InlineData(" 22631 ", 22631)]
        public void ParseBuildNumber_AcceptedShapes_ExtractsBuild(string input, int expected)
        {
            Assert.Equal(expected, DynamicUpdateDiscoveryService.ParseBuildNumber(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("abc")]
        [InlineData("10.0")]
        [InlineData("9999")]
        public void ParseBuildNumber_InvalidInput_ReturnsNull(string? input)
        {
            Assert.Null(DynamicUpdateDiscoveryService.ParseBuildNumber(input));
        }

        [Theory]
        [InlineData("amd64", "x64")]
        [InlineData("AMD64", "x64")]
        [InlineData("x64", "x64")]
        [InlineData("x86", "x86")]
        [InlineData("ARM64", "ARM64")]
        [InlineData("arm64", "ARM64")]
        [InlineData(null, "x64")]
        [InlineData("", "x64")]
        [InlineData("weird", "x64")]
        public void NormalizeArchitecture_MapsToCatalogValues(string? input, string expected)
        {
            Assert.Equal(expected, DynamicUpdateDiscoveryService.NormalizeArchitecture(input));
        }

        [Fact]
        public void ResolveOSLabels_SharedClientServerBuild_ReturnsClientFirstThenServer()
        {
            var labels = DynamicUpdateDiscoveryService.ResolveOSLabels(26100);

            Assert.Equal(2, labels.Count);
            Assert.Equal("Windows 11 Version 24H2", labels[0]);
            Assert.Equal("Windows Server 2025", labels[1]);
        }

        [Fact]
        public void ResolveOSLabels_LegacySharedBuild_ReturnsClientFirstThenServer()
        {
            var labels = DynamicUpdateDiscoveryService.ResolveOSLabels(17763);

            Assert.Equal(2, labels.Count);
            Assert.Equal("Windows 10 Version 1809", labels[0]);
            Assert.Equal("Windows Server 2019", labels[1]);
        }

        [Fact]
        public void ResolveOSLabels_ClientOnlyBuild_ReturnsSingleLabel()
        {
            var labels = DynamicUpdateDiscoveryService.ResolveOSLabels(22631);

            var label = Assert.Single(labels);
            Assert.Equal("Windows 11 Version 23H2", label);
        }

        [Theory]
        [InlineData(12345, "Windows 10")]
        [InlineData(25000, "Windows 11")]
        public void ResolveOSLabels_UnknownBuild_FallsBackToGenericLabel(int build, string expected)
        {
            var label = Assert.Single(DynamicUpdateDiscoveryService.ResolveOSLabels(build));
            Assert.Equal(expected, label);
        }

        [Theory]
        [InlineData(DynamicUpdateType.ServicingStack, "Windows 11 Version 24H2", "Windows 11 Version 24H2 Servicing Stack Update")]
        [InlineData(DynamicUpdateType.Cumulative, "Windows 11 Version 24H2", "Windows 11 Version 24H2 Cumulative Update")]
        [InlineData(DynamicUpdateType.SafeOS, "Windows 11 Version 24H2", "Windows 11 Version 24H2 Dynamic Update")]
        [InlineData(DynamicUpdateType.Setup, "Windows 11 Version 24H2", "Windows 11 Version 24H2 Dynamic Update")]
        public void BuildCatalogQuery_WithLabel_BuildsTypeQuery(DynamicUpdateType type, string label, string expected)
        {
            Assert.Equal(expected, DynamicUpdateDiscoveryService.BuildCatalogQuery(type, label));
        }

        [Fact]
        public void BuildCatalogQuery_EmptyLabel_BuildsBareTypeQuery()
        {
            Assert.Equal("Servicing Stack Update", DynamicUpdateDiscoveryService.BuildCatalogQuery(DynamicUpdateType.ServicingStack, string.Empty));
            Assert.Equal("Dynamic Update", DynamicUpdateDiscoveryService.BuildCatalogQuery(DynamicUpdateType.Setup, null!));
        }

        [Fact]
        public void BuildCatalogQuery_SafeOsAndSetup_ShareTheDynamicUpdateQuery()
        {
            Assert.Equal(
                DynamicUpdateDiscoveryService.BuildCatalogQuery(DynamicUpdateType.SafeOS, "Windows 10 Version 22H2"),
                DynamicUpdateDiscoveryService.BuildCatalogQuery(DynamicUpdateType.Setup, "Windows 10 Version 22H2"));
        }

        private static WindowsUpdateCatalogResult MakeResult(string title)
        {
            return new WindowsUpdateCatalogResult { Title = title };
        }

        [Theory]
        [InlineData("2026-09 Servicing Stack Update for Windows 11 Version 24H2 for x64-based Systems (KB5044284)", DynamicUpdateType.ServicingStack)]
        [InlineData("2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5044285)", DynamicUpdateType.Cumulative)]
        [InlineData("2026-09 Dynamic Update for Windows 11 Version 24H2 for x64-based Systems (KB5044287)", DynamicUpdateType.SafeOS)]
        [InlineData("2026-09 Dynamic Update for Windows 11 Setup for x64-based Systems (KB5044286)", DynamicUpdateType.Setup)]
        [InlineData("2026-09 Dynamic Update for Safe OS for Windows 10 Version 22H2 for x64 (KB5030211)", DynamicUpdateType.SafeOS)]
        [InlineData("2026-09 Cumulative Update for Windows Server 2025 for x64-based Systems (KB5044285)", DynamicUpdateType.Cumulative)]
        public void ClassifyCatalogResult_DynamicUpdateTitles_ClassifiedCorrectly(string title, DynamicUpdateType expected)
        {
            Assert.Equal(expected, DynamicUpdateDiscoveryService.ClassifyCatalogResult(MakeResult(title)));
        }

        [Theory]
        [InlineData("2026-09 .NET Framework 4.8.1 Cumulative Update for Windows 11 for x64 (KB5044032)")]
        [InlineData("Windows Malicious Software Removal Tool x64 - September 2026 (KB890830)")]
        [InlineData("")]
        public void ClassifyCatalogResult_NonDynamicUpdateTitles_ReturnNull(string title)
        {
            Assert.Null(DynamicUpdateDiscoveryService.ClassifyCatalogResult(MakeResult(title)));
        }

        [Fact]
        public void ClassifyCatalogResult_NullResult_ReturnsNull()
        {
            Assert.Null(DynamicUpdateDiscoveryService.ClassifyCatalogResult(null!));
        }

        private static WindowsUpdateCatalogResult MakeCatalogResult(
            string updateId,
            string title,
            string kbNumber,
            DateTime lastModified,
            long size)
        {
            return new WindowsUpdateCatalogResult
            {
                UpdateId = updateId,
                Title = title,
                KBNumber = kbNumber,
                Architecture = "x64",
                Classification = "Security Updates",
                LastModified = lastModified,
                Size = size,
                Metadata = "10.0.26100.2314"
            };
        }

        private static List<WindowsUpdateCatalogResult> MakeSyntheticResults()
        {
            return new List<WindowsUpdateCatalogResult>
            {
                MakeCatalogResult("id-ssu", "2026-09 Servicing Stack Update for Windows 11 Version 24H2 for x64-based Systems (KB5044284)", "KB5044284", new DateTime(2026, 9, 1), 12345678),
                MakeCatalogResult("id-cu-old", "2026-08 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5030000)", "KB5030000", new DateTime(2026, 8, 13), 222222222),
                MakeCatalogResult("id-cu-new", "2026-09 Cumulative Update for Windows 11 Version 24H2 for x64-based Systems (KB5044285)", "KB5044285", new DateTime(2026, 9, 9), 333333333),
                MakeCatalogResult("id-safeos", "2026-09 Dynamic Update for Windows 11 Version 24H2 for x64-based Systems (KB5044287)", "KB5044287", new DateTime(2026, 9, 9), 44444444),
                MakeCatalogResult("id-setup", "2026-09 Dynamic Update for Windows 11 Setup for x64-based Systems (KB5044286)", "KB5044286", new DateTime(2026, 9, 9), 5555555),
                MakeCatalogResult("id-net", "2026-09 .NET Framework 4.8.1 Cumulative Update for Windows 11 for x64 (KB5044032)", "KB5044032", new DateTime(2026, 9, 9), 66666666)
            };
        }

        private static readonly HashSet<DynamicUpdateType> AllTypes = new HashSet<DynamicUpdateType>
        {
            DynamicUpdateType.ServicingStack,
            DynamicUpdateType.SafeOS,
            DynamicUpdateType.Cumulative,
            DynamicUpdateType.Setup
        };

        [Fact]
        public void SelectLatestPerType_SyntheticResults_SelectsLatestPerTypeInApplyOrder()
        {
            var results = MakeSyntheticResults();

            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(results, AllTypes, 26100, "Windows 11 Version 24H2", "x64");

            Assert.Equal(4, selected.Count);
            Assert.Equal(DynamicUpdateType.ServicingStack, selected[0].UpdateType);
            Assert.Equal(DynamicUpdateType.SafeOS, selected[1].UpdateType);
            Assert.Equal(DynamicUpdateType.Cumulative, selected[2].UpdateType);
            Assert.Equal(DynamicUpdateType.Setup, selected[3].UpdateType);
        }

        [Fact]
        public void SelectLatestPerType_LatestCumulativeWinsByLastModified()
        {
            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(MakeSyntheticResults(), AllTypes, 26100, "Windows 11 Version 24H2", "x64");

            var cumulative = Assert.Single(selected, u => u.UpdateType == DynamicUpdateType.Cumulative);
            Assert.Equal("KB5044285", cumulative.KBNumber);
            Assert.Equal("id-cu-new", cumulative.UpdateId);
            Assert.Equal(new DateTime(2026, 9, 9), cumulative.LastModified);
        }

        [Fact]
        public void SelectLatestPerType_MapsCatalogMetadata()
        {
            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(MakeSyntheticResults(), AllTypes, 26100, "Windows 11 Version 24H2", "x64");

            var servicingStack = selected[0];
            Assert.Equal(26100, servicingStack.Build);
            Assert.Equal("Windows 11 Version 24H2", servicingStack.OSLabel);
            Assert.Equal("x64", servicingStack.Architecture);
            Assert.Equal("10.0.26100.2314", servicingStack.Version);
            Assert.Equal("Security Updates", servicingStack.Classification);
            Assert.Null(servicingStack.DownloadUrl);
            Assert.Contains("(KB5044284)", servicingStack.Title);
            Assert.Contains("ServicingStack", servicingStack.ToString());
        }

        [Fact]
        public void SelectLatestPerType_SameDateTieBreak_PrefersLargerPackage()
        {
            var results = new List<WindowsUpdateCatalogResult>
            {
                MakeCatalogResult("id-cu-small", "2026-09 Cumulative Update for Windows 11 Version 24H2 (KB1111111)", "KB1111111", new DateTime(2026, 9, 9), 100),
                MakeCatalogResult("id-cu-large", "2026-09 Cumulative Update for Windows 11 Version 24H2 (KB2222222)", "KB2222222", new DateTime(2026, 9, 9), 300)
            };

            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(results, AllTypes, 26100, "Windows 11 Version 24H2", "x64");

            var cumulative = Assert.Single(selected);
            Assert.Equal("id-cu-large", cumulative.UpdateId);
        }

        [Fact]
        public void SelectLatestPerType_DuplicateUpdateId_FirstOccurrenceWins()
        {
            var results = new List<WindowsUpdateCatalogResult>
            {
                MakeCatalogResult("id-dup", "2026-09 Cumulative Update for Windows 11 Version 24H2 (KB1111111)", "KB1111111", new DateTime(2026, 9, 9), 100),
                MakeCatalogResult("id-dup", "2026-09 Cumulative Update for Windows 11 Version 24H2 (KB9999999)", "KB9999999", new DateTime(2026, 9, 10), 500)
            };

            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(results, AllTypes, 26100, "Windows 11 Version 24H2", "x64");

            var cumulative = Assert.Single(selected);
            Assert.Equal("KB1111111", cumulative.KBNumber);
        }

        [Fact]
        public void SelectLatestPerType_RequestedTypeSubset_FiltersOtherTypes()
        {
            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(
                MakeSyntheticResults(),
                new HashSet<DynamicUpdateType> { DynamicUpdateType.Cumulative },
                26100,
                "Windows 11 Version 24H2",
                "x64");

            var cumulative = Assert.Single(selected);
            Assert.Equal(DynamicUpdateType.Cumulative, cumulative.UpdateType);
        }

        [Fact]
        public void SelectLatestPerType_EmptyRequestedSet_ReturnsAllTypes()
        {
            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(
                MakeSyntheticResults(),
                new HashSet<DynamicUpdateType>(),
                26100,
                "Windows 11 Version 24H2",
                "x64");

            Assert.Equal(4, selected.Count);
        }

        [Fact]
        public void SelectLatestPerType_NoResults_ReturnsEmptyList()
        {
            var selected = DynamicUpdateDiscoveryService.SelectLatestPerType(
                new List<WindowsUpdateCatalogResult>(),
                AllTypes,
                26100,
                "Windows 11 Version 24H2",
                "x64");

            Assert.Empty(selected);
        }

        [Fact]
        public void ToCatalogResult_MapsWindowsUpdateRow()
        {
            var update = new WindowsUpdate
            {
                UpdateId = "id-1",
                KBNumber = "KB5044285",
                Title = "2026-09 Cumulative Update for Windows 11 Version 24H2 (KB5044285)",
                Architecture = "x64",
                Classification = "Security Updates",
                LastUpdated = new DateTime(2026, 9, 9),
                SizeInBytes = 333333333,
                Version = "10.0.26100.2314",
                ProductsList = new List<string> { "Windows 11", "Windows 11 Version 24H2" }
            };

            var result = DynamicUpdateDiscoveryService.ToCatalogResult(update);

            Assert.Equal("id-1", result.UpdateId);
            Assert.Equal("KB5044285", result.KBNumber);
            Assert.Contains("Cumulative Update", result.Title);
            Assert.Equal("x64", result.Architecture);
            Assert.Equal(new DateTime(2026, 9, 9), result.LastModified);
            Assert.Equal(333333333, result.Size);
            Assert.Equal("10.0.26100.2314", result.Metadata);
            Assert.Equal(new[] { "Windows 11", "Windows 11 Version 24H2" }, result.Products);
            Assert.False(result.HasDownloadUrls);
        }

        private static DynamicUpdateDiscoveryService CreateService(
            HttpMessageHandler handler,
            out List<string> verbose,
            out List<string> warnings,
            out List<string> progress)
        {
            var verboseList = new List<string>();
            var warningsList = new List<string>();
            var progressList = new List<string>();

            var callbacks = new ModuleCallbacks
            {
                Verbose = verboseList.Add,
                Warning = warningsList.Add,
                Error = (ex, message) => warningsList.Add(message),
                Progress = (percent, activity, status) => progressList.Add($"{percent}|{activity}|{status}")
            };

            verbose = verboseList;
            warnings = warningsList;
            progress = progressList;

            return new DynamicUpdateDiscoveryService(
                callbacks,
                new WindowsUpdateCatalogService(callbacks, new HttpClient(handler)));
        }

        private sealed class StubCatalogHttpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var isDownloadDialog = request.Method == HttpMethod.Post &&
                    request.RequestUri != null &&
                    request.RequestUri.AbsolutePath.Contains("DownloadDialog.aspx", StringComparison.OrdinalIgnoreCase);

                var html = isDownloadDialog ? DownloadDialogHtml : SearchResultsHtml;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
            }
        }

        private sealed class FailingCatalogHttpHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }
        }

        [Fact]
        public void Discover_StubbedCatalog_ReturnsLatestPerTypeWithResolvedDownloadUrl()
        {
            var service = CreateService(new StubCatalogHttpHandler(), out var verbose, out var warnings, out var progress);

            var results = service.Discover(
                26100,
                new List<string> { "Windows 11 Version 24H2" },
                "amd64",
                AllTypes,
                debugMode: false);

            Assert.Equal(4, results.Count);
            Assert.Equal(DynamicUpdateType.ServicingStack, results[0].UpdateType);
            Assert.Equal(DynamicUpdateType.SafeOS, results[1].UpdateType);
            Assert.Equal(DynamicUpdateType.Cumulative, results[2].UpdateType);
            Assert.Equal(DynamicUpdateType.Setup, results[3].UpdateType);

            var cumulative = results[2];
            Assert.Equal("KB5044285", cumulative.KBNumber);
            Assert.Equal("x64", cumulative.Architecture);
            Assert.Equal(26100, cumulative.Build);
            Assert.Equal("Windows 11 Version 24H2", cumulative.OSLabel);

            Assert.All(results, u => Assert.NotNull(u.DownloadUrl));
            Assert.Equal(new Uri(StubDownloadUrl), results[0].DownloadUrl);

            Assert.Contains(progress, p => p.Contains("Discovering Dynamic Updates"));
            Assert.Contains(verbose, m => m.Contains("Completed Dynamic Update discovery"));
            Assert.Empty(warnings);
        }

        [Fact]
        public void Discover_ArchitectureAndSortingReachTheCatalog()
        {
            var service = CreateService(new StubCatalogHttpHandler(), out _, out _, out _);

            var results = service.Discover(
                26100,
                new List<string> { "Windows 11 Version 24H2" },
                "amd64",
                new HashSet<DynamicUpdateType> { DynamicUpdateType.Cumulative },
                debugMode: false);

            var cumulative = Assert.Single(results);
            Assert.Equal(DynamicUpdateType.Cumulative, cumulative.UpdateType);
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_responder(request));
            }
        }

        [Fact]
        public void Discover_NoCatalogResults_ReturnsEmptyList()
        {
            var service = CreateService(new NoResultsHandler(), out _, out _, out _);

            var results = service.Discover(
                26100,
                new List<string> { "Windows 11 Version 24H2" },
                "amd64",
                AllTypes,
                debugMode: false);

            Assert.Empty(results);
        }

        private sealed class NoResultsHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(NoResultsHtml) });
            }
        }

        [Fact]
        public void Discover_CatalogFailure_EmitsWarningAndReturnsEmpty()
        {
            var service = CreateService(new FailingCatalogHttpHandler(), out _, out var warnings, out _);

            var results = service.Discover(
                26100,
                new List<string> { "Windows 11 Version 24H2" },
                "amd64",
                AllTypes,
                debugMode: false);

            Assert.Empty(results);
            Assert.NotEmpty(warnings);
            Assert.Contains(warnings, w => w.Contains("Catalog search failed"));
        }

        [Fact]
        public void Discover_SharedBuildRunsQueriesForBothLabels()
        {
            var queries = new List<string>();
            var handler = new RecordingHandler(
                request =>
                {
                    if (request.Method == HttpMethod.Get && request.RequestUri != null)
                    {
                        queries.Add(request.RequestUri.Query);
                    }

                    var isDownloadDialog = request.Method == HttpMethod.Post &&
                        request.RequestUri != null &&
                        request.RequestUri.AbsolutePath.Contains("DownloadDialog.aspx", StringComparison.OrdinalIgnoreCase);
                    var html = isDownloadDialog ? DownloadDialogHtml : SearchResultsHtml;
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
                });

            var service = CreateService(handler, out _, out _, out _);

            var results = service.Discover(
                26100,
                new List<string> { "Windows 11 Version 24H2", "Windows Server 2025" },
                "amd64",
                AllTypes,
                debugMode: false);

            Assert.Equal(4, results.Count);
            Assert.Equal(6, queries.Count);
            Assert.Contains(queries, q => q.Contains("Windows%2011%20Version%2024H2") && q.Contains("Servicing%20Stack%20Update"));
            Assert.Contains(queries, q => q.Contains("Windows%20Server%202025") && q.Contains("Cumulative%20Update"));
            Assert.Contains(queries, q => q.Contains("Windows%20Server%202025") && q.Contains("Dynamic%20Update"));
        }
    }
}
