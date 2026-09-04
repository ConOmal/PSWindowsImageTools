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
    /// Persisted via a flat DTO because DirectoryInfo cannot be JSON-serialized directly.
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
        /// Serializable persistence entry (MountedWindowsImage holds DirectoryInfo, which
        /// Newtonsoft cannot serialize)
        /// </summary>
        private sealed class MountSessionEntry
        {
            public string MountId { get; set; } = string.Empty;
            public string SourceImagePath { get; set; } = string.Empty;
            public int ImageIndex { get; set; }
            public string ImageName { get; set; } = string.Empty;
            public string Edition { get; set; } = string.Empty;
            public string Architecture { get; set; } = string.Empty;
            public string MountPath { get; set; } = string.Empty;
            public string WimGuid { get; set; } = string.Empty;
            public DateTime MountedAt { get; set; } = DateTime.UtcNow;
            public string Status { get; set; } = "Mounted";
            public bool IsReadOnly { get; set; } = true;
            public string? ErrorMessage { get; set; }
            public long ImageSize { get; set; }
        }

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
                mounts.RemoveAll(m => string.Equals(m.MountPath, mountPath, StringComparison.OrdinalIgnoreCase));
                mounts.Add(ToEntry(mountedImage));
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
                mounts.RemoveAll(m => string.Equals(m.MountPath, mountPath, StringComparison.OrdinalIgnoreCase));

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
                    .Where(m => !string.IsNullOrEmpty(m.MountPath) && Directory.Exists(m.MountPath))
                    .ToList();

                // Prune dead entries discovered during validation
                if (active.Count != mounts.Count)
                {
                    SaveState(active);
                }

                return active.Select(ToMountedWindowsImage).ToList();
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
                    .Where(m => !string.IsNullOrEmpty(m.MountPath) && Directory.Exists(m.MountPath))
                    .ToList();

                var pruned = mounts.Count - active.Count;
                if (pruned > 0)
                {
                    SaveState(active);
                }

                return pruned;
            }
        }

        private static MountSessionEntry ToEntry(MountedWindowsImage mountedImage)
        {
            return new MountSessionEntry
            {
                MountId = mountedImage.MountId,
                SourceImagePath = mountedImage.SourceImagePath,
                ImageIndex = mountedImage.ImageIndex,
                ImageName = mountedImage.ImageName,
                Edition = mountedImage.Edition,
                Architecture = mountedImage.Architecture,
                MountPath = mountedImage.MountPath!.FullName,
                WimGuid = mountedImage.WimGuid,
                MountedAt = mountedImage.MountedAt,
                Status = mountedImage.Status.ToString(),
                IsReadOnly = mountedImage.IsReadOnly,
                ErrorMessage = mountedImage.ErrorMessage,
                ImageSize = mountedImage.ImageSize
            };
        }

        private static MountedWindowsImage ToMountedWindowsImage(MountSessionEntry entry)
        {
            return new MountedWindowsImage
            {
                MountId = entry.MountId,
                SourceImagePath = entry.SourceImagePath,
                ImageIndex = entry.ImageIndex,
                ImageName = entry.ImageName,
                Edition = entry.Edition,
                Architecture = entry.Architecture,
                MountPath = new DirectoryInfo(entry.MountPath),
                WimGuid = entry.WimGuid,
                MountedAt = entry.MountedAt,
                Status = ParseStatus(entry.Status),
                IsReadOnly = entry.IsReadOnly,
                ErrorMessage = entry.ErrorMessage,
                ImageSize = entry.ImageSize
            };
        }

        private static MountStatus ParseStatus(string status)
        {
            return Enum.TryParse<MountStatus>(status, ignoreCase: true, out var parsed) ? parsed : MountStatus.Mounted;
        }

        private static List<MountSessionEntry> LoadState()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                {
                    return new List<MountSessionEntry>();
                }

                var json = File.ReadAllText(StateFilePath);
                var mounts = JsonConvert.DeserializeObject<List<MountSessionEntry>>(json);

                return mounts ?? new List<MountSessionEntry>();
            }
            catch
            {
                // Corrupt state should never block cmdlets
                return new List<MountSessionEntry>();
            }
        }

        private static void SaveState(List<MountSessionEntry> mounts)
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
