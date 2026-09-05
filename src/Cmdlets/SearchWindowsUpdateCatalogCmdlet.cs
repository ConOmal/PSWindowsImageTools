using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Searches the Microsoft Windows Update Catalog and returns catalog result objects
    /// </summary>
    [Cmdlet(VerbsCommon.Search, "WindowsUpdateCatalog")]
    [OutputType(typeof(WindowsUpdateCatalogResult[]))]
    public class SearchWindowsUpdateCatalogCmdlet : PSCmdlet
    {
        /// <summary>
        /// Search queries (from pipeline)
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "FromPipeline")]
        [ValidateNotNullOrEmpty]
        public string[] InputObject { get; set; } = null!;

        /// <summary>
        /// Search queries (from parameter)
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ParameterSetName = "FromParameter")]
        [ValidateNotNullOrEmpty]
        public string[] Query { get; set; } = null!;

        /// <summary>
        /// Architecture filter
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("x86", "x64", "ARM64")]
        public string? Architecture { get; set; }

        /// <summary>
        /// Maximum results to return per query
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateRange(1, 1000)]
        public int MaxResults { get; set; } = 50;



        /// <summary>
        /// Classification filter (e.g., "Security Updates", "Critical Updates")
        /// </summary>
        [Parameter(Mandatory = false)]
        public string? Classification { get; set; }

        /// <summary>
        /// Product filter (e.g., "Windows 11", "Windows Server 2022")
        /// </summary>
        [Parameter(Mandatory = false)]
        public string? Product { get; set; }

        /// <summary>
        /// Page number for pagination (default: 1)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateRange(1, int.MaxValue)]
        public int Page { get; set; } = 1;

        /// <summary>
        /// Enable debug mode with detailed HTTP logging and global variables
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter DebugMode { get; set; }

        private readonly List<string> _allQueries = new List<string>();
        private const string ComponentName = "WindowsUpdateCatalog";

        /// <summary>
        /// Processes pipeline input
        /// </summary>
        protected override void ProcessRecord()
        {
            try
            {
                // Collect queries from pipeline or parameter
                var queriesToProcess = ParameterSetName == "FromPipeline" ? InputObject : Query;
                _allQueries.AddRange(queriesToProcess);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to process record: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Processes all collected queries
        /// </summary>
        protected override void EndProcessing()
        {
            try
            {
                if (_allQueries.Count == 0)
                {
                    WriteWarning("No search queries provided");
                    return;
                }

                var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                    "Search Windows Update Catalog", $"{_allQueries.Count} queries");

                // Only show query count if multiple queries
                if (_allQueries.Count > 1)
                {
                    LoggingService.WriteVerbose(this, $"Searching catalog with {_allQueries.Count} queries");
                }

                var allResults = new List<WindowsUpdateCatalogResult>();

                // Search for each query
                for (int i = 0; i < _allQueries.Count; i++)
                {
                    var query = _allQueries[i];

                    // Only show progress for multiple queries
                    if (_allQueries.Count > 1)
                    {
                        var progress = (int)((double)(i + 1) / _allQueries.Count * 100);
                        LoggingService.WriteProgress(this, "Searching Windows Update Catalog",
                            $"[{i + 1} of {_allQueries.Count}] - {query}",
                            $"Searching for '{query}' ({progress}%)", progress);
                    }

                    try
                    {
                        var results = SearchSingleQuery(query);
                        allResults.AddRange(results);

                        // Only show individual results for multiple queries
                        if (_allQueries.Count > 1)
                        {
                            LoggingService.WriteVerbose(this, $"[{i + 1} of {_allQueries.Count}] - Found {results.Count} results for '{query}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.WriteError(this, ComponentName, $"Failed to search for '{query}': {ex.Message}", ex);
                        WriteWarning($"Search failed for query '{query}': {ex.Message}");
                    }
                }

                LoggingService.CompleteProgress(this, "Searching Windows Update Catalog");

                // Remove duplicates based on UpdateId
                var uniqueResults = allResults
                    .GroupBy(r => r.UpdateId)
                    .Select(g => g.First())
                    .ToList();

                // Apply filters
                var filteredResults = ApplyFilters(uniqueResults);

                // Limit results
                if (filteredResults.Count > MaxResults)
                {
                    WriteWarning($"Found {filteredResults.Count} results, limiting to {MaxResults}. Use -MaxResults to increase limit.");
                    filteredResults = filteredResults.Take(MaxResults).ToList();
                }

                // Output results
                foreach (var result in filteredResults)
                {
                    WriteObject(result);
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Search Windows Update Catalog", operationStartTime,
                    $"Found {filteredResults.Count} unique results from {_allQueries.Count} queries");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Windows Update Catalog search failed: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Searches for a single query
        /// </summary>
        private List<WindowsUpdateCatalogResult> SearchSingleQuery(string query)
        {
            // Use the existing WindowsUpdateCatalogService but convert to new object model
            using var catalogService = new WindowsUpdateCatalogService(ModuleCallbacks.FromCmdlet(this));

            // Create search criteria from the query
            var criteria = new WindowsUpdateSearchCriteria
            {
                Query = query,
                MaxResults = MaxResults,
                Page = Page,
                Architecture = Architecture,
                Classification = Classification,
                Product = Product
            };

            var searchResult = catalogService.SearchUpdates(criteria, false, DebugMode.IsPresent);

            var newResults = searchResult.Updates.Select(ConvertToNewModel).ToList();

            return newResults;
        }

        /// <summary>
        /// Converts old WindowsUpdate model to new WindowsUpdateCatalogResult model
        /// </summary>
        private WindowsUpdateCatalogResult ConvertToNewModel(WindowsUpdate oldUpdate)
        {
            return new WindowsUpdateCatalogResult
            {
                UpdateId = oldUpdate.UpdateId,
                KBNumber = oldUpdate.KBNumber,
                Title = oldUpdate.Title,
                Description = string.Empty, // WindowsUpdate doesn't have Description
                Products = oldUpdate.ProductsList?.ToArray() ?? Array.Empty<string>(),
                Classification = oldUpdate.Classification,
                LastModified = oldUpdate.LastUpdated,
                Size = oldUpdate.SizeInBytes,
                DownloadUrls = oldUpdate.DownloadUrls?.Where(url => Uri.IsWellFormedUriString(url, UriKind.Absolute))
                    .Select(url => new Uri(url)).ToArray() ?? Array.Empty<Uri>(),
                Architecture = oldUpdate.Architecture,
                Languages = Array.Empty<string>(), // Will be populated later
                HasDownloadUrls = oldUpdate.DownloadUrls?.Any() == true
            };
        }



        /// <summary>
        /// Applies filters to the results
        /// </summary>
        private List<WindowsUpdateCatalogResult> ApplyFilters(List<WindowsUpdateCatalogResult> results)
        {
            var filtered = results.AsEnumerable();

            var filtersApplied = new List<string>();

            if (!string.IsNullOrEmpty(Architecture))
            {
                filtered = filtered.Where(r => r.Architecture.Equals(Architecture, StringComparison.OrdinalIgnoreCase));
                filtersApplied.Add($"Architecture: {Architecture}");
            }

            if (!string.IsNullOrEmpty(Classification))
            {
                filtered = filtered.Where(r => r.Classification.IndexOf(Classification, StringComparison.OrdinalIgnoreCase) >= 0);
                filtersApplied.Add($"Classification: {Classification}");
            }

            if (!string.IsNullOrEmpty(Product))
            {
                filtered = filtered.Where(r => r.Products.Any(p => p.IndexOf(Product, StringComparison.OrdinalIgnoreCase) >= 0));
                filtersApplied.Add($"Product: {Product}");
            }

            // Only log filters if any were applied
            if (filtersApplied.Count > 0)
            {
                LoggingService.WriteVerbose(this, $"Applied filters: {string.Join(", ", filtersApplied)}");
            }

            return filtered.ToList();
        }
    }
}
