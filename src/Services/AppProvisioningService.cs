using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Lists and provisions AppX packages in a mounted Windows image
    /// </summary>
    public class AppProvisioningService
    {
        private const string ServiceName = "AppProvisioningService";
        private readonly ModuleCallbacks _callbacks;

        public AppProvisioningService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Lists provisioned AppX packages in a mounted image
        /// </summary>
        public List<ProvisionedAppInfo> GetProvisionedApps(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var packages = imageService.GetProvisionedAppxPackages(mountPath);

            return packages.Select(p => new ProvisionedAppInfo
            {
                PackageName = p.PackageName ?? string.Empty,
                DisplayName = p.DisplayName ?? string.Empty,
                Publisher = p.PublisherId ?? string.Empty,
                Version = p.Version?.ToString() ?? string.Empty,
                InstallLocation = p.InstallLocation ?? string.Empty
            }).ToList();
        }

        /// <summary>
        /// Provisions a new AppX package into a mounted image
        /// </summary>
        public void AddProvisionedApp(MountedWindowsImage mountedImage, IWindowsImageService imageService, FileInfo appPackagePath, List<FileInfo>? dependencyPackages, FileInfo? licensePath)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var dependencyPaths = (dependencyPackages ?? new List<FileInfo>()).Select(f => f.FullName).ToList();

            imageService.AddProvisionedAppxPackage(
                mountedImage.MountPath.FullName,
                appPackagePath.FullName,
                dependencyPaths,
                licensePath?.FullName);
        }

        /// <summary>
        /// Generates a WinGet Configuration (DSC v3) YAML file describing desired package state,
        /// plus a Scheduled Task XML definition that applies it via `winget configure` on first
        /// boot. Pure file templating — no DISM/image access, since WinGet cannot target an
        /// offline mounted image.
        /// </summary>
        public WinGetConfigurationExportResult ExportWinGetConfiguration(List<WinGetConfigurationEntry> packages, DirectoryInfo destination)
        {
            if (!destination.Exists)
            {
                destination.Create();
            }

            var configPath = new FileInfo(Path.Combine(destination.FullName, "winget-configuration.yaml"));
            var taskPath = new FileInfo(Path.Combine(destination.FullName, "Apply-WinGetConfiguration.xml"));

            var yaml = new StringBuilder();
            yaml.AppendLine("# yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2");
            yaml.AppendLine("properties:");
            yaml.AppendLine("  resources:");

            foreach (var package in packages)
            {
                yaml.AppendLine("  - resource: Microsoft.WinGet.DSC/WinGetPackage");
                yaml.AppendLine("    directives:");
                yaml.AppendLine($"      description: Install {package.PackageIdentifier}");
                yaml.AppendLine("      allowPrerelease: true");
                yaml.AppendLine("    settings:");
                yaml.AppendLine($"      id: {package.PackageIdentifier}");
                if (!string.IsNullOrEmpty(package.Version))
                {
                    yaml.AppendLine($"      version: {package.Version}");
                }
                yaml.AppendLine($"      source: {package.Source}");
            }

            yaml.AppendLine("  configurationVersion: 0.2.0");

            File.WriteAllText(configPath.FullName, yaml.ToString());

            var taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Actions Context=""Author"">
    <Exec>
      <Command>winget</Command>
      <Arguments>configure --file ""{configPath.FullName}"" --accept-configuration-agreements</Arguments>
    </Exec>
  </Actions>
</Task>";

            // The declaration says UTF-16, so the bytes must match it (real scheduled-task XML is UTF-16)
            File.WriteAllText(taskPath.FullName, taskXml, Encoding.Unicode);

            _callbacks.Verbose?.Invoke($"WinGet configuration exported: {configPath.FullName} ({packages.Count} package(s))");

            return new WinGetConfigurationExportResult
            {
                ConfigPath = configPath,
                ScheduledTaskPath = taskPath,
                Packages = packages
            };
        }
    }
}
