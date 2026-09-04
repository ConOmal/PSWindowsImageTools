using System;
using System.Collections.Generic;
using System.IO;
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

            foreach (var group in FindDuplicateOemGroups(current))
            {
                result.DuplicateOem.AddRange(group);
            }

            return result;
        }

        /// <summary>
        /// Groups drivers by (OriginalFileName, ProviderName) and returns only the groups that have
        /// more than one distinct PublishedName — i.e., duplicate OEM driver packages. Pure; null-safe
        /// on both grouping keys. Shared by Compare (which needs the driver objects) and callers that
        /// only need a count.
        /// </summary>
        public static IEnumerable<IGrouping<(string OriginalFileName, string ProviderName), WindowsImageDriverInfo>> FindDuplicateOemGroups(IEnumerable<WindowsImageDriverInfo> drivers)
        {
            return drivers
                .GroupBy(d => ((d.OriginalFileName ?? string.Empty).ToLowerInvariant(), (d.ProviderName ?? string.Empty).ToLowerInvariant()))
                .Where(g => g.Select(d => d.PublishedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
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
                ClassGuid = d.ClassGuid ?? string.Empty,
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
        /// Resolves the on-disk directory containing a driver's files from its DISM-reported
        /// catalog path, handling both absolute paths and paths relative to the image root. Pure.
        /// </summary>
        internal static string? ResolveDriverSourceDirectory(string mountPath, string? catalogFilePath)
        {
            if (string.IsNullOrEmpty(catalogFilePath))
            {
                return null;
            }

            var fullCatalogPath = Path.IsPathRooted(catalogFilePath)
                ? catalogFilePath
                : Path.Combine(mountPath, catalogFilePath!.TrimStart('\\', '/'));

            return Path.GetDirectoryName(fullCatalogPath);
        }

        /// <summary>
        /// Copies a driver's on-disk file repository folder to a destination directory
        /// </summary>
        public void Export(WindowsImageDriverInfo driver, DirectoryInfo destination)
        {
            var sourceDirectory = ResolveDriverSourceDirectory(driver.MountPath, driver.CatalogFile);

            if (sourceDirectory == null || !Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Could not resolve on-disk source directory for driver {driver.PublishedName} (catalog: {driver.CatalogFile ?? "none"})");
            }

            var driverDestination = Path.Combine(destination.FullName, Path.GetFileName(sourceDirectory));
            Directory.CreateDirectory(driverDestination);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var targetPath = Path.Combine(driverDestination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
            }

            _callbacks.Verbose?.Invoke($"Exported driver {driver.PublishedName} to {driverDestination}");
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
