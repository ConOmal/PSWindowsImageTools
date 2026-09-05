using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// ProtectYourPC DWORD semantics for the OOBE key
    /// (<c>HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE\ProtectYourPC</c>)
    /// </summary>
    public enum WindowsImageOobeProtectYourPc
    {
        /// <summary>
        /// Use recommended settings (express settings on; DWORD 1)
        /// </summary>
        Recommended = 1,

        /// <summary>
        /// Recommended settings off — only important updates installed (DWORD 2)
        /// </summary>
        ImportantOnly = 2,

        /// <summary>
        /// Device is not in the recommended program (DWORD 3)
        /// </summary>
        NotInProgram = 3
    }

    /// <summary>
    /// One documented OOBE setting in the catalog of
    /// Get-WindowsImageOOBE / Set-WindowsImageOOBE
    /// </summary>
    public class WindowsImageOobeSettingDefinition
    {
        /// <summary>
        /// Friendly, parameter-shaped setting name (e.g. "SkipPrivacyExperience")
        /// </summary>
        public string SettingName { get; set; } = string.Empty;

        /// <summary>
        /// Registry value name under the OOBE key
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable meaning of the setting (including caveats)
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Returns a string representation of the definition
        /// </summary>
        public override string ToString()
        {
            return $"{SettingName} ({ValueName}): {Description}";
        }
    }

    /// <summary>
    /// One OOBE setting as reported by Get-WindowsImageOOBE from a mounted
    /// Windows image's offline SOFTWARE hive
    /// </summary>
    public class WindowsImageOobeSetting
    {
        /// <summary>
        /// Name of the image the setting was read from
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Path to the mounted Windows image directory
        /// </summary>
        public string MountPath { get; set; } = string.Empty;

        /// <summary>
        /// Friendly setting name (matches the -Set parameter names)
        /// </summary>
        public string SettingName { get; set; } = string.Empty;

        /// <summary>
        /// Registry value name under the OOBE key
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable meaning of the setting
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Whether the value exists in the image's OOBE key
        /// </summary>
        public bool IsSet { get; set; }

        /// <summary>
        /// Raw DWORD value; null when the value is not set
        /// </summary>
        public int? Value { get; set; }

        /// <summary>
        /// Display state ("Set: 1", "Set: 0" or "Not set")
        /// </summary>
        public string State { get; set; } = string.Empty;

        /// <summary>
        /// Returns a string representation of the setting
        /// </summary>
        public override string ToString()
        {
            return $"{SettingName} = {State} on {ImageName}";
        }
    }

    /// <summary>
    /// One requested Set-WindowsImageOOBE change: write ValueName = Value
    /// (DWORD 1 or 0), or remove the value when Value is null
    /// </summary>
    public class WindowsImageOobeChange
    {
        /// <summary>
        /// Registry value name under the OOBE key
        /// </summary>
        public string ValueName { get; set; } = string.Empty;

        /// <summary>
        /// DWORD value to write (1 or 0); null = remove the value
        /// </summary>
        public int? Value { get; set; }

        /// <summary>
        /// Returns a string representation of the change
        /// </summary>
        public override string ToString()
        {
            return Value.HasValue ? $"{ValueName}={Value.Value}" : $"Remove {ValueName}";
        }
    }

    /// <summary>
    /// Result of applying OOBE settings to one mounted Windows image,
    /// from Set-WindowsImageOOBE (one result per image)
    /// </summary>
    public class WindowsImageOobeOperationResult
    {
        /// <summary>
        /// Name of the image the settings were applied to
        /// </summary>
        public string ImageName { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the requested change (same text as ShouldProcess)
        /// </summary>
        public string Operation { get; set; } = string.Empty;

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
            return $"{Operation} on {ImageName}: {status}";
        }
    }
}
