using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Analyzes and cleans up the WinSxS component store of a mounted Windows image
    /// </summary>
    public class ComponentStoreService
    {
        private const string ServiceName = "ComponentStoreService";
        private readonly ModuleCallbacks _callbacks;

        public ComponentStoreService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Classifies packages by state into report counters. Pure — no DISM/filesystem access.
        /// </summary>
        internal static void ClassifyPackages(IEnumerable<(string Name, DismPackageFeatureState State)> packages, ComponentStoreReport report)
        {
            foreach (var (name, state) in packages)
            {
                report.TotalPackages++;

                switch (state)
                {
                    case DismPackageFeatureState.Installed:
                        report.InstalledPackages++;
                        break;
                    case DismPackageFeatureState.Superseded:
                        report.SupersededPackages++;
                        report.SupersededPackageNames.Add(name);
                        break;
                    case DismPackageFeatureState.InstallPending:
                    case DismPackageFeatureState.UninstallPending:
                        report.PendingPackages++;
                        break;
                }
            }
        }

        /// <summary>
        /// Recursively sums file sizes under a directory, in MB. Returns 0 if missing. Pure.
        /// </summary>
        internal static double GetDirectorySizeMB(string path)
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long bytes = new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            return Math.Round(bytes / 1024.0 / 1024.0, 2);
        }

        /// <summary>
        /// Analyzes the component store of a mounted image (read-only)
        /// </summary>
        public ComponentStoreReport Analyze(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            _callbacks.Verbose?.Invoke($"Analyzing component store for {mountedImage.ImageName} at {mountPath}");

            var report = new ComponentStoreReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            try
            {
                var packages = imageService.GetPackages(mountPath);
                ClassifyPackages(packages.Select(p => (p.PackageName ?? string.Empty, p.PackageState)), report);
            }
            catch (Exception ex)
            {
                report.Issues.Add($"Failed to enumerate packages: {ex.Message}");
                _callbacks.Warning?.Invoke($"Failed to enumerate packages for {mountedImage.ImageName}: {ex.Message}");
            }

            report.WinSxSSizeMB = GetDirectorySizeMB(Path.Combine(mountPath, "Windows", "WinSxS"));

            _callbacks.Verbose?.Invoke($"Component store analysis complete for {mountedImage.ImageName}: {report}");
            return report;
        }
    }
}
