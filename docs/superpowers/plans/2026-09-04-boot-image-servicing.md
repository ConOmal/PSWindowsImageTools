# Boot Image Servicing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a boot.wim-specific convenience layer — `Get-WindowsBootImage`, `Add-WindowsBootDriver`, `Optimize-WindowsBootImage` — as thin wrappers over existing generic WIM/driver/component-store services.

**Architecture:** One new subsystem (`src/Models/BootImageModels.cs`, `src/Services/BootImageService.cs`, `src/Cmdlets/BootImageCmdlets.cs`) that locates and reports on `boot.wim` (reusing the existing `WindowsInstallationMedia.FromRoot`) and delegates driver injection / component cleanup to already-existing services (`IWindowsImageService.AddDriversFromDirectory`, `ComponentStoreService.Cleanup`) — no new DISM API surface.

**Tech Stack:** C# / .NET (netstandard2.0), `Microsoft.Dism`, xUnit, Pester.

**Spec:** `docs/superpowers/specs/2026-09-04-boot-image-servicing-design.md`

## Global Constraints

- Cmdlet naming: `Verb-WindowsBootImage`/`Verb-WindowsBootDriver`. `Get`/`Add`/`Optimize` = `VerbsCommon.Get`/`VerbsCommon.Add`/`VerbsCommon.Optimize`.
- **Confirmed existing types/methods this plan reuses, verified against the actual source this session** (do not re-derive or guess these signatures):
  - `WindowsInstallationMedia.FromRoot(DirectoryInfo root) -> WindowsInstallationMedia` with `.BootWim: FileInfo?` (`src/Models/WindowsInstallationMedia.cs`).
  - `IWindowsImageService.AddDriversFromDirectory(string mountPath, string driverDirectory, bool forceUnsigned = false, bool recursive = true, Action<int, string>? progressCallback = null)`.
  - `IWindowsImageService.GetImageInfo(string imagePath) -> List<WindowsImageInfo>`.
  - `ComponentStoreService.Cleanup(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool resetBase, PSCmdlet cmdlet, int timeoutMinutes = 90) -> ComponentStoreCleanupResult` (from the Phase 1 plan, already merged to `main`).
  - `MountedWindowsImage { MountId, SourceImagePath, ImageIndex, ImageName, Edition, Architecture, MountPath: DirectoryInfo?, Status: MountStatus, IsReadOnly: bool }`.
