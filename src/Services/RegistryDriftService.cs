using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Captures drift-relevant registry keys from offline hives of a mounted image and
    /// diffs two snapshots' registry data (added / removed / changed per hive).
    /// Hive reading is thin (via <see cref="IRegistryHiveReader"/>); key selection,
    /// value normalization, capture projection and diffing are pure and unit-testable.
    /// </summary>
    public class RegistryDriftService
    {
        private const string ServiceName = "RegistryDriftService";
        private const string DefaultValueName = "(Default)";
        private const string SubKeyValueType = "SubKey";

        /// <summary>
        /// Canonical hive name for the SOFTWARE hive
        /// </summary>
        public const string SoftwareHiveName = "HKLM\\SOFTWARE";

        /// <summary>
        /// Canonical hive name for the SYSTEM hive
        /// </summary>
        public const string SystemHiveName = "HKLM\\SYSTEM";

        private readonly ModuleCallbacks _callbacks;

        public RegistryDriftService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// The documented default set of drift-relevant registry keys
        /// </summary>
        public static List<RegistryDriftKeyDefinition> GetDefaultDriftKeyDefinitions()
        {
            return new List<RegistryDriftKeyDefinition>
            {
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\Run", RegistryKeyCaptureMode.Values, "Autostart entries (HKLM Run key)"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\RunOnce", RegistryKeyCaptureMode.Values, "One-shot autostart entries (RunOnce key)"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\Policies\System", RegistryKeyCaptureMode.Values, "System security policy (UAC, logon, shutdown)"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\Policies\Explorer", RegistryKeyCaptureMode.Values, "Shell / Explorer policy"),
                DriftKey(SoftwareHiveName, @"Policies\Microsoft\Windows\WindowsUpdate", RegistryKeyCaptureMode.Values, "Windows Update policy"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update", RegistryKeyCaptureMode.Values, "Automatic Update configuration"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows NT\CurrentVersion\Winlogon", RegistryKeyCaptureMode.Values, "Winlogon configuration (autologon, shell, logon UI)"),
                DriftKey(SoftwareHiveName, @"Microsoft\Windows\CurrentVersion\Uninstall", RegistryKeyCaptureMode.SubKeyNames, "Installed software signature (native view)"),
                DriftKey(SoftwareHiveName, @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", RegistryKeyCaptureMode.SubKeyNames, "Installed software signature (WOW64 view)"),
                DriftKey(SystemHiveName, @"ControlSet001\Control\ComputerName\ComputerName", RegistryKeyCaptureMode.Values, "Computer name"),
                DriftKey(SystemHiveName, @"ControlSet001\Control\Session Manager", RegistryKeyCaptureMode.Values, "Session Manager (boot execute, memory config)"),
                DriftKey(SystemHiveName, @"ControlSet001\Control\Lsa", RegistryKeyCaptureMode.Values, "Local Security Authority configuration"),
                DriftKey(SystemHiveName, @"ControlSet001\Control\Session Manager\Environment", RegistryKeyCaptureMode.Values, "System environment variables"),
                DriftKey(SystemHiveName, @"ControlSet001\Services\Tcpip\Parameters", RegistryKeyCaptureMode.Values, "TCP/IP parameters (DHCP, DNS suffix, hostname)"),
                DriftKey(SystemHiveName, @"ControlSet001\Control\Terminal Server", RegistryKeyCaptureMode.Values, "Terminal Server state (RDP, fDenyTSConnections)"),
                DriftKey(SystemHiveName, @"ControlSet001\Services", RegistryKeyCaptureMode.SubKeyNames, "Installed service set signature")
            };
        }

        /// <summary>
        /// Captures the drift-relevant registry values from a mounted image's offline hives
        /// </summary>
        /// <param name="reader">Offline hive reader (in-memory, no hive mounting)</param>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="definitions">Drift key definitions (defaults when null)</param>
        /// <returns>Captured registry values, sorted by full path (empty when a hive is missing)</returns>
        public List<RegistrySnapshotValue> CaptureDriftValues(IRegistryHiveReader reader, string mountPath, IReadOnlyList<RegistryDriftKeyDefinition>? definitions = null)
        {
            var definitionsList = definitions ?? GetDefaultDriftKeyDefinitions();
            var output = new List<RegistrySnapshotValue>();

            foreach (var hiveGroup in definitionsList.GroupBy(d => d.Hive, StringComparer.OrdinalIgnoreCase))
            {
                var hiveName = hiveGroup.Key;
                var hivePath = ResolveHivePath(mountPath, hiveName);

                if (!File.Exists(hivePath))
                {
                    _callbacks.Verbose?.Invoke($"Registry hive not found at {hivePath}; skipping drift capture for {hiveName}");
                    continue;
                }

                var hive = reader.OpenHive(hivePath);

                foreach (var definition in hiveGroup)
                {
                    try
                    {
                        var key = reader.GetKey(hive, definition.KeyPath);
                        if (key == null)
                        {
                            continue;
                        }

                        var values = key.Values
                            .Where(v => v != null)
                            .Select(v => (ValueName: v.ValueName ?? string.Empty, ValueType: v.ValueType ?? "REG_SZ", ValueData: NormalizeValueData(v.ValueData)));

                        var subKeyNames = key.SubKeys
                            .Select(s => s.KeyName)
                            .Where(n => !string.IsNullOrEmpty(n));

                        AppendCapture(hiveName, definition, values, subKeyNames, output);
                    }
                    catch (Exception ex)
                    {
                        _callbacks.Warning?.Invoke($"Failed to capture {hiveName}\\{definition.KeyPath}: {ex.Message}");
                    }
                }
            }

            _callbacks.Verbose?.Invoke($"Captured {output.Count} registry drift values from {definitionsList.Count} key definitions");
            return output;
        }

        /// <summary>
        /// Resolves a hive file path inside a mounted Windows image
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="hive">Canonical hive name (HKLM\SOFTWARE or HKLM\SYSTEM)</param>
        /// <returns>Full path to the hive file under Windows\System32\config</returns>
        internal static string ResolveHivePath(string mountPath, string hive)
        {
            string hiveFile;

            if (string.Equals(hive, SoftwareHiveName, StringComparison.OrdinalIgnoreCase))
            {
                hiveFile = "SOFTWARE";
            }
            else if (string.Equals(hive, SystemHiveName, StringComparison.OrdinalIgnoreCase))
            {
                hiveFile = "SYSTEM";
            }
            else
            {
                hiveFile = hive.Replace('\\', '_');
            }

            return Path.Combine(mountPath, "Windows", "System32", "config", hiveFile);
        }

        /// <summary>
        /// Projects a definition's captured values into registry snapshot values
        /// </summary>
        internal static List<RegistrySnapshotValue> CaptureValues(string hive, RegistryDriftKeyDefinition definition, IEnumerable<(string ValueName, string ValueType, string ValueData)> values)
        {
            var captured = values
                .Select(v => new RegistrySnapshotValue
                {
                    Hive = hive,
                    KeyPath = definition.KeyPath,
                    ValueName = string.IsNullOrEmpty(v.ValueName) ? DefaultValueName : v.ValueName,
                    ValueType = v.ValueType,
                    ValueData = v.ValueData
                })
                .ToList();

            captured.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
            return captured;
        }

        /// <summary>
        /// Projects a definition's subkey-name signature into registry snapshot values
        /// </summary>
        internal static List<RegistrySnapshotValue> CaptureSubKeyNames(string hive, RegistryDriftKeyDefinition definition, IEnumerable<string> subKeyNames)
        {
            var captured = subKeyNames
                .Select(name => new RegistrySnapshotValue
                {
                    Hive = hive,
                    KeyPath = definition.KeyPath,
                    ValueName = name,
                    ValueType = SubKeyValueType,
                    ValueData = string.Empty
                })
                .ToList();

            captured.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
            return captured;
        }

        /// <summary>
        /// Appends a definition's capture to an output list, routing by capture mode
        /// </summary>
        internal static void AppendCapture(string hive, RegistryDriftKeyDefinition definition, IEnumerable<(string ValueName, string ValueType, string ValueData)> values, IEnumerable<string> subKeyNames, List<RegistrySnapshotValue> output)
        {
            if (definition.Mode == RegistryKeyCaptureMode.SubKeyNames)
            {
                output.AddRange(CaptureSubKeyNames(hive, definition, subKeyNames));
            }
            else
            {
                output.AddRange(CaptureValues(hive, definition, values));
            }
        }

        /// <summary>
        /// Normalizes raw value data for stable comparison (CRLF/CR -> LF, trimmed)
        /// </summary>
        internal static string NormalizeValueData(string? data)
        {
            if (data == null)
            {
                return string.Empty;
            }

            return data.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        }

        /// <summary>
        /// Diffs two snapshots' registry data per hive
        /// </summary>
        /// <param name="referenceName">Reference (before) image name</param>
        /// <param name="differenceName">Difference (after) image name</param>
        /// <param name="reference">Reference registry values</param>
        /// <param name="difference">Difference registry values</param>
        /// <returns>Per-hive drift result (identical when both sides are empty or equal)</returns>
        internal static RegistryDriftResult CompareRegistry(string referenceName, string differenceName, List<RegistrySnapshotValue> reference, List<RegistrySnapshotValue> difference)
        {
            var result = new RegistryDriftResult
            {
                ReferenceName = referenceName,
                DifferenceName = differenceName
            };

            var referenceSnapshot = reference ?? new List<RegistrySnapshotValue>();
            var differenceSnapshot = difference ?? new List<RegistrySnapshotValue>();

            result.ReferenceValueCount = referenceSnapshot.Count;
            result.DifferenceValueCount = differenceSnapshot.Count;

            var hives = referenceSnapshot.Select(v => v.Hive)
                .Concat(differenceSnapshot.Select(v => v.Hive))
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase);

            foreach (var hive in hives)
            {
                var referenceByPath = referenceSnapshot
                    .Where(v => string.Equals(v.Hive, hive, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.IsNullOrEmpty(v.FullPath))
                    .GroupBy(v => v.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var differenceByPath = differenceSnapshot
                    .Where(v => string.Equals(v.Hive, hive, StringComparison.OrdinalIgnoreCase))
                    .Where(v => !string.IsNullOrEmpty(v.FullPath))
                    .GroupBy(v => v.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var hiveDifference = new RegistryHiveDifference { Hive = hive };

                foreach (var path in differenceByPath.Keys)
                {
                    var current = differenceByPath[path];

                    if (!referenceByPath.ContainsKey(path))
                    {
                        hiveDifference.Added.Add(current);
                    }
                    else if (!ValuesEqual(referenceByPath[path], current))
                    {
                        hiveDifference.Changed.Add(new RegistryValueChange
                        {
                            Hive = current.Hive,
                            KeyPath = current.KeyPath,
                            ValueName = current.ValueName,
                            ValueType = current.ValueType,
                            PreviousData = referenceByPath[path].ValueData,
                            CurrentData = current.ValueData
                        });
                    }
                }

                foreach (var path in referenceByPath.Keys)
                {
                    if (!differenceByPath.ContainsKey(path))
                    {
                        hiveDifference.Removed.Add(referenceByPath[path]);
                    }
                }

                hiveDifference.Added.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
                hiveDifference.Removed.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
                hiveDifference.Changed.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));

                result.Hives.Add(hiveDifference);
            }

            return result;
        }

        private static bool ValuesEqual(RegistrySnapshotValue a, RegistrySnapshotValue b)
        {
            return string.Equals(a.ValueData, b.ValueData, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.ValueType, b.ValueType, StringComparison.OrdinalIgnoreCase);
        }

        private static RegistryDriftKeyDefinition DriftKey(string hive, string keyPath, RegistryKeyCaptureMode mode, string description)
        {
            return new RegistryDriftKeyDefinition
            {
                Hive = hive,
                KeyPath = keyPath,
                Mode = mode,
                Description = description
            };
        }
    }
}