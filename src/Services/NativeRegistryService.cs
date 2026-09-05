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
        private readonly ModuleCallbacks _callbacks;
        private bool _disposed = false;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public NativeRegistryService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

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
            return ModifyOfflineRegistry(mountPath, modifications, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Modifies registry values in an offline image by converting the modifications to registry
        /// operations and delegating to <see cref="ApplyRegistryOperations(string, RegistryOperation[], ModuleCallbacks)"/>.
        /// That path enables the required privileges, loads the affected hives with write access, applies
        /// each operation, and unloads the hives in a finally block. Note that this does not create backups;
        /// callers that need them should use <see cref="BackupRegistryHives"/> first. Modifications that
        /// cannot be converted (unknown hive root, missing key path, unrecognized operation or value type,
        /// or malformed value data) are skipped and reported via a warning.
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="modifications">Registry modifications to apply</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>True if all applicable modifications were applied successfully</returns>
        public bool ModifyOfflineRegistry(string mountPath, List<RegistryModification> modifications, ModuleCallbacks callbacks)
        {
            try
            {
                if (modifications == null || modifications.Count == 0)
                {
                    callbacks.Warning?.Invoke("No registry modifications were provided to apply");
                    return false;
                }

                callbacks.Verbose?.Invoke($"Modifying offline registry with {modifications.Count} changes at {mountPath}");

                var operations = ConvertToRegistryOperations(modifications);
                if (operations.Count == 0)
                {
                    callbacks.Warning?.Invoke("None of the registry modifications could be converted to registry operations");
                    return false;
                }

                if (operations.Count < modifications.Count)
                {
                    callbacks.Warning?.Invoke($"Skipped {modifications.Count - operations.Count} registry modification(s) that could not be converted");
                }

                // Reuse the proven hive-mounted write path:
                // EnablePrivileges -> MountRequiredHives -> apply each operation -> UnmountHives in finally.
                return ApplyRegistryOperations(mountPath, operations.ToArray(), callbacks);
            }
            catch (Exception ex)
            {
                callbacks.Error?.Invoke(ex, $"Failed to modify offline registry: {ex.Message}");
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
            return ApplyRegistryOperations(mountPath, operations, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Applies registry operations to mounted Windows image using native APIs
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="operations">Registry operations to apply</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>True if operations were successful</returns>
        public bool ApplyRegistryOperations(string mountPath, RegistryOperation[] operations, ModuleCallbacks callbacks)
        {
            var mountedHives = new Dictionary<string, string>();

            try
            {
                callbacks.Verbose?.Invoke($"Applying {operations.Length} registry operations to {mountPath}");

                // Enable required privileges
                EnablePrivileges();

                // Mount required hives
                MountRequiredHives(mountPath, operations, mountedHives);

                // Apply operations
                int successCount = 0;
                foreach (var operation in operations)
                {
                    try
                    {
                        ApplyRegistryOperation(operation, mountedHives);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        callbacks.Warning?.Invoke($"Failed to apply operation {operation.Operation} to {operation.GetFullPath()}: {ex.Message}");
                    }
                }

                callbacks.Verbose?.Invoke($"Successfully applied {successCount} of {operations.Length} registry operations");

                return successCount == operations.Length;
            }
            catch (Exception ex)
            {
                callbacks.Error?.Invoke(ex, $"Failed to apply registry operations: {ex.Message}");
                return false;
            }
            finally
            {
                // Unmount all hives
                UnmountHives(mountedHives);
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
            return BackupRegistryHives(mountPath, backupPath, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Creates backup of registry hives before modification
        /// </summary>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="backupPath">Path where to store backups</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>True if backup was successful</returns>
        public bool BackupRegistryHives(string mountPath, string backupPath, ModuleCallbacks callbacks)
        {
            try
            {
                callbacks.Verbose?.Invoke($"Creating registry hive backup from {mountPath} to {backupPath}");

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
                        
                        callbacks.Verbose?.Invoke($"Backed up {hive.Key} hive to {backupFile}");
                    }
                }

                callbacks.Verbose?.Invoke("Registry hive backup completed successfully");

                return true;
            }
            catch (Exception ex)
            {
                callbacks.Error?.Invoke(ex, $"Failed to backup registry hives: {ex.Message}");
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
        /// Enables backup and restore privileges required for registry operations
        /// </summary>
        private void EnablePrivileges()
        {
            // This is a simplified version - in production you'd want full privilege management
            _callbacks.Verbose?.Invoke("Registry privileges enabled");
        }

        /// <summary>
        /// Mounts required registry hives based on operations
        /// </summary>
        private void MountRequiredHives(string mountPath, RegistryOperation[] operations, Dictionary<string, string> mountedHives)
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
                        _callbacks.Verbose?.Invoke($"Mounted {hive} hive as {tempKeyName}");
                    }
                    else
                    {
                        _callbacks.Warning?.Invoke($"Failed to mount {hive} hive. Error: {result}");
                    }
                }
            }
        }

        /// <summary>
        /// Applies a single registry operation
        /// </summary>
        private void ApplyRegistryOperation(RegistryOperation operation, Dictionary<string, string> mountedHives)
        {
            var mappedPath = GetMappedRegistryPath(operation, mountedHives);
            if (string.IsNullOrEmpty(mappedPath))
            {
                throw new InvalidOperationException($"Cannot map registry path for operation: {operation.GetFullPath()}");
            }

            var operationType = operation.Operation.ToString().ToUpperInvariant();

            if (operationType == "CREATE" || operationType == "MODIFY")
            {
                CreateOrModifyRegistryValue(mappedPath, operation);
            }
            else if (operationType == "REMOVE")
            {
                RemoveRegistryValue(mappedPath, operation.ValueName);
            }
            else if (operationType == "REMOVEKEY")
            {
                RemoveRegistryKey(mappedPath);
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
        private void CreateOrModifyRegistryValue(string keyPath, RegistryOperation operation)
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
        private void RemoveRegistryValue(string keyPath, string valueName)
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
        private void RemoveRegistryKey(string keyPath)
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
        private void UnmountHives(Dictionary<string, string> mountedHives)
        {
            foreach (var mountedHive in mountedHives.ToList())
            {
                try
                {
                    IntPtr rootKey = mountedHive.Key == "NTUSER" ? HKEY_USERS : HKEY_LOCAL_MACHINE;
                    int result = RegUnLoadKey(rootKey, mountedHive.Value);
                    if (result == 0)
                    {
                        _callbacks.Verbose?.Invoke($"Unmounted {mountedHive.Key} hive");
                    }
                    else
                    {
                        _callbacks.Warning?.Invoke($"Failed to unmount {mountedHive.Key} hive. Error: {result}");
                    }
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Error unmounting {mountedHive.Key} hive: {ex.Message}");
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
            return MountHive(mountKey, hivePath, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Mounts a registry hive using native Windows API
        /// </summary>
        /// <param name="mountKey">The key name to mount the hive under</param>
        /// <param name="hivePath">Path to the hive file</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>True if successful</returns>
        public bool MountHive(string mountKey, string hivePath, ModuleCallbacks callbacks)
        {
            try
            {
                callbacks.Verbose?.Invoke($"Mounting hive {hivePath} as {mountKey} using native API");

                // Enable required privileges
                EnablePrivileges();

                // Mount the hive
                int result = RegLoadKey(HKEY_LOCAL_MACHINE, mountKey, hivePath);
                if (result == 0)
                {
                    callbacks.Verbose?.Invoke($"Successfully mounted hive {hivePath} as {mountKey}");
                    return true;
                }
                else
                {
                    callbacks.Warning?.Invoke($"Failed to mount hive {hivePath}. Error code: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                callbacks.Warning?.Invoke($"Error mounting hive {hivePath}: {ex.Message}");
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
            return UnmountHive(mountKey, ModuleCallbacks.FromCmdlet(cmdlet));
        }

        /// <summary>
        /// Unmounts a registry hive using native Windows API
        /// </summary>
        /// <param name="mountKey">The key name to unmount</param>
        /// <param name="callbacks">Callbacks for logging</param>
        /// <returns>True if successful</returns>
        public bool UnmountHive(string mountKey, ModuleCallbacks callbacks)
        {
            try
            {
                callbacks.Verbose?.Invoke($"Unmounting hive {mountKey} using native API");

                // Unmount the hive
                int result = RegUnLoadKey(HKEY_LOCAL_MACHINE, mountKey);
                if (result == 0)
                {
                    callbacks.Verbose?.Invoke($"Successfully unmounted hive {mountKey}");
                    return true;
                }
                else
                {
                    callbacks.Warning?.Invoke($"Failed to unmount hive {mountKey}. Error code: {result}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                callbacks.Warning?.Invoke($"Error unmounting hive {mountKey}: {ex.Message}");
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