- Mutating cmdlets (`Add-WindowsBootDriver`, `Optimize-WindowsBootImage`) require `SupportsShouldProcess = true` + per-image `ShouldProcess`.
- Multi-image cmdlets accept `-ContinueOnError`, matching every prior Phase 1/2 cmdlet's convention (catch → `LoggingService.WriteError` → conditional rethrow).
- `Get-WindowsBootImage` returns/warns (does not throw) when no `boot.wim` is found — this is an expected, non-error outcome for some media layouts.
- `ResetBase` is hardcoded `false` in `Optimize` — a boot/PE image has no update history to reset; this is a deliberate design choice, not a limitation, and must be documented as such in the code (one-line comment).
- This repo commits its compiled binary module DLL (`Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll`) alongside source changes.
- **Working-tree note**: this checkout is shared with other, unrelated, concurrently-active automations. `git status` may show files unrelated to this plan (other subsystems' work). Do not touch, stage, or commit anything this plan's tasks don't explicitly name.
- **DLL rebuild note**: builds performed directly in this working directory can pick up unrelated in-flight changes from other sessions and produce a DLL whose embedded `SourceRevisionId` looks "off" relative to a from-scratch build — this is expected/cosmetic (confirmed this session via byte-level diffing: the difference is only a build timestamp/MVID/git-SHA, not actual foreign code), not something to chase or "fix" with a worktree rebuild. Build normally in this directory.

---

### Task 1: BootImageInfo model + Locate + Get-WindowsBootImage cmdlet

**Files:**
- Create: `src/Models/BootImageModels.cs`
- Create: `src/Services/BootImageService.cs`
- Create: `src/Cmdlets/BootImageCmdlets.cs`
- Test: `tests/PSWindowsImageTools.Tests/BootImageServiceTests.cs`

**Interfaces:**
- Produces: `BootImageInfo { Path: FileInfo, SourceMediaRoot: string?, ImageCount: int, Images: List<WindowsImageInfo> }`
- Produces: `BootImageService.Locate(DirectoryInfo mediaRoot, IWindowsImageService? imageService = null) -> BootImageInfo?`

- [x] **Step 1: Write the failing tests**

```csharp
using System;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class BootImageServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public BootImageServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Fact]
        public void Locate_BootWimPresent_ReturnsBootImageInfo()
        {
            var sourcesDir = Path.Combine(_tempDirectory, "sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllBytes(Path.Combine(sourcesDir, "boot.wim"), new byte[] { 0x00 });

            var result = new BootImageService().Locate(new DirectoryInfo(_tempDirectory));

            Assert.NotNull(result);
            Assert.Equal("boot.wim", result!.Path.Name);
            Assert.Equal(_tempDirectory, result.SourceMediaRoot);
        }

        [Fact]
        public void Locate_NoBootWim_ReturnsNull()
        {
            var result = new BootImageService().Locate(new DirectoryInfo(_tempDirectory));

            Assert.Null(result);
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter BootImageServiceTests`
Expected: FAIL (build error — types don't exist yet)

- [x] **Step 3: Create the model**

```csharp
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
```

- [x] **Step 4: Create the service**

```csharp
using System.IO;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Locates and services boot.wim (the WinPE-based Setup/PE image on Windows installation
    /// media) — a thin convenience layer over the module's generic WIM/driver/component-store
    /// services, since boot.wim is serviced through exactly the same mechanisms as any other WIM.
    /// </summary>
    public class BootImageService
    {
        private const string ServiceName = "BootImageService";
        private readonly ModuleCallbacks _callbacks;

        public BootImageService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Locates boot.wim under an extracted media root and reports the images it contains.
        /// Returns null if no boot.wim is present — a normal outcome for some media layouts, not
        /// an error.
        /// </summary>
        public BootImageInfo? Locate(DirectoryInfo mediaRoot, IWindowsImageService? imageService = null)
        {
            var media = WindowsInstallationMedia.FromRoot(mediaRoot);

            if (media.BootWim == null)
            {
                _callbacks.Verbose?.Invoke($"No boot.wim found under {mediaRoot.FullName}");
                return null;
            }

            var info = new BootImageInfo
            {
                Path = media.BootWim,
                SourceMediaRoot = mediaRoot.FullName
            };

            if (imageService != null)
            {
                try
                {
                    info.Images = imageService.GetImageInfo(media.BootWim.FullName);
                }
                catch (System.Exception ex)
                {
                    _callbacks.Warning?.Invoke($"Failed to read boot.wim image info: {ex.Message}");
                }
            }

            return info;
        }
    }
}
```

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter BootImageServiceTests`
Expected: PASS (both tests)

- [x] **Step 6: Create the cmdlet**

```csharp
using System;
using System.IO;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Locates boot.wim under an extracted Windows installation media root and reports the
    /// images it contains
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsBootImage")]
    [OutputType(typeof(BootImageInfo))]
    public class GetWindowsBootImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsBootImage";

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Root directory of extracted Windows installation media")]
        [ValidateNotNull]
        public DirectoryInfo MediaRoot { get; set; } = null!;

        protected override void ProcessRecord()
        {
            if (!MediaRoot.Exists)
            {
                LoggingService.WriteWarning(this, $"Media root does not exist: {MediaRoot.FullName}");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));

            var result = bootImageService.Locate(MediaRoot, imageService);

            if (result == null)
            {
                LoggingService.WriteWarning(this, $"No boot.wim found under {MediaRoot.FullName}");
                return;
            }

            WriteObject(result);
        }
    }
}
```

- [ ] **Step 7: Build the module and smoke-test the cmdlet is registered**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Get-WindowsBootImage'` to the `CmdletsToExport` array in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (targeted single-line insert; don't reorder or reformat the rest of the array — other tasks in this plan and other concurrent plans also insert single lines into this same array).
Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsBootImage"` — expect the cmdlet to be found.

- [ ] **Step 8: Commit**

```bash
git add src/Models/BootImageModels.cs src/Services/BootImageService.cs src/Cmdlets/BootImageCmdlets.cs tests/PSWindowsImageTools.Tests/BootImageServiceTests.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsBootImage cmdlet"
```

Rebuild and commit the DLL as a small follow-up commit (repo convention):

```bash
dotnet build PSWindowsImageTools.sln
cp Artifacts/bin/PSWindowsImageTools.dll Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git add Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git commit -m "build: rebuild PSWindowsImageTools.dll for Get-WindowsBootImage"
```

---

### Task 2: Add-WindowsBootDriver + Optimize-WindowsBootImage cmdlets

**Files:**
- Modify: `src/Services/BootImageService.cs`
- Modify: `src/Cmdlets/BootImageCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`

**Interfaces:**
- Consumes: `IWindowsImageService.AddDriversFromDirectory` (existing, unchanged), `ComponentStoreService.Cleanup` (existing, from Phase 1, unchanged) — read their CURRENT signatures in `src/Services/WindowsImageService.cs`/`src/Services/ComponentStoreService.cs` before calling; the plan's Global Constraints list the signatures as verified at plan-authoring time but always confirm against the file on disk.
- Produces: `BootImageService.AddDriver(MountedWindowsImage, IWindowsImageService, DirectoryInfo driverDirectory, bool forceUnsigned)`. `BootImageService.Optimize(MountedWindowsImage, IWindowsImageService, PSCmdlet) -> ComponentStoreCleanupResult`.

Both are thin pass-throughs with no independent unit test — same DISM-facing constraint as every other Phase 1/2 wrapper method in this codebase.

- [x] **Step 1: Implement AddDriver and Optimize**

Add to `src/Services/BootImageService.cs` (inside the `BootImageService` class, after `Locate`):

```csharp
        /// <summary>
        /// Injects drivers into a mounted boot.wim
        /// </summary>
        public void AddDriver(MountedWindowsImage mountedImage, IWindowsImageService imageService, DirectoryInfo driverDirectory, bool forceUnsigned)
        {
            if (mountedImage.MountPath == null)
            {
                throw new System.InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            _callbacks.Verbose?.Invoke($"Adding drivers from {driverDirectory.FullName} to boot image {mountedImage.ImageName}");
            imageService.AddDriversFromDirectory(mountedImage.MountPath.FullName, driverDirectory.FullName, forceUnsigned);
        }

        /// <summary>
        /// Runs component cleanup against a mounted boot.wim. ResetBase is intentionally never
        /// offered here — a boot/PE image has no update history to reset, so the option would be
        /// meaningless, not merely unsupported.
        /// </summary>
        public Models.ComponentStoreCleanupResult Optimize(MountedWindowsImage mountedImage, IWindowsImageService imageService, PSCmdlet cmdlet)
        {
            return new ComponentStoreService(_callbacks).Cleanup(mountedImage, imageService, resetBase: false, cmdlet);
        }
```

Add `using System.Management.Automation;` to the top of `BootImageService.cs` if not already present.

- [x] **Step 2: Build to verify it compiles**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings. If `ComponentStoreCleanupResult`'s actual namespace differs from `Models.ComponentStoreCleanupResult` as written above (e.g. it's already in scope via a `using PSWindowsImageTools.Models;` at the top of the file), simplify to just `ComponentStoreCleanupResult` — check the file's current usings first.

