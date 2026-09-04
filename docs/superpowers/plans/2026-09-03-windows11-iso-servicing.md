# Windows 11 ISO Servicing Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give PSWindowsImageTools an end-to-end path from "latest official Windows 11 ISO" to "customized bootable ISO": fetch the ISO, extract it, service `install.wim`/`boot.wim` (with `winre.wim` handled transparently), and rebuild a bootable ISO.

**Architecture:** Four additions layered on the module's existing service + cmdlet + model pattern: (1) `WindowsISODownloadService`/`Get-WindowsISODownloadInfo`/`Save-WindowsISO` mirror the existing Search→GetUrl→Save Windows Update Catalog pattern; (2) `WindowsISOExtractionService`/`Export-WindowsISO` mounts an ISO via `Mount-DiskImage`/`Dismount-DiskImage` (shelled out to `powershell.exe`, matching this codebase's existing `Process.Start` pattern in `ISOService`/`InvokeMediaDynamicUpdateCmdlet`) and copies its tree out; (3) `WinREImageService` plus small edits to `MountWindowsImageListCmdlet`/`DismountWindowsImageListCmdlet` make the `winre.wim` nested inside `install.wim` mount/dismount transparently as `MountedWindowsImage.WinRE`; (4) `New-WindowsISO` finally wires up the already-implemented (but currently unused) `ISOService.CreateBootableISO`.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled), PowerShell binary cmdlets (`System.Management.Automation` via `PowerShellStandard.Library`), Newtonsoft.Json (already referenced), xUnit for tests (already referenced in `tests/PSWindowsImageTools.Tests`).

**Spec:** `docs/superpowers/specs/2026-09-03-windows11-iso-servicing-design.md`

## Global Constraints

- Target framework for all new production code: `netstandard2.0`, `LangVersion 8.0`, `Nullable enable` (from `src/PSWindowsImageTools.csproj`) — do not use C# 9+-only syntax (e.g. `is not`, `ArgumentList` on `ProcessStartInfo`) or APIs outside netstandard2.0.
- No new NuGet package references — reuse what's already referenced: `System.Net.Http`, `Newtonsoft.Json`, `PowerShellStandard.Library`.
- Every new public cmdlet must be added to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (wildcards are explicitly disallowed there).
- Follow existing conventions: services in `src/Services/*Service.cs`, cmdlets in `src/Cmdlets/*Cmdlet.cs`, models in `src/Models/*.cs`; use `LoggingService.WriteVerbose/WriteWarning/WriteError` and `LoggingService.LogOperationStartWithTimestamp`/`LogOperationCompleteWithTimestamp` for logging, `ProgressService.Create*ProgressCallback` for progress.
- Tests live in `tests/PSWindowsImageTools.Tests/*.cs` (xUnit, `Assert.*`), one test class per production class being tested, following the existing `FormatUtilityServiceTests.cs` style (plain `[Fact]`/`[Theory]`, no mocking framework is referenced in this project).
- Build/test commands: `dotnet build src/PSWindowsImageTools.csproj` and `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj` (reference the `.csproj` files directly, not the `.sln`).
- Real DISM mounts, real ISO mounting, and the live Microsoft download endpoint all require administrator rights and/or real files and cannot be unit tested — per the spec's Testing section, those paths are verified manually end-to-end; only pure logic (parsing, path resolution, file-copy helpers) gets automated tests in this plan.

---

### Task 1: `WindowsInstallationMedia` model

**Files:**
- Create: `src/Models/WindowsInstallationMedia.cs`
- Test: `tests/PSWindowsImageTools.Tests/WindowsInstallationMediaTests.cs`

