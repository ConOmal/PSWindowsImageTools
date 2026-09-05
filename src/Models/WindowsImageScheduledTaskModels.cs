using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Friendly state of a scheduled task's TaskCache entry, from the <c>State</c> DWORD
    /// under <c>Schedule\TaskCache\Tasks\&lt;GUID&gt;</c>. The 1-4 mapping is the
    /// community-documented encoding (consistent with Task Scheduler's own state enum);
    /// anything else - including 0, an absent value, or a legacy entry without the value -
    /// surfaces as <see cref="Unknown"/> (the raw DWORD is always available alongside).
    /// </summary>
    public enum WindowsImageScheduledTaskState
    {
        /// <summary>
        /// State value is 0, absent, non-numeric, or outside the known range (display only)
        /// </summary>
        Unknown,

        /// <summary>
        /// Task is disabled (State = 1)
        /// </summary>
        Disabled,

        /// <summary>
        /// Task is queued (State = 2)
        /// </summary>
        Queued,

        /// <summary>
        /// Task is ready (State = 3)
        /// </summary>
        Ready,

        /// <summary>
        /// Task is running (State = 4)
        /// </summary>
        Running
    }

    /// <summary>
    /// One scheduled task registered in a mounted Windows image's offline SOFTWARE hive
    /// (Schedule\TaskCache), from Get-WindowsImageScheduledTask
    /// </summary>
    public class WindowsImageScheduledTaskInfo
    {
        /// <summary>
        /// Name of the image the task was read from
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// Task path composed from the TaskCache\Tree hierarchy (e.g. "\Microsoft\Windows\Defrag\ScheduledDefrag")
        /// </summary>
        public string TaskPath { get; set; } = string.Empty;

        /// <summary>
        /// GUID linking the Tree leaf to its TaskCache\Tasks entry (the tree leaf's Id value, reported as found)
        /// </summary>
        public string TaskGuid { get; set; } = string.Empty;

        /// <summary>
        /// Friendly state derived from the Tasks entry's State DWORD (Unknown when absent or out of range)
        /// </summary>
        public WindowsImageScheduledTaskState State { get; set; } = WindowsImageScheduledTaskState.Unknown;

        /// <summary>
        /// Raw State DWORD of the Tasks entry; -1 when the value is absent or not numeric
        /// </summary>
        public int StateValue { get; set; } = -1;

        /// <summary>
        /// Uri value of the Tasks entry (task path, where present); empty when absent
        /// </summary>
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// Whether the task's GUID has a matching Schedule\TaskCache\Tasks entry
        /// (false means the Tree leaf exists but its cache entry is missing)
        /// </summary>
        public bool HasTasksEntry { get; set; }

        /// <summary>
        /// All raw values of the Tasks cache entry, sorted by value name; null unless
        /// the -Detailed switch was used. Binary values (the undocumented task-definition
        /// blob and the validation hash) appear only in the registry package's decoded
        /// string form and are NOT parsed into triggers/actions.
        /// </summary>
        public Dictionary<string, object>? RegistryValues { get; set; }

        /// <summary>
        /// Returns a string representation of the scheduled task
        /// </summary>
        public override string ToString()
        {
            return $"{TaskPath} ({State}) on {ImageName}";
        }
    }
}
