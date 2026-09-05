using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Discovers available Windows media Dynamic Updates (Servicing Stack, SafeOS,
    /// Cumulative, Setup) for a Windows build in the Microsoft Update Catalog.
    /// Reuses WindowsUpdateCatalogService for all catalog HTTP work; every decision
    /// (query construction, classification, latest-per-type selection) is pure
    /// static logic. Output of Get-WindowsDynamicUpdate
    /// </summary>
    public class DynamicUpdateDiscoveryService
    {
        private const string ServiceName = "DynamicUpdateDiscoveryService";
        private const string ProgressActivity = "Discovering Dynamic Updates";

        /// <summary>
        /// Result cap passed to each underlying catalog search (matches the
        /// Search-WindowsUpdateCatalog default; page 1 holds 25 rows)
        /// </summary>
        public const int MaxResultsPerQuery = 50;

        private static readonly DynamicUpdateType[] ApplyOrder = new[]
        {
            DynamicUpdateType.ServicingStack,
            DynamicUpdateType.SafeOS,
            DynamicUpdateType.Cumulative,
            DynamicUpdateType.Setup
        };

        /// <summary>
        /// Build number to catalog title label(s), client label first, server label
        /// second for builds shared by client and server SKUs
        /// </summary>
        private static readonly Dictionary<int, string[]> KnownBuildLabels = new Dictionary<int, string[]>
        {
            [10240] = new[] { "Windows 10 Version 1507" },
            [10586] = new[] { "Windows 10 Version 1511" },
            [14393] = new[] { "Windows 10 Version 1607", "Windows Server 2016" },
            [15063] = new[] { "Windows 10 Version 1703" },
            [16299] = new[] { "Windows 10 Version 1709" },
            [17134] = new[] { "Windows 10 Version 1803" },
            [17763] = new[] { "Windows 10 Version 1809", "Windows Server 2019" },
            [18362] = new[] { "Windows 10 Version 1903" },
            [18363] = new[] { "Windows 10 Version 1909" },
            [19041] = new[] { "Windows 10 Version 2004" },
            [19042] = new[] { "Windows 10 Version 20H2" },
            [19043] = new[] { "Windows 10 Version 21H1" },
            [19044] = new[] { "Windows 10 Version 21H2" },
            [19045] = new[] { "Windows 10 Version 22H2" },
            [20348] = new[] { "Windows Server 2022" },
            [22000] = new[] { "Windows 11 Version 21H2" },
            [22621] = new[] { "Windows 11 Version 22H2" },
            [22631] = new[] { "Windows 11 Version 23H2" },
            [26100] = new[] { "Windows 11 Version 24H2", "Windows Server 2025" }
        };

        private readonly ModuleCallbacks _callbacks;
        private readonly WindowsUpdateCatalogService? _injectedCatalogService;

        /// <summary>
        /// Initializes a new instance of the Dynamic Update discovery service
        /// </summary>
        public DynamicUpdateDiscoveryService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Initializes a new instance with an explicit catalog service
        /// (used by tests to stub catalog responses; the injected instance is not owned/disposed here)
        /// </summary>
        internal DynamicUpdateDiscoveryService(ModuleCallbacks callbacks, WindowsUpdateCatalogService catalogService)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
            _injectedCatalogService = catalogService;
        }

        /// <summary>
        /// Discovers the latest available Dynamic Updates for a build. Runs one
        /// catalog search per unique query (label x type, deduplicated), classifies
        /// the merged results and selects the latest entry per requested type in
        /// apply order, then resolves the download URL for each selected update
        /// </summary>
        /// <param name="build">Windows build number (e.g., 26100)</param>
        /// <param name="osLabels">Catalog title labels resolved for the build (client first, server second)</param>
        /// <param name="architecture">Requested architecture (amd64/x64/x86/arm64; normalized internally)</param>
        /// <param name="requestedTypes">Dynamic Update types to return (empty/All for every type)</param>
        /// <param name="debugMode">Enable catalog debug logging</param>
        public List<WindowsDynamicUpdate> Discover(
            int build,
            IList<string> osLabels,
            string architecture,
            ISet<DynamicUpdateType> requestedTypes,
            bool debugMode)
        {
            var normalizedArchitecture = NormalizeArchitecture(architecture);
            var labels = ResolveLabels(osLabels, build);
            var types = ResolveTypes(requestedTypes);
            var queryPlan = BuildQueryPlan(labels, types);

            var catalogService = _injectedCatalogService ?? new WindowsUpdateCatalogService(_callbacks);
            try
            {
                return DiscoverCore(catalogService, build, labels, normalizedArchitecture, types, queryPlan, debugMode);
            }
            finally
            {
                if (_injectedCatalogService == null)
                {
                    catalogService.Dispose();
                }
            }
        }

        private List<WindowsDynamicUpdate> DiscoverCore(
            WindowsUpdateCatalogService catalogService,
            int build,
            List<string> labels,
            string normalizedArchitecture,
            ISet<DynamicUpdateType> types,
            List<(string Query, string Label)> queryPlan,
            bool debugMode)
        {
            var startTime = DateTime.UtcNow;
            _callbacks.Verbose?.Invoke(
                $"Starting Dynamic Update discovery at {LoggingService.FormatTimestamp(DateTime.Now)} - Build {build}, labels: {string.Join(", ", labels)}, types: {string.Join(", ", types)}, architecture: {normalizedArchitecture}");

            var candidates = new List<WindowsUpdateCatalogResult>();
            var labelByUpdateId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < queryPlan.Count; i++)
            {
                var plan = queryPlan[i];
                var percent = (int)((double)i / queryPlan.Count * 100);
                var status = $"[{i + 1} of {queryPlan.Count}] - {plan.Query}";
                _callbacks.Progress?.Invoke(percent, ProgressActivity, status);

                try
                {
                    var criteria = new WindowsUpdateSearchCriteria
                    {
                        Query = plan.Query,
                        Architecture = normalizedArchitecture,
                        MaxResults = MaxResultsPerQuery,
                        Page = 1,
                        SortBy = "LastUpdated",
                        SortDirection = "Descending",
                        IncludeSuperseded = false
                    };

                    var searchResult = catalogService.SearchUpdates(criteria, false, debugMode);
                    if (searchResult == null || !searchResult.Success)
                    {
                        _callbacks.Warning?.Invoke(
                            $"Catalog search failed for '{plan.Query}': {searchResult?.ErrorMessage ?? "no result returned"}");
                        continue;
                    }

                    foreach (var update in searchResult.Updates)
                    {
                        var catalogResult = ToCatalogResult(update);
                        candidates.Add(catalogResult);
                        if (catalogResult.UpdateId.Length > 0)
                        {
                            labelByUpdateId[catalogResult.UpdateId] = plan.Label;
                        }
                    }

                    _callbacks.Verbose?.Invoke(
                        $"[{i + 1} of {queryPlan.Count}] - Found {searchResult.Updates.Count} candidates for '{plan.Query}'");
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Catalog search failed for '{plan.Query}': {ex.Message}");
                }
            }

            _callbacks.Progress?.Invoke(100, ProgressActivity, "Selecting latest updates per type");

            var primaryLabel = labels.Count > 0 ? labels[0] : string.Empty;
            var selected = SelectLatestPerType(candidates, types, build, primaryLabel, normalizedArchitecture);

            for (int i = 0; i < selected.Count; i++)
            {
                var update = selected[i];
                if (labelByUpdateId.TryGetValue(update.UpdateId, out var matchedLabel))
                {
                    update.OSLabel = matchedLabel;
                }

                if (update.UpdateId.Length == 0)
                {
                    continue;
                }

                var urls = catalogService.GetDownloadUrls(update.UpdateId);
                if (urls.Count > 0)
                {
                    update.DownloadUrl = urls[0];
                }
                else
                {
                    _callbacks.Warning?.Invoke(
                        $"No download URL resolved for {update.KBNumber} ({update.UpdateType}); use Get-WindowsUpdateDownloadUrl to retry");
                }
            }

            var endTime = DateTime.UtcNow;
            _callbacks.Verbose?.Invoke(
                $"Completed Dynamic Update discovery at {LoggingService.FormatTimestamp(DateTime.Now)} (Duration: {LoggingService.FormatDuration(endTime - startTime)}) - {selected.Count} updates selected from {candidates.Count} candidates across {queryPlan.Count} queries");

            return selected;
        }

        /// <summary>
        /// Parses a build string ("26100", "26100.1234", "10.0.26100",
        /// "10.0.26100.1234") into the major build number. Two-part strings are
        /// treated as build.revision; the result must be a plausible Windows
        /// build (10240 or greater, the first Windows 10 build), otherwise null
        /// </summary>
        internal static int? ParseBuildNumber(string? build)
        {
            const int MinimumPlausibleBuild = 10240;

            if (string.IsNullOrWhiteSpace(build))
            {
                return null;
            }

            var parts = build!.Trim().Split('.');
            var token = parts.Length switch
            {
                1 => parts[0],
                2 => parts[0],
                _ => parts[2]
            };

            return int.TryParse(token, out var value) && value >= MinimumPlausibleBuild ? value : (int?)null;
        }

        /// <summary>
        /// Normalizes an architecture parameter to the catalog's values
        /// (amd64/x64 → "x64", arm64 → "ARM64", x86 → "x86")
        /// </summary>
        internal static string NormalizeArchitecture(string? architecture)
        {
            var value = (architecture ?? string.Empty).Trim();
            if (value.Equals("amd64", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("x64", StringComparison.OrdinalIgnoreCase))
            {
                return "x64";
            }

            if (value.Equals("arm64", StringComparison.OrdinalIgnoreCase))
            {
                return "ARM64";
            }

            if (value.Equals("x86", StringComparison.OrdinalIgnoreCase))
            {
                return "x86";
            }

            return "x64";
        }

        /// <summary>
        /// Resolves the catalog title labels for a build; unknown builds fall back
        /// to the generic product-family label
        /// </summary>
        internal static List<string> ResolveOSLabels(int build)
        {
            if (KnownBuildLabels.TryGetValue(build, out var labels))
            {
                return labels.ToList();
            }

            return new List<string> { build >= 22000 ? "Windows 11" : "Windows 10" };
        }

        /// <summary>
        /// Builds the catalog full-text query for a Dynamic Update type. SafeOS and
        /// Setup share the "Dynamic Update" query; classification separates them
        /// </summary>
        internal static string BuildCatalogQuery(DynamicUpdateType type, string osLabel)
        {
            var label = (osLabel ?? string.Empty).Trim();
            var fragment = type switch
            {
                DynamicUpdateType.ServicingStack => "Servicing Stack Update",
                DynamicUpdateType.Cumulative => "Cumulative Update",
                _ => "Dynamic Update"
            };

            return label.Length == 0 ? fragment : $"{label} {fragment}";
        }

        /// <summary>
        /// Classifies a catalog result into a Dynamic Update type from its title,
        /// or null when the entry is not a media Dynamic Update (e.g., .NET
        /// Framework cumulative updates, unrelated tool entries)
        /// </summary>
        internal static DynamicUpdateType? ClassifyCatalogResult(WindowsUpdateCatalogResult? result)
        {
            var title = result?.Title ?? string.Empty;
            if (title.Length == 0)
            {
                return null;
            }

            if (title.IndexOf(".net framework", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            if (title.IndexOf("servicing stack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DynamicUpdateType.ServicingStack;
            }

            if (title.IndexOf("safe os", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("safeos", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DynamicUpdateType.SafeOS;
            }

            if (title.IndexOf("dynamic update", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return title.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0
                    ? DynamicUpdateType.Setup
                    : DynamicUpdateType.SafeOS;
            }

            if (title.IndexOf("cumulative", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return DynamicUpdateType.Cumulative;
            }

            return null;
        }

        /// <summary>
        /// Selects, per requested Dynamic Update type, the latest candidate
        /// (LastModified, then Size tie-break) from deduplicated results, mapped
        /// into WindowsDynamicUpdate objects ordered by the apply sequence
        /// </summary>
        internal static List<WindowsDynamicUpdate> SelectLatestPerType(
            IEnumerable<WindowsUpdateCatalogResult> results,
            ISet<DynamicUpdateType> requestedTypes,
            int build,
            string osLabel,
            string architecture)
        {
            var selected = new List<WindowsDynamicUpdate>();
            var requested = ResolveTypes(requestedTypes);

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var latestByType = new Dictionary<DynamicUpdateType, WindowsUpdateCatalogResult>();

            foreach (var result in results)
            {
                if (result == null || string.IsNullOrEmpty(result.UpdateId))
                {
                    continue;
                }

                if (!seenIds.Add(result.UpdateId))
                {
                    continue;
                }

                var type = ClassifyCatalogResult(result);
                if (type == null || !requested.Contains(type.Value))
                {
                    continue;
                }

                if (!latestByType.TryGetValue(type.Value, out var current) || IsNewer(result, current))
                {
                    latestByType[type.Value] = result;
                }
            }

            foreach (var type in ApplyOrder)
            {
                if (latestByType.TryGetValue(type, out var result))
                {
                    selected.Add(ToDynamicUpdate(result, type, build, osLabel, architecture));
                }
            }

            return selected;
        }

        /// <summary>
        /// Maps a catalog row to the catalog-result model used by discovery.
        /// The catalog's Version column is preserved via the Metadata field
        /// (WindowsUpdateCatalogResult has no dedicated Version property)
        /// </summary>
        internal static WindowsUpdateCatalogResult ToCatalogResult(WindowsUpdate update)
        {
            return new WindowsUpdateCatalogResult
            {
                UpdateId = update.UpdateId ?? string.Empty,
                KBNumber = update.KBNumber ?? string.Empty,
                Title = update.Title ?? string.Empty,
                Description = string.Empty,
                Products = update.ProductsList != null
                    ? update.ProductsList.ToArray()
                    : Array.Empty<string>(),
                Classification = update.Classification ?? string.Empty,
                LastModified = update.LastUpdated,
                Size = update.SizeInBytes,
                DownloadUrls = Array.Empty<Uri>(),
                Architecture = update.Architecture ?? string.Empty,
                Languages = Array.Empty<string>(),
                HasDownloadUrls = false,
                Metadata = update.Version ?? string.Empty
            };
        }

        private static bool IsNewer(WindowsUpdateCatalogResult candidate, WindowsUpdateCatalogResult current)
        {
            if (candidate.LastModified != current.LastModified)
            {
                return candidate.LastModified > current.LastModified;
            }

            return candidate.Size > current.Size;
        }

        private static WindowsDynamicUpdate ToDynamicUpdate(
            WindowsUpdateCatalogResult result,
            DynamicUpdateType type,
            int build,
            string osLabel,
            string architecture)
        {
            return new WindowsDynamicUpdate
            {
                UpdateType = type,
                Build = build,
                OSLabel = osLabel ?? string.Empty,
                KBNumber = result.KBNumber ?? string.Empty,
                Title = result.Title ?? string.Empty,
                UpdateId = result.UpdateId ?? string.Empty,
                Architecture = string.IsNullOrEmpty(result.Architecture) ? architecture ?? string.Empty : result.Architecture,
                Version = result.Metadata ?? string.Empty,
                Classification = result.Classification ?? string.Empty,
                LastModified = result.LastModified,
                Size = result.Size
            };
        }

        private static List<string> ResolveLabels(IList<string>? osLabels, int build)
        {
            var labels = osLabels == null
                ? new List<string>()
                : osLabels
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => l!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (labels.Count == 0)
            {
                labels = ResolveOSLabels(build);
            }

            return labels;
        }

        private static HashSet<DynamicUpdateType> ResolveTypes(ISet<DynamicUpdateType>? requestedTypes)
        {
            if (requestedTypes == null || requestedTypes.Count == 0)
            {
                return new HashSet<DynamicUpdateType>(ApplyOrder);
            }

            var types = new HashSet<DynamicUpdateType>();
            foreach (var type in ApplyOrder)
            {
                if (requestedTypes.Contains(type))
                {
                    types.Add(type);
                }
            }

            return types;
        }

        private static List<(string Query, string Label)> BuildQueryPlan(
            List<string> labels,
            ISet<DynamicUpdateType> types)
        {
            var plan = new List<(string Query, string Label)>();
            var seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var label in labels)
            {
                foreach (var type in ApplyOrder)
                {
                    if (!types.Contains(type))
                    {
                        continue;
                    }

                    var query = BuildCatalogQuery(type, label);
                    if (seenQueries.Add(query))
                    {
                        plan.Add((query, label));
                    }
                }
            }

            return plan;
        }
    }
}
