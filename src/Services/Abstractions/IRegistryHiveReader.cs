using System;
using System.Collections.Generic;
using Registry;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Read access to offline registry hive files via RegistryHiveOnDemand (no hive mounting,
    /// no persistent file handles). Implementations read hive files into memory on demand.
    /// </summary>
    public interface IRegistryHiveReader : IDisposable
    {
        /// <summary>
        /// Reads all values from the Windows NT CurrentVersion key of a SOFTWARE hive
        /// </summary>
        /// <param name="softwareHivePath">Path to the SOFTWARE hive file</param>
        /// <returns>Dictionary of value names to raw value data (empty if the key is missing)</returns>
        Dictionary<string, object> GetWindowsVersionInfo(string softwareHivePath);

        /// <summary>
        /// Enumerates installed software entries from the Uninstall keys (native + WOW64) of a SOFTWARE hive
        /// </summary>
        /// <param name="softwareHivePath">Path to the SOFTWARE hive file</param>
        /// <returns>List of software entries (empty if the key is missing)</returns>
        List<Software> GetInstalledSoftware(string softwareHivePath);

        /// <summary>
        /// Reads Windows Update policy values from a SOFTWARE hive
        /// </summary>
        /// <param name="softwareHivePath">Path to the SOFTWARE hive file</param>
        /// <returns>Dictionary of value names to raw value data (empty if the key is missing)</returns>
        Dictionary<string, object> GetWindowsUpdateConfiguration(string softwareHivePath);

        /// <summary>
        /// Opens a hive file for on-demand reading
        /// </summary>
        /// <param name="hivePath">Path to the hive file</param>
        /// <returns>Hive reader instance (caller may discard; no file handle is retained)</returns>
        RegistryHiveOnDemand OpenHive(string hivePath);

        /// <summary>
        /// Gets a key from an open hive
        /// </summary>
        /// <param name="hive">Open hive</param>
        /// <param name="keyPath">Key path relative to the hive root</param>
        /// <returns>Registry key, or null when the path does not exist</returns>
        Registry.Abstractions.RegistryKey? GetKey(RegistryHiveOnDemand hive, string keyPath);
    }
}
