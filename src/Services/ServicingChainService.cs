using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Classifies installed servicing packages (SSU/LCU/etc.) in a mounted Windows image and
    /// checks whether the SSU/LCU pairing looks version-consistent
    /// </summary>
    public class ServicingChainService
    {
        private const string ServiceName = "ServicingChainService";
        private readonly ModuleCallbacks _callbacks;

        private static readonly HashSet<DismReleaseType> ServicingReleaseTypes = new HashSet<DismReleaseType>
        {
            DismReleaseType.CriticalUpdate,
            DismReleaseType.Hotfix,
            DismReleaseType.SecurityUpdate,
            DismReleaseType.SoftwareUpdate,
            DismReleaseType.Update,
            DismReleaseType.UpdateRollup,
            DismReleaseType.ServicePack
        };

        public ServicingChainService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Classifies a single package by its identity string, state, and release type. Pure —
        /// no DISM/filesystem access. Returns null for packages that are no longer present
        /// (Removed/Superseded/NotPresent) or that aren't an update-like release type at all
        /// (feature packs, language packs, drivers, etc. are out of scope for this report).
        /// </summary>
        internal static ServicingPackageInfo? ClassifyPackage(
            string packageName, DismPackageFeatureState state, DismReleaseType releaseType, DateTime? installTime)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            if (state == DismPackageFeatureState.Removed ||
                state == DismPackageFeatureState.Superseded ||
                state == DismPackageFeatureState.NotPresent)
            {
                return null;
            }

            if (!ServicingReleaseTypes.Contains(releaseType))
            {
                return null;
            }

            ServicingPackageRole role;
            ClassificationConfidence confidence;

            if (packageName.StartsWith("Package_for_ServicingStack", StringComparison.OrdinalIgnoreCase))
            {
                role = ServicingPackageRole.ServicingStackUpdate;
                confidence = ClassificationConfidence.Verified;
            }
            else if (packageName.StartsWith("Package_for_RollupFix", StringComparison.OrdinalIgnoreCase))
            {
                role = ServicingPackageRole.CumulativeUpdate;
                confidence = ClassificationConfidence.Verified;
            }
            else if (packageName.IndexOf("SafeOS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                role = ServicingPackageRole.SafeOSUpdate;
                confidence = ClassificationConfidence.Heuristic;
            }
            else if (packageName.IndexOf("NetFramework", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                role = ServicingPackageRole.DotNetUpdate;
                confidence = ClassificationConfidence.Heuristic;
            }
            else
            {
                role = ServicingPackageRole.Other;
                confidence = ClassificationConfidence.Heuristic;
            }

            var (build, revision) = ParseBuildRevision(packageName);

            return new ServicingPackageInfo
            {
                PackageName = packageName,
                Role = role,
                Confidence = confidence,
                Build = build,
                Revision = revision,
                InstallTime = installTime
            };
        }

        /// <summary>
        /// Extracts the Build and Revision components from a DISM package identity string
        /// (format: Name~PublicKeyToken~Architecture~Language~Build.Revision.Major.Minor).
        /// Pure. Returns (0, 0) for anything that doesn't parse.
        /// </summary>
        internal static (int Build, int Revision) ParseBuildRevision(string packageName)
        {
            var segments = packageName.Split('~');
            if (segments.Length < 5)
            {
                return (0, 0);
            }

            var versionParts = segments[4].Split('.');
            if (versionParts.Length < 2)
            {
                return (0, 0);
            }

            int.TryParse(versionParts[0], out var build);
            int.TryParse(versionParts[1], out var revision);
            return (build, revision);
        }

        /// <summary>
        /// Selects the SSU/LCU from an already-classified package list and checks whether the
        /// SSU's revision is recent enough relative to the LCU's. Pure — operates only on
        /// report.Packages, no DISM/filesystem access.
        /// </summary>
        internal static void ValidateOrdering(ServicingChainReport report, int maxRevisionLag = 200)
        {
            report.ServicingStackUpdate = report.Packages
                .Where(p => p.Role == ServicingPackageRole.ServicingStackUpdate)
                .OrderByDescending(p => p.Revision)
                .FirstOrDefault();

            report.CumulativeUpdate = report.Packages
                .Where(p => p.Role == ServicingPackageRole.CumulativeUpdate)
                .OrderByDescending(p => p.Revision)
                .FirstOrDefault();

            if (report.CumulativeUpdate == null)
            {
                return;
            }

            if (report.ServicingStackUpdate == null)
            {
                report.OrderingValid = false;
                report.Issues.Add(
                    $"Cumulative update {report.CumulativeUpdate.PackageName} is present but no Servicing Stack Update was found");
                return;
            }

            var lag = report.CumulativeUpdate.Revision - report.ServicingStackUpdate.Revision;
            if (lag > maxRevisionLag)
            {
                report.OrderingValid = false;
                report.Issues.Add(
                    $"Servicing Stack Update revision {report.ServicingStackUpdate.Revision} appears stale relative to " +
                    $"Cumulative Update revision {report.CumulativeUpdate.Revision} (lag {lag} > {maxRevisionLag})");
            }
        }

        /// <summary>
        /// Analyzes the servicing chain of a mounted image (read-only)
        /// </summary>
        public ServicingChainReport Analyze(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            _callbacks.Verbose?.Invoke($"Analyzing servicing chain for {mountedImage.ImageName} at {mountPath}");

            var report = new ServicingChainReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            try
            {
                var packages = imageService.GetPackages(mountPath);
                foreach (var package in packages)
                {
                    var classified = ClassifyPackage(
                        package.PackageName ?? string.Empty, package.PackageState, package.ReleaseType, package.InstallTime);

                    if (classified != null)
                    {
                        report.Packages.Add(classified);
                    }
                }
            }
            catch (Exception ex)
            {
                report.Issues.Add($"Failed to enumerate packages: {ex.Message}");
                _callbacks.Warning?.Invoke($"Failed to enumerate packages for {mountedImage.ImageName}: {ex.Message}");
            }

            ValidateOrdering(report);

            _callbacks.Verbose?.Invoke($"Servicing chain analysis complete for {mountedImage.ImageName}: {report}");
            return report;
        }
    }
}