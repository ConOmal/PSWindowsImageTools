using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Extracts the contents of a Windows ISO to a working folder using the OS's native disk-image mounting
    /// (Mount-DiskImage/Dismount-DiskImage from the Storage module, invoked in-process via the cmdlet's runtime)
    /// </summary>
    public class WindowsISOExtractionService
    {
        private const string ServiceName = "WindowsISOExtractionService";

        /// <summary>
        /// Mounts the given ISO, copies its full contents to destinationPath, then dismounts it
        /// </summary>
        public WindowsInstallationMedia ExtractIso(FileInfo isoPath, DirectoryInfo destinationPath, PSCmdlet cmdlet, Action<int, string>? progressCallback = null)
        {
            if (isoPath == null || !isoPath.Exists)
            {
                throw new FileNotFoundException($"ISO file not found: {isoPath?.FullName}", isoPath?.FullName);
            }

            if (!destinationPath.Exists)
            {
                destinationPath.Create();
            }

            var mountedRoot = MountIso(isoPath.FullName, cmdlet);

            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Copying media from {mountedRoot} to {destinationPath.FullName}");
                CopyDirectoryTree(mountedRoot, destinationPath.FullName, progressCallback);
            }
            finally
            {
                DismountIso(isoPath.FullName, cmdlet);
            }

            return WindowsInstallationMedia.FromRoot(destinationPath);
        }

        /// <summary>
        /// Mounts an ISO via Mount-DiskImage and returns its drive root (e.g. "D:\")
        /// </summary>
        private string MountIso(string isoPath, PSCmdlet cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Mounting ISO: {isoPath}");

            var script = $"Mount-DiskImage -ImagePath '{isoPath.Replace("'", "''")}' -PassThru | Get-Volume | Select-Object -ExpandProperty DriveLetter";
            var result = cmdlet.InvokeCommand.InvokeScript(script);
            var driveLetter = result?.FirstOrDefault()?.ToString();

            if (string.IsNullOrEmpty(driveLetter))
            {
                throw new InvalidOperationException(
                    $"Could not mount ISO file: {isoPath}. Ensure the Storage module is available and the file is a valid ISO.");
            }

            return $"{driveLetter}:\\";
        }

        /// <summary>
        /// Dismounts a previously mounted ISO
        /// </summary>
        private void DismountIso(string isoPath, PSCmdlet cmdlet)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Dismounting ISO: {isoPath}");
                var script = $"Dismount-DiskImage -ImagePath '{isoPath.Replace("'", "''")}' | Out-Null";
                cmdlet.InvokeCommand.InvokeScript(script);
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to dismount ISO {isoPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Recursively copies a directory tree, clearing read-only attributes on the copies so downstream
        /// tools (DISM, oscdimg) can freely modify them
        /// </summary>
        public static void CopyDirectoryTree(string sourceDir, string destinationDir, Action<int, string>? progressCallback = null)
        {
            var allFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            var totalFiles = allFiles.Length;
            var copiedFiles = 0;

            foreach (var sourceFile in allFiles)
            {
                var relativePath = sourceFile.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var destinationFile = Path.Combine(destinationDir, relativePath);
                var destinationFileDir = Path.GetDirectoryName(destinationFile);

                if (!string.IsNullOrEmpty(destinationFileDir) && !Directory.Exists(destinationFileDir))
                {
                    Directory.CreateDirectory(destinationFileDir);
                }

                File.Copy(sourceFile, destinationFile, overwrite: true);
                File.SetAttributes(destinationFile, FileAttributes.Normal);

                copiedFiles++;
                if (totalFiles > 0)
                {
                    var percentage = (int)((copiedFiles * 100L) / totalFiles);
                    progressCallback?.Invoke(percentage, $"Copied {copiedFiles} of {totalFiles} files");
                }
            }
        }
    }
}
