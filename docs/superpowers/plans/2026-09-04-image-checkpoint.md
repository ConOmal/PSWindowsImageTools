# Image Checkpoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Checkpoint-WindowsImage`, `Restore-WindowsImageCheckpoint`, and `Get-WindowsImageCheckpoint` — a lightweight, directory-mirror-based snapshot/rollback mechanism scoped to a single mount directory.

**Architecture:** One new subsystem (`src/Models/ImageCheckpointModels.cs`, `src/Services/ImageCheckpointService.cs`, `src/Cmdlets/ImageCheckpointCmdlets.cs`). Persists a JSON index of checkpoints mirroring `MountSessionService`'s existing flat-DTO pattern exactly (same file, same problem: `DirectoryInfo`/`FileInfo` can't serialize directly). Checkpoint/restore themselves are plain recursive `File.Copy` operations — no VSS, no external tools.

**Tech Stack:** C# / .NET (netstandard2.0), `Newtonsoft.Json`, xUnit, Pester.

**Spec:** `docs/superpowers/specs/2026-09-04-image-checkpoint-design.md`

## Global Constraints

- Cmdlet naming: `Verb-WindowsImage<Noun>`. `Checkpoint`/`Restore`/`Get` are all confirmed real approved PowerShell verbs this session via `Get-Verb` (`VerbsData.Checkpoint`, `VerbsData.Restore`, `VerbsCommon.Get`).
- **Confirmed existing pattern this plan mirrors exactly** (verified by reading the file this session): `MountSessionService` (`src/Services/MountSessionService.cs`) — static class, `private static readonly object _lock`, `StateFilePath` under `Path.Combine(Path.GetTempPath(), "PSWindowsImageTools", "<name>.json")`, a private sealed `*Entry` DTO class for JSON persistence (since `DirectoryInfo`/`FileInfo` can't serialize directly), `LoadState`/`SaveState` with try/catch-swallow (corrupt/unwritable state must never block cmdlets), `ToEntry`/`ToXxx` mapping methods. This plan's `ImageCheckpointService` follows the identical shape, with its own state file `checkpoints.json`.
- `MountedWindowsImage { MountId: string, MountPath: DirectoryInfo?, Status: MountStatus (enum: Mounted, Mounting, Unmounting, Unmounted, Failed, Corrupted), IsReadOnly: bool }` — confirmed exact fields this session by reading `src/Models/MountedWindowsImage.cs`.
- `Restore-WindowsImageCheckpoint` requires `SupportsShouldProcess = true` (discards current mount state) and must verify the target `MountedWindowsImage.Status == MountStatus.Mounted && !IsReadOnly` before proceeding — throw a clear terminating error otherwise, don't silently no-op.
- `Checkpoint-WindowsImage`/`Get-WindowsImageCheckpoint` are non-mutating, no `SupportsShouldProcess` needed.
- No VSS, no `robocopy.exe` shell-out — plain recursive `Directory`/`File` walk, matching how `Export-WindowsImageDriver` (Phase 1) already does its own recursive copy without shelling out.
- This repo commits its compiled binary module DLL alongside source changes.
- **Working-tree note**: shared checkout with other concurrent automations. Only `git add` files this plan's tasks explicitly name.

---

### Task 1: Models + JSON index + Create + Checkpoint-WindowsImage cmdlet

**Files:**
- Create: `src/Models/ImageCheckpointModels.cs`
- Create: `src/Services/ImageCheckpointService.cs`
- Create: `src/Cmdlets/ImageCheckpointCmdlets.cs`
- Test: `tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs`

**Interfaces:**
- Produces: `ImageCheckpointInfo { CheckpointId: string, MountId: string, Label: string?, CreatedAt: DateTime, SizeBytes: long, CheckpointPath: DirectoryInfo }`
- Produces: `ImageCheckpointService.Create(MountedWindowsImage mountedImage, string? label) -> ImageCheckpointInfo` — real file I/O, unit-testable with a fake `MountedWindowsImage` pointing at a temp directory (no DISM/mount involved at all, matches the spec's stated testing approach).

- [x] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ImageCheckpointServiceTests : IDisposable
    {
        private readonly string _mountDir;
        private readonly string _checkpointRoot;

        public ImageCheckpointServiceTests()
        {
            _mountDir = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Mount-" + Guid.NewGuid().ToString("N"));
            _checkpointRoot = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Checkpoints-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_mountDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_mountDir)) Directory.Delete(_mountDir, true);
            if (Directory.Exists(_checkpointRoot)) Directory.Delete(_checkpointRoot, true);
        }

        private MountedWindowsImage MakeMountedImage()
        {
            return new MountedWindowsImage
            {
                MountId = "test-mount-id",
                ImageName = "Test Image",
                MountPath = new DirectoryInfo(_mountDir),
                Status = MountStatus.Mounted,
                IsReadOnly = false
            };
        }

        [Fact]
        public void Create_CopiesFilesToCheckpointDirectory()
        {
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "original content");

            var service = new ImageCheckpointService(_checkpointRoot);
            var checkpoint = service.Create(MakeMountedImage(), "before-change");

            Assert.Equal("before-change", checkpoint.Label);
            Assert.Equal("test-mount-id", checkpoint.MountId);
            Assert.True(checkpoint.CheckpointPath.Exists);

            var copiedFile = Path.Combine(checkpoint.CheckpointPath.FullName, "marker.txt");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("original content", File.ReadAllText(copiedFile));
        }

        [Fact]
        public void Create_ComputesNonZeroSizeBytesForNonEmptyDirectory()
        {
            File.WriteAllBytes(Path.Combine(_mountDir, "data.bin"), new byte[1024]);

            var service = new ImageCheckpointService(_checkpointRoot);
            var checkpoint = service.Create(MakeMountedImage(), null);

            Assert.True(checkpoint.SizeBytes >= 1024);
        }
    }
}
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: FAIL (build error — types don't exist yet)

- [x] **Step 3: Create the model**

```csharp
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
```

- [x] **Step 4: Create the service with Create() and JSON index persistence**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Creates, lists, restores, and deletes point-in-time checkpoints of a mounted Windows
    /// image's on-disk state — a plain recursive file mirror, not VSS, so it can checkpoint a
    /// single mount directory rather than a whole volume. State (the checkpoint index) is
    /// persisted as JSON under the module's temp directory, mirroring MountSessionService's
    /// existing pattern for the same DirectoryInfo-can't-serialize reason.
    /// </summary>
    public class ImageCheckpointService
    {
        private const string ServiceName = "ImageCheckpointService";
        private static readonly object _lock = new object();
        private readonly ModuleCallbacks _callbacks;
        private readonly string _checkpointRoot;
        private readonly string _stateFilePath;

        /// <param name="checkpointRoot">Root directory checkpoints are stored under. Defaults to
        /// %TEMP%\PSWindowsImageTools\checkpoints, matching MountSessionService's convention.
        /// A non-default root is accepted for testability.</param>
        public ImageCheckpointService(string? checkpointRoot = null, ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
            _checkpointRoot = checkpointRoot ?? Path.Combine(Path.GetTempPath(), "PSWindowsImageTools", "checkpoints");
            _stateFilePath = Path.Combine(_checkpointRoot, "checkpoints.json");
        }

        private sealed class CheckpointEntry
        {
            public string CheckpointId { get; set; } = string.Empty;
            public string MountId { get; set; } = string.Empty;
            public string? Label { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public long SizeBytes { get; set; }
            public string CheckpointPath { get; set; } = string.Empty;
        }

        /// <summary>
        /// Creates a checkpoint of a mounted image's current on-disk state
        /// </summary>
        public ImageCheckpointInfo Create(MountedWindowsImage mountedImage, string? label)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var checkpointId = Guid.NewGuid().ToString("N");
            var checkpointDir = Path.Combine(_checkpointRoot, checkpointId);
            Directory.CreateDirectory(checkpointDir);

            _callbacks.Verbose?.Invoke($"Creating checkpoint {checkpointId} of {mountedImage.ImageName}");
            CopyDirectory(mountedImage.MountPath.FullName, checkpointDir);

            var sizeBytes = new DirectoryInfo(checkpointDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            var info = new ImageCheckpointInfo
            {
                CheckpointId = checkpointId,
                MountId = mountedImage.MountId,
                Label = label,
                SizeBytes = sizeBytes,
                CheckpointPath = new DirectoryInfo(checkpointDir)
            };

            lock (_lock)
            {
                var entries = LoadState();
                entries.Add(ToEntry(info));
                SaveState(entries);
            }

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpointId} created: {sizeBytes / 1024.0 / 1024.0:F1} MB");
            return info;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetPath = Path.Combine(destinationDir, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
            }
        }

        private static ImageCheckpointInfo ToInfo(CheckpointEntry entry)
        {
            return new ImageCheckpointInfo
            {
                CheckpointId = entry.CheckpointId,
                MountId = entry.MountId,
                Label = entry.Label,
                CreatedAt = entry.CreatedAt,
                SizeBytes = entry.SizeBytes,
                CheckpointPath = new DirectoryInfo(entry.CheckpointPath)
            };
        }

        private static CheckpointEntry ToEntry(ImageCheckpointInfo info)
        {
            return new CheckpointEntry
            {
                CheckpointId = info.CheckpointId,
                MountId = info.MountId,
                Label = info.Label,
                CreatedAt = info.CreatedAt,
                SizeBytes = info.SizeBytes,
                CheckpointPath = info.CheckpointPath.FullName
            };
        }

        private List<CheckpointEntry> LoadState()
        {
            try
            {
                if (!File.Exists(_stateFilePath))
                {
                    return new List<CheckpointEntry>();
                }

                var json = File.ReadAllText(_stateFilePath);
                return JsonConvert.DeserializeObject<List<CheckpointEntry>>(json) ?? new List<CheckpointEntry>();
            }
            catch
            {
                return new List<CheckpointEntry>();
            }
        }

        private void SaveState(List<CheckpointEntry> entries)
        {
            try
            {
                if (!Directory.Exists(_checkpointRoot))
                {
                    Directory.CreateDirectory(_checkpointRoot);
                }

                File.WriteAllText(_stateFilePath, JsonConvert.SerializeObject(entries, Formatting.Indented));
            }
            catch
            {
                // Best effort: failure to persist checkpoint index should not break operations
            }
        }
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: PASS (both tests)

- [x] **Step 6: Create the cmdlet**

```csharp
using System;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Creates a checkpoint of a mounted Windows image's current on-disk state
    /// </summary>
    [Cmdlet(VerbsData.Checkpoint, "WindowsImage")]
    [OutputType(typeof(ImageCheckpointInfo))]
    public class CheckpointWindowsImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Checkpoint-WindowsImage";

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows image to checkpoint")]
        [ValidateNotNull]
        public MountedWindowsImage MountedImage { get; set; } = null!;

        [Parameter(HelpMessage = "Optional label for this checkpoint")]
        public string? Label { get; set; }

        protected override void ProcessRecord()
        {
            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));

            try
            {
                var checkpoint = service.Create(MountedImage, Label);
                WriteObject(checkpoint);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "CheckpointFailed", ErrorCategory.WriteError, MountedImage));
            }
        }
    }
}
```

- [ ] **Step 7: Build the module and smoke-test the cmdlet is registered**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Checkpoint-WindowsImage'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.
Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Checkpoint-WindowsImage"` — expect the cmdlet to be found.

- [ ] **Step 8: Commit**

```bash
git add src/Models/ImageCheckpointModels.cs src/Services/ImageCheckpointService.cs src/Cmdlets/ImageCheckpointCmdlets.cs tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Checkpoint-WindowsImage cmdlet"
```

Rebuild and commit the DLL as a follow-up commit:

```bash
dotnet build PSWindowsImageTools.sln
cp Artifacts/bin/PSWindowsImageTools.dll Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git add Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git commit -m "build: rebuild PSWindowsImageTools.dll for Checkpoint-WindowsImage"
```

---

### Task 2: List() + Restore() + Get-WindowsImageCheckpoint + Restore-WindowsImageCheckpoint cmdlets

**Files:**
- Modify: `src/Services/ImageCheckpointService.cs`
- Modify: `src/Cmdlets/ImageCheckpointCmdlets.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`

**Interfaces:**
- Consumes: `ImageCheckpointInfo`, `ImageCheckpointService.Create` (Task 1).
- Produces: `ImageCheckpointService.List(string? mountId) -> List<ImageCheckpointInfo>`. `ImageCheckpointService.Restore(ImageCheckpointInfo checkpoint, MountedWindowsImage mountedImage) -> void`.

- [x] **Step 1: Write the failing tests**

Append to `tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs`:

```csharp
        [Fact]
        public void List_ReturnsCreatedCheckpoints_FilteredByMountId()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var image1 = MakeMountedImage();
            image1.MountId = "mount-1";
            var image2 = MakeMountedImage();
            image2.MountId = "mount-2";

            service.Create(image1, "cp1");
            service.Create(image2, "cp2");

            var all = service.List(null);
            Assert.Equal(2, all.Count);

            var filtered = service.List("mount-1");
            Assert.Single(filtered);
            Assert.Equal("cp1", filtered[0].Label);
        }

        [Fact]
        public void Restore_RevertsModifiedFileToCheckpointContent()
        {
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "original content");

            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "before-edit");

            // Simulate a servicing edit after the checkpoint
            File.WriteAllText(Path.Combine(_mountDir, "marker.txt"), "modified content");
            File.WriteAllText(Path.Combine(_mountDir, "new-file.txt"), "should be removed on restore");

            service.Restore(checkpoint, mountedImage);

            Assert.Equal("original content", File.ReadAllText(Path.Combine(_mountDir, "marker.txt")));
            Assert.False(File.Exists(Path.Combine(_mountDir, "new-file.txt")));
        }

        [Fact]
        public void Restore_ReadOnlyMount_ThrowsInvalidOperationException()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "cp");

            mountedImage.IsReadOnly = true;

            Assert.Throws<InvalidOperationException>(() => service.Restore(checkpoint, mountedImage));
        }

        [Fact]
        public void Restore_UnmountedImage_ThrowsInvalidOperationException()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "cp");

            mountedImage.Status = MountStatus.Unmounted;

            Assert.Throws<InvalidOperationException>(() => service.Restore(checkpoint, mountedImage));
        }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: FAIL (`List`/`Restore` not defined)

- [x] **Step 3: Implement List and Restore**

Add to `src/Services/ImageCheckpointService.cs` (inside the `ImageCheckpointService` class, after `Create`):

```csharp
        /// <summary>
        /// Lists checkpoints, optionally filtered to one mount
        /// </summary>
        public List<ImageCheckpointInfo> List(string? mountId)
        {
            lock (_lock)
            {
                var entries = LoadState();
                if (!string.IsNullOrEmpty(mountId))
                {
                    entries = entries.Where(e => string.Equals(e.MountId, mountId, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return entries.Select(ToInfo).ToList();
            }
        }

        /// <summary>
        /// Restores a mounted image's directory to a previously taken checkpoint. The target
        /// must currently be mounted read-write — restoring is inherently a mutation, and a
        /// read-only or already-dismounted target cannot be safely overwritten.
        /// </summary>
        public void Restore(ImageCheckpointInfo checkpoint, MountedWindowsImage mountedImage)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            if (mountedImage.Status != MountStatus.Mounted)
            {
                throw new InvalidOperationException(
                    $"Cannot restore checkpoint: image {mountedImage.ImageName} is not currently mounted (status: {mountedImage.Status})");
            }

            if (mountedImage.IsReadOnly)
            {
                throw new InvalidOperationException(
                    $"Cannot restore checkpoint: image {mountedImage.ImageName} is mounted read-only");
            }

            _callbacks.Verbose?.Invoke($"Restoring checkpoint {checkpoint.CheckpointId} onto {mountedImage.ImageName}");

            var mountPath = mountedImage.MountPath.FullName;

            // Remove current contents, then mirror the checkpoint back in
            foreach (var entry in Directory.EnumerateFileSystemEntries(mountPath))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, true);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            CopyDirectory(checkpoint.CheckpointPath.FullName, mountPath);

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpoint.CheckpointId} restored onto {mountedImage.ImageName}");
        }
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: PASS (all 6 tests: 2 from Task 1 + 4 new)

- [x] **Step 5: Add the cmdlets**

Add to `src/Cmdlets/ImageCheckpointCmdlets.cs`:

```csharp
    /// <summary>
    /// Lists checkpoints, optionally for a specific mounted image
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageCheckpoint")]
    [OutputType(typeof(ImageCheckpointInfo[]))]
    public class GetWindowsImageCheckpointCmdlet : PSCmdlet
    {
        [Parameter(HelpMessage = "Only list checkpoints for this mounted image")]
        public MountedWindowsImage? MountedImage { get; set; }

        protected override void ProcessRecord()
        {
            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));
            var checkpoints = service.List(MountedImage?.MountId);
            WriteObject(checkpoints.ToArray());
        }
    }

    /// <summary>
    /// Restores a mounted Windows image's directory to a previously taken checkpoint
    /// </summary>
    [Cmdlet(VerbsData.Restore, "WindowsImageCheckpoint", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class RestoreWindowsImageCheckpointCmdlet : PSCmdlet
    {
        private const string ComponentName = "Restore-WindowsImageCheckpoint";
        private readonly List<ImageCheckpointInfo> _allCheckpoints = new List<ImageCheckpointInfo>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Checkpoint(s) to restore")]
        [ValidateNotNull]
        public ImageCheckpointInfo[] Checkpoint { get; set; } = Array.Empty<ImageCheckpointInfo>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Mounted image to restore into")]
        [ValidateNotNull]
        public MountedWindowsImage MountedImage { get; set; } = null!;

        [Parameter(HelpMessage = "Continue processing other checkpoints if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allCheckpoints.AddRange(Checkpoint);
        }

        protected override void EndProcessing()
        {
            if (_allCheckpoints.Count == 0)
            {
                LoggingService.WriteWarning(this, "No checkpoints provided to restore");
                return;
            }

            var service = new ImageCheckpointService(callbacks: ModuleCallbacks.FromCmdlet(this));

            foreach (var checkpoint in _allCheckpoints)
            {
                var target = MountedImage.MountPath?.FullName ?? MountedImage.ImageName;
                if (!ShouldProcess(target, $"Restore checkpoint {(checkpoint.Label ?? checkpoint.CheckpointId)}"))
                {
                    continue;
                }

                try
                {
                    service.Restore(checkpoint, MountedImage);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to restore checkpoint {checkpoint.CheckpointId}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
```

Add `using System.Collections.Generic;` to the top of `ImageCheckpointCmdlets.cs` if not already present.

- [x] **Step 6: Build and register the cmdlets**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Get-WindowsImageCheckpoint'` and `'Restore-WindowsImageCheckpoint'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 7: Commit**

```bash
git add src/Services/ImageCheckpointService.cs src/Cmdlets/ImageCheckpointCmdlets.cs tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsImageCheckpoint and Restore-WindowsImageCheckpoint cmdlets"
```

Rebuild and commit the DLL as a follow-up commit, same pattern as Task 1.

---

### Task 3: Delete() + -RemoveAfterRestore + integration test + verification

**Files:**
- Modify: `src/Services/ImageCheckpointService.cs`
- Modify: `src/Cmdlets/ImageCheckpointCmdlets.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `ImageCheckpointInfo`, `ImageCheckpointService.Restore` (Task 2).
- Produces: `ImageCheckpointService.Delete(ImageCheckpointInfo checkpoint) -> void`.

- [x] **Step 1: Write the failing test**

Append to `tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs`:

```csharp
        [Fact]
        public void Delete_RemovesCheckpointDirectoryAndIndexEntry()
        {
            var service = new ImageCheckpointService(_checkpointRoot);
            var mountedImage = MakeMountedImage();
            var checkpoint = service.Create(mountedImage, "to-delete");

            Assert.True(checkpoint.CheckpointPath.Exists);

            service.Delete(checkpoint);

            Assert.False(Directory.Exists(checkpoint.CheckpointPath.FullName));
            Assert.Empty(service.List(mountedImage.MountId));
        }
```

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: FAIL (`Delete` not defined)

- [x] **Step 3: Implement Delete**

Add to `src/Services/ImageCheckpointService.cs` (inside the class, after `Restore`):

```csharp
        /// <summary>
        /// Deletes a checkpoint: removes its directory and index entry
        /// </summary>
        public void Delete(ImageCheckpointInfo checkpoint)
        {
            if (checkpoint.CheckpointPath.Exists)
            {
                Directory.Delete(checkpoint.CheckpointPath.FullName, recursive: true);
            }

            lock (_lock)
            {
                var entries = LoadState();
                entries.RemoveAll(e => e.CheckpointId == checkpoint.CheckpointId);
                SaveState(entries);
            }

            _callbacks.Verbose?.Invoke($"Checkpoint {checkpoint.CheckpointId} deleted");
        }
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageCheckpointServiceTests`
Expected: PASS (all 7 tests)

- [x] **Step 5: Wire -RemoveAfterRestore into RestoreWindowsImageCheckpointCmdlet**

Modify `RestoreWindowsImageCheckpointCmdlet` in `src/Cmdlets/ImageCheckpointCmdlets.cs`: add a new parameter and call `service.Delete(checkpoint)` after a successful restore.

```csharp
        [Parameter(HelpMessage = "Delete the checkpoint after successfully restoring it")]
        public SwitchParameter RemoveAfterRestore { get; set; }
```

In the `try` block inside `EndProcessing`'s `foreach`, after `service.Restore(checkpoint, MountedImage);` succeeds, add:

```csharp
                    if (RemoveAfterRestore.IsPresent)
                    {
                        service.Delete(checkpoint);
                    }
```

- [x] **Step 6: Build and verify**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings. (No new cmdlet added this step, no `psd1` change needed.)

- [ ] **Step 7: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`:

```powershell
Describe "Integration: image checkpoint" -Tag Integration {

    It "checkpoints, modifies, and restores a mounted image" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $markerPath = Join-Path $mounted.MountPath.FullName "marker.txt"
            $checkpoint = $mounted | Checkpoint-WindowsImage -Label "baseline"
            $checkpoint | Should -Not -BeNullOrEmpty

            Set-Content -Path $markerPath -Value "modified-after-checkpoint"

            $checkpoint | Restore-WindowsImageCheckpoint -MountedImage $mounted -Confirm:$false

            Get-Content $markerPath -Raw | Should -Match "integration-test"
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 8: Commit**

```bash
git add src/Services/ImageCheckpointService.cs src/Cmdlets/ImageCheckpointCmdlets.cs tests/PSWindowsImageTools.Tests/ImageCheckpointServiceTests.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1
git commit -m "feat: add checkpoint deletion and -RemoveAfterRestore"
```

Rebuild and commit the DLL as a follow-up commit, same pattern as prior tasks.

---

### Task 4: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests`
Expected: PASS — all pre-existing tests plus the 7 new ones across Tasks 1-3.

- [x] **Step 2: Build the full solution**

Run: `dotnet build PSWindowsImageTools.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Verify the module manifest lists all 3 new cmdlets and PowerShell can discover them**

Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Checkpoint-WindowsImage, Get-WindowsImageCheckpoint, Restore-WindowsImageCheckpoint"`
Expected: all 3 cmdlets found.

- [ ] **Step 4: Run the integration suite (requires an elevated Windows session with real DISM)**

Run: `pwsh tests/integration/run-integration.ps1`
Expected: PASS — including the `-Tag Integration` describe block added in Task 3.

- [ ] **Step 5: Commit any final cleanup**

```bash
git status
```

If the working tree is clean (aside from unrelated files belonging to other concurrent sessions — do not touch those), no commit is needed.
