using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Result of creating a bootable ISO
    /// </summary>
    public class ISOCreationResult
    {
        /// <summary>
        /// Path to the Windows setup folder used as source
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>
        /// Path of the created ISO
        /// </summary>
        public string OutputIsoPath { get; set; } = string.Empty;

        /// <summary>
        /// Volume label applied to the ISO
        /// </summary>
        public string VolumeLabel { get; set; } = string.Empty;

        /// <summary>
        /// Boot mode requested (UEFI, BIOS, Both)
        /// </summary>
        public string BootMode { get; set; } = string.Empty;

        /// <summary>
        /// Whether the ISO was created successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Size of the created ISO in bytes
        /// </summary>
        public long OutputSize { get; set; }

        /// <summary>
        /// Error message when creation failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// How long creation took
        /// </summary>
        public TimeSpan Duration { get; set; }

        public override string ToString()
        {
            var status = Success ? $"SUCCESS ({OutputSize / 1024 / 1024} MB)" : $"FAILED: {ErrorMessage}";
            return $"ISO {OutputIsoPath}: {status} ({Duration.TotalSeconds:F1}s)";
        }
    }
}