**Interfaces:**
- Produces: `PSWindowsImageTools.Models.WindowsInstallationMedia` with `DirectoryInfo Root`, `FileInfo? InstallWim`, `FileInfo? BootWim`, and `static WindowsInstallationMedia FromRoot(DirectoryInfo root)`. Used by Task 2's `WindowsISOExtractionService.ExtractIso`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsInstallationMediaTests : IDisposable
    {
        private readonly string _tempRoot;

        public WindowsInstallationMediaTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsInstallationMediaTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void FromRoot_ResolvesExistingWimFiles()
        {
            var sourcesDir = Path.Combine(_tempRoot, "sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllText(Path.Combine(sourcesDir, "install.wim"), "fake");
            File.WriteAllText(Path.Combine(sourcesDir, "boot.wim"), "fake");

            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.NotNull(media.InstallWim);
            Assert.NotNull(media.BootWim);
            Assert.Equal(Path.Combine(sourcesDir, "install.wim"), media.InstallWim!.FullName);
        }

        [Fact]
        public void FromRoot_ReturnsNullForMissingFiles()
        {
            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.Null(media.InstallWim);
            Assert.Null(media.BootWim);
        }

        [Fact]
        public void ToString_ReturnsRootPath()
        {
            var media = WindowsInstallationMedia.FromRoot(new DirectoryInfo(_tempRoot));

            Assert.Equal(_tempRoot, media.ToString());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsInstallationMediaTests"`
Expected: FAIL (build error — `WindowsInstallationMedia` does not exist yet)

- [ ] **Step 3: Write minimal implementation**

```csharp
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
        /// Path to sources\boot.wim, if present
        /// </summary>
        public FileInfo? BootWim { get; set; }

        /// <summary>
        /// Resolves a WindowsInstallationMedia from an extracted media root directory
        /// </summary>
        public static WindowsInstallationMedia FromRoot(DirectoryInfo root)
        {
            var installWim = new FileInfo(Path.Combine(root.FullName, "sources", "install.wim"));
            var bootWim = new FileInfo(Path.Combine(root.FullName, "sources", "boot.wim"));

            return new WindowsInstallationMedia
            {
                Root = root,
                InstallWim = installWim.Exists ? installWim : null,
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsInstallationMediaTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Models/WindowsInstallationMedia.cs tests/PSWindowsImageTools.Tests/WindowsInstallationMediaTests.cs
git commit -m "Add WindowsInstallationMedia model for extracted ISO media"
```

---

### Task 2: `WindowsISOExtractionService` + `Export-WindowsISO` cmdlet

**Files:**
- Create: `src/Services/WindowsISOExtractionService.cs`
- Create: `src/Cmdlets/ExportWindowsISOCmdlet.cs`
- Modify: `src/Cmdlets/GetWindowsImageListCmdlet.cs:321-334` (the `GetImageFilePath` method)
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (add `'Export-WindowsISO'` to `CmdletsToExport`)
- Test: `tests/PSWindowsImageTools.Tests/WindowsISOExtractionServiceTests.cs`

**Interfaces:**
- Consumes: `PSWindowsImageTools.Models.WindowsInstallationMedia.FromRoot(DirectoryInfo)` (Task 1).
- Produces: `PSWindowsImageTools.Services.WindowsISOExtractionService` with instance method `WindowsInstallationMedia ExtractIso(FileInfo isoPath, DirectoryInfo destinationPath, PSCmdlet? cmdlet, Action<int,string>? progressCallback = null)` and `public static void CopyDirectoryTree(string sourceDir, string destinationDir, Action<int,string>? progressCallback = null)`. The `Export-WindowsISO` cmdlet (`[Cmdlet(VerbsData.Export, "WindowsISO")]`) outputs `WindowsInstallationMedia`.

- [ ] **Step 1: Write the failing test (pure copy-logic only — mounting a real ISO needs admin rights and is verified manually in Step 7)**

```csharp
using System;
using System.IO;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsISOExtractionServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public WindowsISOExtractionServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsISOExtractionServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void CopyDirectoryTree_CopiesNestedFilesAndClearsReadOnly()
        {
            var source = Path.Combine(_tempRoot, "source");
            var destination = Path.Combine(_tempRoot, "destination");
            Directory.CreateDirectory(Path.Combine(source, "sources"));
            Directory.CreateDirectory(Path.Combine(source, "boot"));

            var installWim = Path.Combine(source, "sources", "install.wim");
            File.WriteAllText(installWim, "install-wim-content");
            File.SetAttributes(installWim, FileAttributes.ReadOnly);

            File.WriteAllText(Path.Combine(source, "boot", "bootmgr"), "bootmgr-content");

            WindowsISOExtractionService.CopyDirectoryTree(source, destination);

            var copiedInstallWim = Path.Combine(destination, "sources", "install.wim");
            var copiedBootmgr = Path.Combine(destination, "boot", "bootmgr");

            Assert.True(File.Exists(copiedInstallWim));
            Assert.Equal("install-wim-content", File.ReadAllText(copiedInstallWim));
            Assert.False(File.GetAttributes(copiedInstallWim).HasFlag(FileAttributes.ReadOnly));

            Assert.True(File.Exists(copiedBootmgr));
            Assert.Equal("bootmgr-content", File.ReadAllText(copiedBootmgr));
        }

        [Fact]
        public void CopyDirectoryTree_ReportsCompletionProgress()
        {
            var source = Path.Combine(_tempRoot, "source2");
            var destination = Path.Combine(_tempRoot, "destination2");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "b.txt"), "b");

            var reportedPercentages = new System.Collections.Generic.List<int>();
            WindowsISOExtractionService.CopyDirectoryTree(source, destination, (percentage, status) => reportedPercentages.Add(percentage));

            Assert.Equal(100, reportedPercentages[reportedPercentages.Count - 1]);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsISOExtractionServiceTests"`
Expected: FAIL (build error — `WindowsISOExtractionService` does not exist yet)

- [ ] **Step 3: Write the service**

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Extracts the contents of a Windows ISO to a working folder using the OS's native disk-image mounting
    /// (Mount-DiskImage/Dismount-DiskImage from the Storage module, invoked via powershell.exe)
    /// </summary>
    public class WindowsISOExtractionService : IDisposable
    {
        private const string ServiceName = "WindowsISOExtractionService";
        private bool _disposed;

        /// <summary>
        /// Mounts the given ISO, copies its full contents to destinationPath, then dismounts it
        /// </summary>
        public WindowsInstallationMedia ExtractIso(FileInfo isoPath, DirectoryInfo destinationPath, PSCmdlet? cmdlet, Action<int, string>? progressCallback = null)
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
        private string MountIso(string isoPath, PSCmdlet? cmdlet)
        {
            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Mounting ISO: {isoPath}");

            const string script = "param([string]$IsoPath) (Mount-DiskImage -ImagePath $IsoPath -PassThru | Get-Volume).DriveLetter";
            var driveLetter = RunPowerShellScript(script, isoPath).Trim();

            if (string.IsNullOrEmpty(driveLetter))
            {
                throw new InvalidOperationException($"Mounted ISO {isoPath} did not report a drive letter");
            }

            return $"{driveLetter}:\\";
        }

        /// <summary>
        /// Dismounts a previously mounted ISO
        /// </summary>
        private void DismountIso(string isoPath, PSCmdlet? cmdlet)
        {
            try
            {
                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Dismounting ISO: {isoPath}");
                const string script = "param([string]$IsoPath) Dismount-DiskImage -ImagePath $IsoPath | Out-Null";
                RunPowerShellScript(script, isoPath);
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(cmdlet, ServiceName, $"Failed to dismount ISO {isoPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs a PowerShell script (written to a temp .ps1 file) with a single string argument, returning its stdout.
        /// Written to a temp file rather than passed as -Command text so paths never need shell-escaping.
        /// </summary>
        private static string RunPowerShellScript(string script, string argument)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"PSWindowsImageTools_{Guid.NewGuid():N}.ps1");
            File.WriteAllText(scriptPath, script);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" \"{argument}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start powershell.exe");
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"powershell.exe exited with code {process.ExitCode}: {error}");
                }

                return output;
            }
            finally
            {
                File.Delete(scriptPath);
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

        /// <summary>
        /// Disposes the service
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
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsISOExtractionServiceTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Add the `Export-WindowsISO` cmdlet**

```csharp
using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Extracts a Windows ISO's contents to a working folder, ready for Get-WindowsImageList
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsISO")]
    [OutputType(typeof(WindowsInstallationMedia))]
    public class ExportWindowsISOCmdlet : PSCmdlet
    {
        /// <summary>
        /// Path to the Windows ISO file
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, HelpMessage = "Path to the Windows ISO file")]
        [ValidateNotNull]
        public FileInfo IsoPath { get; set; } = null!;

        /// <summary>
        /// Destination folder to extract the ISO contents to
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination folder to extract the ISO contents to")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        private const string ComponentName = "ExportWindowsISO";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!IsoPath.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new FileNotFoundException($"ISO file not found: {IsoPath.FullName}", IsoPath.FullName),
                    "IsoFileNotFound",
                    ErrorCategory.ObjectNotFound,
                    IsoPath.FullName));
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Export Windows ISO", $"{IsoPath.FullName} -> {DestinationPath.FullName}");

            try
            {
                var progressCallback = ProgressService.CreateProgressCallback(
                    this, "Extracting Windows ISO", IsoPath.Name, 1, 1);

                using var extractionService = new WindowsISOExtractionService();
                var media = extractionService.ExtractIso(IsoPath, DestinationPath, this, progressCallback);

                LoggingService.CompleteProgress(this, "Extracting Windows ISO");

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Export Windows ISO", operationStartTime,
                    $"Extracted to {DestinationPath.FullName}");

                WriteObject(media);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to export ISO: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "ExportWindowsISOFailed", ErrorCategory.NotSpecified, IsoPath.FullName));
            }
        }
    }
}
```

- [ ] **Step 6: Update `Get-WindowsImageList`'s ISO error message to point at `Export-WindowsISO`**

In `src/Cmdlets/GetWindowsImageListCmdlet.cs`, replace the body of `GetImageFilePath` (currently lines 321-334):

```csharp
        /// <summary>
        /// Determines the actual image file path, handling ISO files
        /// </summary>
        /// <param name="inputPath">Input file path</param>
        /// <returns>Path to the WIM/ESD file to process</returns>
        private string GetImageFilePath(string inputPath)
        {
            var extension = Path.GetExtension(inputPath).ToLowerInvariant();

            if (extension == ".iso")
            {
                LoggingService.WriteVerbose(this, "ISO file detected - DISM cannot write changes back into a WIM on a read-only mounted ISO");
                throw new NotSupportedException(
                    "Direct ISO processing is not supported: DISM cannot commit changes back into a WIM sitting " +
                    "on a read-only mounted ISO. Run Export-WindowsISO first, then point ImagePath at the " +
                    "extracted sources\\install.wim or sources\\boot.wim.");
            }

            return inputPath;
        }
```

- [ ] **Step 7: Add `Export-WindowsISO` to the module manifest**

In `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`, in the `CmdletsToExport` array, add a new group right after the `# ESD/ISO Conversion` group (after `'Convert-ESDToWindowsImage',`):

```
        # ISO Media Management
        'Export-WindowsISO',
```

- [ ] **Step 8: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 9: Manual verification (requires a real Windows ISO and administrator rights — not automatable in this test suite)**

```powershell
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1 -Force
$media = Export-WindowsISO -IsoPath C:\ISO\Win11.iso -DestinationPath C:\Media\Win11
$media.InstallWim.Exists   # expect True
$media.BootWim.Exists      # expect True
Get-DiskImage | Where-Object { $_.ImagePath -eq 'C:\ISO\Win11.iso' }   # expect no results (dismounted)
```

- [ ] **Step 10: Commit**

```bash
git add src/Services/WindowsISOExtractionService.cs src/Cmdlets/ExportWindowsISOCmdlet.cs src/Cmdlets/GetWindowsImageListCmdlet.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1 tests/PSWindowsImageTools.Tests/WindowsISOExtractionServiceTests.cs
git commit -m "Add Export-WindowsISO to extract ISO contents for servicing"
```

---

### Task 3: `WinREImageService` and `MountedWindowsImage.WinRE`

**Files:**
- Create: `src/Services/WinREImageService.cs`
- Modify: `src/Models/MountedWindowsImage.cs` (add `WinRE` property)
- Test: `tests/PSWindowsImageTools.Tests/WinREImageServiceTests.cs`

**Interfaces:**
- Produces: `PSWindowsImageTools.Services.WinREImageService` — `const string EmbeddedWinREPath`, `static bool TryGetEmbeddedWinREPath(string mountPath, out string winREPath)`, `static void ExtractEmbeddedWinRE(string mountPath, string destinationWimPath)`, `static void ReplaceEmbeddedWinRE(string mountPath, string updatedWimPath)`. `MountedWindowsImage.WinRE` becomes `MountedWindowsImage?`. Used by Task 4 and Task 5.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WinREImageServiceTests : IDisposable
    {
        private readonly string _tempRoot;

        public WinREImageServiceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "WinREImageServiceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        [Fact]
        public void TryGetEmbeddedWinREPath_ReturnsFalseWhenMissing()
        {
            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);

            Assert.False(found);
            Assert.Equal(Path.Combine(_tempRoot, "Windows", "System32", "Recovery", "Winre.wim"), path);
        }

        [Fact]
        public void TryGetEmbeddedWinREPath_ReturnsTrueWhenPresent()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            File.WriteAllText(Path.Combine(recoveryDir, "Winre.wim"), "fake-wim-content");

            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);

            Assert.True(found);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public void ExtractEmbeddedWinRE_ThrowsWhenMissing()
        {
            var destination = Path.Combine(_tempRoot, "extracted.wim");

            Assert.Throws<FileNotFoundException>(() => WinREImageService.ExtractEmbeddedWinRE(_tempRoot, destination));
        }

        [Fact]
        public void ExtractEmbeddedWinRE_CopiesFileOutAndClearsReadOnly()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            var sourcePath = Path.Combine(recoveryDir, "Winre.wim");
            File.WriteAllText(sourcePath, "fake-wim-content");
            File.SetAttributes(sourcePath, FileAttributes.ReadOnly);

            var destination = Path.Combine(_tempRoot, "extracted.wim");
            WinREImageService.ExtractEmbeddedWinRE(_tempRoot, destination);

            Assert.True(File.Exists(destination));
            Assert.Equal("fake-wim-content", File.ReadAllText(destination));
            Assert.False(File.GetAttributes(destination).HasFlag(FileAttributes.ReadOnly));
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_ThrowsWhenSourceMissing()
        {
            Assert.Throws<FileNotFoundException>(() => WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, Path.Combine(_tempRoot, "missing.wim")));
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_CopiesFileIntoNestedPath()
        {
            var updatedSource = Path.Combine(_tempRoot, "updated.wim");
            File.WriteAllText(updatedSource, "updated-content");

            WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, updatedSource);

            var found = WinREImageService.TryGetEmbeddedWinREPath(_tempRoot, out var path);
            Assert.True(found);
            Assert.Equal("updated-content", File.ReadAllText(path));
        }

        [Fact]
        public void ReplaceEmbeddedWinRE_OverwritesReadOnlyExisting()
        {
            var recoveryDir = Path.Combine(_tempRoot, "Windows", "System32", "Recovery");
            Directory.CreateDirectory(recoveryDir);
            var existingPath = Path.Combine(recoveryDir, "Winre.wim");
            File.WriteAllText(existingPath, "old-content");
            File.SetAttributes(existingPath, FileAttributes.ReadOnly);

            var updatedSource = Path.Combine(_tempRoot, "updated.wim");
            File.WriteAllText(updatedSource, "new-content");

            WinREImageService.ReplaceEmbeddedWinRE(_tempRoot, updatedSource);

            Assert.Equal("new-content", File.ReadAllText(existingPath));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WinREImageServiceTests"`
Expected: FAIL (build error — `WinREImageService` does not exist yet)

- [ ] **Step 3: Write the service**

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WinREImageServiceTests"`
Expected: PASS (6 tests)

- [ ] **Step 5: Add the `WinRE` property to `MountedWindowsImage`**

In `src/Models/MountedWindowsImage.cs`, add this property after `LastUpdateResult` (currently line 79, right before the `ToString()` override):

```csharp
        /// <summary>
        /// The embedded WinRE image (Windows\System32\Recovery\Winre.wim) mounted alongside this image, if present
        /// </summary>
        public MountedWindowsImage? WinRE { get; set; }
```

- [ ] **Step 6: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/Services/WinREImageService.cs src/Models/MountedWindowsImage.cs tests/PSWindowsImageTools.Tests/WinREImageServiceTests.cs
git commit -m "Add WinREImageService and MountedWindowsImage.WinRE"
```

---

### Task 4: Auto-mount embedded WinRE in `Mount-WindowsImageList`

**Files:**
- Modify: `src/Cmdlets/MountWindowsImageListCmdlet.cs`

**Interfaces:**
- Consumes: `WinREImageService.TryGetEmbeddedWinREPath`/`ExtractEmbeddedWinRE` (Task 3), `MountedWindowsImage.WinRE` (Task 3), existing `NativeDismService.MountImage(string, string, uint, bool, Action<int,string>?, PSCmdlet?)`.
- Produces: after this task, any `MountedWindowsImage` returned by `Mount-WindowsImageList -ReadWrite` for an `install.wim` with an embedded WinRE has `.WinRE` populated and mounted. The extracted WinRE WIM's temp file path is stashed on `mountedImage.WinRE.SourceImagePath` for Task 5 to re-embed on dismount.

This task has no new automated test: it wires together `WinREImageService` (already unit tested in Task 3) with real DISM mounting, which requires administrator rights and a real Windows image — verified manually in Step 3.

- [ ] **Step 1: Add the WinRE auto-mount helper and wire it in**

In `src/Cmdlets/MountWindowsImageListCmdlet.cs`, in `MountSingleImage`, change:

```csharp
                mountedImage.Status = MountStatus.Mounted;
                mountedImage.MountedAt = DateTime.UtcNow;

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Image mounted successfully using native API: {imageInfo.Name} (Duration: {LoggingService.FormatDuration(mountDuration)})");

                return mountedImage;
```

to:

```csharp
                mountedImage.Status = MountStatus.Mounted;
                mountedImage.MountedAt = DateTime.UtcNow;

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Image mounted successfully using native API: {imageInfo.Name} (Duration: {LoggingService.FormatDuration(mountDuration)})");

                TryMountEmbeddedWinRE(mountedImage, currentIndex, totalCount);

                return mountedImage;
```

Then add this new private method to the class (after `MountSingleImage`):

```csharp
        /// <summary>
        /// Detects an embedded winre.wim inside a just-mounted image and mounts it too, exposed as .WinRE
        /// </summary>
        private void TryMountEmbeddedWinRE(MountedWindowsImage mountedImage, int currentIndex, int totalCount)
        {
            if (mountedImage.MountPath == null)
            {
                return;
            }

            if (!WinREImageService.TryGetEmbeddedWinREPath(mountedImage.MountPath.FullName, out _))
            {
                return;
            }

            var winREWimPath = Path.Combine(
                Path.GetDirectoryName(mountedImage.MountPath.FullName) ?? Path.GetTempPath(),
                $"WinRE_{Guid.NewGuid():N}.wim");
            var winREMountPath = mountedImage.MountPath.FullName + "_WinRE";

            try
            {
                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Found embedded WinRE image, extracting and mounting");

                WinREImageService.ExtractEmbeddedWinRE(mountedImage.MountPath.FullName, winREWimPath);

                using var nativeDismService = new NativeDismService();
                var mountSuccess = nativeDismService.MountImage(
                    winREWimPath,
                    winREMountPath,
                    imageIndex: 1,
                    readOnly: mountedImage.IsReadOnly,
                    progressCallback: null,
                    cmdlet: this);

                if (!mountSuccess)
                {
                    throw new InvalidOperationException("Failed to mount embedded WinRE image");
                }

                mountedImage.WinRE = new MountedWindowsImage
                {
                    MountId = Guid.NewGuid().ToString(),
                    SourceImagePath = winREWimPath,
                    ImageIndex = 1,
                    ImageName = $"{mountedImage.ImageName} (WinRE)",
                    MountPath = new DirectoryInfo(winREMountPath),
                    WimGuid = mountedImage.WimGuid,
                    Status = MountStatus.Mounted,
                    IsReadOnly = mountedImage.IsReadOnly,
                    MountedAt = DateTime.UtcNow
                };

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - WinRE image mounted at {winREMountPath}");
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Failed to mount embedded WinRE image: {ex.Message}");
                mountedImage.WinRE = null;
            }
        }
```

- [ ] **Step 2: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Manual verification (requires administrator rights and a real install.wim with an embedded WinRE)**

```powershell
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1 -Force
$images = Get-WindowsImageList -ImagePath C:\Media\Win11\sources\install.wim
$mounted = $images[0] | Mount-WindowsImageList -MountPath C:\Mount -ReadWrite
$mounted.WinRE               # expect a MountedWindowsImage with Status 'Mounted'
Test-Path $mounted.WinRE.MountPath.FullName   # expect True
```

- [ ] **Step 4: Commit**

```bash
git add src/Cmdlets/MountWindowsImageListCmdlet.cs
git commit -m "Auto-mount embedded WinRE image when mounting install.wim"
```

---

### Task 5: Auto-dismount/re-embed WinRE in `Dismount-WindowsImageList`

**Files:**
- Modify: `src/Cmdlets/DismountWindowsImageListCmdlet.cs`

**Interfaces:**
- Consumes: `WinREImageService.ReplaceEmbeddedWinRE` (Task 3), `MountedWindowsImage.WinRE` populated by Task 4 (with the temp WinRE WIM path on `WinRE.SourceImagePath`), existing `WindowsImageService.ForCmdlet(PSCmdlet?).UnmountImage(string, bool, Action<int,string>?)`.
- Produces: `Dismount-WindowsImageList -Save` on a `MountedWindowsImage` with a populated `.WinRE` now commits/dismounts the WinRE mount first and re-embeds the resulting `winre.wim` into the parent image before the parent itself is dismounted.

This task has no new automated test: it wires together `WinREImageService` (already unit tested in Task 3) with real DISM dismounting, which requires administrator rights and a real mounted image — verified manually in Step 3, continuing from Task 4's manual verification.

- [ ] **Step 1: Add the WinRE dismount/re-embed helper and wire it in**

In `src/Cmdlets/DismountWindowsImageListCmdlet.cs`, in `DismountSingleImage`, change:

```csharp
                // Determine save/discard behavior
                var shouldSave = Save.IsPresent && !Discard.IsPresent;
                var saveMode = shouldSave ? (Append.IsPresent ? "Save with Append" : "Save") : "Discard";

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Dismounting image from {mountedImage.MountPath.FullName} using native DISM API");
```

to:

```csharp
                // Determine save/discard behavior
                var shouldSave = Save.IsPresent && !Discard.IsPresent;
                var saveMode = shouldSave ? (Append.IsPresent ? "Save with Append" : "Save") : "Discard";

                DismountEmbeddedWinRE(mountedImage, shouldSave, currentIndex, totalCount);

                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Dismounting image from {mountedImage.MountPath.FullName} using native DISM API");
```

Also add `WinRE = mountedImage.WinRE,` to the `result` object's initializer at the top of `DismountSingleImage` (so the dismounted WinRE state is reflected in the cmdlet's output), i.e. change:

```csharp
            var result = new MountedWindowsImage
            {
                MountId = mountedImage.MountId,
                SourceImagePath = mountedImage.SourceImagePath,
                ImageIndex = mountedImage.ImageIndex,
                ImageName = mountedImage.ImageName,
                Edition = mountedImage.Edition,
                Architecture = mountedImage.Architecture,
                MountPath = mountedImage.MountPath,
                WimGuid = mountedImage.WimGuid,
                MountedAt = mountedImage.MountedAt,
                Status = MountStatus.Unmounting,
                IsReadOnly = mountedImage.IsReadOnly,
                ImageSize = mountedImage.ImageSize
            };
```

to:

```csharp
            var result = new MountedWindowsImage
            {
                MountId = mountedImage.MountId,
                SourceImagePath = mountedImage.SourceImagePath,
                ImageIndex = mountedImage.ImageIndex,
                ImageName = mountedImage.ImageName,
                Edition = mountedImage.Edition,
                Architecture = mountedImage.Architecture,
                MountPath = mountedImage.MountPath,
                WimGuid = mountedImage.WimGuid,
                MountedAt = mountedImage.MountedAt,
                Status = MountStatus.Unmounting,
                IsReadOnly = mountedImage.IsReadOnly,
                ImageSize = mountedImage.ImageSize,
                WinRE = mountedImage.WinRE
            };
```

Then add this new private method to the class (after `DismountSingleImage`):

```csharp
        /// <summary>
        /// Dismounts a mounted embedded WinRE image first, re-embedding it into the parent when saving
        /// </summary>
        private void DismountEmbeddedWinRE(MountedWindowsImage mountedImage, bool shouldSave, int currentIndex, int totalCount)
        {
            var winRE = mountedImage.WinRE;
            if (winRE?.MountPath == null || winRE.Status != MountStatus.Mounted)
            {
                return;
            }

            try
            {
                LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Dismounting embedded WinRE image from {winRE.MountPath.FullName}");

                using var imageService = WindowsImageService.ForCmdlet(this);
                imageService.UnmountImage(winRE.MountPath.FullName, commitChanges: shouldSave && !winRE.IsReadOnly);

                if (shouldSave && !winRE.IsReadOnly && mountedImage.MountPath != null)
                {
                    WinREImageService.ReplaceEmbeddedWinRE(mountedImage.MountPath.FullName, winRE.SourceImagePath);
                    LoggingService.WriteVerbose(this, $"[{currentIndex} of {totalCount}] - Re-embedded updated WinRE image into parent");
                }

                winRE.Status = MountStatus.Unmounted;
            }
            catch (Exception ex)
            {
                LoggingService.WriteWarning(this, $"[{currentIndex} of {totalCount}] - Failed to dismount/re-embed WinRE image: {ex.Message}");
                winRE.Status = MountStatus.Failed;
                winRE.ErrorMessage = ex.Message;
            }
            finally
            {
                try
                {
                    if (winRE.MountPath.Exists)
                    {
                        winRE.MountPath.Delete(recursive: true);
                    }

                    if (File.Exists(winRE.SourceImagePath))
                    {
                        File.Delete(winRE.SourceImagePath);
                    }
                }
                catch (Exception cleanupEx)
                {
                    LoggingService.WriteWarning(this, $"Failed to clean up WinRE mount artifacts: {cleanupEx.Message}");
                }
            }
        }
```

- [ ] **Step 2: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Manual verification (continues from Task 4's manual verification, requires administrator rights)**

```powershell
"test" | Out-File (Join-Path $mounted.WinRE.MountPath.FullName 'winre-marker.txt')
$mounted | Dismount-WindowsImageList -Save

# Re-mount to confirm the WinRE edit persisted
$images2 = Get-WindowsImageList -ImagePath C:\Media\Win11\sources\install.wim
$mounted2 = $images2[0] | Mount-WindowsImageList -MountPath C:\Mount2 -ReadWrite
Test-Path (Join-Path $mounted2.WinRE.MountPath.FullName 'winre-marker.txt')   # expect True
$mounted2 | Dismount-WindowsImageList -Discard
```

- [ ] **Step 4: Commit**

```bash
git add src/Cmdlets/DismountWindowsImageListCmdlet.cs
git commit -m "Auto-dismount and re-embed WinRE image when dismounting install.wim"
```

---

### Task 6: `New-WindowsISO` cmdlet

**Files:**
- Create: `src/Cmdlets/NewWindowsISOCmdlet.cs`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (add `'New-WindowsISO'`)

**Interfaces:**
- Consumes: existing `PSWindowsImageTools.Services.ISOService.CreateBootableISO(string, string, string, BootMode, Action<int,string>?, PSCmdlet?)` and `BootMode` enum (both already implemented in `src/Services/ISOService.cs`, currently unused).
- Produces: `[Cmdlet(VerbsCommon.New, "WindowsISO")]` outputting the created `FileInfo`.

No new automated test: `ISOService.CreateBootableISO` requires a real `oscdimg.exe`/`mkisofs` and a real media folder to produce a real ISO, which is verified manually in Step 3 (consistent with the spec's Testing section).

- [ ] **Step 1: Write the cmdlet**

```csharp
using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Builds a bootable ISO from a Windows installation media folder
    /// </summary>
    [Cmdlet(VerbsCommon.New, "WindowsISO")]
    [OutputType(typeof(FileInfo))]
    public class NewWindowsISOCmdlet : PSCmdlet
    {
        /// <summary>
        /// Path to the Windows installation media folder (containing boot\ and sources\)
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, HelpMessage = "Path to the Windows installation media folder (containing boot\\ and sources\\)")]
        [ValidateNotNull]
        public DirectoryInfo SourcePath { get; set; } = null!;

        /// <summary>
        /// Path for the output ISO file
        /// </summary>
        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path for the output ISO file")]
        [ValidateNotNull]
        public FileInfo DestinationPath { get; set; } = null!;

        /// <summary>
        /// Volume label for the ISO
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string VolumeLabel { get; set; } = "Windows";

        /// <summary>
        /// Boot mode for the ISO (UEFI, BIOS, or Both)
        /// </summary>
        [Parameter(Mandatory = false)]
        public BootMode BootMode { get; set; } = BootMode.Both;

        private const string ComponentName = "NewWindowsISO";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!SourcePath.Exists)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new DirectoryNotFoundException($"Source path not found: {SourcePath.FullName}"),
                    "SourcePathNotFound",
                    ErrorCategory.ObjectNotFound,
                    SourcePath.FullName));
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Create Windows ISO", $"{SourcePath.FullName} -> {DestinationPath.FullName}");

            try
            {
                var progressCallback = ProgressService.CreateProgressCallback(
                    this, "Creating Windows ISO", DestinationPath.Name, 1, 1);

                using var isoService = new ISOService();
                var success = isoService.CreateBootableISO(
                    SourcePath.FullName,
                    DestinationPath.FullName,
                    VolumeLabel,
                    BootMode,
                    progressCallback,
                    this);

                LoggingService.CompleteProgress(this, "Creating Windows ISO");

                if (!success)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new InvalidOperationException(
                            "ISO creation failed: no usable ISO creation tool was found. Install the Windows ADK " +
                            "deployment tools with Install-ADK -IncludeDeploymentTools (which provides oscdimg.exe), " +
                            "or install mkisofs, then try again."),
                        "NoIsoCreationToolAvailable",
                        ErrorCategory.NotInstalled,
                        null));
                    return;
                }

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Create Windows ISO", operationStartTime,
                    $"Created {DestinationPath.FullName}");

                DestinationPath.Refresh();
                WriteObject(DestinationPath);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to create ISO: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "NewWindowsISOFailed", ErrorCategory.NotSpecified, DestinationPath.FullName));
            }
        }
    }
}
```

- [ ] **Step 2: Add `New-WindowsISO` to the module manifest**

In `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`, in the `CmdletsToExport` array, add to the `# ISO Media Management` group created in Task 2:

