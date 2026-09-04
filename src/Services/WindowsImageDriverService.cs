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

            var referencePublishedNames = new HashSet<string>(reference.Select(d => d.PublishedName), StringComparer.OrdinalIgnoreCase);
            var currentPublishedNames = new HashSet<string>(current.Select(d => d.PublishedName), StringComparer.OrdinalIgnoreCase);

            foreach (var driver in current)
            {
                if (!referencePublishedNames.Contains(driver.PublishedName))
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
                if (!currentPublishedNames.Contains(driver.PublishedName))
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

        /// <summary>
        /// Enumerates drivers present in a mounted image
        /// </summary>
        public List<WindowsImageDriverInfo> GetDrivers(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool all = false)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var drivers = imageService.GetDrivers(mountPath, all);

            return drivers.Select(d => new WindowsImageDriverInfo
            {
                PublishedName = d.PublishedName ?? string.Empty,
                OriginalFileName = d.OriginalFileName ?? string.Empty,
                ProviderName = d.ProviderName ?? string.Empty,
                ClassName = d.ClassName ?? string.Empty,
                ClassDescription = d.ClassDescription ?? string.Empty,
                Date = d.Date,
                Version = d.Version?.ToString() ?? string.Empty,
                BootCritical = d.BootCritical,
                InBox = d.InBox,
                DriverSignature = d.DriverSignature,
                ImageName = mountedImage.ImageName,
                MountPath = mountPath,
                CatalogFile = d.CatalogFile
            }).ToList();
        }

        /// <summary>
        /// Checks whether the candidate driver beats ANY matching reference entry, not necessarily the highest-versioned one in the group.
        /// A reference group containing multiple versions of the same driver can produce a Superseded result even when a still-higher reference version was also removed.
        /// </summary>
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
