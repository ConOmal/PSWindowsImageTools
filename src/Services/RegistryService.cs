using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Native Windows Registry service using P/Invoke for reliable cross-platform compatibility
    /// </summary>
    public static class RegistryService
    {
        private const string ServiceName = "RegistryService";

        // Registry hive constants
        public const int HKEY_LOCAL_MACHINE = unchecked((int)0x80000002);
        public const int HKEY_CURRENT_USER = unchecked((int)0x80000001);
        public const int HKEY_USERS = unchecked((int)0x80000003);

        // Registry access rights
        private const int KEY_READ = 0x20019;
        private const int KEY_ENUMERATE_SUB_KEYS = 0x0008;
        private const int KEY_QUERY_VALUE = 0x0001;

        // Error codes
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_NO_MORE_ITEMS = 259;
        private const int ERROR_FILE_NOT_FOUND = 2;

        // Registry value types
        public const int REG_SZ = 1;
        public const int REG_DWORD = 4;

        // Native API declarations
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyEx(int hKey, string subKey, int ulOptions, int samDesired, out IntPtr hkResult);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegEnumKeyEx(IntPtr hKey, int dwIndex, StringBuilder lpName, ref int lpcchName, 
            IntPtr lpReserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, 
            out int lpType, StringBuilder lpData, ref int lpcbData);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, 
            out int lpType, byte[] lpData, ref int lpcbData);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr hKey);

        /// <summary>
        /// Opens a registry key for reading
        /// </summary>
        /// <param name="hive">Registry hive (use HKEY_* constants)</param>
        /// <param name="subKey">Subkey path</param>
                /// <returns>Registry key handle or IntPtr.Zero if failed</returns>
        public static IntPtr OpenKey(int hive, string subKey)
        {
            try
            {
                int result = RegOpenKeyEx(hive, subKey, 0, KEY_READ, out IntPtr keyHandle);
                if (result == ERROR_SUCCESS)
                {
                    return keyHandle;
                }
            }
            catch
            {
                // Silently handle registry access failures
            }
            
            return IntPtr.Zero;
        }

        /// <summary>
        /// Closes a registry key handle
        /// </summary>
        /// <param name="keyHandle">Key handle to close</param>
        public static void CloseKey(IntPtr keyHandle)
        {
            if (keyHandle != IntPtr.Zero)
            {
                RegCloseKey(keyHandle);
            }
        }

        /// <summary>
        /// Enumerates subkeys of a registry key
        /// </summary>
        /// <param name="keyHandle">Parent key handle</param>
                /// <returns>List of subkey names</returns>
        public static List<string> EnumerateSubKeys(IntPtr keyHandle)
        {
            var subKeys = new List<string>();
            
            if (keyHandle == IntPtr.Zero)
                return subKeys;

            try
            {
                int index = 0;
                while (true)
                {
                    var nameBuilder = new StringBuilder(256);
                    int nameLength = nameBuilder.Capacity;
                    
                    int result = RegEnumKeyEx(keyHandle, index, nameBuilder, ref nameLength, 
                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    
                    if (result == ERROR_SUCCESS)
                    {
                        subKeys.Add(nameBuilder.ToString());
                        index++;
                    }
                    else if (result == ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
                // Silently handle enumeration failures
            }

            return subKeys;
        }

        /// <summary>
        /// Gets a string value from a registry key
        /// </summary>
        /// <param name="keyHandle">Registry key handle</param>
        /// <param name="valueName">Value name</param>
                /// <returns>String value or null if not found</returns>
        public static string? GetStringValue(IntPtr keyHandle, string valueName)
        {
            if (keyHandle == IntPtr.Zero)
                return null;

            try
            {
                int dataSize = 0;
                int result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out int valueType, (StringBuilder)null!, ref dataSize);
                
                if (result != ERROR_SUCCESS || valueType != REG_SZ || dataSize <= 0)
                    return null;

                var dataBuilder = new StringBuilder(dataSize / 2); // Unicode characters
                result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out valueType, dataBuilder, ref dataSize);
                
                if (result == ERROR_SUCCESS)
                {
                    return dataBuilder.ToString();
                }
            }
            catch
            {
                // Silently handle value access failures
            }

            return null;
        }

        /// <summary>
        /// Gets a DWORD value from a registry key
        /// </summary>
        /// <param name="keyHandle">Registry key handle</param>
        /// <param name="valueName">Value name</param>
                /// <returns>DWORD value or null if not found</returns>
        public static int? GetDWordValue(IntPtr keyHandle, string valueName)
        {
            if (keyHandle == IntPtr.Zero)
                return null;

            try
            {
                var data = new byte[4];
                int dataSize = data.Length;
                int result = RegQueryValueEx(keyHandle, valueName, IntPtr.Zero, out int valueType, data, ref dataSize);
                
                if (result == ERROR_SUCCESS && valueType == REG_DWORD && dataSize == 4)
                {
                    return BitConverter.ToInt32(data, 0);
                }
            }
            catch
            {
                // Silently handle value access failures
            }

            return null;
        }

        /// <summary>
        /// Enumerates registry entries in the uninstall section
        /// </summary>
                /// <returns>Dictionary of registry entries with their properties</returns>
        public static Dictionary<string, Dictionary<string, string>> EnumerateUninstallEntries()
        {
            var entries = new Dictionary<string, Dictionary<string, string>>();
            
            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var registryPath in registryPaths)
            {
                var baseKey = OpenKey(HKEY_LOCAL_MACHINE, registryPath);
                if (baseKey == IntPtr.Zero)
                    continue;

                try
                {
                    var subKeys = EnumerateSubKeys(baseKey);
                    
                    foreach (var subKeyName in subKeys)
                    {
                        var subKey = OpenKey(HKEY_LOCAL_MACHINE, $@"{registryPath}\{subKeyName}");
                        if (subKey == IntPtr.Zero)
                            continue;

                        try
                        {
                            var properties = new Dictionary<string, string>();
                            
                            // Get common uninstall properties
                            var displayName = GetStringValue(subKey, "DisplayName");
                            if (!string.IsNullOrEmpty(displayName))
                            {
                                properties["DisplayName"] = displayName ?? string.Empty;
                                properties["InstallLocation"] = GetStringValue(subKey, "InstallLocation") ?? string.Empty;
                                properties["DisplayVersion"] = GetStringValue(subKey, "DisplayVersion") ?? string.Empty;
                                properties["Publisher"] = GetStringValue(subKey, "Publisher") ?? string.Empty;
                                properties["InstallDate"] = GetStringValue(subKey, "InstallDate") ?? string.Empty;
                                properties["RegistryPath"] = $@"{registryPath}\{subKeyName}";
                                
                                entries[subKeyName] = properties;
                            }
                        }
                        finally
                        {
                            CloseKey(subKey);
                        }
                    }
                }
                finally
                {
                    CloseKey(baseKey);
                }
            }

            return entries;
        }
    }
}


