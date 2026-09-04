using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Enumerates, compares, exports, and removes drivers present inside a mounted (offline)
    /// Windows image
    /// </summary>
    public class WindowsImageDriverService
    {
        private const string ServiceName = "WindowsImageDriverService";
        private readonly ModuleCallbacks _callbacks;

        public WindowsImageDriverService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Compares two driver lists. Pure — operates only on already-captured WindowsImageDriverInfo,
        /// no DISM or filesystem access.
        /// </summary>
        public DriverComparisonResult Compare(List<WindowsImageDriverInfo> reference, List<WindowsImageDriverInfo> current)
        {
            var result = new DriverComparisonResult
            {
                ReferenceName = reference.FirstOrDefault()?.ImageName ?? string.Empty,
                CurrentName = current.FirstOrDefault()?.ImageName ?? string.Empty
            };

            var referenceByPublished = reference.ToDictionary(d => d.PublishedName, StringComparer.OrdinalIgnoreCase);
            var currentByPublished = current.ToDictionary(d => d.PublishedName, StringComparer.OrdinalIgnoreCase);

            foreach (var driver in current)
            {
                if (!referenceByPublished.ContainsKey(driver.PublishedName))
                {
                    result.Added.Add(driver);

                    var sameOriginInReference = reference.Any(r =>
                        string.Equals(r.OriginalFileName, driver.OriginalFileName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.ProviderName, driver.ProviderName, StringComparison.OrdinalIgnoreCase));

                    if (sameOriginInReference && IsHigherVersion(driver, reference))
                    {
                        result.Superseded.Add(driver);
                    }
                }
            }

            foreach (var driver in reference)
            {
                if (!currentByPublished.ContainsKey(driver.PublishedName))
                {
                    result.Removed.Add(driver);
                }
            }

            var duplicateGroups = current
                .GroupBy(d => (d.OriginalFileName.ToLowerInvariant(), d.ProviderName.ToLowerInvariant()))
                .Where(g => g.Select(d => d.PublishedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

            foreach (var group in duplicateGroups)
            {
                result.DuplicateOem.AddRange(group);
            }

            return result;
        }

        private static bool IsHigherVersion(WindowsImageDriverInfo candidate, List<WindowsImageDriverInfo> reference)
        {
            if (!Version.TryParse(candidate.Version, out var candidateVersion))
            {
                return false;
            }

            return reference
                .Where(r => string.Equals(r.OriginalFileName, candidate.OriginalFileName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.ProviderName, candidate.ProviderName, StringComparison.OrdinalIgnoreCase))
                .Any(r => Version.TryParse(r.Version, out var referenceVersion) && candidateVersion > referenceVersion);
        }
    }
}
