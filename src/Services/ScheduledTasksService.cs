using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Inventories the scheduled tasks registered in a mounted Windows image's offline
    /// SOFTWARE hive (<c>Schedule\TaskCache</c>).
    ///
    /// Reads go through the existing in-memory <see cref="IRegistryHiveReader"/> (no hive
    /// mounting, no persistent handles). This service is strictly read-only: the
    /// <c>Tasks\&lt;GUID&gt;</c> task-definition blob is undocumented binary and is NOT
    /// parsed — only what is reliably readable is reported (task path from the Tree
    /// hierarchy, the associated GUID, the State DWORD where present, the Uri value where
    /// present, and raw decoded entry values under the detailed flag). All decision logic
    /// (tree-path composition, path filtering, state mapping, projection, value
    /// collection) is pure and unit-testable without hive files, DISM sessions or real
    /// images.
    /// </summary>
    public class ScheduledTasksService
    {
        private const string ServiceName = "ScheduledTasksService";

        /// <summary>
        /// Canonical hive name for the SOFTWARE hive
        /// </summary>
        public const string SoftwareHiveName = "HKLM\\SOFTWARE";

        /// <summary>
        /// Task Scheduler registration-cache key path within the SOFTWARE hive (relative to the hive root)
        /// </summary>
        internal const string TaskCacheKeyPath = @"Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache";

        /// <summary>
        /// Name of the Tree subkey (task-path hierarchy; leaves carry the Id GUID value)
        /// </summary>
        internal const string TreeSubKeyName = "Tree";

        /// <summary>
        /// Name of the Tasks subkey (one subkey per GUID holding the task cache entry)
        /// </summary>
        internal const string TasksSubKeyName = "Tasks";

        /// <summary>
        /// Name of the Tree leaf value holding the task's GUID
        /// </summary>
        internal const string TreeIdValueName = "Id";

        /// <summary>
        /// Name of the Tasks-entry value holding the task state DWORD (modern Windows)
        /// </summary>
        internal const string StateValueName = "State";

        /// <summary>
        /// Name of the Tasks-entry value holding the task path (where present)
        /// </summary>
        internal const string UriValueName = "Uri";

        private readonly ModuleCallbacks _callbacks;

        /// <summary>
        /// Creates the service with explicit callbacks
        /// </summary>
        public ScheduledTasksService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Enumerates the scheduled tasks registered in a mounted image's offline SOFTWARE
        /// hive TaskCache. Thin hive-reading path (the only method that touches
        /// <see cref="IRegistryHiveReader"/>); filtering, state mapping and projection are
        /// pure helpers.
        /// </summary>
        /// <param name="reader">Offline hive reader (in-memory, no hive mounting)</param>
        /// <param name="imageName">Name of the image (copied onto each result)</param>
        /// <param name="mountPath">Path where the Windows image is mounted</param>
        /// <param name="pathFilter">Optional task-path filter (exact path, or a regular expression pattern); null for all</param>
        /// <param name="detailed">When true, attach the raw TaskCache entry values to each result</param>
        /// <param name="progress">Optional progress callback (percent, status)</param>
        /// <returns>Tasks matching the filter, sorted by task path (empty when the hive or TaskCache is missing)</returns>
        public List<WindowsImageScheduledTaskInfo> GetScheduledTasks(
            IRegistryHiveReader reader,
            string imageName,
            string mountPath,
            string? pathFilter = null,
            bool detailed = false,
            Action<int, string>? progress = null)
        {
            var output = new List<WindowsImageScheduledTaskInfo>();
            var hivePath = ResolveSoftwareHivePath(mountPath);

            if (!File.Exists(hivePath))
            {
                _callbacks.Verbose?.Invoke($"SOFTWARE hive not found at {hivePath}; no scheduled tasks enumerated for {imageName}");
                return output;
            }

            var hive = reader.OpenHive(hivePath);
            var treeKey = reader.GetKey(hive, $"{TaskCacheKeyPath}\\{TreeSubKeyName}");
            if (treeKey == null)
            {
                _callbacks.Verbose?.Invoke($"Task Scheduler cache '{TaskCacheKeyPath}\\{TreeSubKeyName}' not found in {hivePath}; no scheduled tasks reported for {imageName}");
                return output;
            }

            var treeTasks = new List<(string TaskPath, string TaskGuid)>();
            WalkTree(treeKey, string.Empty, treeTasks, imageName);

            var filtered = FilterTreeTasks(treeTasks, pathFilter);

            _callbacks.Verbose?.Invoke($"Found {treeTasks.Count} registered task(s) in the TaskCache tree of {imageName} ({filtered.Count} matching the filter)");

            for (var index = 0; index < filtered.Count; index++)
            {
                var (taskPath, taskGuid) = filtered[index];

                if (progress != null)
                {
                    progress((index + 1) * 100 / filtered.Count, $"Reading {taskPath}");
                }

                try
                {
                    var entryKey = reader.GetKey(hive, $"{TaskCacheKeyPath}\\{TasksSubKeyName}\\{taskGuid}");
                    var entryValues = entryKey == null
                        ? null
                        : entryKey.Values
                            .Where(v => v != null)
                            .Select(v => (Name: v.ValueName ?? string.Empty, Data: (object?)v.ValueData))
                            .ToList();

                    output.Add(BuildTaskInfo(imageName, mountPath, taskPath, taskGuid, entryKey != null, entryValues, detailed));
                }
                catch (Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to read scheduled task '{taskPath}' ({taskGuid}): {ex.Message}");
                }
            }

            if (progress != null)
            {
                progress(100, $"Enumerated {output.Count} scheduled task(s)");
            }

            _callbacks.Verbose?.Invoke($"Enumerated {output.Count} scheduled tasks from {hivePath}");
            return output;
        }

        /// <summary>
        /// Depth-first walk of the TaskCache\Tree hierarchy, collecting (TaskPath, TaskGuid)
        /// for every leaf carrying a non-empty Id value. Thin: per-node failures are warned
        /// and skipped so one bad node never drops the inventory.
        /// </summary>
        private void WalkTree(
            Registry.Abstractions.RegistryKey node,
            string nodePath,
            List<(string TaskPath, string TaskGuid)> tasks,
            string imageName)
        {
            try
            {
                var values = node.Values
                    .Where(v => v != null)
                    .Select(v => (Name: v.ValueName ?? string.Empty, Data: (object?)v.ValueData))
                    .ToList();

                var id = GetStringValue(values, TreeIdValueName);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    tasks.Add((nodePath, id!));
                }

                if (node.SubKeys == null)
                {
                    return;
                }

                foreach (var subKey in node.SubKeys)
                {
                    var name = subKey?.KeyName;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    WalkTree(subKey!, JoinTreePath(nodePath, name!), tasks, imageName);
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to walk TaskCache tree node '{nodePath}' for {imageName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves the SOFTWARE hive file path inside a mounted Windows image
        /// </summary>
        internal static string ResolveSoftwareHivePath(string mountPath)
        {
            return Path.Combine(mountPath, "Windows", "System32", "config", "SOFTWARE");
        }

        /// <summary>
        /// Composes a task path from a parent path and a tree node name. Pure.
        /// The Tree root maps to the empty string, so root children get a leading
        /// backslash ("" + "Microsoft" -> "\Microsoft").
        /// </summary>
        internal static string JoinTreePath(string parentPath, string nodeName)
        {
            return parentPath + "\\" + nodeName;
        }

        /// <summary>
        /// Filters tree tasks by task path and sorts the result by path (ordinal-ignore-case).
        /// Pure.
        /// </summary>
        internal static List<(string TaskPath, string TaskGuid)> FilterTreeTasks(
            List<(string TaskPath, string TaskGuid)> tasks,
            string? pathFilter)
        {
            var snapshot = tasks ?? new List<(string TaskPath, string TaskGuid)>();

            return snapshot
                .Where(t => MatchesPathFilter(t.TaskPath, pathFilter))
                .OrderBy(t => t.TaskPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True when a task path matches a filter: blank filter matches everything;
        /// exact case-insensitive match wins; otherwise the filter is treated as a
        /// regex (anchored, case-insensitive, 1s timeout). An invalid pattern or
        /// timeout matches nothing. Pure.
        /// </summary>
        internal static bool MatchesPathFilter(string? taskPath, string? filter)
        {
            if (string.IsNullOrWhiteSpace(taskPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            var trimmedFilter = filter!.Trim();
            if (string.Equals(taskPath, trimmedFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return new Regex(
                    "^(?i:" + trimmedFilter + ")$",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)).IsMatch(taskPath!);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Maps a raw TaskCache State DWORD to its friendly state
        /// (Unknown for 0, absent and anything outside 1-4). Pure.
        /// </summary>
        internal static WindowsImageScheduledTaskState ParseTaskState(int value)
        {
            switch (value)
            {
                case 1: return WindowsImageScheduledTaskState.Disabled;
                case 2: return WindowsImageScheduledTaskState.Queued;
                case 3: return WindowsImageScheduledTaskState.Ready;
                case 4: return WindowsImageScheduledTaskState.Running;
                default: return WindowsImageScheduledTaskState.Unknown;
            }
        }

        /// <summary>
        /// Reads a value as a DWORD by name (ordinal-ignore-case); null when absent or non-numeric. Pure.
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
                    return Convert.ToInt32(value.Data, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a value as a string by name (ordinal-ignore-case); empty when absent. Pure.
        /// </summary>
        internal static string GetStringValue(IEnumerable<(string Name, object? Data)> values, string valueName)
        {
            foreach (var value in values)
            {
                if (string.Equals(value.Name, valueName, StringComparison.OrdinalIgnoreCase))
                {
                    return value.Data?.ToString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Copies raw TaskCache-entry values into a dictionary sorted by value name. Pure.
        /// </summary>
        internal static Dictionary<string, object>? CollectValues(IEnumerable<(string Name, object? Data)> values)
        {
            var output = new Dictionary<string, object>();

            foreach (var value in values
                .Where(v => !string.IsNullOrEmpty(v.Name))
                .OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                output[value.Name] = value.Data!;
            }

            return output;
        }

        /// <summary>
        /// Projects one Tree leaf plus its optional Tasks-entry values into a
        /// WindowsImageScheduledTaskInfo. Pure.
        /// </summary>
        internal static WindowsImageScheduledTaskInfo BuildTaskInfo(
            string imageName,
            string mountPath,
            string taskPath,
            string taskGuid,
            bool hasTasksEntry,
            IEnumerable<(string Name, object? Data)>? values,
            bool detailed)
        {
            var entryValues = values?.ToList() ?? new List<(string Name, object? Data)>();
            var stateValue = hasTasksEntry ? GetDwordValue(entryValues, StateValueName) : null;

            return new WindowsImageScheduledTaskInfo
            {
                ImageName = imageName,
                MountPath = mountPath,
                TaskPath = taskPath,
                TaskGuid = taskGuid,
                State = stateValue.HasValue ? ParseTaskState(stateValue.Value) : WindowsImageScheduledTaskState.Unknown,
                StateValue = stateValue ?? -1,
                Uri = hasTasksEntry ? GetStringValue(entryValues, UriValueName) : string.Empty,
                HasTasksEntry = hasTasksEntry,
                RegistryValues = hasTasksEntry && detailed ? CollectValues(entryValues) : null
            };
        }
    }
}