- [x] **Step 3: Add the cmdlets**

Add to `src/Cmdlets/BootImageCmdlets.cs`:

```csharp
    /// <summary>
    /// Injects drivers into one or more mounted boot.wim images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsBootDriver", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class AddWindowsBootDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsBootDriver";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted boot images to add drivers to")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Directory containing driver INF files")]
        [ValidateNotNull]
        public DirectoryInfo DriverPath { get; set; } = null!;

        [Parameter(HelpMessage = "Allow installation of unsigned drivers")]
        public SwitchParameter ForceUnsigned { get; set; }

        [Parameter(HelpMessage = "Continue processing other images if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted boot images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, "Add boot drivers"))
                {
                    continue;
                }

                try
                {
                    bootImageService.AddDriver(mountedImage, imageService, DriverPath, ForceUnsigned.IsPresent);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to add drivers to {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Runs component cleanup against one or more mounted boot.wim images
    /// </summary>
    [Cmdlet(VerbsCommon.Optimize, "WindowsBootImage", SupportsShouldProcess = true)]
    [OutputType(typeof(ComponentStoreCleanupResult[]))]
    public class OptimizeWindowsBootImageCmdlet : PSCmdlet
    {
        private const string ComponentName = "Optimize-WindowsBootImage";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted boot images to optimize")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Continue processing other images if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted boot images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var bootImageService = new BootImageService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreCleanupResult>();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, "Optimize boot image component store"))
                {
                    continue;
                }

                try
                {
                    results.Add(bootImageService.Optimize(mountedImage, imageService, this));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to optimize {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(results.ToArray());
        }
    }
```

Add `using PSWindowsImageTools.Models;` to the top of `BootImageCmdlets.cs` if not already present (needed for `ComponentStoreCleanupResult`).

- [ ] **Step 4: Build and register the cmdlets**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Add-WindowsBootDriver'` and `'Optimize-WindowsBootImage'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 5: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (read the file's `BeforeAll` block first for the exact `$BaselineWim`/`$MountRoot`/`$Workspace` variable names):

```powershell
Describe "Integration: boot image servicing" -Tag Integration {

    It "adds drivers and optimizes a mounted boot image without error" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        $emptyDriverDir = Join-Path $Workspace "empty-drivers"
        New-Item -ItemType Directory -Force -Path $emptyDriverDir | Out-Null

        try {
            { $mounted | Add-WindowsBootDriver -DriverPath $emptyDriverDir -Confirm:$false } | Should -Not -Throw
            $result = $mounted | Optimize-WindowsBootImage -Confirm:$false
            $result | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add src/Services/BootImageService.cs src/Cmdlets/BootImageCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Add-WindowsBootDriver and Optimize-WindowsBootImage cmdlets"
```

Rebuild and commit the DLL as a follow-up commit, same as Task 1's Step 8.

---

### Task 3: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests`
Expected: PASS — all pre-existing tests plus the 2 new ones from Task 1.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build PSWindowsImageTools.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Verify the module manifest lists all 3 new cmdlets and PowerShell can discover them**

Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsBootImage, Add-WindowsBootDriver, Optimize-WindowsBootImage"`
Expected: all 3 cmdlets found.

- [ ] **Step 4: Run the integration suite (requires an elevated Windows session with real DISM)**

Run: `pwsh tests/integration/run-integration.ps1`
Expected: PASS — including the `-Tag Integration` describe block added in Task 2.

- [ ] **Step 5: Commit any final cleanup**

```bash
git status
```

If the working tree is clean (aside from unrelated files belonging to other concurrent sessions — do not touch those), no commit is needed.
