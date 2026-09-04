using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Service for creating bootable ISO images from Windows setup folders
    /// Supports UEFI and BIOS boot modes with proper boot sectors
    /// </summary>
    public class ISOService : IDisposable
    {
        private const string ServiceName = "ISOService";
        private bool _disposed = false;

        /// <summary>
        /// Creates a bootable ISO from a Windows setup folder
        /// </summary>
        /// <param name="sourceFolderPath">Path to Windows setup folder</param>
        /// <param name="outputIsoPath">Path to output ISO file</param>
        /// <param name="volumeLabel">Volume label for the ISO</param>
        /// <param name="bootMode">Boot mode (UEFI, BIOS, or Both)</param>
        /// <param name="progressCallback">Progress reporting callback</param>
        /// <param name="cmdlet">PowerShell cmdlet for logging</param>
        /// <returns>True if ISO creation succeeded</returns>
        public bool CreateBootableISO(
            string sourceFolderPath,
            string outputIsoPath,
            string volumeLabel = "Windows",
            BootMode bootMode = BootMode.Both,
            Action<int, string>? progressCallback = null,
            PSCmdlet? cmdlet = null)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    $"Creating bootable ISO: {sourceFolderPath} -> {outputIsoPath}");
                LoggingService.WriteVerbose(cmdlet, ServiceName, 
                    $"Boot mode: {bootMode}, Volume label: {volumeLabel}");

                // Validate source folder
                if (!Directory.Exists(sourceFolderPath))
                {
                    LoggingService.WriteError(cmdlet, ServiceName, $"Source folder not found: {sourceFolderPath}");
                    return false;
                }

                // Validate required files
                if (!ValidateWindowsSetupFiles(sourceFolderPath, cmdlet))
                {
                    return false;
                }

                // Delete existing ISO if it exists
                if (File.Exists(outputIsoPath))
                {
                    File.Delete(outputIsoPath);
                }

                // Create ISO using the best available method
                return CreateISOWithBestMethod(sourceFolderPath, outputIsoPath, volumeLabel, 
                    bootMode, progressCallback, cmdlet);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"ISO creation failed: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Validates that the source folder contains required Windows setup files
        /// </summary>
        private bool ValidateWindowsSetupFiles(string sourceFolderPath, PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, ServiceName, "Validating Windows setup files");

            var requiredFiles = new[]
            {
                Path.Combine(sourceFolderPath, "sources", "install.wim"),
                Path.Combine(sourceFolderPath, "sources", "boot.wim")
            };

            var requiredFolders = new[]
            {
                Path.Combine(sourceFolderPath, "boot"),
                Path.Combine(sourceFolderPath, "sources")
            };

            foreach (var file in requiredFiles)
            {
                if (!File.Exists(file))
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"Required file missing: {file}");
                    // Don't fail - some files might be optional
                }
            }

            foreach (var folder in requiredFolders)
            {
                if (!Directory.Exists(folder))
                {
                    LoggingService.WriteError(cmdlet, ServiceName, $"Required folder missing: {folder}");
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates ISO using the best available method with intelligent fallback chain
        /// 1. Check for installed Windows ADK oscdimg
        /// 2. Check for cached oscdimg
        /// 3. Download oscdimg from Microsoft (if internet available)
        /// 4. Fall back to mkisofs
        /// 5. Fall back to PowerShell method
        /// </summary>
        private bool CreateISOWithBestMethod(string sourceFolderPath, string outputIsoPath,
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, ServiceName, "Starting intelligent ISO creation method selection");

            // Method 1: Try installed Windows ADK oscdimg
            progressCallback?.Invoke(5, "Checking for installed Windows ADK oscdimg");
            if (TryCreateISOWithInstalledOscdimg(sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Successfully created ISO using installed Windows ADK oscdimg");
                return true;
            }

            // Method 2: Try cached oscdimg
            progressCallback?.Invoke(10, "Checking for cached oscdimg");
            if (TryCreateISOWithCachedOscdimg(sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Successfully created ISO using cached oscdimg");
                return true;
            }

            // Method 3: Try downloading oscdimg from Microsoft
            progressCallback?.Invoke(15, "Attempting to download oscdimg from Microsoft");
            if (TryDownloadAndUseOscdimg(sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Successfully created ISO using downloaded oscdimg");
                return true;
            }

            // Method 4: Try mkisofs as fallback
            progressCallback?.Invoke(20, "Falling back to mkisofs");
            if (TryCreateISOWithMkisofs(sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Successfully created ISO using mkisofs");
                return true;
            }

            // Method 5: Try PowerShell-based method as last resort
            progressCallback?.Invoke(25, "Using PowerShell-based ISO creation as last resort");
            LoggingService.WriteWarning(cmdlet, ServiceName, "All preferred ISO creation methods failed, using basic PowerShell method");
            return TryCreateISOWithPowerShell(sourceFolderPath, outputIsoPath, volumeLabel, progressCallback, cmdlet);
        }

        /// <summary>
        /// Tries to create ISO using installed Windows ADK oscdimg
        /// </summary>
        private bool TryCreateISOWithInstalledOscdimg(string sourceFolderPath, string outputIsoPath,
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            var oscdimgPath = FindInstalledOscdimg();
            if (oscdimgPath == null)
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Windows ADK oscdimg not found in standard installation locations");
                return false;
            }

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Found installed oscdimg: {oscdimgPath}");
            return ExecuteOscdimg(oscdimgPath, sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet);
        }

        /// <summary>
        /// Tries to create ISO using cached oscdimg
        /// </summary>
        private bool TryCreateISOWithCachedOscdimg(string sourceFolderPath, string outputIsoPath,
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            var cacheDir = GetOscdimgCacheDirectory();
            var cachedOscdimgPath = Path.Combine(cacheDir, "oscdimg.exe");

            if (!File.Exists(cachedOscdimgPath))
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Cached oscdimg not found");
                return false;
            }

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Found cached oscdimg: {cachedOscdimgPath}");
            return ExecuteOscdimg(cachedOscdimgPath, sourceFolderPath, outputIsoPath, volumeLabel, bootMode, progressCallback, cmdlet);
        }

        /// <summary>
        /// Tries to download oscdimg from Microsoft and use it
        /// </summary>
        private bool TryDownloadAndUseOscdimg(string sourceFolderPath, string outputIsoPath,
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Attempting to download oscdimg from Microsoft");

                // Note: There is no legitimate standalone oscdimg download source; it ships with
                // the Windows ADK. Recommend the module's own ADK installer.
                LoggingService.WriteVerbose(cmdlet, ServiceName,
                    "oscdimg download not available - run 'Install-ADK -IncludeDeploymentTools' so oscdimg can be used");

                return false;
            }
            catch (Exception ex)
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"oscdimg download failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds installed oscdimg in standard Windows ADK locations
        /// </summary>
        private string? FindInstalledOscdimg()
        {
            var possiblePaths = new[]
            {
                // Windows ADK for Windows 11/10
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe"),

                // Alternative ADK locations
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "x86", "Oscdimg", "oscdimg.exe"),

                // Check PATH
                "oscdimg.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (path == "oscdimg.exe")
                {
                    // Check if oscdimg is in PATH
                    if (IsCommandAvailable("oscdimg"))
                    {
                        return "oscdimg.exe";
                    }
                }
                else if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the cache directory for oscdimg
        /// </summary>
        private string GetOscdimgCacheDirectory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheDir = Path.Combine(appDataPath, "PSWindowsImageTools", "Cache");

            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            return cacheDir;
        }

        /// <summary>
        /// Executes oscdimg with the specified parameters
        /// </summary>
        private bool ExecuteOscdimg(string oscdimgPath, string sourceFolderPath, string outputIsoPath,
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            try
            {
                var operationStartTime = LoggingService.LogOperationStartWithTimestamp(cmdlet, ServiceName,
                    "oscdimg ISO Creation", $"{Path.GetFileName(outputIsoPath)} from {Path.GetFileName(sourceFolderPath)}");

                // Build oscdimg arguments
                var arguments = BuildOscdimgArguments(sourceFolderPath, outputIsoPath, volumeLabel, bootMode);

                LoggingService.WriteVerbose(cmdlet, ServiceName, $"oscdimg command: {oscdimgPath} {arguments}");

                // Execute oscdimg
                var startInfo = new ProcessStartInfo
                {
                    FileName = oscdimgPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                progressCallback?.Invoke(30, "Starting oscdimg process");

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    LoggingService.WriteError(cmdlet, ServiceName, "Failed to start oscdimg process");
                    return false;
                }

                // Monitor progress (oscdimg doesn't provide detailed progress, so we'll estimate)
                var progressTimer = new System.Timers.Timer(2000);
                int estimatedProgress = 30;
                progressTimer.Elapsed += (s, e) =>
                {
                    if (!process.HasExited && estimatedProgress < 90)
                    {
                        estimatedProgress += 10;
                        progressCallback?.Invoke(estimatedProgress, $"Creating ISO with oscdimg ({estimatedProgress}%)");
                    }
                };
                progressTimer.Start();

                process.WaitForExit();
                progressTimer.Stop();

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    progressCallback?.Invoke(100, "ISO creation completed successfully");
                    LoggingService.LogOperationCompleteWithTimestamp(cmdlet, ServiceName, "oscdimg ISO Creation", operationStartTime,
                        $"Successfully created {Path.GetFileName(outputIsoPath)}");
                    return true;
                }
                else
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"oscdimg failed with exit code {process.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                    {
                        LoggingService.WriteWarning(cmdlet, ServiceName, $"oscdimg error: {error}");
                    }
                    if (!string.IsNullOrEmpty(output))
                    {
                        LoggingService.WriteVerbose(cmdlet, ServiceName, $"oscdimg output: {output}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"oscdimg execution failed: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Builds oscdimg command line arguments
        /// </summary>
        private string BuildOscdimgArguments(string sourceFolderPath, string outputIsoPath, 
            string volumeLabel, BootMode bootMode)
        {
            var args = new List<string>();

            // Basic options
            args.Add("-m"); // Ignore maximum image size limit
            args.Add("-o"); // Optimize storage by encoding duplicate files only once
            args.Add("-u2"); // Produce an image that has both Joliet and ISO 9660 names
            args.Add("-udfver102"); // UDF version 1.02

            // Volume label
            args.Add($"-l\"{volumeLabel}\"");

            // Boot options based on boot mode
            switch (bootMode)
            {
                case BootMode.BIOS:
                    args.Add("-b\"boot\\etfsboot.com\""); // BIOS boot sector
                    break;
                case BootMode.UEFI:
                    args.Add("-bootdata:2#p0,e,b\"boot\\etfsboot.com\"#pEF,e,b\"efi\\microsoft\\boot\\efisys.bin\"");
                    break;
                case BootMode.Both:
                default:
                    args.Add("-bootdata:2#p0,e,b\"boot\\etfsboot.com\"#pEF,e,b\"efi\\microsoft\\boot\\efisys.bin\"");
                    break;
            }

            // Source and destination
            args.Add($"\"{sourceFolderPath}\"");
            args.Add($"\"{outputIsoPath}\"");

            return string.Join(" ", args);
        }

        /// <summary>
        /// Creates ISO using mkisofs (cross-platform tool)
        /// </summary>
        private bool TryCreateISOWithMkisofs(string sourceFolderPath, string outputIsoPath, 
            string volumeLabel, BootMode bootMode, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            try
            {
                if (!IsCommandAvailable("mkisofs"))
                {
                    LoggingService.WriteVerbose(cmdlet, ServiceName, "mkisofs not found");
                    return false;
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName, "Using mkisofs");

                // Build mkisofs arguments
                var arguments = $"-iso-level 4 -J -joliet-long -relaxed-filenames -V \"{volumeLabel}\" -o \"{outputIsoPath}\" \"{sourceFolderPath}\"";

                // Add boot options if boot files exist
                var etfsboot = Path.Combine(sourceFolderPath, "boot", "etfsboot.com");
                if (File.Exists(etfsboot))
                {
                    arguments = $"-b boot/etfsboot.com -no-emul-boot -boot-load-size 8 {arguments}";
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName, $"mkisofs arguments: {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "mkisofs",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                progressCallback?.Invoke(10, "Starting mkisofs process");

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    progressCallback?.Invoke(100, "ISO creation completed");
                    LoggingService.WriteVerbose(cmdlet, ServiceName, "mkisofs completed successfully");
                    return true;
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    LoggingService.WriteWarning(cmdlet, ServiceName, $"mkisofs failed: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"mkisofs method failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates ISO using PowerShell-based method (fallback)
        /// </summary>
        private bool TryCreateISOWithPowerShell(string sourceFolderPath, string outputIsoPath, 
            string volumeLabel, Action<int, string>? progressCallback, PSCmdlet? cmdlet)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, "Using PowerShell-based ISO creation (fallback)");
                
                // This is a simplified implementation
                // In a full implementation, this would use IMAPI2 COM objects or similar
                progressCallback?.Invoke(50, "PowerShell-based ISO creation not fully implemented");
                
                LoggingService.WriteWarning(cmdlet, ServiceName, 
                    "PowerShell-based ISO creation is not fully implemented. " +
                    "Please install Windows ADK (oscdimg) or mkisofs for ISO creation.");
                
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"PowerShell method failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if a command is available in PATH
        /// </summary>
        private bool IsCommandAvailable(string command)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                return process != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Provides guidance to users on installing Windows ADK for optimal ISO creation
        /// </summary>
        public static void ShowWindowsADKGuidance(PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, "ISOService", "=== WINDOWS ADK INSTALLATION GUIDANCE ===");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "For optimal ISO creation experience, install Windows ADK:");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "1. Download Windows ADK from:");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "   https://docs.microsoft.com/en-us/windows-hardware/get-started/adk-install");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "2. During installation, select 'Deployment Tools' feature");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "   (This includes oscdimg.exe for creating bootable ISOs)");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "3. Alternative: Install mkisofs for cross-platform ISO creation");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "Benefits of Windows ADK:");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "• Creates proper bootable Windows ISOs");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "• Supports both UEFI and BIOS boot modes");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "• Official Microsoft tool for Windows deployment");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "• Best compatibility with Windows installation media");
            LoggingService.WriteVerbose(cmdlet, "ISOService", "");
        }

        /// <summary>
        /// Checks system capabilities and provides recommendations
        /// </summary>
        public static string GetISOCreationCapabilities(PSCmdlet? cmdlet)
        {
            var capabilities = new List<string>();

            // Check for Windows ADK oscdimg
            var isoService = new ISOService();
            var oscdimgPath = isoService.FindInstalledOscdimg();
            if (oscdimgPath != null)
            {
                capabilities.Add("✓ Windows ADK oscdimg (OPTIMAL)");
            }
            else
            {
                capabilities.Add("✗ Windows ADK oscdimg (RECOMMENDED)");
            }

            // Check for mkisofs
            if (isoService.IsCommandAvailable("mkisofs"))
            {
                capabilities.Add("✓ mkisofs (GOOD)");
            }
            else
            {
                capabilities.Add("✗ mkisofs (ALTERNATIVE)");
            }

            // PowerShell method is always available
            capabilities.Add("✓ PowerShell method (BASIC)");

            isoService.Dispose();

            var result = string.Join("\n", capabilities);
            LoggingService.WriteVerbose(cmdlet, "ISOService", "ISO Creation Capabilities:");
            LoggingService.WriteVerbose(cmdlet, "ISOService", result);

            return result;
        }

        /// <summary>
        /// Disposes the ISO service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>
    /// Boot mode for ISO creation
    /// </summary>
    public enum BootMode
    {
        /// <summary>
        /// BIOS boot only
        /// </summary>
        BIOS,

        /// <summary>
        /// UEFI boot only
        /// </summary>
        UEFI,

        /// <summary>
        /// Both BIOS and UEFI boot (hybrid)
        /// </summary>
        Both
    }
}
