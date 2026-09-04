using System.IO;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Windows installation media extracted from an ISO, with resolved paths to its key files
    /// </summary>
    public class WindowsInstallationMedia
    {
        /// <summary>
        /// Root directory the media was extracted to
        /// </summary>
        public DirectoryInfo Root { get; set; } = null!;

        /// <summary>
        /// Path to sources\install.wim, if present
        /// </summary>
        public FileInfo? InstallWim { get; set; }

        /// <summary>
        /// Path to sources\install.esd, if present (used instead of install.wim on some media)
        /// </summary>
        public FileInfo? InstallEsd { get; set; }

        /// <summary>
        /// Path to sources\boot.wim, if present
        /// </summary>
        public FileInfo? BootWim { get; set; }

        /// <summary>
        /// Resolves a WindowsInstallationMedia from an extracted media root directory
        /// </summary>
        public static WindowsInstallationMedia FromRoot(DirectoryInfo root)
        {
            var installWim = new FileInfo(Path.Combine(root.FullName, "sources", "install.wim"));
            var installEsd = new FileInfo(Path.Combine(root.FullName, "sources", "install.esd"));
            var bootWim = new FileInfo(Path.Combine(root.FullName, "sources", "boot.wim"));

            return new WindowsInstallationMedia
            {
                Root = root,
                InstallWim = installWim.Exists ? installWim : null,
                InstallEsd = installEsd.Exists ? installEsd : null,
                BootWim = bootWim.Exists ? bootWim : null
            };
        }

        /// <summary>
        /// Returns the root path
        /// </summary>
        public override string ToString()
        {
            return Root?.FullName ?? string.Empty;
        }
    }
}
