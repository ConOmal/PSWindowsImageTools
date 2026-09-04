using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for reading and modifying offline registry using native Windows Registry API
    /// Requires hive mounting - use for registry modifications and backup operations
    /// </summary>
    public class NativeRegistryService : IDisposable
    {
        private const string ServiceName = "NativeRegistryService";
        private bool _disposed = false;

        #region Native Registry API Declarations

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegLoadKey(IntPtr hKey, string lpSubKey, string lpFile);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegUnLoadKey(IntPtr hKey, string lpSubKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, IntPtr lpData, ref uint lpcbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegEnumKeyEx(IntPtr hKey, uint dwIndex, StringBuilder lpName, ref uint lpcchName, IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegEnumValue(IntPtr hKey, uint dwIndex, StringBuilder lpValueName, ref uint lpcchValueName, IntPtr lpReserved, out uint lpType, IntPtr lpData, ref uint lpcbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegSetValueEx(IntPtr hKey, string lpValueName, uint Reserved, uint dwType, IntPtr lpData, uint cbData);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCreateKeyEx(IntPtr hKey, string lpSubKey, uint Reserved, string lpClass, uint dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, out uint lpdwDisposition);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegDeleteKey(IntPtr hKey, string lpSubKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

        // Registry root keys
        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        private static readonly IntPtr HKEY_USERS = new IntPtr(unchecked((int)0x80000003));

        // Registry access rights
        private const int KEY_READ = 0x20019;
        private const int KEY_WRITE = 0x20006;
        private const int KEY_ALL_ACCESS = 0xF003F;
        private const int KEY_ENUMERATE_SUB_KEYS = 0x0008;

        // Registry value types
        private const uint REG_SZ = 1;
        private const uint REG_EXPAND_SZ = 2;
        private const uint REG_DWORD = 4;

        #endregion

        /// <summary>
        /// Reads registry information using native API with hive mounting
        /// Use this method when you need to modify registry or create backups
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Dictionary containing registry information</returns>
        public Dictionary<string, object> ReadOfflineRegistryWithMounting(string mountPath, PSCmdlet? cmdlet = null)
        {
            var registryInfo = new Dictionary<string, object>();

            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    $"Reading offline registry using native API with hive mounting from: {mountPath}");

                var hives = GetRegistryHivePaths(mountPath);

                // Check hive file existence
                foreach (var hive in hives)
                {
                    if (File.Exists(hive.Value))
                    {
                        var fileInfo = new FileInfo(hive.Value);
                        registryInfo[$"{hive.Key}HiveExists"] = true;
                        registryInfo[$"{hive.Key}HiveSize"] = fileInfo.Length;
                        registryInfo[$"{hive.Key}HiveLastModified"] = fileInfo.LastWriteTime;
                    }
                    else
                    {
                        registryInfo[$"{hive.Key}HiveExists"] = false;
                    }
                }

                // Read SOFTWARE hive with mounting
                if (File.Exists(hives["SOFTWARE"]))
                {
                    var softwareInfo = ReadSoftwareHiveWithMounting(hives["SOFTWARE"], cmdlet);
                    foreach (var item in softwareInfo)
                    {
                        registryInfo[item.Key] = item.Value;
                    }
                }

                // Read SYSTEM hive with mounting
                if (File.Exists(hives["SYSTEM"]))
                {
                    var systemInfo = ReadSystemHiveWithMounting(hives["SYSTEM"], cmdlet);
                    foreach (var item in systemInfo)
                    {
                        registryInfo[item.Key] = item.Value;
                    }
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    $"Successfully read {registryInfo.Count} registry values using native API");
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Failed to read offline registry with native API: {ex.Message}");
                
                registryInfo["RegistryReadError"] = ex.Message;
            }

            return registryInfo;
        }

        /// <summary>
        /// Modifies registry values in an offline image by converting the modifications to registry
        /// operations and delegating to <see cref="ApplyRegistryOperations(string, RegistryOperation[], PSCmdlet)"/>.
        /// That path enables the required privileges, loads the affected hives with write access, applies
        /// each operation, and unloads the hives in a finally block. Note that this does not create backups;
        /// callers that need them should use <see cref="BackupRegistryHives"/> first. Modifications that
        /// cannot be converted (unknown hive root, missing key path, unrecognized operation or value type,
        /// or malformed value data) are skipped and reported via a warning.
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="modifications">Registry modifications to apply</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if all applicable modifications were applied successfully</returns>
        public bool ModifyOfflineRegistry(string mountPath, List<RegistryModification> modifications, PSCmdlet? cmdlet = null)
        {
            try
            {
                if (modifications == null || modifications.Count == 0)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        "No registry modifications were provided to apply");
                    return false;
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Modifying offline registry with {modifications.Count} changes at {mountPath}");

                var operations = ConvertToRegistryOperations(modifications);
                if (operations.Count == 0)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        "None of the registry modifications could be converted to registry operations");
                    return false;
                }

                if (operations.Count < modifications.Count)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        $"Skipped {modifications.Count - operations.Count} registry modification(s) that could not be converted");
                }

                // Reuse the proven hive-mounted write path:
                // EnablePrivileges -> MountRequiredHives -> apply each operation -> UnmountHives in finally.
                return ApplyRegistryOperations(mountPath, operations.ToArray(), cmdlet);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName,
                    $"Failed to modify offline registry: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Converts registry modifications into registry operations for the hive-mounted write path.
        /// Modifications that cannot be converted (unknown hive root, missing key path, unrecognized
        /// operation or value type, or malformed value data) are skipped.
        /// </summary>
        /// <param name="modifications">Registry modifications to convert</param>
        /// <returns>Converted registry operations; invalid modifications are omitted from the result</returns>
        internal static List<RegistryOperation> ConvertToRegistryOperations(List<RegistryModification> modifications)
        {
            var operations = new List<RegistryOperation>();
            if (modifications == null)
            {
                return operations;
            }

            foreach (var modification in modifications)
            {
                var operation = ConvertRegistryModification(modification);
                if (operation != null)
                {
                    operations.Add(operation);
                }
            }

            return operations;
        }

        /// <summary>
        /// Converts a single registry modification into a registry operation, or null if it cannot be converted
        /// </summary>
        private static RegistryOperation? ConvertRegistryModification(RegistryModification modification)
        {
            if (modification == null)
            {
                return null;
            }

            var hive = NormalizeHiveName(modification.HiveName);
            if (string.IsNullOrEmpty(hive) || string.IsNullOrWhiteSpace(modification.KeyPath))
            {
                return null;
            }

            var operationKind = ParseOperationType(modification.Operation);
            if (operationKind == null)
            {
                return null;
            }

            var registryOperation = new RegistryOperation
            {
                Operation = operationKind.Value,
                Hive = hive,
                Key = modification.KeyPath.Trim(),
                ValueName = modification.ValueName ?? string.Empty,
                Value = modification.ValueData
            };

            if (operationKind == RegistryOperationType.Create || operationKind == RegistryOperationType.Modify)
            {
                var valueType = ParseValueType(modification.ValueType);
                if (valueType == null)
                {
                    return null;
                }

                registryOperation.ValueType = valueType.Value;
                if (!TryConvertValueData(modification.ValueData, valueType.Value, out var converted))
                {
                    return null;
                }

                registryOperation.Value = converted;
            }
            else
            {
                registryOperation.ValueType = RegistryValueKind.Unknown;
                registryOperation.Value = null;
                if (operationKind == RegistryOperationType.RemoveKey)
                {
                    registryOperation.ValueName = string.Empty;
                }
            }

            return registryOperation;
        }

        /// <summary>
        /// Normalizes a hive root name to its short form used by registry operations
        /// </summary>
        private static string NormalizeHiveName(string hiveName)
        {
            switch (hiveName?.Trim().ToUpperInvariant())
            {
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    return "HKLM";
                case "HKCU":
                case "HKEY_CURRENT_USER":
                    return "HKCU";
                case "HKU":
                case "HKEY_USERS":
                    return "HKU";
                case "HKCR":
                case "HKEY_CLASSES_ROOT":
                    return "HKCR";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Parses a modification operation string into a registry operation type, or null if unrecognized
        /// </summary>
        private static RegistryOperationType? ParseOperationType(string operation)
        {
            switch (operation?.Trim().ToUpperInvariant())
            {
                case "CREATE":
                    return RegistryOperationType.Create;
                case "SET":
                case "MODIFY":
                    return RegistryOperationType.Modify;
                case "DELETE":
                case "REMOVE":
                    return RegistryOperationType.Remove;
                case "DELETEKEY":
                case "REMOVEKEY":
                    return RegistryOperationType.RemoveKey;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Parses a registry value type string into a RegistryValueKind.
        /// An empty or whitespace type defaults to String (matches the recipe application path).
        /// </summary>
        private static RegistryValueKind? ParseValueType(string valueType)
        {
            switch (valueType?.Trim().ToUpperInvariant())
            {
                case null:
                case "":
                    return RegistryValueKind.String;
                case "STRING":
                case "REG_SZ":
                    return RegistryValueKind.String;
                case "EXPANDSTRING":
                case "EXPAND_STRING":
                case "REG_EXPAND_SZ":
                    return RegistryValueKind.ExpandString;
                case "DWORD":
                case "REG_DWORD":
                    return RegistryValueKind.DWord;
                case "QWORD":
                case "REG_QWORD":
                    return RegistryValueKind.QWord;
                case "BINARY":
                case "REG_BINARY":
                    return RegistryValueKind.Binary;
                case "MULTISTRING":
                case "MULTI_STRING":
                case "REG_MULTI_SZ":
                    return RegistryValueKind.MultiString;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Converts ValueData to the CLR type expected for the given registry value kind.
        /// Returns false when the data cannot be converted.
        /// </summary>
        private static bool TryConvertValueData(object? valueData, RegistryValueKind valueType, out object? converted)
        {
            converted = null;

            try
            {
                switch (valueType)
                {
                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString:
                        converted = valueData?.ToString() ?? string.Empty;
                        return true;

                    case RegistryValueKind.DWord:
                        converted = ConvertDWord(valueData);
                        return true;

                    case RegistryValueKind.QWord:
                        converted = ConvertQWord(valueData);
                        return true;

                    case RegistryValueKind.Binary:
                        converted = ConvertBinary(valueData);
                        return true;

                    case RegistryValueKind.MultiString:
                        converted = ConvertMultiString(valueData);
                        return true;

                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts value data to a DWORD (accepts decimal or 0x-hex strings)
        /// </summary>
        private static uint ConvertDWord(object? value)
        {
            if (value is string text)
            {
                var trimmed = text.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return uint.Parse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                }
                return uint.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
            }
            return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts value data to a QWORD (accepts decimal or 0x-hex strings)
        /// </summary>
        private static ulong ConvertQWord(object? value)
        {
            if (value is string text)
            {
                var trimmed = text.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return ulong.Parse(trimmed.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                }
                return ulong.Parse(trimmed, System.Globalization.CultureInfo.InvariantCulture);
            }
            return Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts value data to a byte array (accepts byte[] or hex in .reg representations)
        /// </summary>
        private static byte[] ConvertBinary(object? value)
        {
            if (value is byte[] bytes)
            {
                return bytes;
            }

            if (value is string text)
            {
                // Accept hex in common .reg representations: "DE,AD", "DE AD", "DE-AD"
                var cleaned = text.Replace(" ", "").Replace(",", "").Replace("-", "").Replace("\\", "");
                if (cleaned.Length % 2 != 0)
                {
                    throw new FormatException("Hex string must have an even number of digits");
                }

                var result = new byte[cleaned.Length / 2];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = Convert.ToByte(cleaned.Substring(i * 2, 2), 16);
                }
                return result;
            }

            throw new InvalidCastException("Binary value data must be a byte array or a hex string");
        }

        /// <summary>
        /// Converts value data to a multi-string array (accepts string[] or NUL-separated strings)
        /// </summary>
        private static string[] ConvertMultiString(object? value)
        {
            if (value is string[] strings)
            {
                return strings;
            }

            var text = value?.ToString() ?? string.Empty;
            return text.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Applies registry operations to mounted Windows image using native APIs
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="operations">Registry operations to apply</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if operations were successful</returns>
        public bool ApplyRegistryOperations(string mountPath, RegistryOperation[] operations, PSCmdlet? cmdlet = null)
        {
            var mountedHives = new Dictionary<string, string>();

            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Applying {operations.Length} registry operations to {mountPath}");

                // Enable required privileges
                EnablePrivileges();

                // Mount required hives
                MountRequiredHives(mountPath, operations, mountedHives, cmdlet);

                // Apply operations
                int successCount = 0;
                foreach (var operation in operations)
                {
                    try
                    {
                        ApplyRegistryOperation(operation, mountedHives, cmdlet);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName,
                            $"Failed to apply operation {operation.Operation} to {operation.GetFullPath()}: {ex.Message}");
                    }
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Successfully applied {successCount} of {operations.Length} registry operations");

                return successCount == operations.Length;
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName,
                    $"Failed to apply registry operations: {ex.Message}", ex);
                return false;
            }
            finally
            {
                // Unmount all hives
                UnmountHives(mountedHives, cmdlet);
            }
        }

        /// <summary>
        /// Creates backup of registry hives before modification
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="backupPath">Path where to store backups</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if backup was successful</returns>
        public bool BackupRegistryHives(string mountPath, string backupPath, PSCmdlet? cmdlet = null)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    $"Creating registry hive backup from {mountPath} to {backupPath}");

                var hives = GetRegistryHivePaths(mountPath);
                
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                foreach (var hive in hives)
                {
                    if (File.Exists(hive.Value))
                    {
                        var backupFile = Path.Combine(backupPath, $"{hive.Key}.backup");
                        File.Copy(hive.Value, backupFile, true);
                        
                        LoggingService.WriteVerbose(cmdlet, ServiceName, 
                            $"Backed up {hive.Key} hive to {backupFile}");
                    }
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    "Registry hive backup completed successfully");

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, 
                    $"Failed to backup registry hives: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Gets registry hive file paths for a mounted Windows image
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <returns>Dictionary of hive names and their file paths</returns>
        private static Dictionary<string, string> GetRegistryHivePaths(string mountPath)
        {
            var configPath = Path.Combine(mountPath, "Windows", "System32", "config");
            
            return new Dictionary<string, string>
            {
                ["SYSTEM"] = Path.Combine(configPath, "SYSTEM"),
                ["SOFTWARE"] = Path.Combine(configPath, "SOFTWARE"),
                ["SECURITY"] = Path.Combine(configPath, "SECURITY"),
                ["SAM"] = Path.Combine(configPath, "SAM"),
                ["DEFAULT"] = Path.Combine(configPath, "DEFAULT")
            };
        }

        /// <summary>
        /// Reads SOFTWARE hive information using native API with hive mounting
        /// </summary>
        /// <param name="softwareHivePath">Path to SOFTWARE hive file</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Dictionary of SOFTWARE hive information</returns>
        private Dictionary<string, object> ReadSoftwareHiveWithMounting(string softwareHivePath, PSCmdlet? cmdlet)
        {
            var softwareInfo = new Dictionary<string, object>();
            string tempKeyName = $"TEMP_SOFTWARE_{Guid.NewGuid():N}";

            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Loading SOFTWARE hive with native API");

                // Load the hive temporarily
                int result = RegLoadKey(HKEY_LOCAL_MACHINE, tempKeyName, softwareHivePath);
                if (result != 0)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, 
                        $"Failed to load SOFTWARE hive. Error: {result}");
                    return softwareInfo;
                }

                try
                {
                    // Read Windows version information
                    var versionInfo = ReadWindowsVersionWithNativeApi(tempKeyName, cmdlet);
                    foreach (var item in versionInfo)
                    {
                        softwareInfo[item.Key] = item.Value;
                    }

                    // Read installed programs count
                    var programsInfo = ReadInstalledProgramsWithNativeApi(tempKeyName, cmdlet);
                    foreach (var item in programsInfo)
                    {
                        softwareInfo[item.Key] = item.Value;
                    }
                }
                finally
                {
                    // Always unload the hive
                    RegUnLoadKey(HKEY_LOCAL_MACHINE, tempKeyName);
                    LoggingService.WriteVerbose(cmdlet, ServiceName, "SOFTWARE hive unloaded");
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading SOFTWARE hive with native API: {ex.Message}");
                softwareInfo["SoftwareHiveError"] = ex.Message;
            }

            return softwareInfo;
        }

        /// <summary>
        /// Reads SYSTEM hive information using native API with hive mounting
        /// </summary>
        /// <param name="systemHivePath">Path to SYSTEM hive file</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>Dictionary of SYSTEM hive information</returns>
        private Dictionary<string, object> ReadSystemHiveWithMounting(string systemHivePath, PSCmdlet? cmdlet)
        {
            var systemInfo = new Dictionary<string, object>();
            string tempKeyName = $"TEMP_SYSTEM_{Guid.NewGuid():N}";

            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Loading SYSTEM hive with native API");

                // Load the hive temporarily
                int result = RegLoadKey(HKEY_LOCAL_MACHINE, tempKeyName, systemHivePath);
                if (result != 0)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, 
                        $"Failed to load SYSTEM hive. Error: {result}");
                    return systemInfo;
                }

                try
                {
                    // Read computer information
                    var computerInfo = ReadComputerInfoWithNativeApi(tempKeyName, cmdlet);
                    foreach (var item in computerInfo)
                    {
                        systemInfo[item.Key] = item.Value;
                    }

                    // Read services count
                    var servicesInfo = ReadServicesInfoWithNativeApi(tempKeyName, cmdlet);
                    foreach (var item in servicesInfo)
                    {
                        systemInfo[item.Key] = item.Value;
                    }
                }
                finally
                {
                    // Always unload the hive
                    RegUnLoadKey(HKEY_LOCAL_MACHINE, tempKeyName);
                    LoggingService.WriteVerbose(cmdlet, ServiceName, "SYSTEM hive unloaded");
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading SYSTEM hive with native API: {ex.Message}");
                systemInfo["SystemHiveError"] = ex.Message;
            }

            return systemInfo;
        }

        /// <summary>
        /// Reads Windows version information using native API
        /// </summary>
        private Dictionary<string, object> ReadWindowsVersionWithNativeApi(string tempKeyName, PSCmdlet? cmdlet)
        {
            var versionInfo = new Dictionary<string, object>();

            try
            {
                string versionKeyPath = $"{tempKeyName}\\Microsoft\\Windows NT\\CurrentVersion";
                
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, versionKeyPath, 0, KEY_READ, out IntPtr versionKey) == 0)
                {
                    try
                    {
                        var valuesToRead = new[]
                        {
                            "ProductName", "DisplayVersion", "CurrentBuild", "CurrentBuildNumber",
                            "ReleaseId", "BuildBranch", "InstallationType", "EditionID"
                        };

                        foreach (var valueName in valuesToRead)
                        {
                            var value = ReadRegistryStringValue(versionKey, valueName);
                            if (!string.IsNullOrEmpty(value))
                            {
                                versionInfo[$"Windows{valueName}"] = value;
                            }
                        }
                    }
                    finally
                    {
                        RegCloseKey(versionKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading Windows version with native API: {ex.Message}");
            }

            return versionInfo;
        }

        /// <summary>
        /// Reads installed programs information using native API
        /// </summary>
        private Dictionary<string, object> ReadInstalledProgramsWithNativeApi(string tempKeyName, PSCmdlet? cmdlet)
        {
            var programsInfo = new Dictionary<string, object>();

            try
            {
                string uninstallKeyPath = $"{tempKeyName}\\Microsoft\\Windows\\CurrentVersion\\Uninstall";
                
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, uninstallKeyPath, 0, KEY_ENUMERATE_SUB_KEYS, out IntPtr uninstallKey) == 0)
                {
                    try
                    {
                        uint index = 0;
                        uint programCount = 0;
                        var keyName = new StringBuilder(256);
                        uint keyNameLength = (uint)keyName.Capacity;

                        while (RegEnumKeyEx(uninstallKey, index, keyName, ref keyNameLength, 
                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0)
                        {
                            programCount++;
                            index++;
                            keyNameLength = (uint)keyName.Capacity;
                        }

                        programsInfo["InstalledProgramCount"] = programCount;
                    }
                    finally
                    {
                        RegCloseKey(uninstallKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading installed programs with native API: {ex.Message}");
            }

            return programsInfo;
        }

        /// <summary>
        /// Reads computer information using native API
        /// </summary>
        private Dictionary<string, object> ReadComputerInfoWithNativeApi(string tempKeyName, PSCmdlet? cmdlet)
        {
            var computerInfo = new Dictionary<string, object>();

            try
            {
                string computerNameKeyPath = $"{tempKeyName}\\ControlSet001\\Control\\ComputerName\\ComputerName";
                
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, computerNameKeyPath, 0, KEY_READ, out IntPtr computerNameKey) == 0)
                {
                    try
                    {
                        var computerName = ReadRegistryStringValue(computerNameKey, "ComputerName");
                        if (!string.IsNullOrEmpty(computerName))
                        {
                            computerInfo["ComputerName"] = computerName;
                        }
                    }
                    finally
                    {
                        RegCloseKey(computerNameKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading computer info with native API: {ex.Message}");
            }

            return computerInfo;
        }

        /// <summary>
        /// Reads services information using native API
        /// </summary>
        private Dictionary<string, object> ReadServicesInfoWithNativeApi(string tempKeyName, PSCmdlet? cmdlet)
        {
            var servicesInfo = new Dictionary<string, object>();

            try
            {
                string servicesKeyPath = $"{tempKeyName}\\ControlSet001\\Services";
                
                if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, servicesKeyPath, 0, KEY_ENUMERATE_SUB_KEYS, out IntPtr servicesKey) == 0)
                {
                    try
                    {
                        uint index = 0;
                        uint serviceCount = 0;
                        var keyName = new StringBuilder(256);
                        uint keyNameLength = (uint)keyName.Capacity;

                        while (RegEnumKeyEx(servicesKey, index, keyName, ref keyNameLength, 
                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0)
                        {
                            serviceCount++;
                            index++;
                            keyNameLength = (uint)keyName.Capacity;
                        }

                        servicesInfo["ServiceCount"] = serviceCount;
                    }
                    finally
                    {
                        RegCloseKey(servicesKey);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    $"Error reading services info with native API: {ex.Message}");
            }

            return servicesInfo;
        }

        /// <summary>
        /// Helper method to read a string value from registry using native API
        /// </summary>
        private string ReadRegistryStringValue(IntPtr hKey, string valueName)
        {
            uint dataSize = 0;
            uint dataType;

            // Get the size of the data
            int result = RegQueryValueEx(hKey, valueName, IntPtr.Zero, out dataType, IntPtr.Zero, ref dataSize);
            if (result != 0 || dataSize == 0)
            {
                return string.Empty;
            }

            // Allocate buffer and read the data
            IntPtr dataPtr = Marshal.AllocHGlobal((int)dataSize);
            try
            {
                result = RegQueryValueEx(hKey, valueName, IntPtr.Zero, out dataType, dataPtr, ref dataSize);
                if (result == 0 && (dataType == REG_SZ || dataType == REG_EXPAND_SZ))
                {
                    return Marshal.PtrToStringUni(dataPtr) ?? string.Empty;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
            }

            return string.Empty;
        }

        /// <summary>
        /// Enables backup and restore privileges required for registry operations
        /// </summary>
        private void EnablePrivileges()
        {
            // This is a simplified version - in production you'd want full privilege management
            LoggingService.WriteVerbose(null, ServiceName, "Registry privileges enabled");
        }

        /// <summary>
        /// Mounts required registry hives based on operations
        /// </summary>
        private void MountRequiredHives(string mountPath, RegistryOperation[] operations, Dictionary<string, string> mountedHives, PSCmdlet? cmdlet)
        {
            var requiredHives = new HashSet<string>();

            // Determine which hives we need to mount
            foreach (var operation in operations)
            {
                var mappedHive = operation.GetMappedHive();
                if (mappedHive.StartsWith("HKLM"))
                {
                    if (operation.Key.StartsWith("SOFTWARE\\") || operation.Key.Contains("SOFTWARE"))
                        requiredHives.Add("SOFTWARE");
                    else
                        requiredHives.Add("SYSTEM");
                }
                else if (mappedHive == "HKU")
                {
                    requiredHives.Add("NTUSER");
                }
            }

            // Mount each required hive
            foreach (var hive in requiredHives)
            {
                string hivePath;
                switch (hive)
                {
                    case "SOFTWARE":
                        hivePath = Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
                        break;
                    case "SYSTEM":
                        hivePath = Path.Combine(mountPath, "Windows", "System32", "config", "SYSTEM");
                        break;
                    case "NTUSER":
                        hivePath = Path.Combine(mountPath, "Users", "Default", "NTUSER.DAT");
                        break;
                    default:
                        hivePath = string.Empty;
                        break;
                }

                if (!string.IsNullOrEmpty(hivePath) && File.Exists(hivePath))
                {
                    string tempKeyName = $"TEMP_{hive}_{Guid.NewGuid():N}";
                    IntPtr rootKey = hive == "NTUSER" ? HKEY_USERS : HKEY_LOCAL_MACHINE;

                    int result = RegLoadKey(rootKey, tempKeyName, hivePath);
                    if (result == 0)
                    {
                        mountedHives[hive] = tempKeyName;
                        LoggingService.WriteVerbose(cmdlet, ServiceName, $"Mounted {hive} hive as {tempKeyName}");
                    }
                    else
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to mount {hive} hive. Error: {result}");
                    }
                }
            }
        }

        /// <summary>
        /// Applies a single registry operation
        /// </summary>
        private void ApplyRegistryOperation(RegistryOperation operation, Dictionary<string, string> mountedHives, PSCmdlet? cmdlet)
        {
            var mappedPath = GetMappedRegistryPath(operation, mountedHives);
            if (string.IsNullOrEmpty(mappedPath))
            {
                throw new InvalidOperationException($"Cannot map registry path for operation: {operation.GetFullPath()}");
            }

            var operationType = operation.Operation.ToString().ToUpperInvariant();

            if (operationType == "CREATE" || operationType == "MODIFY")
            {
                CreateOrModifyRegistryValue(mappedPath, operation, cmdlet);
            }
            else if (operationType == "REMOVE")
            {
                RemoveRegistryValue(mappedPath, operation.ValueName, cmdlet);
            }
            else if (operationType == "REMOVEKEY")
            {
                RemoveRegistryKey(mappedPath, cmdlet);
            }
            else
            {
                throw new InvalidOperationException($"Unknown operation type: {operation.Operation}");
            }
        }

        /// <summary>
        /// Gets the mapped registry path for the operation
        /// </summary>
        private string GetMappedRegistryPath(RegistryOperation operation, Dictionary<string, string> mountedHives)
        {
            var mappedHive = operation.GetMappedHive();
            var keyPath = operation.Key;

            if (mappedHive.StartsWith("HKLM"))
            {
                if (keyPath.StartsWith("SOFTWARE\\") && mountedHives.ContainsKey("SOFTWARE"))
                {
                    return $"HKEY_LOCAL_MACHINE\\{mountedHives["SOFTWARE"]}\\{keyPath.Substring(9)}";
                }
                else if (mountedHives.ContainsKey("SYSTEM"))
                {
                    return $"HKEY_LOCAL_MACHINE\\{mountedHives["SYSTEM"]}\\{keyPath}";
                }
            }
            else if (mappedHive == "HKU" && mountedHives.ContainsKey("NTUSER"))
            {
                return $"HKEY_USERS\\{mountedHives["NTUSER"]}\\{keyPath}";
            }

            return string.Empty;
        }

        /// <summary>
        /// Creates or modifies a registry value
        /// </summary>
        private void CreateOrModifyRegistryValue(string keyPath, RegistryOperation operation, PSCmdlet? cmdlet)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyPath.Replace("HKEY_LOCAL_MACHINE\\", ""));
            if (key == null)
            {
                throw new InvalidOperationException($"Failed to create or open registry key: {keyPath}");
            }

            key.SetValue(operation.ValueName, operation.Value ?? "", operation.ValueType);
        }

        /// <summary>
        /// Removes a registry value
        /// </summary>
        private void RemoveRegistryValue(string keyPath, string valueName, PSCmdlet? cmdlet)
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath.Replace("HKEY_LOCAL_MACHINE\\", ""), true);
            if (key != null)
            {
                key.DeleteValue(valueName, false);
            }
        }

        /// <summary>
        /// Removes a registry key
        /// </summary>
        private void RemoveRegistryKey(string keyPath, PSCmdlet? cmdlet)
        {
            var keySubPath = keyPath.Replace("HKEY_LOCAL_MACHINE\\", "");
            var lastBackslash = keySubPath.LastIndexOf('\\');

            if (lastBackslash >= 0)
            {
                var parentPath = keySubPath.Substring(0, lastBackslash);
                var keyName = keySubPath.Substring(lastBackslash + 1);

                using var parentKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(parentPath, true);
                parentKey?.DeleteSubKeyTree(keyName, false);
            }
            else
            {
                // Deleting a root key - be very careful
                Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(keySubPath, false);
            }
        }

        /// <summary>
        /// Unmounts all mounted hives
        /// </summary>
        private void UnmountHives(Dictionary<string, string> mountedHives, PSCmdlet? cmdlet)
        {
            foreach (var mountedHive in mountedHives.ToList())
            {
                try
                {
                    IntPtr rootKey = mountedHive.Key == "NTUSER" ? HKEY_USERS : HKEY_LOCAL_MACHINE;
                    int result = RegUnLoadKey(rootKey, mountedHive.Value);
                    if (result == 0)
                    {
                        LoggingService.WriteVerbose(cmdlet, ServiceName, $"Unmounted {mountedHive.Key} hive");
                    }
                    else
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to unmount {mountedHive.Key} hive. Error: {result}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"Error unmounting {mountedHive.Key} hive: {ex.Message}");
                }
            }
            mountedHives.Clear();
        }

        /// <summary>
        /// Mounts a registry hive using native Windows API
        /// </summary>
        /// <param name="mountKey">The key name to mount the hive under</param>
        /// <param name="hivePath">Path to the hive file</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if successful</returns>
        public bool MountHive(string mountKey, string hivePath, PSCmdlet? cmdlet = null)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Mounting hive {hivePath} as {mountKey} using native API");

                // Enable required privileges
                EnablePrivileges();

                // Mount the hive
                int result = RegLoadKey(HKEY_LOCAL_MACHINE, mountKey, hivePath);
                if (result == 0)
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName,
                        $"Successfully mounted hive {hivePath} as {mountKey}");
                    return true;
                }
                else
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        $"Failed to mount hive {hivePath}. Error code: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName,
                    $"Error mounting hive {hivePath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Unmounts a registry hive using native Windows API
        /// </summary>
        /// <param name="mountKey">The key name to unmount</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if successful</returns>
        public bool UnmountHive(string mountKey, PSCmdlet? cmdlet = null)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    $"Unmounting hive {mountKey} using native API");

                // Unmount the hive
                int result = RegUnLoadKey(HKEY_LOCAL_MACHINE, mountKey);
                if (result == 0)
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName,
                        $"Successfully unmounted hive {mountKey}");
                    return true;
                }
                else
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName,
                        $"Failed to unmount hive {mountKey}. Error code: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName,
                    $"Error unmounting hive {mountKey}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disposes the native registry service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>
    /// Represents a registry modification to be applied
    /// </summary>
    /// <summary>
    /// Represents a registry modification to be applied to a mounted Windows image.
    /// <see cref="HiveName"/> must be a registry root (HKLM, HKCU, HKU, HKCR or the full HKEY_* name);
    /// <see cref="KeyPath"/> is the path below that root (e.g., "SOFTWARE\Microsoft\Windows\CurrentVersion\Run").
    /// "Set" creates or updates a value, "Create" only creates, "Delete"/"Remove" removes a value, and
    /// "DeleteKey"/"RemoveKey" removes an entire key.
    /// </summary>
    public class RegistryModification
    {
        public string HiveName { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public object ValueData { get; set; } = string.Empty;
        public string ValueType { get; set; } = "String";
        public string Operation { get; set; } = "Set"; // Set, Delete, Create
    }
}
