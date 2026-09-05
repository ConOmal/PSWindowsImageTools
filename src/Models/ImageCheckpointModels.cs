using System;
using System.IO;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A point-in-time snapshot of a mounted Windows image's on-disk state, for later rollback
    /// </summary>
    public class ImageCheckpointInfo
    {
        public string CheckpointId { get; set; } = string.Empty;
        public string MountId { get; set; } = string.Empty;
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public long SizeBytes { get; set; }
        public DirectoryInfo CheckpointPath { get; set; } = null!;

        public override string ToString() =>
            $"{(Label ?? CheckpointId)}: {SizeBytes / 1024.0 / 1024.0:F1} MB, {CreatedAt:yyyy-MM-dd HH:mm}UTC";
    }
}
