using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Creates, lists, restores, and deletes point-in-time checkpoints of a mounted Windows
    /// image's on-disk state — a plain recursive file mirror, not VSS, so it can checkpoint a
    /// single mount directory rather than a whole volume. State (the checkpoint index) is
    /// persisted as JSON under the module's temp directory, mirroring MountSessionService's
    /// existing pattern for the same DirectoryInfo-can't-serialize reason.
    /// </summary>
    public class ImageCheckpointService
    {
        private const string ServiceName = "ImageCheckpointService";
        private static readonly object _lock = new object();
        private readonly ModuleCallbacks _callbacks;
        private readonly string _checkpointRoot;
        private readonly string _stateFilePath;

        /// <param name="checkpointRoot">Root directory checkpoints are stored under. Defaults to
        /// %TEMP%\PSWindowsImageTools\checkpoints, matching MountSessionService's convention.
        /// A non-default root is accepted for testability.</param>
        public ImageCheckpointService(string? checkpointRoot = null, ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
            _checkpointRoot = checkpointRoot ?? Path.Combine(Path.GetTempPath(), "PSWindowsImageTools", "checkpoints");
            _stateFilePath = Path.Combine(_checkpointRoot, "checkpoints.json");
        }

        private sealed class CheckpointEntry
        {
            public string CheckpointId { get; set; } = string.Empty;
            public string MountId { get; set; } = string.Empty;
            public string? Label { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public long SizeBytes { get; set; }
            public string CheckpointPath { get; set; } = string.Empty;
        }

        /// <summary>
        /// Creates a checkpoint of a mounted image's current on-disk state
        /// </summary>
        public ImageCheckpointInfo Create(MountedWindowsImage mountedImage, string? label)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var checkpointId = Guid.NewGuid().ToString("N");
            var checkpointDir = Path.Combine(_checkpointRoot, checkpointId);
            Directory.CreateDirectory(checkpointDir);

            _callbacks.Verbose?.Invoke($"Creating checkpoint {checkpointId} of {mountedImage.ImageName}");
            CopyDirectory(mountedImage.MountPath.FullName, checkpointDir);

            var sizeBytes = new DirectoryInfo(checkpointDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            var info = new ImageCheckpointInfo
            {
                CheckpointId = checkpointId,
                MountId = mountedImage.MountId,
                Label = label,
                SizeBytes = sizeBytes,
                CheckpointPath = new DirectoryInfo(checkpointDir)
            };

            lock (_lock)
            {
                var entries = LoadState();
                entries.Add(ToEntry(info));
                SaveState(entries);
            }

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpointId} created: {sizeBytes / 1024.0 / 1024.0:F1} MB");
            return info;
        }

        /// <summary>
        /// Lists checkpoints, optionally filtered to one mount
        /// </summary>
        public List<ImageCheckpointInfo> List(string? mountId)
        {
            lock (_lock)
            {
                var entries = LoadState();
                if (!string.IsNullOrEmpty(mountId))
                {
                    entries = entries.Where(e => string.Equals(e.MountId, mountId, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return entries.Select(ToInfo).ToList();
            }
        }

        /// <summary>
        /// Restores a mounted image's directory to a previously taken checkpoint. The target
        /// must currently be mounted read-write — restoring is inherently a mutation, and a
        /// read-only or already-dismounted target cannot be safely overwritten.
        /// </summary>
        public void Restore(ImageCheckpointInfo checkpoint, MountedWindowsImage mountedImage)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            if (mountedImage.Status != MountStatus.Mounted)
            {
                throw new InvalidOperationException(
                    $"Cannot restore checkpoint: image {mountedImage.ImageName} is not currently mounted (status: {mountedImage.Status})");
            }

            if (mountedImage.IsReadOnly)
            {
                throw new InvalidOperationException(
                    $"Cannot restore checkpoint: image {mountedImage.ImageName} is mounted read-only");
            }

            _callbacks.Verbose?.Invoke($"Restoring checkpoint {checkpoint.CheckpointId} onto {mountedImage.ImageName}");

            var mountPath = mountedImage.MountPath.FullName;

            // Remove current contents, then mirror the checkpoint back in
            foreach (var entry in Directory.EnumerateFileSystemEntries(mountPath))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            CopyDirectory(checkpoint.CheckpointPath.FullName, mountPath);

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpoint.CheckpointId} restored onto {mountedImage.ImageName}");
        }

        /// <summary>
        /// Deletes a checkpoint: removes its directory and index entry
        /// </summary>
        public void Delete(ImageCheckpointInfo checkpoint)
        {
            if (checkpoint.CheckpointPath.Exists)
            {
                Directory.Delete(checkpoint.CheckpointPath.FullName, recursive: true);
            }

            lock (_lock)
            {
                var entries = LoadState();
                entries.RemoveAll(e => e.CheckpointId == checkpoint.CheckpointId);
                SaveState(entries);
            }

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpoint.CheckpointId} deleted");
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetPath = Path.Combine(destinationDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
            }
        }

        private static ImageCheckpointInfo ToInfo(CheckpointEntry entry)
        {
            return new ImageCheckpointInfo
            {
                CheckpointId = entry.CheckpointId,
                MountId = entry.MountId,
                Label = entry.Label,
                CreatedAt = entry.CreatedAt,
                SizeBytes = entry.SizeBytes,
                CheckpointPath = new DirectoryInfo(entry.CheckpointPath)
            };
        }

        private static CheckpointEntry ToEntry(ImageCheckpointInfo info)
        {
            return new CheckpointEntry
            {
                CheckpointId = info.CheckpointId,
                MountId = info.MountId,
                Label = info.Label,
                CreatedAt = info.CreatedAt,
                SizeBytes = info.SizeBytes,
                CheckpointPath = info.CheckpointPath.FullName
            };
        }

        private List<CheckpointEntry> LoadState()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    return new List<CheckpointEntry>();
                }

                var json = File.ReadAllText(_stateFilePath);
                return JsonConvert.DeserializeObject<List<CheckpointEntry>>(json) ?? new List<CheckpointEntry>();
            }
            catch
            {
                return new List<CheckpointEntry>();
            }
        }

        private void SaveState(List<CheckpointEntry> entries)
        {
            try
            {
                if (!Directory.Exists(_checkpointRoot))
                {
                    Directory.CreateDirectory(_checkpointRoot);
                }

                File.WriteAllText(_stateFilePath, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch
            {
                // Best effort: failure to persist checkpoint index should not break operations
            }
        }
    }
}
