using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Queries and changes the Out-of-Box Experience (OOBE) configuration of a
    /// mounted Windows image's offline SOFTWARE hive
    /// (<c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE</c>).
    ///
    /// Reads go through the existing in-memory <see cref="IRegistryHiveReader"/>
    /// (no hive mounting, no persistent handles). Writes are delegated to the
    /// existing hive-mounted native path
    /// <see cref="NativeRegistryService.ApplyRegistryOperations"/> — this service
    /// never mounts hives itself. All decision logic (setting catalog, DWORD
    /// lookup, projection, validation, operation building, description and
    /// result building) is pure and unit-testable without hive files, DISM
    /// sessions or real images.
    /// </summary>
    public class WindowsImageOobeService
    {
        private const string ServiceName = "WindowsImageOobeService";

        /// <summary>
        /// Canonical hive name for the SOFTWARE hive
        /// </summary>
        public const string SoftwareHiveName = "HKLM\\SOFTWARE";

        /// <summary>
        /// OOBE key path within the SOFTWARE hive (relative to the hive root; read path)
        /// </summary>
        internal const string OobeKeyPath = @"Microsoft\Windows\CurrentVersion\OOBE";

        /// <summary>
        /// OOBE key path as used by <see cref="RegistryOperation"/> (the write path
        /// needs the "SOFTWARE\" prefix so NativeRegistryService mounts and maps the
        /// SOFTWARE hive)
        /// </summary>
        internal const string OobeOperationKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public WindowsImageOobeService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// The documented default catalog of OOBE settings
        /// (DWORD values under the offline OOBE key, fixed display order)
        /// </summary>
        public static List<WindowsImageOobeSettingDefinition> GetDefaultSettings()
        {
            return new List<WindowsImageOobeSettingDefinition>
            {
                OobeSetting("SkipMachineOOBE", "1 = skip the machine OOBE phase (legacy switch; informational on Windows 10/11 images, honored by Windows 7-era setup and some tooling)"),
                OobeSetting("SkipUserOOBE", "1 = skip the user OOBE phase (legacy switch; same caveat as SkipMachineOOBE)"),
                OobeSetting("SkipPrivacyExperience", "1 = skip the privacy / express-settings experience screen (Windows 10 1709+ and Windows 11)"),
                OobeSetting("ProtectYourPC", "1 = use recommended settings, 2 = recommended settings off (only important updates), 3 = not in the recommended program"),
                OobeSetting("BypassNRO", "1 = allow completing OOBE without a network connection (Windows 11; removed in some newer builds, informational if ignored by the image)"),
                OobeSetting("HideOnlineAccountScreens", "1 = hide Microsoft-account online sign-up / sign-in screens during OOBE"),
                OobeSetting("HideWirelessSetupInOOBE", "1 = hide the wireless-network setup screen during OOBE")
            };
        }

        /// <summary>
        /// Reads the OOBE configuration from a mounted image's offline SOFTWARE hive.
        /// Thin hive-reading path (the only method that touches <see cref="IRegistryHiveReader"/>).
        /// </summary>
        /// <param name="reader">Offline hive reader (in-memory, no hive mounting)</param>
        /// <param name="imageName">Name of the image (copied onto each result)</param>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <returns>One entry per catalog setting, in catalog order (empty when the hive is missing)</returns>
        public List<WindowsImageOobeSetting> GetSettings(IRegistryHiveReader reader, string imageName, string mountPath)
        {
            var output = new List<WindowsImageOobeSetting>();
            var hivePath = ResolveSoftwareHivePath(mountPath);

            if (!File.Exists(hivePath))
            {
                _callbacks.Verbose?.Invoke($"SOFTWARE hive not found at {hivePath}; no OOBE settings enumerated for {imageName}");
                return output;
            }

            var hive = reader.OpenHive(hivePath);
            var oobeKey = reader.GetKey(hive, OobeKeyPath);
            if (oobeKey == null)
            {
                _callbacks.Verbose?.Invoke($"OOBE key '{OobeKeyPath}' not found in {hivePath}; reporting catalog defaults");
            }

            var values = oobeKey == null
                ? new List<(string Name, object? Data)>()
                : oobeKey.Values
                    .Where(v => v != null)
                    .Select(v => (Name: v.ValueName ?? string.Empty, Data: (object?)v.ValueData))
                    .ToList();

            foreach (var definition in GetDefaultSettings())
            {
                try
                {
                    output.Add(ProjectSetting(imageName, mountPath, definition, GetDwordValue(values, definition.ValueName)));
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to read OOBE setting '{definition.ValueName}' for {imageName}: {ex.Message}");
                }
            }

            _callbacks.Verbose?.Invoke($"Read {output.Count(s => s.IsSet)} set of {output.Count} catalog OOBE settings from {hivePath}");
            return output;
        }

        /// <summary>
        /// Resolves the SOFTWARE hive file path inside a mounted Windows image
        /// </summary>
        internal static string ResolveSoftwareHivePath(string mountPath)
        {
            return Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
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
        /// Projects one catalog definition plus its raw DWORD into a reported setting. Pure.
        /// </summary>
        internal static WindowsImageOobeSetting ProjectSetting(
            string imageName,
            string mountPath,
            WindowsImageOobeSettingDefinition definition,
            int? value)
        {
            return new WindowsImageOobeSetting
            {
                ImageName = imageName,
                MountPath = mountPath,
                SettingName = definition.SettingName,
                ValueName = definition.ValueName,
                Description = definition.Description,
                IsSet = value.HasValue,
                Value = value,
                State = value.HasValue ? $"Set: {value.Value}" : "Not set"
            };
        }

        /// <summary>
        /// True when the value name is a documented OOBE catalog name (ordinal-ignore-case). Pure.
        /// </summary>
        internal static bool IsValidValueName(string? valueName)
        {
            if (string.IsNullOrWhiteSpace(valueName))
            {
                return false;
            }

            return GetDefaultSettings().Any(s =>
                string.Equals(s.ValueName, valueName!.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Converts a ProtectYourPC mode to its registry DWORD
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Unmapped enum member</exception>
        internal static int ToProtectYourPcValue(WindowsImageOobeProtectYourPc mode)
        {
            switch (mode)
            {
                case WindowsImageOobeProtectYourPc.Recommended: return 1;
                case WindowsImageOobeProtectYourPc.ImportantOnly: return 2;
                case WindowsImageOobeProtectYourPc.NotInProgram: return 3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown ProtectYourPC mode cannot be written to the registry.");
            }
        }

        /// <summary>
        /// Validates a Set request: at least one change is requested, every value
        /// name is a documented catalog name, and no name is both written and removed. Pure.
        /// </summary>
        /// <exception cref="ArgumentException">When the request is invalid</exception>
        internal static void ValidateChanges(List<WindowsImageOobeChange>? changes)
        {
            if (changes == null || changes.Count == 0)
            {
                throw new ArgumentException(
                    "Specify at least one OOBE change (a setting switch, -ProtectYourPC, or -Remove).",
                    nameof(changes));
            }

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in changes)
            {
                if (change == null || !IsValidValueName(change.ValueName))
                {
                    throw new ArgumentException(
                        $"OOBE value name '{change?.ValueName}' is not a documented OOBE setting.",
                        nameof(changes));
                }

                var normalized = change.ValueName.Trim();

                if (change.Value.HasValue)
                {
                    if (!written.Add(normalized))
                    {
                        throw new ArgumentException(
                            $"OOBE value '{normalized}' was specified more than once.",
                            nameof(changes));
                    }

                    if (removed.Contains(normalized))
                    {
                        throw new ArgumentException(
                            $"OOBE value '{normalized}' cannot be both written and removed in one request.",
                            nameof(changes));
                    }
                }
                else
                {
                    if (!removed.Add(normalized))
                    {
                        throw new ArgumentException(
                            $"OOBE value '{normalized}' was specified more than once.",
                            nameof(changes));
                    }

                    if (written.Contains(normalized))
                    {
                        throw new ArgumentException(
                            $"OOBE value '{normalized}' cannot be both written and removed in one request.",
                            nameof(changes));
                    }
                }
            }
        }

        /// <summary>
        /// Builds the registry operations for a validated Set request. Pure:
        /// writes (Modify) in catalog order first, then removals (Remove).
        /// </summary>
        internal static List<RegistryOperation> BuildSetOperations(List<WindowsImageOobeChange> changes)
        {
            var operations = new List<RegistryOperation>();
            if (changes == null)
            {
                return operations;
            }

            var catalogOrder = GetDefaultSettings()
                .Select((s, index) => (Name: s.ValueName, Index: index))
                .ToDictionary(p => p.Name, p => p.Index, StringComparer.OrdinalIgnoreCase);

            var ordered = changes
                .OrderBy(c => c.Value.HasValue ? 0 : 1)
                .ThenBy(c => catalogOrder.TryGetValue(c.ValueName.Trim(), out var index) ? index : int.MaxValue);

            foreach (var change in ordered)
            {
                if (change.Value.HasValue)
                {
                    operations.Add(new RegistryOperation
                    {
                        Operation = RegistryOperationType.Modify,
                        Hive = "HKLM",
                        Key = OobeOperationKeyPath,
                        ValueName = change.ValueName.Trim(),
                        Value = (uint)change.Value.Value,
                        ValueType = RegistryValueKind.DWord
                    });
                }
                else
                {
                    operations.Add(new RegistryOperation
                    {
                        Operation = RegistryOperationType.Remove,
                        Hive = "HKLM",
                        Key = OobeOperationKeyPath,
                        ValueName = change.ValueName.Trim(),
                        Value = null,
                        ValueType = RegistryValueKind.Unknown
                    });
                }
            }

            return operations;
        }

        /// <summary>
        /// Describes a Set change in human terms (used by ShouldProcess, the result
        /// Operation and logging). Pure.
        /// </summary>
        internal static string DescribeSetChange(List<WindowsImageOobeChange> changes)
        {
            if (changes == null || changes.Count == 0)
            {
                return "No OOBE changes";
            }

            var catalogOrder = GetDefaultSettings()
                .Select((s, index) => (Name: s.ValueName, Index: index))
                .ToDictionary(p => p.Name, p => p.Index, StringComparer.OrdinalIgnoreCase);

            var parts = changes
                .OrderBy(c => c.Value.HasValue ? 0 : 1)
                .ThenBy(c => catalogOrder.TryGetValue(c.ValueName.Trim(), out var index) ? index : int.MaxValue)
                .Select(c => c.Value.HasValue
                    ? $"Write {c.ValueName.Trim()}={c.Value.Value}"
                    : $"Remove {c.ValueName.Trim()}");

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Builds a Set operation result. Pure.
        /// </summary>
        internal static WindowsImageOobeOperationResult BuildSetResult(
            string imageName,
            string operation,
            bool success,
            string? errorMessage)
        {
            return new WindowsImageOobeOperationResult
            {
                ImageName = imageName,
                Operation = operation,
                Success = success,
                ErrorMessage = errorMessage
            };
        }

        /// <summary>
        /// Builds one catalog entry (SettingName == ValueName for this catalog)
        /// </summary>
        private static WindowsImageOobeSettingDefinition OobeSetting(string valueName, string description)
        {
            return new WindowsImageOobeSettingDefinition
            {
                SettingName = valueName,
                ValueName = valueName,
                Description = description
            };
        }
    }
}
