using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Persistent registry of mounted Windows images so cmdlets can re-discover mounts across
    /// PowerShell sessions. State is stored as JSON under the module's temp directory.
    /// </summary>
    public static class MountSessionService
    {
        private static readonly object _lock = new object();

        /// <summary>
        /// Path of the mount session state file
        /// </summary>
        public static string StateFilePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "PSWindowsImageTools",
            "mounts.json");

        /// <summary>
        /// Registers a mounted image
        /// </summary>
        /// <param name="mountedImage">Mounted image to register</param>
        public static void Register(MountedWindowsImage mountedImage)
        {
            if (mountedImage?.MountPath == null)
            {
                return;
            }

            lock (_lock)
            {
                var mounts = LoadState();
                var mountPath = mountedImage.MountPath.FullName;

                // Replace any existing entry for the same mount path
                mounts.RemoveAll(m => string.Equals(m.MountPath?.FullName, mountPath, StringComparison.OrdinalIgnoreCase));
                mounts.Add(mountedImage);
                SaveState(mounts);
            }
        }

        /// <summary>
        /// Unregisters a mounted image by mount path
        /// </summary>
        /// <param name="mountPath">Mount path to unregister</param>
        public static void Unregister(string mountPath)
        {
            if (string.IsNullOrWhiteSpace(mountPath))
            {
                return;
            }

            lock (_lock)
            {
                var mounts = LoadState();
                var before = mounts.Count;
                mounts.RemoveAll(m => string.Equals(m.MountPath?.FullName, mountPath, StringComparison.OrdinalIgnoreCase));

                if (mounts.Count != before)
                {
                    SaveState(mounts);
                }
            }
        }

        /// <summary>
        /// Gets currently registered mounts whose directories still exist
        /// </summary>
        /// <returns>Active mounted images</returns>
        public static List<MountedWindowsImage> GetActive()
        {
            lock (_lock)
            {
                var mounts = LoadState();
                var active = mounts
                    .Where(m => m.MountPath != null && Directory.Exists(m.MountPath.FullName))
                    .ToList();

                // Prune dead entries discovered during validation
                if (active.Count != mounts.Count)
                {
                    SaveState(active);
                }

                return active;
            }
        }

        /// <summary>
        /// Removes entries whose mount directories no longer exist
        /// </summary>
        /// <returns>Number of pruned entries</returns>
        public static int Prune()
        {
            lock (_lock)
            {
                var mounts = LoadState();
                var active = mounts
                    .Where(m => m.MountPath != null && Directory.Exists(m.MountPath.FullName))
                    .ToList();

                var pruned = mounts.Count - active.Count;
                if (pruned > 0)
                {
                    SaveState(active);
                }

                return pruned;
            }
        }

        private static List<MountedWindowsImage> LoadState()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return new List<MountedWindowsImage>();
                }

                var json = File.ReadAllText(StateFilePath);
                var mounts = JsonConvert.DeserializeObject<List<MountedWindowsImage>>(json);

                return mounts ?? new List<MountedWindowsImage>();
            }
            catch
            {
                // Corrupt state should never block cmdlets
                return new List<MountedWindowsImage>();
            }
        }

        private static void SaveState(List<MountedWindowsImage> mounts)
        {
            try
            {
                var directory = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(StateFilePath, JsonConvert.SerializeObject(mounts, Formatting.Indented));
            }
            catch
            {
                // Best effort: failure to persist session state should not break operations
            }
        }
    }
}
