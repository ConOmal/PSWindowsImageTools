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
    }
}
