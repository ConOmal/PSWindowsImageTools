using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Indexes a Features on Demand (FoD) payload source directory and reports the
    /// capability (Feature on Demand) packages it offers. Capability metadata is
    /// parsed from the .cab file names per the documented convention
    /// (Microsoft-Windows-&lt;CapabilityName&gt;~&lt;token&gt;~&lt;arch&gt;~&lt;language&gt;~&lt;version&gt;.cab) —
    /// filename-derived, not read from inside the cab. Strictly read-only: no DISM,
    /// no mounted image, no ShouldProcess.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsCapabilityRepository")]
    [OutputType(typeof(CapabilityRepositoryEntry[]))]
    [OutputType(typeof(CapabilityRepositoryGroup[]))]
    public class GetWindowsCapabilityRepositoryCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsCapabilityRepository";
        private const string OperationName = "Capability repository index";

        /// <summary>
        /// Directory containing the FoD payload .cab files to index
        /// </summary>
        [Parameter(
            Mandatory = true,
            Position = 0,
            HelpMessage = "Directory containing the FoD payload .cab files to index (e.g., a FoD disk/ISO root or sources\\LanguagesAndOptionalFeatures)")]
        [ValidateNotNullOrEmpty]
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Regular expression the capability name must match
        /// </summary>
        [Parameter(
            Position = 1,
            HelpMessage = "Regular expression the capability name must match (e.g., 'Rsat\\.')")]
        [ValidateNotNullOrEmpty]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Regular expression the architecture must match
        /// </summary>
        [Parameter(
            Position = 2,
            HelpMessage = "Regular expression the architecture must match (e.g., 'amd64'; 'neutral' for language-neutral)")]
        public string Architecture { get; set; } = string.Empty;

        /// <summary>
        /// Regular expression the language must match
        /// </summary>
        [Parameter(
            Position = 3,
            HelpMessage = "Regular expression the language must match (e.g., 'en-us'; 'neutral' for language-neutral)")]
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Collapse multi-architecture/multi-language packages into one summary entry per capability name
        /// </summary>
        [Parameter(HelpMessage = "Collapse multi-architecture/multi-language packages into one summary entry per capability name")]
        public SwitchParameter GroupByName { get; set; }

        /// <summary>
        /// Performs the capability repository indexing operation
        /// </summary>
        protected override void EndProcessing()
        {
            var resolvedSourcePath = GetUnresolvedProviderPathFromPSPath(SourcePath) ?? SourcePath;

            if (!Directory.Exists(resolvedSourcePath))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException($"Source directory not found: {resolvedSourcePath}"),
                    "DirectoryNotFound",
                    ErrorCategory.ObjectNotFound,
                    resolvedSourcePath));
                return;
            }

            if (!CapabilityRepositoryService.IsValidRegexPattern(Name) ||
                !CapabilityRepositoryService.IsValidRegexPattern(Architecture) ||
                !CapabilityRepositoryService.IsValidRegexPattern(Language))
            {
                ThrowTerminatingError(new ErrorRecord(
                    new ArgumentException("One or more filter values (-Name, -Architecture, -Language) are not valid regular expressions."),
                    "InvalidFilterRegex",
                    ErrorCategory.InvalidArgument,
                    SourcePath));
                return;
            }

            var sourceDirectory = new DirectoryInfo(resolvedSourcePath);
            var startTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName, OperationName, resolvedSourcePath);

            try
            {
                var service = new CapabilityRepositoryService(ModuleCallbacks.FromCmdlet(this));
                var progress = ProgressService.CreateProgressCallback(
                    this,
                    OperationName,
                    sourceDirectory.Name,
                    currentIndex: 1,
                    totalCount: 1);

                var entries = service.IndexRepository(sourceDirectory, Name, Architecture, Language, this, progress);

                if (GroupByName.IsPresent)
                {
                    WriteObject(CapabilityRepositoryService.GroupEntries(entries).ToArray());
                }
                else
                {
                    WriteObject(entries.ToArray());
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, OperationName, startTime,
                    $"{entries.Count} package(s) indexed{(GroupByName.IsPresent ? ", grouped by capability name" : string.Empty)}");
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to index capability repository source {resolvedSourcePath}: {ex.Message}", ex);
                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, OperationName, startTime, $"failed: {ex.Message}");
                throw;
            }
        }
    }
}
