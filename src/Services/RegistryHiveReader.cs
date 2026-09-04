using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Registry;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Reads offline registry hive files via RegistryHiveOnDemand.
    /// Hive files are parsed into memory on demand; no hive mounting and no persistent
    /// file handles are involved, so no special cleanup is required after reads.
    /// </summary>
    public class RegistryHiveReader : IRegistryHiveReader
    {
        private const string ServiceName = "RegistryHiveReader";
        private readonly ModuleCallbacks _callbacks;

        public RegistryHiveReader(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Resolves the SOFTWARE hive path inside a mounted Windows image
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <returns>Full path to the SOFTWARE hive file</returns>
        public static string GetSoftwareHivePath(string mountPath)
        {
            return Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
        }

        /// <inheritdoc />
        public Dictionary<string, object> GetWindowsVersionInfo(string softwareHivePath)
        {
            var versionInfo = new Dictionary<string, object>();

            try
            {
                _callbacks.Verbose?.Invoke($"Reading Windows version information from hive: {softwareHivePath}");

                if (!File.Exists(softwareHivePath))
                {
                    _callbacks.Warning?.Invoke($"SOFTWARE hive not found at: {softwareHivePath}");
                    return versionInfo;
                }

                var currentVersionKey = GetKey(OpenHive(softwareHivePath), @"Microsoft\Windows NT\CurrentVersion");
                if (currentVersionKey != null)
                {
                    foreach (var value in currentVersionKey.Values)
                    {
                        versionInfo[value.ValueName] = value.ValueData!;
                    }
                }

                _callbacks.Verbose?.Invoke($"Successfully read {versionInfo.Count} version properties");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to read Windows version information from hive: {ex.Message}");
            }

            return versionInfo;
        }

        /// <inheritdoc />
        public List<Software> GetInstalledSoftware(string softwareHivePath)
        {
            var softwareList = new List<Software>();

            try
            {
                _callbacks.Verbose?.Invoke($"Reading installed software from hive: {softwareHivePath}");

                if (!File.Exists(softwareHivePath))
                {
                    _callbacks.Warning?.Invoke($"SOFTWARE hive not found at: {softwareHivePath}");
                    return softwareList;
                }

                var hive = OpenHive(softwareHivePath);

                // Native 64-bit and WOW64 uninstall keys
                CollectSoftwareEntries(hive, @"Microsoft\Windows\CurrentVersion\Uninstall", softwareList);
                CollectSoftwareEntries(hive, @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", softwareList);

                _callbacks.Verbose?.Invoke($"Found {softwareList.Count} installed software entries");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to read installed software from hive: {ex.Message}");
            }

            return softwareList;
        }

        /// <inheritdoc />
        public Dictionary<string, object> GetWindowsUpdateConfiguration(string softwareHivePath)
        {
            var wuConfigInfo = new Dictionary<string, object>();

            try
            {
                _callbacks.Verbose?.Invoke($"Reading Windows Update configuration from hive: {softwareHivePath}");

                if (!File.Exists(softwareHivePath))
                {
                    _callbacks.Warning?.Invoke($"SOFTWARE hive not found at: {softwareHivePath}");
                    return wuConfigInfo;
                }

                var wuKey = GetKey(OpenHive(softwareHivePath), @"Policies\Microsoft\Windows\WindowsUpdate");
                if (wuKey != null)
                {
                    foreach (var value in wuKey.Values)
                    {
                        wuConfigInfo[value.ValueName] = value.ValueData!;
                    }
                }

                _callbacks.Verbose?.Invoke($"Read {wuConfigInfo.Count} Windows Update configuration properties");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to read Windows Update configuration from hive: {ex.Message}");
            }

            return wuConfigInfo;
        }

        /// <inheritdoc />
        public RegistryHiveOnDemand OpenHive(string hivePath)
        {
            return new RegistryHiveOnDemand(hivePath);
        }

        /// <inheritdoc />
        public Registry.Abstractions.RegistryKey? GetKey(RegistryHiveOnDemand hive, string keyPath)
        {
            if (hive == null || string.IsNullOrWhiteSpace(keyPath))
            {
                return null;
            }

            try
            {
                return hive.GetKey(keyPath);
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to read key '{keyPath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads one uninstall key into software entries
        /// </summary>
        private void CollectSoftwareEntries(RegistryHiveOnDemand hive, string keyPath, List<Software> softwareList)
        {
            var uninstallKey = GetKey(hive, keyPath);
            if (uninstallKey == null)
            {
                return;
            }

            foreach (var subKey in uninstallKey.SubKeys)
            {
                try
                {
                    var displayName = GetStringValue(subKey, "DisplayName");

                    // Only include entries with a display name
                    if (string.IsNullOrEmpty(displayName))
                    {
                        continue;
                    }

                    var registryKeyPath = $@"HKLM\SOFTWARE\{keyPath}\{subKey.KeyName}";

                    var displayVersionRaw = GetStringValue(subKey, "DisplayVersion");
                    var installDateRaw = GetStringValue(subKey, "InstallDate");
                    var publisherRaw = GetStringValue(subKey, "Publisher");

                    // Use parsed Version when possible, otherwise the original string
                    object? displayVersion = displayVersionRaw != null && FormatUtilityService.TryParseVersion(displayVersionRaw, out var parsedVersion)
                        ? (object)parsedVersion
                        : (object?)displayVersionRaw;

                    // Use parsed DateTime when possible, otherwise the original string
                    object? installDate = installDateRaw != null && FormatUtilityService.TryParseDate(installDateRaw, out var parsedDate)
                        ? (object)parsedDate
                        : (object?)installDateRaw;

                    softwareList.Add(new Software
                    {
                        DisplayName = displayName!,
                        Publisher = publisherRaw ?? string.Empty,
                        DisplayVersion = displayVersion,
                        InstallDate = installDate,
                        RegistryKeyPath = registryKeyPath
                    });
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Error reading software entry {subKey.KeyName}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets a string value from a registry key by value name
        /// </summary>
        private static string? GetStringValue(Registry.Abstractions.RegistryKey key, string valueName)
        {
            var value = key.Values.FirstOrDefault(v =>
                string.Equals(v.ValueName, valueName, StringComparison.OrdinalIgnoreCase));

            return value?.ValueData?.ToString();
        }

        public void Dispose()
        {
            // No unmanaged resources: hives are parsed into memory on demand
        }
    }
}