```
        # ISO Media Management
        'Export-WindowsISO',
        'New-WindowsISO',
```

- [ ] **Step 3: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Manual verification (requires Windows ADK's oscdimg.exe or mkisofs installed)**

```powershell
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1 -Force
New-WindowsISO -SourcePath C:\Media\Win11 -DestinationPath C:\ISO\Win11-serviced.iso -VolumeLabel "WIN11-SERVICED"
Test-Path C:\ISO\Win11-serviced.iso   # expect True
```

- [ ] **Step 5: Commit**

```bash
git add src/Cmdlets/NewWindowsISOCmdlet.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "Add New-WindowsISO to build bootable ISOs from serviced media"
```

---

### Task 7: `WindowsISODownloadInfo` model, `WindowsISODownloadParser`, `WindowsISODownloadService`, `Get-WindowsISODownloadInfo` cmdlet

**Files:**
- Create: `src/Models/WindowsISODownloadInfo.cs`
- Create: `src/Services/WindowsISODownloadParser.cs`
- Create: `src/Services/WindowsISODownloadService.cs`
- Create: `src/Cmdlets/GetWindowsISODownloadInfoCmdlet.cs`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (add `'Get-WindowsISODownloadInfo'`)
- Test: `tests/PSWindowsImageTools.Tests/WindowsISODownloadParserTests.cs`

**Interfaces:**
- Produces: `WindowsISODownloadInfo` (`Uri Url`, `string FileName`, `string Edition`, `string Architecture`, `string Language`); `WindowsISODownloadParser` (pure, static: `ResolveProductEditionId(string, string)`, `ThrowIfRejected(string)`, `SelectSkuId(string, string)`, `SelectDownloadUrl(string)`); `WindowsISODownloadService.GetDownloadInfo(string edition, string architecture, string language, PSCmdlet? cmdlet)`. Used by Task 8's `Save-WindowsISO`.

This service talks to Microsoft's real, undocumented `software-download-connector` API (session registration, an `ov-df.microsoft.com` bot-detection challenge, then SKU/link lookups) — it cannot be exercised in a unit test. Only the pure parsing/selection logic in `WindowsISODownloadParser` is unit tested here; the live flow is verified manually in Step 5.

- [ ] **Step 1: Write the failing parser tests**

```csharp
using System;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsISODownloadParserTests
    {
        [Theory]
        [InlineData("Windows 11", "x64", "3321")]
        [InlineData("Windows 11", "X64", "3321")]
        [InlineData("Windows 11", "arm64", "3324")]
        [InlineData("Windows 11", "ARM64", "3324")]
        public void ResolveProductEditionId_ReturnsKnownIds(string edition, string architecture, string expected)
        {
            Assert.Equal(expected, WindowsISODownloadParser.ResolveProductEditionId(edition, architecture));
        }

        [Fact]
        public void ResolveProductEditionId_ThrowsForUnsupportedEdition()
        {
            Assert.Throws<ArgumentException>(() => WindowsISODownloadParser.ResolveProductEditionId("Windows 10", "x64"));
        }

        [Fact]
        public void ResolveProductEditionId_ThrowsForUnsupportedArchitecture()
        {
            Assert.Throws<ArgumentException>(() => WindowsISODownloadParser.ResolveProductEditionId("Windows 11", "x86"));
        }

        [Fact]
        public void ThrowIfRejected_ThrowsOnSentinelRejection()
        {
            var body = "{\"errors\":[\"Sentinel marked this request as rejected.\"]}";

            var ex = Assert.Throws<InvalidOperationException>(() => WindowsISODownloadParser.ThrowIfRejected(body));
            Assert.Contains("Save-WindowsISO -Url", ex.Message);
        }

        [Fact]
        public void ThrowIfRejected_ThrowsOnGenericBlock()
        {
            var body = "We are unable to complete your request at this time.";

            Assert.Throws<InvalidOperationException>(() => WindowsISODownloadParser.ThrowIfRejected(body));
        }

        [Fact]
        public void ThrowIfRejected_DoesNothingForNormalResponse()
        {
            WindowsISODownloadParser.ThrowIfRejected("{\"Skus\":[]}");
        }

        [Fact]
        public void SelectSkuId_FindsMatchingLanguage()
        {
            var json = "{\"Skus\":[{\"Id\":\"1\",\"Language\":\"Arabic\"},{\"Id\":\"47\",\"Language\":\"English International\"}]}";

            Assert.Equal("47", WindowsISODownloadParser.SelectSkuId(json, "English International"));
        }

        [Fact]
        public void SelectSkuId_IsCaseInsensitiveOnLanguage()
        {
            var json = "{\"Skus\":[{\"Id\":\"47\",\"Language\":\"English International\"}]}";

            Assert.Equal("47", WindowsISODownloadParser.SelectSkuId(json, "english international"));
        }

        [Fact]
        public void SelectSkuId_ReturnsNullWhenLanguageNotFound()
        {
            var json = "{\"Skus\":[{\"Id\":\"1\",\"Language\":\"Arabic\"}]}";

            Assert.Null(WindowsISODownloadParser.SelectSkuId(json, "English International"));
        }

        [Fact]
        public void SelectSkuId_ReturnsNullWhenSkusMissing()
        {
            Assert.Null(WindowsISODownloadParser.SelectSkuId("{}", "English International"));
        }

        [Fact]
        public void SelectDownloadUrl_ReturnsFirstUri()
        {
            var json = "{\"ProductDownloadOptions\":[{\"DownloadType\":1,\"Uri\":\"https://example.com/x.iso\"}]}";

            Assert.Equal("https://example.com/x.iso", WindowsISODownloadParser.SelectDownloadUrl(json));
        }

        [Fact]
        public void SelectDownloadUrl_ReturnsNullWhenEmpty()
        {
            Assert.Null(WindowsISODownloadParser.SelectDownloadUrl("{\"ProductDownloadOptions\":[]}"));
        }

        [Fact]
        public void SelectDownloadUrl_ReturnsNullWhenMissing()
        {
            Assert.Null(WindowsISODownloadParser.SelectDownloadUrl("{}"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsISODownloadParserTests"`
Expected: FAIL (build error — `WindowsISODownloadParser` does not exist yet)

- [ ] **Step 3: Write the model and parser**

`src/Models/WindowsISODownloadInfo.cs`:

```csharp
using System;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Resolved, time-limited direct download link for an official Windows ISO
    /// </summary>
    public class WindowsISODownloadInfo
    {
        /// <summary>
        /// Time-limited direct CDN download URL
        /// </summary>
        public Uri Url { get; set; } = null!;

        /// <summary>
        /// Suggested local file name for the download
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Requested edition (e.g. "Windows 11")
        /// </summary>
        public string Edition { get; set; } = string.Empty;

        /// <summary>
        /// Requested architecture (e.g. "x64", "arm64")
        /// </summary>
        public string Architecture { get; set; } = string.Empty;

        /// <summary>
        /// Requested language SKU (e.g. "English International")
        /// </summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>
        /// Returns a human-readable summary
        /// </summary>
        public override string ToString()
        {
            return $"{Edition} {Architecture} ({Language})";
        }
    }
}
```

`src/Services/WindowsISODownloadParser.cs`:

```csharp
using System;
using Newtonsoft.Json.Linq;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Pure parsing/selection logic for Microsoft's software-download-connector API responses,
    /// kept separate from WindowsISODownloadService so it can be unit tested without live HTTP calls
    /// </summary>
    public static class WindowsISODownloadParser
    {
        private const string SentinelRejectedText = "Sentinel marked this request as rejected.";
        private const string RequestBlockedText = "We are unable to complete your request at this time.";

        /// <summary>
        /// Resolves Microsoft's numeric ProductEditionId for a given edition/architecture combination
        /// </summary>
        public static string ResolveProductEditionId(string edition, string architecture)
        {
            if (!string.Equals(edition, "Windows 11", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported edition '{edition}'. Only \"Windows 11\" is currently supported.", nameof(edition));
            }

            return (architecture ?? string.Empty).ToLowerInvariant() switch
            {
                "x64" => "3321",
                "arm64" => "3324",
                _ => throw new ArgumentException($"Unsupported architecture '{architecture}'. Supported values: x64, arm64.", nameof(architecture))
            };
        }

        /// <summary>
        /// Throws if a connector API response indicates Microsoft's bot-detection layer rejected the request
        /// </summary>
        public static void ThrowIfRejected(string responseBody)
        {
            if (string.IsNullOrEmpty(responseBody))
            {
                return;
            }

            if (responseBody.Contains(SentinelRejectedText))
            {
                throw new InvalidOperationException(
                    "Microsoft rejected the automated ISO download request (Sentinel bot detection). This can " +
                    "happen from datacenter or VPN IP ranges. Obtain the ISO URL manually from " +
                    "https://www.microsoft.com/software-download/windows11 and pass it to Save-WindowsISO -Url instead.");
            }

            if (responseBody.Contains(RequestBlockedText))
            {
                throw new InvalidOperationException(
                    "Microsoft blocked the automated ISO download request. Obtain the ISO URL manually from " +
                    "https://www.microsoft.com/software-download/windows11 and pass it to Save-WindowsISO -Url instead.");
            }
        }

        /// <summary>
        /// Selects the SKU id matching the requested language from a getskuinformationbyproductedition response
        /// </summary>
        public static string? SelectSkuId(string skuJson, string language)
        {
            var skus = JObject.Parse(skuJson)["Skus"] as JArray;
            if (skus == null)
            {
                return null;
            }

            foreach (var sku in skus)
            {
                if (string.Equals((string?)sku["Language"], language, StringComparison.OrdinalIgnoreCase))
                {
                    return (string?)sku["Id"];
                }
            }

            return null;
        }

        /// <summary>
        /// Selects the download URL from a GetProductDownloadLinksBySku response
        /// </summary>
        public static string? SelectDownloadUrl(string linkJson)
        {
            var options = JObject.Parse(linkJson)["ProductDownloadOptions"] as JArray;
            if (options == null || options.Count == 0)
            {
                return null;
            }

            return (string?)options[0]["Uri"];
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsISODownloadParserTests"`
Expected: PASS (12 tests)

- [ ] **Step 5: Write the download service (network orchestration — not unit tested, see rationale above)**

`src/Services/WindowsISODownloadService.cs`:

```csharp
using System;
using System.Management.Automation;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Resolves official, time-limited direct download links for the Windows 11 ISO from Microsoft's public
    /// consumer software-download-connector API -- the same unauthenticated flow the browser download page
    /// uses. This is an undocumented Microsoft flow and can change without notice; Save-WindowsISO accepts a
    /// plain -Url as a manual bypass if this stops working.
    /// </summary>
    public class WindowsISODownloadService : IDisposable
    {
        private const string ServiceName = "WindowsISODownloadService";
        private const string Profile = "606624d44113";
        private const string OrgId = "y6jn8c31";
        private const string OvInstanceId = "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";
        private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/117.0";
        private const string DownloadPageUrl = "https://www.microsoft.com/en-us/software-download/windows11";

        private readonly HttpClient _httpClient;
        private bool _disposed;

        /// <summary>
        /// Creates the service with a pre-configured HttpClient (custom User-Agent, empty Accept, cert bypass)
        /// </summary>
        public WindowsISODownloadService()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", string.Empty);
        }

        /// <summary>
        /// Resolves a time-limited direct download URL for the given edition/architecture/language
        /// </summary>
        public WindowsISODownloadInfo GetDownloadInfo(string edition, string architecture, string language, PSCmdlet? cmdlet)
        {
            var productId = WindowsISODownloadParser.ResolveProductEditionId(edition, architecture);
            var sessionId = Guid.NewGuid().ToString();

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Registering session {sessionId} for ProductEditionId {productId}");
            RegisterSession(sessionId);

            LoggingService.WriteVerbose(cmdlet, ServiceName, "Completing bot-detection challenge");
            CompleteBotDetectionChallenge(sessionId);

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Requesting SKU list for language: {language}");
            var skuJson = Get(BuildSkuUrl(productId, sessionId), DownloadPageUrl);
            WindowsISODownloadParser.ThrowIfRejected(skuJson);

            var skuId = WindowsISODownloadParser.SelectSkuId(skuJson, language);
            if (string.IsNullOrEmpty(skuId))
            {
                throw new InvalidOperationException($"Microsoft did not return a SKU for language '{language}'. Response: {skuJson}");
            }

            LoggingService.WriteVerbose(cmdlet, ServiceName, $"Requesting download link for SKU {skuId}");
            var linkJson = Get(BuildLinkUrl(skuId!, sessionId), DownloadPageUrl);
            WindowsISODownloadParser.ThrowIfRejected(linkJson);

            var url = WindowsISODownloadParser.SelectDownloadUrl(linkJson);
            if (string.IsNullOrEmpty(url))
            {
                throw new InvalidOperationException($"Microsoft did not return a download link for SKU {skuId}. Response: {linkJson}");
            }

            var fileName = System.IO.Path.GetFileName(new Uri(url!).LocalPath);
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"Win11_{architecture}.iso";
            }

            return new WindowsISODownloadInfo
            {
                Url = new Uri(url!),
                FileName = fileName,
                Edition = edition,
                Architecture = architecture,
                Language = language
            };
        }

        private void RegisterSession(string sessionId)
        {
            var url = $"https://vlscppe.microsoft.com/tags?org_id={OrgId}&session_id={sessionId}";
            Get(url, null);
        }

        private void CompleteBotDetectionChallenge(string sessionId)
        {
            var challengeUrl = $"https://ov-df.microsoft.com/mdt.js?instanceId={OvInstanceId}&PageId=si&session_id={sessionId}";
            var challengeResponse = Get(challengeUrl, null);

            var tokenMatch = Regex.Match(challengeResponse, "[?&]w=([A-Fa-f0-9]+)");
            var ticksMatch = Regex.Match(challengeResponse, "rticks=\"?\\+?(\\d+)");

            if (!tokenMatch.Success || !ticksMatch.Success)
            {
                throw new InvalidOperationException(
                    "Could not complete Microsoft's bot-detection challenge (unexpected ov-df response). This " +
                    "flow may have changed; use Save-WindowsISO -Url with a manually obtained link instead.");
            }

            Thread.Sleep(200);

            var replyUrl = $"https://ov-df.microsoft.com/?session_id={sessionId}&CustomerId={OvInstanceId}&PageId=si" +
                           $"&w={tokenMatch.Groups[1].Value}&mdt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&rticks={ticksMatch.Groups[1].Value}";
            Get(replyUrl, null);
        }

        private static string BuildSkuUrl(string productId, string sessionId)
        {
            return "https://www.microsoft.com/software-download-connector/api/getskuinformationbyproductedition" +
                   $"?profile={Profile}&ProductEditionId={productId}&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}";
        }

        private static string BuildLinkUrl(string skuId, string sessionId)
        {
            return "https://www.microsoft.com/software-download-connector/api/GetProductDownloadLinksBySku" +
                   $"?profile={Profile}&ProductEditionId=undefined&SKU={skuId}&friendlyFileName=undefined&Locale=en-US&sessionID={sessionId}";
        }

        private string Get(string url, string? referer)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.Referrer = new Uri(referer!);
            }

            using var response = _httpClient.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            return response.Content.ReadAsStringAsync().Result;
        }

        /// <summary>
        /// Disposes the service
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _httpClient.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
```

- [ ] **Step 6: Write the cmdlet**

```csharp
using System;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Resolves a time-limited direct download URL for the latest official Windows 11 ISO
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsISODownloadInfo")]
    [OutputType(typeof(WindowsISODownloadInfo))]
    public class GetWindowsISODownloadInfoCmdlet : PSCmdlet
    {
        /// <summary>
        /// Windows edition to resolve (only "Windows 11" is currently supported, matching Microsoft's public download page)
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string Edition { get; set; } = "Windows 11";

        /// <summary>
        /// Target architecture
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateSet("x64", "arm64")]
        public string Architecture { get; set; } = "x64";

        /// <summary>
        /// Language SKU, as labeled on Microsoft's download page (e.g. "English International")
        /// </summary>
        [Parameter(Mandatory = false)]
        [ValidateNotNullOrEmpty]
        public string Language { get; set; } = "English International";

        private const string ComponentName = "GetWindowsISODownloadInfo";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Resolve Windows ISO download link", $"{Edition} {Architecture} ({Language})");

            try
            {
                using var downloadService = new WindowsISODownloadService();
                var info = downloadService.GetDownloadInfo(Edition, Architecture, Language, this);

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Resolve Windows ISO download link", operationStartTime,
                    $"Resolved: {info.FileName}");

                WriteObject(info);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to resolve Windows ISO download link: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "GetWindowsISODownloadInfoFailed", ErrorCategory.NotSpecified, null));
            }
        }
    }
}
```

- [ ] **Step 7: Add `Get-WindowsISODownloadInfo` to the module manifest**

In `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`, add to the `# ISO Media Management` group:

```
        # ISO Media Management
        'Export-WindowsISO',
        'New-WindowsISO',
        'Get-WindowsISODownloadInfo',
```

- [ ] **Step 8: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 9: Manual verification (requires live internet access; may fail if Microsoft's Sentinel bot-detection flags the request — that's the documented risk in the spec)**

```powershell
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1 -Force
Get-WindowsISODownloadInfo -Architecture x64 -Verbose
```

- [ ] **Step 10: Commit**

```bash
git add src/Models/WindowsISODownloadInfo.cs src/Services/WindowsISODownloadParser.cs src/Services/WindowsISODownloadService.cs src/Cmdlets/GetWindowsISODownloadInfoCmdlet.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1 tests/PSWindowsImageTools.Tests/WindowsISODownloadParserTests.cs
git commit -m "Add Get-WindowsISODownloadInfo to resolve the latest official Windows 11 ISO URL"
```

---

### Task 8: `Save-WindowsISO` cmdlet

**Files:**
- Modify: `src/Services/NetworkService.cs` (add `DownloadFileWithResume`)
- Create: `src/Cmdlets/SaveWindowsISOCmdlet.cs`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (add `'Save-WindowsISO'`)

**Interfaces:**
- Consumes: `WindowsISODownloadInfo` (Task 7).
- Produces: `NetworkService.DownloadFileWithResume(Uri url, FileInfo destinationFile, bool resume, PSCmdlet? cmdlet, Action<int,string>? progressCallback)`; `[Cmdlet(VerbsData.Save, "WindowsISO")]` outputting the downloaded `FileInfo`, accepting either a `WindowsISODownloadInfo` via the pipeline or a plain `-Url` as the manual bypass documented in the spec.

No new automated test: this performs a real multi-gigabyte HTTP download, which isn't practical to exercise in the unit test suite. Verified manually in Step 3.

- [ ] **Step 1: Add `DownloadFileWithResume` to `NetworkService`**

In `src/Services/NetworkService.cs`, add this method to the `NetworkService` class (after `DownloadFile`):

```csharp
        /// <summary>
        /// Downloads a file from a URL with optional resume support and progress reporting
        /// </summary>
        /// <param name="url">URL to download from</param>
        /// <param name="destinationFile">Local file to save to</param>
        /// <param name="resume">Whether to resume from an existing partial file</param>
        /// <param name="cmdlet">Cmdlet for logging</param>
        /// <param name="progressCallback">Progress callback for reporting download progress</param>
        /// <returns>True if download succeeded</returns>
        public static bool DownloadFileWithResume(Uri url, FileInfo destinationFile, bool resume, PSCmdlet? cmdlet = null, Action<int, string>? progressCallback = null)
        {
            try
            {
                var destinationDir = destinationFile.DirectoryName;
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                long startPosition = 0;
                if (resume && destinationFile.Exists)
                {
                    startPosition = destinationFile.Length;
                    LoggingService.WriteVerbose(cmdlet, ServiceName, $"Resuming download from position: {startPosition:N0} bytes");
                }

                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                    UseDefaultCredentials = true
                };

                using var httpClient = new HttpClient(handler);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (startPosition > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startPosition, null);
                }

                using var response = httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).Result;

                if (startPosition > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                {
                    LoggingService.WriteWarning(cmdlet, ServiceName, "Server doesn't support resume, starting fresh download");
                    startPosition = 0;
                    destinationFile.Delete();
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                }

                var totalBytes = (response.Content.Headers.ContentLength ?? 0) + startPosition;

                using var contentStream = response.Content.ReadAsStreamAsync().Result;
                using var fileStream = new FileStream(destinationFile.FullName,
                    startPosition > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 8192, false);

                var buffer = new byte[8192];
                long totalBytesRead = startPosition;
                int bytesRead;
                var lastProgressReport = 0;

                while ((bytesRead = contentStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progressPercentage = (int)((totalBytesRead * 100) / totalBytes);
                        if (progressPercentage > lastProgressReport)
                        {
                            lastProgressReport = progressPercentage;
                            progressCallback?.Invoke(progressPercentage, $"Downloaded {totalBytesRead:N0} of {totalBytes:N0} bytes ({progressPercentage}%)");
                        }
                    }
                    else
                    {
                        progressCallback?.Invoke(-1, $"Downloaded {totalBytesRead:N0} bytes");
                    }
                }

                LoggingService.WriteVerbose(cmdlet, ServiceName, $"Download completed: {totalBytesRead:N0} bytes");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(cmdlet, ServiceName, $"Download failed: {ex.Message}", ex);
                return false;
            }
        }
```

- [ ] **Step 2: Write the cmdlet**

```csharp
using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Downloads a Windows ISO, resolved via Get-WindowsISODownloadInfo or supplied directly with -Url
    /// </summary>
    [Cmdlet(VerbsData.Save, "WindowsISO")]
    [OutputType(typeof(FileInfo))]
    public class SaveWindowsISOCmdlet : PSCmdlet
    {
        /// <summary>
        /// Download info from Get-WindowsISODownloadInfo
        /// </summary>
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "FromDownloadInfo")]
        [ValidateNotNull]
        public WindowsISODownloadInfo InputObject { get; set; } = null!;

        /// <summary>
        /// A manually obtained ISO URL, used as a bypass if Get-WindowsISODownloadInfo's automated flow fails
        /// </summary>
        [Parameter(Mandatory = true, ParameterSetName = "FromUrl")]
        [ValidateNotNull]
        public Uri Url { get; set; } = null!;

        /// <summary>
        /// Local path to save the ISO to
        /// </summary>
        [Parameter(Mandatory = true, Position = 1)]
        [ValidateNotNull]
        public FileInfo DestinationPath { get; set; } = null!;

        /// <summary>
        /// Re-download even if the destination file already exists
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Force { get; set; }

        /// <summary>
        /// Resume an existing partial download
        /// </summary>
        [Parameter(Mandatory = false)]
        public SwitchParameter Resume { get; set; }

        private const string ComponentName = "SaveWindowsISO";

        /// <summary>
        /// Processes the cmdlet
        /// </summary>
        protected override void ProcessRecord()
        {
            var downloadUrl = ParameterSetName == "FromUrl" ? Url : InputObject.Url;

            if (DestinationPath.Exists && !Force.IsPresent && !Resume.IsPresent)
            {
                LoggingService.WriteVerbose(this, ComponentName, $"File already exists, skipping: {DestinationPath.FullName}");
                WriteObject(DestinationPath);
                return;
            }

            var operationStartTime = LoggingService.LogOperationStartWithTimestamp(this, ComponentName,
                "Download Windows ISO", $"{downloadUrl} -> {DestinationPath.FullName}");

            try
            {
                var progressCallback = ProgressService.CreateDownloadProgressCallback(
                    this, "Downloading Windows ISO", DestinationPath.Name, 1, 1);

                var success = NetworkService.DownloadFileWithResume(downloadUrl, DestinationPath, Resume.IsPresent, this, progressCallback);

                if (!success)
                {
                    ThrowTerminatingError(new ErrorRecord(
                        new InvalidOperationException($"Failed to download ISO from {downloadUrl}"),
                        "SaveWindowsISOFailed",
                        ErrorCategory.NotSpecified,
                        downloadUrl));
                    return;
                }

                DestinationPath.Refresh();

                LoggingService.LogOperationCompleteWithTimestamp(this, ComponentName, "Download Windows ISO", operationStartTime,
                    $"Downloaded {DestinationPath.FullName}");

                WriteObject(DestinationPath);
            }
            catch (Exception ex)
            {
                LoggingService.WriteError(this, ComponentName, $"Failed to download ISO: {ex.Message}", ex);
                ThrowTerminatingError(new ErrorRecord(ex, "SaveWindowsISOFailed", ErrorCategory.NotSpecified, downloadUrl));
            }
        }
    }
}
```

- [ ] **Step 3: Add `Save-WindowsISO` to the module manifest**

In `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`, add to the `# ISO Media Management` group:

```
        # ISO Media Management
        'Export-WindowsISO',
        'New-WindowsISO',
        'Get-WindowsISODownloadInfo',
        'Save-WindowsISO',
```

- [ ] **Step 4: Build to confirm everything compiles**

Run: `dotnet build src/PSWindowsImageTools.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj`
Expected: PASS (all tests, including every test added in Tasks 1, 2, 3, and 7)

- [ ] **Step 6: Manual end-to-end verification (requires live internet access and administrator rights)**

```powershell
Import-Module .\Module\PSWindowsImageTools\PSWindowsImageTools.psd1 -Force

Get-WindowsISODownloadInfo -Architecture x64 |
    Save-WindowsISO -DestinationPath C:\ISO\Win11.iso

$media = Export-WindowsISO -IsoPath C:\ISO\Win11.iso -DestinationPath C:\Media\Win11

$images = Get-WindowsImageList -ImagePath $media.InstallWim.FullName
$mounted = $images[0] | Mount-WindowsImageList -MountPath C:\Mount -ReadWrite
"test" | Out-File (Join-Path $mounted.MountPath.FullName 'marker.txt')
"test" | Out-File (Join-Path $mounted.WinRE.MountPath.FullName 'winre-marker.txt')
$mounted | Dismount-WindowsImageList -Save

New-WindowsISO -SourcePath $media.Root.FullName -DestinationPath C:\ISO\Win11-serviced.iso

# Round-trip: re-extract the rebuilt ISO and confirm both edits persisted
$media2 = Export-WindowsISO -IsoPath C:\ISO\Win11-serviced.iso -DestinationPath C:\Media\Win11-Verify
$images2 = Get-WindowsImageList -ImagePath $media2.InstallWim.FullName
$mounted2 = $images2[0] | Mount-WindowsImageList -MountPath C:\MountVerify -ReadWrite
Test-Path (Join-Path $mounted2.MountPath.FullName 'marker.txt')                 # expect True
Test-Path (Join-Path $mounted2.WinRE.MountPath.FullName 'winre-marker.txt')     # expect True
$mounted2 | Dismount-WindowsImageList -Discard
```

- [ ] **Step 7: Commit**

```bash
git add src/Services/NetworkService.cs src/Cmdlets/SaveWindowsISOCmdlet.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "Add Save-WindowsISO to complete the ISO servicing pipeline"
```
