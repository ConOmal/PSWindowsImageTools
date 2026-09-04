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
    }
}
