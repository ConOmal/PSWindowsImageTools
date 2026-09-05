using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Queries and changes the service configuration of a mounted Windows image's
    /// offline SYSTEM hive (<c>ControlSet001\Services</c>).
    ///
    /// Reads go through the existing in-memory <see cref="IRegistryHiveReader"/>
    /// (no hive mounting, no persistent handles). Writes are delegated to the
    /// existing hive-mounted native path
    /// <see cref="NativeRegistryService.ApplyRegistryOperations"/> — this service
    /// never mounts hives itself. All decision logic (start-type mapping, name
    /// filtering, projection, validation, operation building, result building) is
    /// pure and unit-testable without hive files, DISM sessions or real images.
    /// </summary>
    public class WindowsImageServicesService
    {
        private const string ServiceName = "WindowsImageServicesService";

        /// <summary>
        /// Canonical hive name for the SYSTEM hive
        /// </summary>
        public const string SystemHiveName = "HKLM\\SYSTEM";

        /// <summary>
        /// Canonical services key path within the SYSTEM hive (relative to the hive root)
        /// </summary>
        internal const string ServicesKeyPath = @"ControlSet001\Services";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public WindowsImageServicesService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Enumerates the services configured in a mounted image's offline SYSTEM hive.
        /// Thin hive-reading path (the only method beside <see cref="ServiceExists"/>
        /// that touches <see cref="IRegistryHiveReader"/>).
        /// </summary>
        /// <param name="reader">Offline hive reader (in-memory, no hive mounting)</param>
        /// <param name="imageName">Name of the image (copied onto each result)</param>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="nameFilter">Optional service-name filter (exact or regex); null for all</param>
        /// <param name="detailed">When true, attach the raw key values to each result</param>
        /// <returns>Services matching the filter, sorted by name (empty when the hive is missing)</returns>
        public List<WindowsImageServiceInfo> GetServices(
            IRegistryHiveReader reader,
            string imageName,
            string mountPath,
            string? nameFilter = null,
            bool detailed = false)
        {
            var output = new List<WindowsImageServiceInfo>();
            var hivePath = ResolveSystemHivePath(mountPath);

            if (!File.Exists(hivePath))
            {
                _callbacks.Verbose?.Invoke($"SYSTEM hive not found at {hivePath}; no services enumerated for {imageName}");
                return output;
            }

            var hive = reader.OpenHive(hivePath);
            var servicesKey = reader.GetKey(hive, ServicesKeyPath);
            if (servicesKey == null)
            {
                _callbacks.Verbose?.Invoke($"Services key '{ServicesKeyPath}' not found in {hivePath}");
                return output;
            }

            var serviceNames = servicesKey.SubKeys
                .Select(s => s.KeyName)
                .Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            foreach (var serviceName in serviceNames)
            {
                if (!MatchesNameFilter(serviceName, nameFilter))
                {
                    continue;
                }

                try
                {
                    var key = reader.GetKey(hive, $"{ServicesKeyPath}\\{serviceName}");
                    if (key == null)
                    {
                        continue;
                    }

                    var values = key.Values
                        .Where(v => v != null)
                        .Select(v => (Name: v.ValueName ?? string.Empty, Data: (object?)v.ValueData))
                        .ToList();

                    var info = ProjectServiceInfo(imageName, mountPath, serviceName, values);
                    if (detailed)
                    {
                        info.RegistryValues = CollectValues(values);
                    }

                    output.Add(info);
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to read service '{serviceName}': {ex.Message}");
                }
            }

            _callbacks.Verbose?.Invoke($"Enumerated {output.Count} services from {hivePath}");
            return output;
        }

        /// <summary>
        /// True when the given service key exists in the image's offline SYSTEM hive.
        /// Cheap pre-flight for Set (fails on typos before any hive is mounted/unmounted).
        /// </summary>
        public bool ServiceExists(IRegistryHiveReader reader, string mountPath, string serviceName)
        {
            if (!IsValidServiceName(serviceName))
            {
                return false;
            }

            var hivePath = ResolveSystemHivePath(mountPath);
            if (!File.Exists(hivePath))
            {
                return false;
            }

            try
            {
                var hive = reader.OpenHive(hivePath);
                return reader.GetKey(hive, $"{ServicesKeyPath}\\{serviceName}") != null;
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check service '{serviceName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolves the SYSTEM hive file path inside a mounted Windows image
        /// </summary>
        internal static string ResolveSystemHivePath(string mountPath)
        {
            return Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM");
        }

        /// <summary>
        /// True when the value is usable as a service key name (non-blank, no path separators)
        /// </summary>
        internal static bool IsValidServiceName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && name.IndexOfAny(new[] { '\\', '/', '\0' }) < 0;
        }

        /// <summary>
        /// Maps a raw Start DWORD to its friendly start type (Unknown for anything outside 0-4)
        /// </summary>
        internal static WindowsImageServiceStartType ParseStartType(int value)
        {
            switch (value)
            {
                case 0: return WindowsImageServiceStartType.Boot;
                case 1: return WindowsImageServiceStartType.System;
                case 2: return WindowsImageServiceStartType.Automatic;
                case 3: return WindowsImageServiceStartType.Manual;
                case 4: return WindowsImageServiceStartType.Disabled;
                default: return WindowsImageServiceStartType.Unknown;
            }
        }

        /// <summary>
        /// Converts a friendly start type to its registry Start DWORD
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Unknown cannot be written</exception>
        internal static int ToStartValue(WindowsImageServiceStartType type)
        {
            switch (type)
            {
                case WindowsImageServiceStartType.Boot: return 0;
                case WindowsImageServiceStartType.System: return 1;
                case WindowsImageServiceStartType.Automatic: return 2;
                case WindowsImageServiceStartType.Manual: return 3;
                case WindowsImageServiceStartType.Disabled: return 4;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown start type cannot be written to the registry.");
            }
        }

        /// <summary>
        /// Reads a value as a DWORD by name (ordinal-ignore-case); null when absent or non-numeric
        /// </summary>
        internal static int? GetDwordValue(IEnumerable<(string Name, object? Data)> values, string valueName)
        {
            foreach (var value in values)
            {
                if (!string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (value.Data == null)
                {
                    return null;
                }

                try
                {
                    return Convert.ToInt32(value.Data, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a value as a string by name (ordinal-ignore-case); empty when absent
        /// </summary>
        internal static string GetStringValue(IEnumerable<(string Name, object? Data)> values, string valueName)
        {
            foreach (var value in values)
            {
                if (string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Data?.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// True when the DelayedAutoStart DWORD equals 1 (false when absent or 0)
        /// </summary>
        internal static bool GetDelayedAutoStart(IEnumerable<(string Name, object? Data)> values)
        {
            return GetDwordValue(values, "DelayedAutoStart") == 1;
        }

        /// <summary>
        /// Projects a service key's raw values into a WindowsImageServiceInfo. Pure.
        /// </summary>
        internal static WindowsImageServiceInfo ProjectServiceInfo(
            string imageName,
            string mountPath,
            string name,
            IEnumerable<(string Name, object? Data)> values)
        {
            var startValue = GetDwordValue(values, "Start") ?? -1;

            return new WindowsImageServiceInfo
            {
                ImageName = imageName,
                MountPath = mountPath,
                Name = name,
                DisplayName = GetStringValue(values, "DisplayName"),
                ImagePath = GetStringValue(values, "ImagePath"),
                Description = GetStringValue(values, "Description"),
                StartType = ParseStartType(startValue),
                StartValue = startValue,
                DelayedAutoStart = GetDelayedAutoStart(values)
            };
        }

        /// <summary>
        /// Copies raw service-key values into a dictionary sorted by value name. Pure.
        /// </summary>
        internal static Dictionary<string, object> CollectValues(IEnumerable<(string Name, object? Data)> values)
        {
            var output = new Dictionary<string, object>();

            foreach (var value in values
                .Where(v => !string.IsNullOrEmpty(v.Name))
                .OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                output[value.Name] = value.Data!;
            }

            return output;
        }

        /// <summary>
        /// True when a service name matches a filter: blank filter matches everything;
        /// exact case-insensitive match wins; otherwise the filter is treated as a
        /// regex (anchored, case-insensitive, 1s timeout). An invalid pattern or
        /// timeout matches nothing. Pure.
        /// </summary>
        internal static bool MatchesNameFilter(string? serviceName, string? filter)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            var trimmedFilter = filter!.Trim();
            if (string.Equals(serviceName, trimmedFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return new Regex(
                    "^(?i:" + trimmedFilter + ")$",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).IsMatch(serviceName!);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validates a Set request: at least one change is requested, and
        /// DelayedAutoStart is only valid alongside an Automatic start type.
        /// Pure.
        /// </summary>
        /// <exception cref="ArgumentException">When the combination is invalid</exception>
        internal static void ValidateSetParameters(WindowsImageServiceStartType? startType, bool setDelayedAutoStart)
        {
            if (!startType.HasValue && !setDelayedAutoStart)
            {
                throw new ArgumentException("Specify at least one of -StartType or -DelayedAutoStart.", nameof(startType));
            }

            if (setDelayedAutoStart && startType.HasValue && startType.Value != WindowsImageServiceStartType.Automatic)
            {
                throw new ArgumentException(
                    "-DelayedAutoStart can only be combined with -StartType Automatic.",
                    nameof(startType));
            }
        }

        /// <summary>
        /// Builds the registry operations for a validated Set request. Pure.
        /// </summary>
        internal static List<RegistryOperation> BuildSetOperations(
            string serviceName,
            WindowsImageServiceStartType? startType,
            bool setDelayedAutoStart)
        {
            var operations = new List<RegistryOperation>();
            var serviceKey = $"{ServicesKeyPath}\\{serviceName}";

            if (startType.HasValue)
            {
                operations.Add(new RegistryOperation
                {
                    Operation = RegistryOperationType.Modify,
                    Hive = "HKLM",
                    Key = serviceKey,
                    ValueName = "Start",
                    Value = ToStartValue(startType.Value),
                    ValueType = RegistryValueKind.DWord
                });
            }

            if (setDelayedAutoStart)
            {
                operations.Add(new RegistryOperation
                {
                    Operation = RegistryOperationType.Modify,
                    Hive = "HKLM",
                    Key = serviceKey,
                    ValueName = "DelayedAutoStart",
                    Value = 1u,
                    ValueType = RegistryValueKind.DWord
                });
            }

            return operations;
        }

        /// <summary>
        /// Describes a Set change in human terms (used by ShouldProcess and result Operation). Pure.
        /// </summary>
        internal static string DescribeSetChange(WindowsImageServiceStartType? startType, bool setDelayedAutoStart)
        {
            if (startType.HasValue && setDelayedAutoStart)
            {
                return $"Set start type to {startType.Value} and enable delayed auto start";
            }

            if (startType.HasValue)
            {
                return $"Set start type to {startType.Value}";
            }

            return "Enable delayed auto start";
        }

        /// <summary>
        /// Builds a Set operation result. Pure.
        /// </summary>
        internal static WindowsImageServiceOperationResult BuildSetResult(
            string imageName,
            string serviceName,
            WindowsImageServiceStartType? startType,
            bool setDelayedAutoStart,
            bool success,
            string? errorMessage)
        {
            return new WindowsImageServiceOperationResult
            {
                ImageName = imageName,
                ServiceName = serviceName,
                Operation = DescribeSetChange(startType, setDelayedAutoStart),
                RequestedStartType = startType,
                SetDelayedAutoStart = setDelayedAutoStart,
                Success = success,
                ErrorMessage = errorMessage
            };
        }
    }
}