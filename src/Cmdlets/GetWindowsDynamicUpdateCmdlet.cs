using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Discovers the available Windows media Dynamic Updates (Servicing Stack,
    /// SafeOS, Cumulative, Setup) for a Windows build in the Microsoft Update
    /// Catalog. Read-only; pairs with Get-WindowsUpdateDownloadUrl /
    /// Save-WindowsUpdateCatalogResult for download and Invoke-MediaDynamicUpdate
    /// for apply
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsDynamicUpdate")]
    [OutputType(typeof(WindowsDynamicUpdate[]))]
    public class GetWindowsDynamicUpdateCmdlet : PSCmdlet
    {
        private const string ComponentName = "DynamicUpdateDiscovery";
        private const string ProgressActivity = "Discovering Dynamic Updates";

        /// <summary>
        /// Windows build to discover Dynamic Updates for (e.g., "26100", "26100.1234", "10.0.26100.1234")
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            ValueFromPipelineByPropertyName = true,
            HelpMessage = "Windows build number (e.g., 26100, 26100.1234 or 10.0.26100.1234)")]
        [ValidateNotNullOrEmpty]
        public string Build { get; set; } = null!;

        /// <summary>
        /// Target architecture (default: amd64)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("amd64", "x64", "x86", "arm64")]
        public string Architecture { get; set; } = "amd64";

        /// <summary>
        /// Dynamic Update type filter (default: All)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("ServicingStack", "Cumulative", "SafeOS", "Setup", "All")]
        public string Type { get; set; } = "All";

        /// <summary>
        /// Explicit catalog title label override (e.g., "Windows Server 2025");
        /// by default the label(s) are resolved from the build number
        /// </summary>
        [Parameter(Mandatory = false)]
        public string? OSLabel { get; set; }

        /// <summary>
        /// Enable debug mode with detailed catalog HTTP logging
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter DebugMode { get; set; }

        /// <summary>
        /// Discovers the Dynamic Updates
        /// </summary>
        protected override void ProcessRecord()
        {
            var build = DynamicUpdateDiscoveryService.ParseBuildNumber(Build);
            if (build == null)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException(
                        $"Invalid build '{Build}'. Provide a build number such as 26100, 26100.1234 or 10.0.26100.1234"),
                    "GetWindowsDynamicUpdateInvalidBuild",
                    ErrorCategory.InvalidArgument,
                    Build));
                return; // ThrowTerminatingError always throws; return satisfies nullable flow analysis
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Get Windows Dynamic Updates", $"Build {build.Value}, type {Type}, architecture {Architecture}");

            try
            {
                var labels = string.IsNullOrWhiteSpace(OSLabel)
                    ? DynamicUpdateDiscoveryService.ResolveOSLabels(build.Value)
                    : new List<string> { OSLabel!.Trim() };

                var requestedTypes = ResolveRequestedTypes();

                var callbacks = new ModuleCallbacks
                {
                    Verbose = message => LoggingService.WriteVerbose(this, ComponentName, message),
                    Warning = message => LoggingService.WriteWarning(this, ComponentName, message),
                    Error = (exception, message) => LoggingService.WriteError(this, ComponentName, message, exception),
                    Progress = (percent, activity, status) => LoggingService.WriteProgress(this, activity, status, percent)
                };

                var discoveryService = new DynamicUpdateDiscoveryService(callbacks);
                var results = discoveryService.Discover(
                    build.Value,
                    labels,
                    Architecture,
                    requestedTypes,
                    DebugMode.IsPresent);

                if (results.Count == 0)
                {
                    WriteWarning(
                        $"No Dynamic Updates found for build {build.Value} ({string.Join(", ", labels)}) with architecture {DynamicUpdateDiscoveryService.NormalizeArchitecture(Architecture)}");
                }

                foreach (var result in results)
                {
                    WriteObject(result);
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Get Windows Dynamic Updates",
                    operationStartTime, $"Discovered {results.Count} Dynamic Updates");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Dynamic Update discovery failed: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(
                    ex,
                    "GetWindowsDynamicUpdateFailed",
                    ErrorCategory.NotSpecified,
                    Build));
            }
            finally
            {
                LoggingService.CompleteProgress(this, ProgressActivity);
            }
        }

        private HashSet<DynamicUpdateType> ResolveRequestedTypes()
        {
            var types = new HashSet<DynamicUpdateType>();

            if (Type.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                types.Add(DynamicUpdateType.ServicingStack);
                types.Add(DynamicUpdateType.SafeOS);
                types.Add(DynamicUpdateType.Cumulative);
                types.Add(DynamicUpdateType.Setup);
                return types;
            }

            if (Enum.TryParse(Type, true, out DynamicUpdateType parsed))
            {
                types.Add(parsed);
                return types;
            }

            throw new ArgumentException($"Unknown Dynamic Update type '{Type}'");
        }
    }
}
