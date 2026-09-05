using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Gets download URLs for Windows Update Catalog results
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsUpdateDownloadUrl")]
    [OutputType(typeof(WindowsUpdateCatalogResult[]))]
    public class GetWindowsUpdateDownloadUrlCmdlet : PSCmdlet
    {
        /// <summary>
        /// Windows Update Catalog results from pipeline
        /// </summary>
        [Parameter(Mandatory = true, ValueFromPipeline = true)]
        [ValidateNotNull]
        public WindowsUpdateCatalogResult[] InputObject { get; set; } = null!;

        /// <summary>
        /// Enable debug mode for detailed logging
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter DebugMode { get; set; }

        private readonly List<WindowsUpdateCatalogResult> _allResults = new List<WindowsUpdateCatalogResult>();
        private const string ComponentName = "GetWindowsUpdateDownloadUrl";

        protected override void ProcessRecord()
        {
            try
            {
                // Collect all results from pipeline
                _allResults.AddRange(InputObject);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, $"Error processing pipeline input: {ex.Message}");
                ThrowTerminatingError(new ErrorRecord(ex, "PSWindowsImageTools.Error", ErrorCategory.InvalidOperation, InputObject));
            }
        }

        protected override void EndProcessing()
        {
            if (_allResults.Count == 0)
            {
                WriteWarning("No Windows Update Catalog results provided");
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                $"Get Download URLs for {_allResults.Count} updates");

            try
            {
                using var catalogService = new WindowsUpdateCatalogService(ModuleCallbacks.FromCmdlet(this));

                LoggingService.WriteVerbose(this, $"Getting download URLs for {_allResults.Count} updates");

                // Get download URLs for all results
                GetDownloadUrls(_allResults, catalogService);

                // Output results with download URLs
                foreach (var result in _allResults)
                {
                    WriteObject(result);
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, 
                    $"Get Download URLs for {_allResults.Count} updates", operationStartTime,
                    $"Successfully retrieved download URLs for {_allResults.Count} updates");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, $"Failed to get download URLs: {ex.Message}");
                ThrowTerminatingError(new ErrorRecord(ex, "PSWindowsImageTools.Error", ErrorCategory.InvalidOperation, null));
            }
        }

        /// <summary>
        /// Gets download URLs for catalog results
        /// </summary>
        private void GetDownloadUrls(List<WindowsUpdateCatalogResult> results, WindowsUpdateCatalogService catalogService)
        {
            var resultsWithoutUrls = results.Where(r => !r.HasDownloadUrls).ToList();
            
            if (resultsWithoutUrls.Count == 0)
            {
                LoggingService.WriteVerbose(this, "All results already have download URLs");
                return;
            }

            LoggingService.WriteVerbose(this, $"Getting download URLs for {resultsWithoutUrls.Count} results");

            foreach (var result in resultsWithoutUrls)
            {
                try
                {
                    LoggingService.WriteVerbose(this, $"Getting download URLs for: {result.Title}");

                    var downloadUrls = catalogService.GetDownloadUrls(result.UpdateId);
                    result.DownloadUrls = downloadUrls.ToArray();
                    result.HasDownloadUrls = downloadUrls.Any();

                    LoggingService.WriteVerbose(this, $"Found {downloadUrls.Count} download URLs for {result.Title}");
                }
                catch (Exception ex)
                {
                    LoggingService.WriteWarning(this, $"Failed to get download URLs for {result.Title}: {ex.Message}");
                    result.DownloadUrls = Array.Empty<Uri>();
                    result.HasDownloadUrls = false;
                }
            }
        }
    }
}
