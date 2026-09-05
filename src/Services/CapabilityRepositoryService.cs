using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Indexes a Features on Demand (FoD) payload source directory and reports the
    /// capability packages it offers. Strictly read-only — no DISM, no mounted image,
    /// no cab-content parsing: every capability field is derived from the cab file
    /// name per the documented convention
    /// (<c>Microsoft-Windows-&lt;CapabilityName&gt;~&lt;token&gt;~&lt;arch&gt;~&lt;language&gt;~&lt;version&gt;.cab</c>).
    /// Files that do not follow the convention are skipped with a verbose note and
    /// counted — never errors. All decision logic (parsing, filtering, grouping,
    /// sorting) is pure and unit-testable; the only filesystem surface is the
    /// <see cref="IndexRepository"/> enumeration.
    /// </summary>
    public class CapabilityRepositoryService
    {
        private const string ServiceName = "CapabilityRepositoryService";

        /// <summary>
        /// Required prefix of a FoD payload cab file name (before the capability name)
        /// </summary>
        internal const string CabFileNamePrefix = "Microsoft-Windows-";

        /// <summary>
        /// Reported for empty architecture/language segments (language-neutral packages)
        /// </summary>
        public const string NeutralToken = "neutral";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public CapabilityRepositoryService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Indexes a FoD payload source directory for capability packages
        /// </summary>
        /// <param name="sourceDirectory">Directory containing the FoD payload .cab files (top-level only)</param>
        /// <param name="nameFilter">Optional regular expression the capability name must match; null/empty for all</param>
        /// <param name="architectureFilter">Optional regular expression the architecture must match; null/empty for all</param>
        /// <param name="languageFilter">Optional regular expression the language must match; null/empty for all</param>
        /// <param name="cmdlet">Cmdlet for logging</param>
        /// <param name="progress">Optional progress callback (percent, current file name)</param>
        /// <returns>Indexed entries matching the filters, sorted by capability name, language, architecture and version</returns>
        public List<CapabilityRepositoryEntry> IndexRepository(
            DirectoryInfo sourceDirectory,
            string? nameFilter,
            string? architectureFilter,
            string? languageFilter,
            PSCmdlet cmdlet,
            Action<int, string>? progress = null)
        {
            return IndexRepository(sourceDirectory, nameFilter, architectureFilter, languageFilter, ModuleCallbacks.FromCmdlet(cmdlet), progress);
        }

        /// <summary>
        /// Indexes a FoD payload source directory for capability packages using callbacks
        /// </summary>
        /// <param name="sourceDirectory">Directory containing the FoD payload .cab files (top-level only)</param>
        /// <param name="nameFilter">Optional regular expression the capability name must match; null/empty for all</param>
        /// <param name="architectureFilter">Optional regular expression the architecture must match; null/empty for all</param>
        /// <param name="languageFilter">Optional regular expression the language must match; null/empty for all</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <param name="progress">Optional progress callback (percent, current file name)</param>
        /// <returns>Indexed entries matching the filters, sorted by capability name, language, architecture and version</returns>
        public List<CapabilityRepositoryEntry> IndexRepository(
            DirectoryInfo sourceDirectory,
            string? nameFilter,
            string? architectureFilter,
            string? languageFilter,
            ModuleCallbacks callbacks,
            Action<int, string>? progress = null)
        {
            var output = new List<CapabilityRepositoryEntry>();

            if (sourceDirectory == null || !sourceDirectory.Exists)
            {
                _callbacks.Warning?.Invoke($"Source directory does not exist: {(sourceDirectory == null ? string.Empty : sourceDirectory.FullName)}; no capability packages indexed");
                return output;
            }

            _callbacks.Verbose?.Invoke($"Indexing capability repository source {sourceDirectory.FullName} (top-level .cab files only)");

            var cabFiles = sourceDirectory.GetFiles("*.cab", SearchOption.TopDirectoryOnly);
            Array.Sort(cabFiles, (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

            var nonConformingCount = 0;
            var filteredOutCount = 0;

            for (var index = 0; index < cabFiles.Length; index++)
            {
                var cabFile = cabFiles[index];
                var percent = cabFiles.Length == 0 ? 100 : (int)(((double)(index + 1) / cabFiles.Length) * 100);

                try
                {
                    progress?.Invoke(percent, cabFile.Name);

                    var entry = ParseCabFileName(cabFile.FullName);
                    if (entry == null)
                    {
                        nonConformingCount++;
                        _callbacks.Verbose?.Invoke($"Skipping {cabFile.Name}: file name does not follow the FoD convention '{CabFileNamePrefix}<CapabilityName>~<token>~<arch>~<language>~<version>.cab'");
                        continue;
                    }

                    entry.FileSize = cabFile.Length;

                    if (!MatchesFilters(entry, nameFilter, architectureFilter, languageFilter))
                    {
                        filteredOutCount++;
                        continue;
                    }

                    output.Add(entry);
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to index {cabFile.FullName}: {ex.Message}");
                }
            }

            _callbacks.Verbose?.Invoke($"Capability repository scan of {sourceDirectory.FullName} complete: {output.Count} package(s) indexed, {nonConformingCount} file name(s) not following the FoD convention skipped, {filteredOutCount} package(s) excluded by filters ({cabFiles.Length} .cab file(s) scanned)");

            return output;
        }

        /// <summary>
        /// Parses a FoD payload cab file name into a capability repository entry.
        /// Pure; returns null when the file name does not follow the documented
        /// convention (5 ~-separated segments after a Microsoft-Windows- prefix).
        /// </summary>
        /// <param name="filePath">Path of the .cab file to parse</param>
        /// <returns>Parsed entry (FileSize is not set — the caller owns disk access), or null when non-conforming</returns>
        internal static CapabilityRepositoryEntry? ParseCabFileName(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var fileName = Path.GetFileName(filePath);

            if (!fileName.EndsWith(".cab", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var stem = Path.GetFileNameWithoutExtension(fileName);
            var segments = stem.Split('~');

            if (segments.Length != 5)
            {
                return null;
            }

            var capabilityName = ExtractCapabilityName(segments[0]);
            if (capabilityName == null)
            {
                return null;
            }

            return new CapabilityRepositoryEntry
            {
                FileName = fileName,
                FilePath = filePath,
                CapabilityName = capabilityName,
                Token = segments[1].Trim(),
                Architecture = NormalizeNeutral(segments[2]),
                Language = NormalizeNeutral(segments[3]),
                Version = segments[4].Trim()
            };
        }

        /// <summary>
        /// Strips the required Microsoft-Windows- prefix from a cab file name's first
        /// segment. Pure; returns null when the prefix is absent or nothing remains.
        /// </summary>
        /// <param name="firstSegment">First ~-separated segment of the cab file name stem</param>
        /// <returns>The capability name, or null when non-conforming</returns>
        internal static string? ExtractCapabilityName(string? firstSegment)
        {
            if (firstSegment == null)
            {
                return null;
            }

            var trimmed = firstSegment.Trim();

            if (!trimmed.StartsWith(CabFileNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var name = trimmed.Substring(CabFileNamePrefix.Length).Trim();

            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// Applies the optional name/architecture/language regular-expression filters to
        /// an entry. Pure; null/empty filters impose no constraint; comparisons are
        /// case-insensitive and culture-invariant.
        /// </summary>
        /// <param name="entry">Parsed entry to test</param>
        /// <param name="nameFilter">Optional capability-name regex</param>
        /// <param name="architectureFilter">Optional architecture regex</param>
        /// <param name="languageFilter">Optional language regex</param>
        /// <returns>True when the entry satisfies every provided filter</returns>
        internal static bool MatchesFilters(
            CapabilityRepositoryEntry entry,
            string? nameFilter,
            string? architectureFilter,
            string? languageFilter)
        {
            return MatchesFilter(entry.CapabilityName, nameFilter)
                && MatchesFilter(entry.Architecture, architectureFilter)
                && MatchesFilter(entry.Language, languageFilter);
        }

        /// <summary>
        /// Reports whether a regular-expression pattern is usable as a filter. Pure;
        /// null/empty/whitespace patterns are valid (no constraint).
        /// </summary>
        /// <param name="pattern">Pattern to validate</param>
        /// <returns>True when the pattern compiles as a regex (or is empty)</returns>
        internal static bool IsValidRegexPattern(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return true;
            }

            try
            {
                new Regex(pattern);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Collapses per-architecture/per-language entries sharing one capability name
        /// into summary groups. Pure; groups are sorted by capability name
        /// (ordinal-ignore-case) and each member list is distinct and sorted.
        /// </summary>
        /// <param name="entries">Indexed entries to group</param>
        /// <returns>One summary group per distinct capability name</returns>
        internal static List<CapabilityRepositoryGroup> GroupEntries(IEnumerable<CapabilityRepositoryEntry> entries)
        {
            var groups = new List<CapabilityRepositoryGroup>();

            if (entries == null)
            {
                return groups;
            }

            foreach (var group in entries.GroupBy(entry => entry.CapabilityName, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();

                groups.Add(new CapabilityRepositoryGroup
                {
                    CapabilityName = first.CapabilityName,
                    PackageCount = group.Count(),
                    Architectures = DistinctSorted(group.Select(entry => entry.Architecture)),
                    Languages = DistinctSorted(group.Select(entry => entry.Language)),
                    Versions = DistinctSorted(group.Select(entry => entry.Version)),
                    TotalSize = group.Sum(entry => entry.FileSize)
                });
            }

            return groups;
        }

        /// <summary>
        /// Normalizes an empty architecture/language segment to the neutral token. Pure.
        /// </summary>
        private static string NormalizeNeutral(string segment)
        {
            var value = segment?.Trim() ?? string.Empty;

            return value.Length == 0 ? NeutralToken : value;
        }

        /// <summary>
        /// Tests one value against one optional regex filter. Pure.
        /// </summary>
        private static bool MatchesFilter(string value, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            try
            {
                return Regex.IsMatch(value ?? string.Empty, filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Distinct (ordinal-ignore-case) and sorted projection of member values. Pure.
        /// </summary>
        private static List<string> DistinctSorted(IEnumerable<string> values)
        {
            return values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
