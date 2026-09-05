using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Friendly start type of an offline service as found in the SYSTEM hive's
    /// <c>ControlSet001\Services\&lt;name&gt;</c> "Start" DWORD:
    /// 0 = Boot, 1 = System, 2 = Automatic, 3 = Manual, 4 = Disabled.
    /// Anything else is surfaced as <see cref="Unknown"/> (display only).
    /// </summary>
    public enum WindowsImageServiceStartType
    {
        /// <summary>
        /// Boot start (only valid for boot-start drivers; Start = 0)
        /// </summary>
        Boot,

        /// <summary>
        /// System start (only valid for system-start drivers; Start = 1)
        /// </summary>
        System,

        /// <summary>
        /// Automatic start at boot (Start = 2)
        /// </summary>
        Automatic,

        /// <summary>
        /// Started manually (Start = 3)
        /// </summary>
        Manual,

        /// <summary>
        /// Disabled (Start = 4)
        /// </summary>
        Disabled,

        /// <summary>
        /// Start value is absent or does not map to a known type (display only;
        /// never accepted back for writes)
        /// </summary>
        Unknown
    }

    /// <summary>
    /// One service configured in a mounted Windows image's offline SYSTEM hive,
    /// from Get-WindowsImageService
    /// </summary>
    public class WindowsImageServiceInfo
    {
        /// <summary>
        /// Name of the image the service was read from
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// Service key name (the subkey under ControlSet001\Services)
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Localized display name (DisplayName value); empty when absent
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Service binary path (ImagePath value); empty when absent
        /// </summary>
        public string ImagePath { get; set; } = string.Empty;

        /// <summary>
        /// Service description (Description value); empty when absent
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Friendly start type derived from the raw Start DWORD
        /// </summary>
        public WindowsImageServiceStartType StartType { get; set; } = WindowsImageServiceStartType.Unknown;

        /// <summary>
        /// Raw Start DWORD value; -1 when the value is absent or not numeric
        /// </summary>
        public int StartValue { get; set; } = -1;

        /// <summary>
        /// Whether the service is configured for delayed auto start (DelayedAutoStart
        /// DWORD equals 1); only meaningful when StartType is Automatic
        /// </summary>
        public bool DelayedAutoStart { get; set; }

        /// <summary>
        /// All raw values of the service key, sorted by value name; null unless
        /// the -Detailed switch was used
        /// </summary>
        public Dictionary<string, object>? RegistryValues { get; set; }

        /// <summary>
        /// Returns a string representation of the service
        /// </summary>
        public override string ToString()
        {
            return $"{Name} ({StartType}) on {ImageName}";
        }
    }

    /// <summary>
    /// Result of changing one service's configuration in a mounted Windows image,
    /// from Set-WindowsImageService (one result per image)
    /// </summary>
    public class WindowsImageServiceOperationResult
    {
        /// <summary>
        /// Name of the image the service was changed on
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Service key name that was targeted
        /// </summary>
        public string ServiceName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the requested change
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// Requested start type (null when only DelayedAutoStart was changed)
        /// </summary>
        public WindowsImageServiceStartType? RequestedStartType { get; set; }

        /// <summary>
        /// Whether delayed auto start was requested (DelayedAutoStart = 1)
        /// </summary>
        public bool SetDelayedAutoStart { get; set; }

        /// <summary>
        /// Whether the change was applied successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message when the change failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Returns a string representation of the operation result
        /// </summary>
        public override string ToString()
        {
            var status = Success ? "succeeded" : $"failed: {ErrorMessage}";
            return $"{Operation} for {ServiceName} on {ImageName}: {status}";
        }
    }
}