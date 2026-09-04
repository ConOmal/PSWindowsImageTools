using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Utility service for common formatting and conversion operations used throughout the module
    /// </summary>
    public static class FormatUtilityService
    {
        /// <summary>
        /// Lazy-loaded date time format properties for efficient parsing
        /// </summary>
        private static readonly Lazy<DateTimeFormatProperties> _dateTimeProperties =
            new Lazy<DateTimeFormatProperties>(() => new DateTimeFormatProperties());

        /// <summary>
        /// Container for date time formatting properties and patterns
        /// </summary>
        private class DateTimeFormatProperties
        {
            public List<string> FormatList { get; }
            public List<DateTimeStyles> Styles { get; }

            public DateTimeFormatProperties()
            {
                FormatList = new List<string>();

                // Add all patterns from current culture
                FormatList.AddRange(DateTimeFormatInfo.CurrentInfo.GetAllDateTimePatterns());

                // Add all patterns from invariant culture
                FormatList.AddRange(DateTimeFormatInfo.InvariantInfo.GetAllDateTimePatterns());

                // Add custom patterns commonly used by Microsoft
                FormatList.Add("yyyyMM");
                FormatList.Add("yyyyMMdd");
                FormatList.Add("yyyy-MM-dd");
                FormatList.Add("MM/dd/yyyy");
                FormatList.Add("dd/MM/yyyy");

                // Configure parsing styles
                Styles = new List<DateTimeStyles>
                {
                    DateTimeStyles.AssumeUniversal,
                    DateTimeStyles.AllowWhiteSpaces,
                    DateTimeStyles.None
                };
            }
        }

        /// <summary>
        /// Attempts to parse a date string using globalization-based date formats
        /// </summary>
        /// <param name="dateString">The date string to parse</param>
        /// <param name="result">The parsed DateTime if successful</param>
        /// <returns>True if parsing was successful, false otherwise</returns>
        public static bool TryParseDate(string dateString, out DateTime result)
        {
            result = default;

            if (string.IsNullOrWhiteSpace(dateString))
                return false;

            // Clean the date string
            var cleanedDate = CleanDateString(dateString);
            var formatProperties = _dateTimeProperties.Value;

            // Try standard DateTime.Parse first with different styles
            foreach (var style in formatProperties.Styles)
            {
                if (DateTime.TryParse(cleanedDate, CultureInfo.InvariantCulture, style, out result))
                    return true;

                if (DateTime.TryParse(cleanedDate, CultureInfo.CurrentCulture, style, out result))
                    return true;
            }

            // Try each format from globalization with different styles
            foreach (var format in formatProperties.FormatList)
            {
                foreach (var style in formatProperties.Styles)
                {
                    if (DateTime.TryParseExact(cleanedDate, format, CultureInfo.InvariantCulture, style, out result))
                        return true;

                    if (DateTime.TryParseExact(cleanedDate, format, CultureInfo.CurrentCulture, style, out result))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses a date string using globalization-based date formats (PowerShell-friendly version)
        /// </summary>
        /// <param name="dateString">The date string to parse</param>
        /// <returns>Parsed DateTime if successful, null otherwise</returns>
        public static DateTime? ParseDate(string dateString)
        {
            return TryParseDate(dateString, out var result) ? result : (DateTime?)null;
        }

        /// <summary>
        /// Cleans a date string by removing common formatting issues
        /// </summary>
        private static string CleanDateString(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
                return string.Empty;

            // Remove extra whitespace
            var cleaned = Regex.Replace(dateString.Trim(), @"\s+", " ");

            // Remove common prefixes/suffixes
            cleaned = Regex.Replace(cleaned, @"^(released|available|updated|on|date):\s*", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*(released|available|updated)$", "", RegexOptions.IgnoreCase);

            // Handle ordinal numbers (1st, 2nd, 3rd, 4th, etc.)
            cleaned = Regex.Replace(cleaned, @"(\d+)(st|nd|rd|th)", "$1", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }

        /// <summary>
        /// Attempts to parse a version string into a Version object
        /// </summary>
        /// <param name="versionString">The version string to parse</param>
        /// <param name="result">The parsed Version if successful</param>
        /// <returns>True if parsing was successful, false otherwise</returns>
        public static bool TryParseVersion(string versionString, out Version result)
        {
            result = new Version();

            if (string.IsNullOrWhiteSpace(versionString))
                return false;

            // Clean the version string
            var cleanedVersion = CleanVersionString(versionString);

            // Try direct parsing first
            if (Version.TryParse(cleanedVersion, out result))
            {
                // Ensure Windows versions have proper 10.0.x.x format
                result = NormalizeWindowsVersion(result);
                return true;
            }

            // Try extracting 4-part version pattern
            var versionMatch = Regex.Match(cleanedVersion, @"(\d+)\.(\d+)\.(\d+)\.(\d+)");
            if (versionMatch.Success)
            {
                if (int.TryParse(versionMatch.Groups[1].Value, out var major) &&
                    int.TryParse(versionMatch.Groups[2].Value, out var minor) &&
                    int.TryParse(versionMatch.Groups[3].Value, out var build) &&
                    int.TryParse(versionMatch.Groups[4].Value, out var revision))
                {
                    result = new Version(major, minor, build, revision);
                    result = NormalizeWindowsVersion(result);
                    return true;
                }
            }

            // Try 3-part version pattern
            versionMatch = Regex.Match(cleanedVersion, @"(\d+)\.(\d+)\.(\d+)");
            if (versionMatch.Success)
            {
                if (int.TryParse(versionMatch.Groups[1].Value, out var major) &&
                    int.TryParse(versionMatch.Groups[2].Value, out var minor) &&
                    int.TryParse(versionMatch.Groups[3].Value, out var build))
                {
                    result = new Version(major, minor, build, 0);
                    result = NormalizeWindowsVersion(result);
                    return true;
                }
            }

            // Try 2-part version pattern (build.revision only)
            versionMatch = Regex.Match(cleanedVersion, @"^(\d+)\.(\d+)$");
            if (versionMatch.Success)
            {
                if (int.TryParse(versionMatch.Groups[1].Value, out var build) &&
                    int.TryParse(versionMatch.Groups[2].Value, out var revision))
                {
                    // Assume Windows 10/11 format: 10.0.build.revision
                    result = new Version(10, 0, build, revision);
                    return true;
                }
            }

            // Try single number (build number only)
            if (int.TryParse(cleanedVersion, out var buildOnly) && buildOnly > 1000)
            {
                // Assume Windows 10/11 format: 10.0.build.0
                result = new Version(10, 0, buildOnly, 0);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Normalizes a version to a Windows-consistent format where possible.
        /// Current Windows releases report kernel version 10.0 (e.g., 10.0.build.revision) and are
        /// passed through unchanged. Future kernel versions with a major greater than 10 (e.g.,
        /// 11.0.26100.1) are treated as authoritative: the kernel version (major.minor) is preserved,
        /// including a non-zero minor (e.g., 11.1.x.y), which is kept as part of the kernel version
        /// rather than being rewritten. A future kernel version that has a build but no revision is
        /// padded to a complete 4-part form (e.g., 11.0.26100 becomes 11.0.26100.0) so the value stays
        /// comparable. A future kernel version that omits the build entirely (e.g., 11.0) is left
        /// unchanged, mirroring how a bare 10.0 is handled today.
        /// </summary>
        /// <param name="version">The version to normalize</param>
        /// <returns>Normalized version with proper Windows format</returns>
        private static Version NormalizeWindowsVersion(Version version)
        {
            // Current Windows releases report kernel version 10.0 with a 10.0.build.revision layout
            if (version.Major == 10 && version.Minor == 0)
            {
                // Already in correct format
                return version;
            }
            else if (version.Major >= 10000 && version.Minor >= 0)
            {
                // Likely a build.revision format, convert to 10.0.build.revision
                return new Version(10, 0, version.Major, version.Minor);
            }
            else if (version.Major > 10 && version.Minor >= 0)
            {
                // Future kernel version (e.g., 11.0.x.y). The kernel version (major.minor) is treated
                // as authoritative and preserved as-is, including a non-zero minor. Missing components
                // are padded only when there is a build but no revision, yielding a consistent 4-part form.
                if (version.Build >= 0 && version.Revision == -1)
                {
                    return new Version(version.Major, version.Minor, version.Build, 0);
                }

                return version;
            }
            else if (version.Major > 0 && version.Minor == 0 && version.Build == -1)
            {
                // Single number version, treat as build number
                return new Version(10, 0, version.Major, 0);
            }

            // All other formats (e.g., 10.1.x.y or partial versions) are returned unchanged
            return version;
        }

        /// <summary>
        /// Parses a version string into a Version object (PowerShell-friendly version)
        /// </summary>
        /// <param name="versionString">The version string to parse</param>
        /// <returns>Parsed Version if successful, null otherwise</returns>
        public static Version? ParseVersion(string versionString)
        {
            return TryParseVersion(versionString, out var result) ? result : null;
        }

        /// <summary>
        /// Cleans a version string by removing common formatting issues
        /// </summary>
        private static string CleanVersionString(string versionString)
        {
            if (string.IsNullOrWhiteSpace(versionString))
                return string.Empty;

            // Remove common prefixes
            var cleaned = Regex.Replace(versionString.Trim(), @"^(version|build|v|ver):\s*", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"^(version|build|v|ver)\s+", "", RegexOptions.IgnoreCase);

            // Remove parentheses and brackets
            cleaned = Regex.Replace(cleaned, @"[()[\]]", "");

            // Remove extra whitespace
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        /// <summary>
        /// Extracts KB article numbers from text
        /// </summary>
        /// <param name="text">Text to search for KB articles</param>
        /// <returns>Array of KB article numbers found</returns>
        public static string[] ExtractKBArticles(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            var kbMatches = Regex.Matches(text, @"KB\s*(\d{6,})", RegexOptions.IgnoreCase);
            return kbMatches.Cast<Match>()
                           .Select(m => $"KB{m.Groups[1].Value}")
                           .Distinct()
                           .ToArray();
        }

        /// <summary>
        /// Formats a list of items with intelligent separators
        /// </summary>
        /// <param name="items">Items to format</param>
        /// <param name="separator">Separator to use (default: ", ")</param>
        /// <param name="lastSeparator">Last separator to use (default: " and ")</param>
        /// <returns>Formatted string</returns>
        public static string FormatList(IEnumerable<string> items, string separator = ", ", string lastSeparator = " and ")
        {
            if (items == null)
                return string.Empty;

            var itemArray = items.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            return itemArray.Length switch
            {
                0 => string.Empty,
                1 => itemArray[0],
                2 => $"{itemArray[0]}{lastSeparator}{itemArray[1]}",
                _ => $"{string.Join(separator, itemArray.Take(itemArray.Length - 1))}{lastSeparator}{itemArray.Last()}"
            };
        }

        /// <summary>
        /// Formats a list of items with intelligent separators and optional maximum display count
        /// </summary>
        /// <param name="items">Items to format</param>
        /// <param name="maxItems">Maximum number of items to display (0 = no limit)</param>
        /// <param name="separator">Separator to use (default: ", ")</param>
        /// <param name="lastSeparator">Last separator to use (default: " and ")</param>
        /// <param name="moreText">Text to show when items are truncated (default: "and {0} more")</param>
        /// <returns>Formatted string</returns>
        public static string FormatListWithLimit(IEnumerable<string> items, int maxItems = 0, string separator = ", ",
            string lastSeparator = " and ", string moreText = "and {0} more")
        {
            if (items == null)
                return string.Empty;

            var itemArray = items.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            if (maxItems <= 0 || itemArray.Length <= maxItems)
                return FormatList(itemArray, separator, lastSeparator);

            var displayItems = itemArray.Take(maxItems).ToArray();
            var remainingCount = itemArray.Length - maxItems;
            var formattedList = FormatList(displayItems, separator, separator);

            return $"{formattedList}{separator}{string.Format(moreText, remainingCount)}";
        }

        /// <summary>
        /// Creates a formatted summary of collection statistics
        /// </summary>
        /// <param name="items">Items to analyze</param>
        /// <param name="itemName">Name of the items (singular)</param>
        /// <param name="itemNamePlural">Name of the items (plural, optional - will add 's' if not provided)</param>
        /// <returns>Formatted summary string</returns>
        public static string FormatCollectionSummary<T>(IEnumerable<T> items, string itemName, string? itemNamePlural = null)
        {
            if (items == null)
                return $"No {itemNamePlural ?? itemName + "s"}";

            var itemArray = items.ToArray();
            var count = itemArray.Length;

            if (count == 0)
                return $"No {itemNamePlural ?? itemName + "s"}";

            if (count == 1)
                return $"1 {itemName}";

            return $"{count} {itemNamePlural ?? itemName + "s"}";
        }

        /// <summary>
        /// Normalizes operating system names for consistent comparison
        /// </summary>
        /// <param name="osName">Operating system name to normalize</param>
        /// <returns>Normalized operating system name</returns>
        public static string NormalizeOperatingSystemName(string osName)
        {
            if (string.IsNullOrWhiteSpace(osName))
                return string.Empty;

            var normalized = osName.Trim();

            // Common normalizations
            normalized = Regex.Replace(normalized, @"Microsoft\s+", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ");

            // Standardize Windows versions
            normalized = Regex.Replace(normalized, @"Windows\s+10", "Windows 10", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"Windows\s+11", "Windows 11", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"Windows\s+Server\s+(\d{4})", "Windows Server $1", RegexOptions.IgnoreCase);

            return normalized.Trim();
        }

        /// <summary>
        /// Normalizes release IDs for consistent comparison
        /// </summary>
        /// <param name="releaseId">Release ID to normalize</param>
        /// <returns>Normalized release ID</returns>
        public static string NormalizeReleaseId(string releaseId)
        {
            if (string.IsNullOrWhiteSpace(releaseId))
                return string.Empty;

            var normalized = releaseId.Trim().ToUpperInvariant();

            // Handle common variations
            normalized = Regex.Replace(normalized, @"VERSION\s+", "", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^V", "", RegexOptions.IgnoreCase);

            // Ensure proper format for year-based releases (21H2, 22H2, etc.)
            var yearMatch = Regex.Match(normalized, @"^(\d{2})H([12])$");
            if (yearMatch.Success)
            {
                normalized = $"{yearMatch.Groups[1].Value}H{yearMatch.Groups[2].Value}";
            }

            return normalized;
        }

        /// <summary>
        /// Determines if a string contains case-insensitive text (compatible with .NET Standard 2.0)
        /// </summary>
        /// <param name="source">Source string</param>
        /// <param name="value">Value to search for</param>
        /// <returns>True if the source contains the value (case-insensitive)</returns>
        public static bool ContainsIgnoreCase(string source, string value)
        {
            if (source == null || value == null)
                return false;

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Safely gets a string value from a dictionary with case-insensitive key matching
        /// </summary>
        /// <param name="dictionary">Dictionary to search</param>
        /// <param name="key">Key to find</param>
        /// <param name="defaultValue">Default value if key not found</param>
        /// <returns>Value from dictionary or default value</returns>
        public static string GetValueIgnoreCase(IDictionary<string, string> dictionary, string key, string defaultValue = "")
        {
            if (dictionary == null || string.IsNullOrEmpty(key))
                return defaultValue;

            // Try exact match first
            if (dictionary.TryGetValue(key, out var exactValue))
                return exactValue ?? defaultValue;

            // Try case-insensitive match
            var matchingKey = dictionary.Keys.FirstOrDefault(k => 
                string.Equals(k, key, StringComparison.OrdinalIgnoreCase));

            return matchingKey != null ? dictionary[matchingKey] ?? defaultValue : defaultValue;
        }

        /// <summary>
        /// Formats a TimeSpan for display with intelligent units
        /// </summary>
        /// <param name="timeSpan">TimeSpan to format</param>
        /// <returns>Formatted string</returns>
        public static string FormatDuration(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.TotalDays:F1} days";
            
            if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.TotalHours:F1} hours";
            
            if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.TotalMinutes:F1} minutes";
            
            return $"{timeSpan.TotalSeconds:F1} seconds";
        }

        /// <summary>
        /// Validates and normalizes a KB article number
        /// </summary>
        /// <param name="kbArticle">KB article to validate</param>
        /// <returns>Normalized KB article or empty string if invalid</returns>
        public static string NormalizeKBArticle(string kbArticle)
        {
            if (string.IsNullOrWhiteSpace(kbArticle))
                return string.Empty;

            // Extract KB number
            var match = Regex.Match(kbArticle.Trim(), @"(?:KB\s*)?(\d{6,})", RegexOptions.IgnoreCase);
            
            return match.Success ? $"KB{match.Groups[1].Value}" : string.Empty;
        }
    }
}
