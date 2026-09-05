using System.Collections.Generic;
using System.IO;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Located boot.wim on extracted Windows installation media, with the images it contains
    /// </summary>
    public class BootImageInfo
    {
        public FileInfo Path { get; set; } = null!;
        public string? SourceMediaRoot { get; set; }
        public int ImageCount => Images.Count;
        public List<WindowsImageInfo> Images { get; set; } = new List<WindowsImageInfo>();

        public override string ToString() => $"{Path.FullName} ({ImageCount} image(s))";
    }
}
