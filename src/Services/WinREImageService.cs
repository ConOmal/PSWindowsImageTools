using System.IO;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Extracts and re-embeds the WinRE image (Windows\System32\Recovery\Winre.wim) nested inside a mounted Windows image
    /// </summary>
    public static class WinREImageService
    {
        /// <summary>
        /// Relative path, under a mounted Windows image, to the embedded WinRE image
        /// </summary>
        public const string EmbeddedWinREPath = @"Windows\System32\Recovery\Winre.wim";

        /// <summary>
        /// Checks whether a mounted Windows image has an embedded WinRE image, returning its full path if so
        /// </summary>
        public static bool TryGetEmbeddedWinREPath(string mountPath, out string winREPath)
        {
            winREPath = Path.Combine(mountPath, EmbeddedWinREPath);
            return File.Exists(winREPath);
        }

        /// <summary>
        /// Copies the embedded WinRE image out of a mounted Windows image to a standalone file
        /// </summary>
        public static void ExtractEmbeddedWinRE(string mountPath, string destinationWimPath)
        {
            if (!TryGetEmbeddedWinREPath(mountPath, out var sourcePath))
            {
                throw new FileNotFoundException($"No embedded WinRE image found at {Path.Combine(mountPath, EmbeddedWinREPath)}");
            }

            var destinationDir = Path.GetDirectoryName(destinationWimPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            File.Copy(sourcePath, destinationWimPath, overwrite: true);
            File.SetAttributes(destinationWimPath, FileAttributes.Normal);
        }

        /// <summary>
        /// Copies an updated standalone WinRE image back into its nested location inside a mounted Windows image
        /// </summary>
        public static void ReplaceEmbeddedWinRE(string mountPath, string updatedWimPath)
        {
            if (!File.Exists(updatedWimPath))
            {
                throw new FileNotFoundException($"Updated WinRE image not found: {updatedWimPath}");
            }

            var destinationPath = Path.Combine(mountPath, EmbeddedWinREPath);
            var destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            if (File.Exists(destinationPath))
            {
                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }

            File.Copy(updatedWimPath, destinationPath, overwrite: true);
        }
    }
}
