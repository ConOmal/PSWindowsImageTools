using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Composite health assessment of a mounted Windows image: corruption, missing registry
    /// hives, orphaned/superseded packages, and driver issues
    /// </summary>
    public class WindowsImageHealthCheckService
    {
        private const string ServiceName = "WindowsImageHealthCheckService";
        private readonly ModuleCallbacks _callbacks;

        public WindowsImageHealthCheckService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        public HealthCheckReport Run(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool restoreHealth, PSCmdlet cmdlet)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var report = new HealthCheckReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            CheckCorruption(mountPath, restoreHealth, report);
            CheckRegistryHives(mountPath, report);
            CheckComponentStore(mountedImage, imageService, report);
            CheckDrivers(mountedImage, imageService, report);

            return report;
        }

        private void CheckCorruption(string mountPath, bool restoreHealth, HealthCheckReport report)
        {
            try
            {
                using var session = DismApi.OpenOfflineSession(mountPath);
                var healthState = DismApi.CheckImageHealth(session, scanImage: true);

                if (healthState != DismImageHealthState.Healthy)
                {
                    if (restoreHealth)
                    {
                        DismApi.RestoreImageHealth(session, limitAccess: false);
                        report.Findings.Add(new HealthFinding
                        {
                            Category = "Corruption",
                            Severity = HealthStatus.Warning,
                            Message = $"Component store was {healthState}; repair attempted"
                        });
                    }
                    else
                    {
                        report.Findings.Add(new HealthFinding
                        {
                            Category = "Corruption",
                            Severity = HealthStatus.Unhealthy,
                            Message = $"Component store is {healthState}; run with -RestoreHealth to repair"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check image health: {ex.Message}");
                report.Findings.Add(new HealthFinding { Category = "Corruption", Severity = HealthStatus.Warning, Message = $"Health check failed: {ex.Message}" });
            }
        }

        private void CheckRegistryHives(string mountPath, HealthCheckReport report)
        {
            var configDir = Path.Combine(mountPath, "Windows", "System32", "config");

            foreach (var hive in new[] { "SOFTWARE", "SYSTEM" })
            {
                var hivePath = Path.Combine(configDir, hive);
                if (!File.Exists(hivePath))
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "MissingRegistryHive",
                        Severity = HealthStatus.Warning,
                        Message = $"{hive} hive not found at {hivePath}"
                    });
                }
            }
        }

        private void CheckComponentStore(MountedWindowsImage mountedImage, IWindowsImageService imageService, HealthCheckReport report)
        {
            try
            {
                var componentStoreReport = new ComponentStoreService(_callbacks).Analyze(mountedImage, imageService);

                if (componentStoreReport.SupersededPackages > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "OrphanedOrSupersededPackage",
                        Severity = HealthStatus.Warning,
                        Message = $"{componentStoreReport.SupersededPackages} superseded package(s) present; consider Optimize-WindowsImageComponentStore"
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check component store: {ex.Message}");
            }
        }

        private void CheckDrivers(MountedWindowsImage mountedImage, IWindowsImageService imageService, HealthCheckReport report)
        {
            try
            {
                var drivers = new WindowsImageDriverService(_callbacks).GetDrivers(mountedImage, imageService);

                var unsignedCount = drivers.Count(d => d.DriverSignature == DismDriverSignature.Unsigned);
                if (unsignedCount > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "DriverIssue",
                        Severity = HealthStatus.Warning,
                        Message = $"{unsignedCount} unsigned driver(s) detected"
                    });
                }

                var duplicateCount = drivers
                    .GroupBy(d => (d.OriginalFileName.ToLowerInvariant(), d.ProviderName.ToLowerInvariant()))
                    .Count(g => g.Select(d => d.PublishedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

                if (duplicateCount > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "DriverIssue",
                        Severity = HealthStatus.Warning,
                        Message = $"{duplicateCount} duplicate OEM driver group(s) detected"
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check drivers: {ex.Message}");
            }
        }
    }
}
