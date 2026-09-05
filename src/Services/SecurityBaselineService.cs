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
    /// Curated security baseline for offline Windows images: compliance reporting
    /// (Get) and remediation (Set) against a documented set of security-relevant
    /// registry values.
    ///
    /// Reads go through the existing in-memory <see cref="IRegistryHiveReader"/>
    /// (no hive mounting, no persistent handles). Writes are delegated to the
    /// existing hive-mounted native path
    /// <see cref="NativeRegistryService.ApplyRegistryOperations"/> — this service
    /// never mounts hives itself. All decision logic (the baseline table, hive-path
    /// resolution, value normalization, compliance comparison, operation building,
    /// result building) is pure and unit-testable without hive files, DISM sessions
    /// or real images.
    /// </summary>
    public class SecurityBaselineService
    {
        private const string ServiceName = "SecurityBaselineService";

        /// <summary>
        /// Canonical hive name for the offline SOFTWARE hive
        /// </summary>
        public const string SoftwareHiveName = "HKLM\\SOFTWARE";

        /// <summary>
        /// Canonical hive name for the offline SYSTEM hive
        /// </summary>
        public const string SystemHiveName = "HKLM\\SYSTEM";

        /// <summary>
        /// Canonical hive name for the image's default-user profile hive
        /// (Users\Default\NTUSER.DAT — the module's HKU/default-user convention)
        /// </summary>
        public const string DefaultUserHiveName = "HKU\\DefaultUser";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public SecurityBaselineService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// The curated security baseline. Each entry documents the expected value of
        /// one security-relevant registry setting (see
        /// docs/superpowers/specs/2026-09-04-security-baselines-design.md for the
        /// full rationale table).
        /// </summary>
        public static List<WindowsImageSecurityBaselineEntry> GetBaselineEntries()
        {
            return new List<WindowsImageSecurityBaselineEntry>
            {
                // --- HKLM\SOFTWARE: UAC, logon UX, Autorun, RDP policy -----------------
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "EnableLUA",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "User Account Control enabled; Windows default enforced so an image cannot ship with UAC silently off."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "ConsentPromptBehaviorAdmin",
                    ExpectedValue = "2",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Elevation prompts for consent on the secure desktop (Windows default, CIS-aligned)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "PromptOnSecureDesktop",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Elevation UI only on the secure desktop; defeats UAC prompt spoofing."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "dontdisplaylastusername",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Interactive logon: Don't display last signed-in; avoids leaking account names (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\System",
                    ValueName = "DisableAutomaticRestartSignOn",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Disables ARSO (auto sign-in of the last interactive user after a restart); CIS L1."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    ValueName = "NoDriveTypeAutoRun",
                    ExpectedValue = "255",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Autoplay disabled on all drive types (0xFF); classic Autorun hardening (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows\CurrentVersion\Policies\Explorer",
                    ValueName = "NoAutorun",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Disallow Autoplay for non-volume devices; companion to NoDriveTypeAutoRun (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Microsoft\Windows NT\CurrentVersion\Winlogon",
                    ValueName = "AutoAdminLogon",
                    ExpectedValue = "0",
                    ValueType = RegistryValueKind.String,
                    Rationale = "No cached autologon: an image must never boot unattended into a desktop. REG_SZ by design."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SoftwareHiveName,
                    KeyPath = @"Policies\Microsoft\Windows NT\Terminal Services",
                    ValueName = "fDenyTSConnections",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Remote Desktop disabled at the policy level (the GPO-honored switch); images enable RDP deliberately."
                },

                // --- HKLM\SYSTEM: LSA/NTLM hardening, SMB signing, RDP, Remote Assist --
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Lsa",
                    ValueName = "RunAsPPL",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "LSA Protection (credential theft mitigation); default on Win11 22H2+, enforced explicitly."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Lsa",
                    ValueName = "LmCompatibilityLevel",
                    ExpectedValue = "5",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Send NTLMv2 responses only; refuse LM and NTLM (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Lsa",
                    ValueName = "NoLMHash",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Never store LM password hashes in the SAM."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Lsa",
                    ValueName = "RestrictAnonymous",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Restrict anonymous enumeration of SAM accounts (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Lsa",
                    ValueName = "RestrictAnonymousSam",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Restrict anonymous enumeration of SAM names (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Services\LanmanServer\Parameters",
                    ValueName = "SMB1",
                    ExpectedValue = "0",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "SMB1 server component off; deprecated, worm-exploited protocol (MS17-010 class)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Services\LanmanServer\Parameters",
                    ValueName = "RequireSecuritySignature",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Microsoft network server: Digitally sign communications (always) — SMB signing server-side (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Services\LanmanWorkstation\Parameters",
                    ValueName = "RequireSecuritySignature",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Microsoft network client: Digitally sign communications (always) — SMB signing client-side (CIS L1)."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Terminal Server",
                    ValueName = "fDenyTSConnections",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "RDP disabled at the system level; pairs with the Terminal Services policy entry."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Terminal Server\WinStations\RDP-Tcp",
                    ValueName = "UserAuthentication",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Network Level Authentication required for RDP; defense in depth for images that later enable RDP."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = SystemHiveName,
                    KeyPath = @"ControlSet001\Control\Remote Assistance",
                    ValueName = "fAllowToGetHelp",
                    ExpectedValue = "0",
                    ValueType = RegistryValueKind.DWord,
                    Rationale = "Remote Assistance solicited help disabled (CIS L1)."
                },

                // --- Default-user profile hive: screen-saver lock for new profiles ------
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = DefaultUserHiveName,
                    KeyPath = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                    ValueName = "ScreenSaverIsSecure",
                    ExpectedValue = "1",
                    ValueType = RegistryValueKind.String,
                    Rationale = "Password-protected screen saver for every new profile created from the default user hive (CIS L1). REG_SZ by design."
                },
                new WindowsImageSecurityBaselineEntry
                {
                    Hive = DefaultUserHiveName,
                    KeyPath = @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                    ValueName = "ScreenSaveTimeOut",
                    ExpectedValue = "900",
                    ValueType = RegistryValueKind.String,
                    Rationale = "15-minute inactivity lock for new profiles (CIS L1 upper bound). REG_SZ by design."
                }
            };
        }

        /// <summary>
        /// Reports compliance of a mounted image against the baseline.
        /// Thin hive-reading path (the only method that touches
        /// <see cref="IRegistryHiveReader"/>). Missing hives, keys and values never
        /// throw — they report <see cref="WindowsImageBaselineComplianceState.NotPresent"/>.
        /// </summary>
        /// <param name="reader">Offline hive reader (in-memory, no hive mounting)</param>
        /// <param name="imageName">Name of the image (copied onto each observation)</param>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="entries">Baseline entries to check; null for the curated default</param>
        /// <returns>One observation per entry, in baseline order</returns>
        public WindowsImageSecurityBaselineReport GetBaselineCompliance(
            IRegistryHiveReader reader,
            string imageName,
            string mountPath,
            IReadOnlyList<WindowsImageSecurityBaselineEntry>? entries = null)
        {
            var baseline = entries ?? GetBaselineEntries();
            var report = new WindowsImageSecurityBaselineReport
            {
                ImageName = imageName,
                MountPath = mountPath
            };

            // Reads are grouped per hive (one OpenHive per hive file); the report is
            // emitted in the caller's baseline order afterwards.
            var collected = new Dictionary<string, WindowsImageSecurityBaselineObservation>(StringComparer.OrdinalIgnoreCase);

            foreach (var hiveGroup in baseline.GroupBy(e => e.Hive.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var hivePath = ResolveHivePath(mountPath, hiveGroup.Key);

                if (!File.Exists(hivePath))
                {
                    _callbacks.Verbose?.Invoke(
                        $"{hiveGroup.Key} hive not found at {hivePath}; {hiveGroup.Count()} baseline entries report NotPresent for {imageName}");
                    foreach (var entry in hiveGroup)
                    {
                        collected[ObservationKey(entry)] = BuildObservation(imageName, mountPath, entry, null, string.Empty);
                    }

                    continue;
                }

                var hive = reader.OpenHive(hivePath);

                foreach (var entry in hiveGroup)
                {
                    try
                    {
                        var key = reader.GetKey(hive, entry.KeyPath);
                        string? observedValue = null;
                        var observedType = string.Empty;

                        if (key != null)
                        {
                            var value = FindValue(key, entry.ValueName);
                            if (value != null)
                            {
                                observedValue = NormalizeValueData(value.ValueData);
                                observedType = value.ValueType ?? string.Empty;
                            }
                        }

                        collected[ObservationKey(entry)] = BuildObservation(imageName, mountPath, entry, observedValue, observedType);
                    }
                    catch (Exception ex)
                    {
                        _callbacks.Warning?.Invoke($"Failed to read baseline entry '{entry}': {ex.Message}");
                        collected[ObservationKey(entry)] = BuildObservation(imageName, mountPath, entry, null, string.Empty);
                    }
                }
            }

            foreach (var entry in baseline)
            {
                if (collected.TryGetValue(ObservationKey(entry), out var observation))
                {
                    report.Entries.Add(observation);
                }
            }

            _callbacks.Verbose?.Invoke(
                $"Checked {report.TotalEntries} baseline entries for {imageName}: {report.CompliantCount} compliant, {report.NonCompliantCount} non-compliant, {report.NotPresentCount} not present");
            return report;
        }

        /// <summary>
        /// Stable identity of a baseline entry within a report. Pure.
        /// </summary>
        private static string ObservationKey(WindowsImageSecurityBaselineEntry entry)
        {
            return $"{entry.Hive.Trim()}\\{entry.KeyPath.Trim()}\\{entry.ValueName.Trim()}";
        }

        /// <summary>
        /// Finds a value on a key by name (ordinal-ignore-case). An empty entry name
        /// matches the key's default value. Pure.
        /// </summary>
        private static Registry.Abstractions.KeyValue? FindValue(
            Registry.Abstractions.RegistryKey key,
            string valueName)
        {
            foreach (var value in key.Values)
            {
                if (value == null)
                {
                    continue;
                }

                var name = value.ValueName ?? string.Empty;
                if (string.IsNullOrEmpty(valueName))
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return value;
                    }
                }
                else if (string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Resolves a baseline hive name to its file path inside a mounted Windows image.
        /// Unknown hive names fall back to the config folder (keeps the helper total).
        /// Pure.
        /// </summary>
        internal static string ResolveHivePath(string mountPath, string hive)
        {
            var normalizedName = (hive ?? string.Empty).Trim();

            if (string.Equals(normalizedName, SoftwareHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
            }

            if (string.Equals(normalizedName, SystemHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM");
            }

            if (string.Equals(normalizedName, DefaultUserHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(mountPath, "Users", "Default", "NTUSER.DAT");
            }

            return Path.Combine(mountPath, "Windows", "System32", "config", normalizedName.Replace('\\', '_'));
        }

        /// <summary>
        /// Normalizes raw value data for comparison: null/blank → empty,
        /// CRLF/CR → LF, trim. Pure.
        /// </summary>
        internal static string NormalizeValueData(string? data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return string.Empty;
            }

            return data!.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        /// <summary>
        /// Maps a baseline RegistryValueKind to the friendly type string the offline
        /// hive parser reports (verified against Registry.dll: RegDword/RegSz/
        /// RegExpandSz/RegQword). Pure.
        /// </summary>
        internal static string ToExpectedTypeString(RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return "RegDword";
                case RegistryValueKind.QWord: return "RegQword";
                case RegistryValueKind.String: return "RegSz";
                case RegistryValueKind.ExpandString: return "RegExpandSz";
                case RegistryValueKind.MultiString: return "RegMultiSz";
                case RegistryValueKind.Binary: return "RegBinary";
                default: return kind.ToString();
            }
        }

        /// <summary>
        /// Compares an expected value against an observed value: both sides are
        /// trimmed; when both parse as integers they are compared numerically,
        /// otherwise case-insensitively as strings. Pure.
        /// </summary>
        internal static bool ValuesEquivalent(string? expected, string? observed)
        {
            if (expected == null || observed == null)
            {
                return expected == null && observed == null;
            }

            var expectedValue = expected.Trim();
            var observedValue = observed.Trim();

            if (long.TryParse(expectedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedNumber) &&
                long.TryParse(observedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var observedNumber))
            {
                return expectedNumber == observedNumber;
            }

            return string.Equals(expectedValue, observedValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Maps a baseline entry plus its observed value (null when absent) to a
        /// compliance state. Pure.
        /// </summary>
        internal static WindowsImageBaselineComplianceState CompareEntry(
            WindowsImageSecurityBaselineEntry entry,
            string? observedValue)
        {
            if (observedValue == null)
            {
                return WindowsImageBaselineComplianceState.NotPresent;
            }

            return ValuesEquivalent(entry.ExpectedValue, observedValue)
                ? WindowsImageBaselineComplianceState.Compliant
                : WindowsImageBaselineComplianceState.NonCompliant;
        }

        /// <summary>
        /// Builds one compliance observation. Pure.
        /// </summary>
        internal static WindowsImageSecurityBaselineObservation BuildObservation(
            string imageName,
            string mountPath,
            WindowsImageSecurityBaselineEntry entry,
            string? observedValue,
            string observedValueType)
        {
            return new WindowsImageSecurityBaselineObservation
            {
                ImageName = imageName ?? string.Empty,
                MountPath = mountPath ?? string.Empty,
                Hive = entry.Hive,
                KeyPath = entry.KeyPath,
                ValueName = entry.ValueName,
                ExpectedValue = entry.ExpectedValue,
                ValueType = entry.ValueType,
                Rationale = entry.Rationale,
                State = CompareEntry(entry, observedValue),
                ObservedValue = observedValue ?? string.Empty,
                ObservedValueType = observedValueType ?? string.Empty
            };
        }

        /// <summary>
        /// Maps a baseline hive name to the registry root string the write path
        /// expects (HKLM → machine hives, HKU → default-user hive). Pure.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown hive name</exception>
        internal static string MapOperationHive(string hive)
        {
            var normalizedName = (hive ?? string.Empty).Trim();

            if (normalizedName.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase))
            {
                return "HKLM";
            }

            if (string.Equals(normalizedName, DefaultUserHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return "HKU";
            }

            throw new ArgumentException($"Unknown baseline hive '{hive}'.", nameof(hive));
        }

        /// <summary>
        /// Maps a baseline hive + key path to the RegistryOperation key string the
        /// write path expects: SOFTWARE entries carry a "SOFTWARE\" prefix (the write
        /// path strips it after mounting the SOFTWARE hive), SYSTEM entries are
        /// already relative to the SYSTEM hive root (ControlSet001\...), and
        /// default-user entries are relative to the default-user hive root. Pure.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown hive name</exception>
        internal static string MapOperationKey(string hive, string keyPath)
        {
            var normalizedName = (hive ?? string.Empty).Trim();

            if (string.Equals(normalizedName, SoftwareHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return @"SOFTWARE\" + (keyPath ?? string.Empty).Trim();
            }

            if (string.Equals(normalizedName, SystemHiveName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedName, DefaultUserHiveName, StringComparison.OrdinalIgnoreCase))
            {
                return (keyPath ?? string.Empty).Trim();
            }

            throw new ArgumentException($"Unknown baseline hive '{hive}'.", nameof(hive));
        }

        /// <summary>
        /// Converts an entry's expected value to the CLR value the write path needs
        /// for its RegistryValueKind. Pure.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Unsupported value kind</exception>
        internal static object ToWriteValue(WindowsImageSecurityBaselineEntry entry)
        {
            switch (entry.ValueType)
            {
                case RegistryValueKind.DWord:
                    return Convert.ToUInt32(entry.ExpectedValue, CultureInfo.InvariantCulture);
                case RegistryValueKind.QWord:
                    return Convert.ToUInt64(entry.ExpectedValue, CultureInfo.InvariantCulture);
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    return entry.ExpectedValue.Trim();
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(entry),
                        entry.ValueType,
                        "The security baseline only supports DWord, QWord, String and ExpandString values.");
            }
        }

        /// <summary>
        /// Builds one Modify RegistryOperation per entry, shaped for
        /// <see cref="NativeRegistryService.ApplyRegistryOperations"/>. Pure.
        /// </summary>
        internal static List<RegistryOperation> BuildApplyOperations(
            IReadOnlyList<WindowsImageSecurityBaselineEntry> entries)
        {
            var operations = new List<RegistryOperation>();

            if (entries == null)
            {
                return operations;
            }

            foreach (var entry in entries)
            {
                operations.Add(new RegistryOperation
                {
                    Operation = RegistryOperationType.Modify,
                    Hive = MapOperationHive(entry.Hive),
                    Key = MapOperationKey(entry.Hive, entry.KeyPath),
                    ValueName = entry.ValueName,
                    Value = ToWriteValue(entry),
                    ValueType = entry.ValueType
                });
            }

            return operations;
        }

        /// <summary>
        /// Describes the apply action for ShouldProcess. Pure.
        /// </summary>
        internal static string DescribeApplyAction(int pendingCount, int alreadyCount, string imageName)
        {
            return $"Apply {pendingCount} security baseline entr{(pendingCount == 1 ? "y" : "ies")} ({pendingCount} to write, {alreadyCount} already compliant) to {imageName}";
        }

        /// <summary>
        /// Describes the apply target for ShouldProcess. Pure.
        /// </summary>
        internal static string DescribeApplyTarget(string imageName, string mountPath)
        {
            return $"security baseline on {imageName} ({mountPath})";
        }

        /// <summary>
        /// Builds apply-result rows for two entry groups (e.g. written vs already
        /// compliant, or pending vs skipped). Pure.
        /// </summary>
        internal static List<WindowsImageSecurityBaselineApplyEntry> BuildApplyRows(
            string imageName,
            IReadOnlyList<WindowsImageSecurityBaselineEntry> primary,
            WindowsImageBaselineApplyState primaryState,
            string? primaryDetail,
            IReadOnlyList<WindowsImageSecurityBaselineEntry> secondary,
            WindowsImageBaselineApplyState secondaryState,
            string? secondaryDetail)
        {
            var rows = new List<WindowsImageSecurityBaselineApplyEntry>();

            AppendApplyRows(imageName, primary, primaryState, primaryDetail, rows);
            AppendApplyRows(imageName, secondary, secondaryState, secondaryDetail, rows);
            return rows;
        }

        /// <summary>
        /// Appends one apply-result row per entry. Pure.
        /// </summary>
        private static void AppendApplyRows(
            string imageName,
            IReadOnlyList<WindowsImageSecurityBaselineEntry>? entries,
            WindowsImageBaselineApplyState state,
            string? detail,
            List<WindowsImageSecurityBaselineApplyEntry> rows)
        {
            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                rows.Add(new WindowsImageSecurityBaselineApplyEntry
                {
                    ImageName = imageName ?? string.Empty,
                    Hive = entry.Hive,
                    KeyPath = entry.KeyPath,
                    ValueName = entry.ValueName,
                    ExpectedValue = entry.ExpectedValue,
                    State = state,
                    Detail = detail ?? string.Empty
                });
            }
        }

        /// <summary>
        /// Builds the per-image apply result. Pure.
        /// </summary>
        internal static WindowsImageSecurityBaselineApplyResult BuildApplyResult(
            string imageName,
            string mountPath,
            List<WindowsImageSecurityBaselineApplyEntry> rows,
            bool success,
            string? errorMessage)
        {
            return new WindowsImageSecurityBaselineApplyResult
            {
                ImageName = imageName ?? string.Empty,
                MountPath = mountPath ?? string.Empty,
                Results = rows ?? new List<WindowsImageSecurityBaselineApplyEntry>(),
                Success = success,
                ErrorMessage = errorMessage
            };
        }
    }
}
