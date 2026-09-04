# Phase 1 Extensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add component-store analysis/cleanup, offline-image driver management, an SBOM export, and a composite health check to PSWindowsImageTools, as compiled C# cmdlets matching the module's existing conventions.

**Architecture:** Each subsystem is a `Services/*Service.cs` (DISM/filesystem work) + thin `Cmdlets/*Cmdlet.cs` (`PSCmdlet` wrapping) + `Models/*.cs` (output types), reusing `LoggingService`, `ModuleCallbacks`, `WindowsImageService.ForCmdlet(this)`, and `ProcessMonitoringService` exactly as existing cmdlets do. Where a method must call the real `Microsoft.Dism` API (whose types have non-public constructors and can't be faked in tests), the DISM-facing wrapper is kept thin and untested-by-unit-test; all classification/diffing/path-building logic is factored into separate `internal static` pure methods that unit tests drive directly.

**Tech Stack:** C# / .NET (netstandard2.0 per existing `.csproj`), `Microsoft.Dism`, `Newtonsoft.Json`, xUnit (`tests/PSWindowsImageTools.Tests/`), Pester (`tests/integration/`).

**Spec:** `docs/superpowers/specs/2026-09-03-phase1-component-store-drivers-inventory-validation-design.md`

## Global Constraints

- Cmdlet naming: `Verb-WindowsImage<Noun>` using PowerShell-approved verbs only (no `PSImage` prefix). Verified this session: `Get`/`Optimize`/`Remove` = `VerbsCommon`; `Compare`/`Export` = `VerbsData`; `Invoke` = `VerbsLifecycle`.
- `Microsoft.Dism.DismPackage` and `Microsoft.Dism.DismDriverPackage` have non-public constructors (confirmed via reflection) — never attempt to construct them in test code. Map to our own `Models` POCOs at the service boundary instead.
- `DismPackage.PackageState` is typed `Microsoft.Dism.DismPackageFeatureState` (shared with features), with values `NotPresent, UninstallPending, Staged, Resolved, Removed, Installed, InstallPending, Superseded, PartiallyInstalled` (confirmed via reflection).
- `DismApi.GetDrivers(DismSession session, bool allDrivers)`, `DismApi.RemoveDriver(DismSession session, string driverPath)`, `DismApi.CheckImageHealth`, `DismApi.RestoreImageHealth` are all confirmed present on the module's bundled `Microsoft.Dism.dll`.
- DISM session pattern (copy exactly, see `WindowsImageService.GetPackages`): `Initialize(); using var session = DismApi.OpenOfflineSession(mountPath); var x = DismApi.GetXxx(session).ToList();` inside try/catch that logs via `_callbacks.Error?.Invoke(ex, ...)` and rethrows.
- Mutating cmdlets must implement `SupportsShouldProcess` and call `ShouldProcess` before making changes.
- Multi-image cmdlets accept a `-ContinueOnError` switch; per-image failures are caught and recorded on the result object unless the switch is absent, in which case the exception propagates (mirrors `Reset-WindowsImageBase`).
- Unit tests live in `tests/PSWindowsImageTools.Tests/` (xUnit, one test class per service, temp-directory fixtures where filesystem access is needed — see `ImageComparisonServiceTests.cs` for the pattern). Integration tests live in `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (Pester, tag `Integration`, built against a synthetic DISM-captured WIM — see the existing `BeforeAll` block for the fixture pattern).

---

### Task 1: ComponentStoreReport/ComponentStoreCleanupResult models + package classification logic

**Files:**
- Create: `src/Models/ComponentStoreModels.cs`
- Create: `src/Services/ComponentStoreService.cs`
- Test: `tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs`

**Interfaces:**
- Produces: `ComponentStoreReport { ImageName, ImagePath, MountPath, GeneratedAt, WinSxSSizeMB, TotalPackages, InstalledPackages, SupersededPackages, PendingPackages, SupersededPackageNames: List<string>, Issues: List<string> }`
- Produces: `ComponentStoreCleanupResult { Before: ComponentStoreReport, After: ComponentStoreReport?, ExitCode: int, Duration: TimeSpan, Success: bool }`
- Produces: `ComponentStoreService.ClassifyPackages(IEnumerable<(string Name, DismPackageFeatureState State)>, ComponentStoreReport)` — `internal static`, pure.

- [ ] **Step 1: Write the failing test for package classification**

```csharp
using System.Collections.Generic;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ComponentStoreServiceTests
    {
        [Fact]
        public void ClassifyPackages_CountsInstalledSupersededAndPending()
        {
            var report = new ComponentStoreReport();
            var packages = new List<(string Name, DismPackageFeatureState State)>
            {
                ("Package-A", DismPackageFeatureState.Installed),
                ("Package-B", DismPackageFeatureState.Superseded),
                ("Package-C", DismPackageFeatureState.InstallPending),
                ("Package-D", DismPackageFeatureState.UninstallPending),
                ("Package-E", DismPackageFeatureState.Installed),
            };

            ComponentStoreService.ClassifyPackages(packages, report);

            Assert.Equal(5, report.TotalPackages);
            Assert.Equal(2, report.InstalledPackages);
            Assert.Equal(1, report.SupersededPackages);
            Assert.Equal(2, report.PendingPackages);
            Assert.Equal(new[] { "Package-B" }, report.SupersededPackageNames);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: FAIL (build error — `ComponentStoreReport`/`ComponentStoreService` don't exist yet)

- [ ] **Step 3: Create the models**

```csharp
using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Component-store (WinSxS) analysis for a mounted Windows image
    /// </summary>
    public class ComponentStoreReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public double WinSxSSizeMB { get; set; }
        public int TotalPackages { get; set; }
        public int InstalledPackages { get; set; }
        public int SupersededPackages { get; set; }
        public int PendingPackages { get; set; }
        public List<string> SupersededPackageNames { get; set; } = new List<string>();
        public List<string> Issues { get; set; } = new List<string>();

        public override string ToString() =>
            $"{ImageName}: {TotalPackages} packages, {SupersededPackages} superseded, WinSxS {WinSxSSizeMB:F1} MB";
    }

    /// <summary>
    /// Result of a component-store cleanup (StartComponentCleanup / ResetBase) operation
    /// </summary>
    public class ComponentStoreCleanupResult
    {
        public ComponentStoreReport Before { get; set; } = new ComponentStoreReport();
        public ComponentStoreReport? After { get; set; }
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success => ExitCode == 0;

        public override string ToString() =>
            $"{Before.ImageName}: cleanup {(Success ? "succeeded" : "failed")} (exit {ExitCode})";
    }
}
```

- [ ] **Step 4: Create the service with the pure classification method**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Analyzes and cleans up the WinSxS component store of a mounted Windows image
    /// </summary>
    public class ComponentStoreService
    {
        private const string ServiceName = "ComponentStoreService";
        private readonly ModuleCallbacks _callbacks;

        public ComponentStoreService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Classifies packages by state into report counters. Pure — no DISM/filesystem access.
        /// </summary>
        internal static void ClassifyPackages(IEnumerable<(string Name, DismPackageFeatureState State)> packages, ComponentStoreReport report)
        {
            foreach (var (name, state) in packages)
            {
                report.TotalPackages++;

                switch (state)
                {
                    case DismPackageFeatureState.Installed:
                        report.InstalledPackages++;
                        break;
                    case DismPackageFeatureState.Superseded:
                        report.SupersededPackages++;
                        report.SupersededPackageNames.Add(name);
                        break;
                    case DismPackageFeatureState.InstallPending:
                    case DismPackageFeatureState.UninstallPending:
                        report.PendingPackages++;
                        break;
                }
            }
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Models/ComponentStoreModels.cs src/Services/ComponentStoreService.cs tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs
git commit -m "feat: add ComponentStoreReport model and package classification logic"
```

---

### Task 2: WinSxS size helper + Analyze() + Get-WindowsImageComponentStore cmdlet

**Files:**
- Modify: `src/Services/ComponentStoreService.cs`
- Create: `src/Cmdlets/ComponentStoreCmdlets.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `ComponentStoreReport`, `ComponentStoreService.ClassifyPackages` (Task 1); `IWindowsImageService.GetPackages(string mountPath)`, `MountedWindowsImage { MountPath: DirectoryInfo?, ImageName, SourceImagePath }`, `WindowsImageService.ForCmdlet(PSCmdlet)` (existing).
- Produces: `ComponentStoreService.GetDirectorySizeMB(string path) -> double` (`internal static`, pure). `ComponentStoreService.Analyze(MountedWindowsImage, IWindowsImageService) -> ComponentStoreReport`.

- [ ] **Step 1: Write the failing test for the size helper**

```csharp
[Fact]
public void GetDirectorySizeMB_SumsFileSizesRecursively()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
    var nested = Path.Combine(tempDir, "nested");
    Directory.CreateDirectory(nested);
    try
    {
        File.WriteAllBytes(Path.Combine(tempDir, "a.bin"), new byte[1024 * 1024]);
        File.WriteAllBytes(Path.Combine(nested, "b.bin"), new byte[1024 * 1024]);

        var sizeMb = ComponentStoreService.GetDirectorySizeMB(tempDir);

        Assert.Equal(2.0, sizeMb, precision: 1);
    }
    finally
    {
        Directory.Delete(tempDir, true);
    }
}

[Fact]
public void GetDirectorySizeMB_MissingDirectory_ReturnsZero()
{
    var missing = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Missing-" + Guid.NewGuid().ToString("N"));
    Assert.Equal(0, ComponentStoreService.GetDirectorySizeMB(missing));
}
```

Add `using System.IO;` and `using System;` to the test file's usings if not already present.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: FAIL (`GetDirectorySizeMB` not defined)

- [ ] **Step 3: Implement GetDirectorySizeMB and Analyze**

Add to `src/Services/ComponentStoreService.cs` (inside the `ComponentStoreService` class, after `ClassifyPackages`):

```csharp
        /// <summary>
        /// Recursively sums file sizes under a directory, in MB. Returns 0 if missing. Pure.
        /// </summary>
        internal static double GetDirectorySizeMB(string path)
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long bytes = new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);

            return Math.Round(bytes / 1024.0 / 1024.0, 2);
        }

        /// <summary>
        /// Analyzes the component store of a mounted image (read-only)
        /// </summary>
        public ComponentStoreReport Analyze(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            _callbacks.Verbose?.Invoke($"Analyzing component store for {mountedImage.ImageName} at {mountPath}");

            var report = new ComponentStoreReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            try
            {
                var packages = imageService.GetPackages(mountPath);
                ClassifyPackages(packages.Select(p => (p.PackageName ?? string.Empty, p.PackageState)), report);
            }
            catch (Exception ex)
            {
                report.Issues.Add($"Failed to enumerate packages: {ex.Message}");
                _callbacks.Warning?.Invoke($"Failed to enumerate packages for {mountedImage.ImageName}: {ex.Message}");
            }

            report.WinSxSSizeMB = GetDirectorySizeMB(Path.Combine(mountPath, "Windows", "WinSxS"));

            _callbacks.Verbose?.Invoke($"Component store analysis complete for {mountedImage.ImageName}: {report}");
            return report;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: PASS

- [ ] **Step 5: Create the cmdlet**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Analyzes the WinSxS component store of one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageComponentStore")]
    [OutputType(typeof(ComponentStoreReport[]))]
    public class GetWindowsImageComponentStoreCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageComponentStore";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to analyze")]
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
                LoggingService.WriteWarning(this, "No mounted images provided for component store analysis");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var componentStoreService = new ComponentStoreService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    results.Add(componentStoreService.Analyze(mountedImage, imageService));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to analyze {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(results.ToArray());
        }
    }
}
```

- [ ] **Step 6: Build the module and smoke-test the cmdlet is registered**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Get-WindowsImageComponentStore'` to the `CmdletsToExport` array in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (alphabetically among the other `Get-WindowsImage*` entries).
Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageComponentStore"` — expect the cmdlet to be found.

- [ ] **Step 7: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (new `Describe` block, matching the file's existing style):

```powershell
Describe "Integration: component store" -Tag Integration {

    It "reports package counts and WinSxS size for a mounted image" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Get-WindowsImageComponentStore
            $report | Should -Not -BeNullOrEmpty
            $report.ImageName | Should -Be $mounted.ImageName
            $report.TotalPackages | Should -BeGreaterOrEqual 0
            $report.WinSxSSizeMB | Should -BeGreaterOrEqual 0
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 8: Commit**

```bash
git add src/Services/ComponentStoreService.cs src/Cmdlets/ComponentStoreCmdlets.cs tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsImageComponentStore cmdlet"
```

---

### Task 3: dism.exe cleanup argument builder + Cleanup() + Optimize-WindowsImageComponentStore cmdlet

**Files:**
- Modify: `src/Services/ComponentStoreService.cs`
- Modify: `src/Cmdlets/ComponentStoreCmdlets.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `ComponentStoreReport`, `ComponentStoreCleanupResult` (Task 1), `ComponentStoreService.Analyze` (Task 2), `ProcessMonitoringService.ExecuteProcessWithMonitoring` (existing, signature: `(string fileName, string arguments, string? workingDirectory, int timeoutMinutes, string progressTitle, string progressDescription, PSCmdlet? cmdlet) -> int`).
- Produces: `ComponentStoreService.BuildCleanupArguments(string mountPath, bool resetBase) -> string` (`internal static`, pure). `ComponentStoreService.Cleanup(MountedWindowsImage, IWindowsImageService, bool resetBase, PSCmdlet, int timeoutMinutes = 90) -> ComponentStoreCleanupResult`.

- [ ] **Step 1: Write the failing test for the argument builder**

```csharp
[Theory]
[InlineData(false, "/Image:\"C:\\Mount\" /Cleanup-Image /StartComponentCleanup")]
[InlineData(true, "/Image:\"C:\\Mount\" /Cleanup-Image /StartComponentCleanup /ResetBase")]
public void BuildCleanupArguments_ReturnsExpectedDismArgs(bool resetBase, string expected)
{
    var args = ComponentStoreService.BuildCleanupArguments(@"C:\Mount", resetBase);
    Assert.Equal(expected, args);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: FAIL (`BuildCleanupArguments` not defined)

- [ ] **Step 3: Implement BuildCleanupArguments and Cleanup**

Add to `src/Services/ComponentStoreService.cs`:

```csharp
        /// <summary>
        /// Builds the dism.exe argument string for component cleanup. Pure.
        /// </summary>
        internal static string BuildCleanupArguments(string mountPath, bool resetBase)
        {
            var args = $"/Image:\"{mountPath}\" /Cleanup-Image /StartComponentCleanup";
            return resetBase ? args + " /ResetBase" : args;
        }

        /// <summary>
        /// Runs component cleanup (and optionally ResetBase) against a mounted image via dism.exe,
        /// since Microsoft.Dism has no managed API for this operation. Captures a before/after report.
        /// </summary>
        public ComponentStoreCleanupResult Cleanup(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool resetBase, PSCmdlet cmdlet, int timeoutMinutes = 90)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var before = Analyze(mountedImage, imageService);
            var mountPath = mountedImage.MountPath.FullName;
            var args = BuildCleanupArguments(mountPath, resetBase);

            _callbacks.Verbose?.Invoke($"Running component cleanup for {mountedImage.ImageName}: dism.exe {args}");

            var startTime = DateTime.UtcNow;
            var processMonitor = new ProcessMonitoringService();
            var exitCode = processMonitor.ExecuteProcessWithMonitoring(
                "dism.exe",
                args,
                workingDirectory: null,
                timeoutMinutes: timeoutMinutes,
                progressTitle: "Optimizing Windows Image Component Store",
                progressDescription: $"Cleaning up {mountedImage.ImageName}",
                cmdlet);
            var duration = DateTime.UtcNow - startTime;

            var result = new ComponentStoreCleanupResult
            {
                Before = before,
                ExitCode = exitCode,
                Duration = duration
            };

            if (exitCode == 0)
            {
                result.After = Analyze(mountedImage, imageService);
            }
            else
            {
                _callbacks.Warning?.Invoke($"Component cleanup for {mountedImage.ImageName} exited with code {exitCode}");
            }

            return result;
        }
```

Add `using System.Management.Automation;` to the top of `ComponentStoreService.cs` if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ComponentStoreServiceTests`
Expected: PASS

- [ ] **Step 5: Add the cmdlet to ComponentStoreCmdlets.cs**

```csharp
    /// <summary>
    /// Runs component cleanup (and optionally ResetBase) against one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Optimize, "WindowsImageComponentStore", SupportsShouldProcess = true)]
    [OutputType(typeof(ComponentStoreCleanupResult[]))]
    public class OptimizeWindowsImageComponentStoreCmdlet : PSCmdlet
    {
        private const string ComponentName = "Optimize-WindowsImageComponentStore";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to clean up")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Also reset the component store base (makes prior updates non-removable)")]
        public SwitchParameter ResetBase { get; set; }

        [Parameter(HelpMessage = "Timeout in minutes for the cleanup operation")]
        [ValidateRange(1, 600)]
        public int TimeoutMinutes { get; set; } = 90;

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
                LoggingService.WriteWarning(this, "No mounted images provided for component store optimization");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var componentStoreService = new ComponentStoreService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ComponentStoreCleanupResult>();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                var action = ResetBase.IsPresent ? "Component cleanup + ResetBase" : "Component cleanup";

                if (!ShouldProcess(target, action))
                {
                    continue;
                }

                try
                {
                    results.Add(componentStoreService.Cleanup(mountedImage, imageService, ResetBase.IsPresent, this, TimeoutMinutes));
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

- [ ] **Step 6: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Optimize-WindowsImageComponentStore'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 7: Add the integration test**

Append to the `Describe "Integration: component store"` block from Task 2:

```powershell
    It "optimizes the component store and reports before/after" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = $mounted | Optimize-WindowsImageComponentStore -Confirm:$false
            $result | Should -Not -BeNullOrEmpty
            $result.Before | Should -Not -BeNullOrEmpty
            $result.ExitCode | Should -Be 0
            $result.After | Should -Not -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
```

- [ ] **Step 8: Commit**

```bash
git add src/Services/ComponentStoreService.cs src/Cmdlets/ComponentStoreCmdlets.cs tests/PSWindowsImageTools.Tests/ComponentStoreServiceTests.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Optimize-WindowsImageComponentStore cmdlet"
```

---

### Task 4: Driver models + diff logic (pure, no DISM dependency)

**Files:**
- Create: `src/Models/DriverModels.cs`
- Create: `src/Services/WindowsImageDriverService.cs`
- Create: `tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs`

**Interfaces:**
- Produces: `WindowsImageDriverInfo { PublishedName, OriginalFileName, ProviderName, ClassName, ClassDescription, Date: DateTime, Version: string, BootCritical: bool, InBox: bool, ImageName, MountPath, CatalogFile }`
- Produces: `DriverComparisonResult { ReferenceName, CurrentName, Added: List<WindowsImageDriverInfo>, Removed: List<WindowsImageDriverInfo>, Superseded: List<WindowsImageDriverInfo>, DuplicateOem: List<WindowsImageDriverInfo> }`
- Produces: `WindowsImageDriverService.Compare(List<WindowsImageDriverInfo> reference, List<WindowsImageDriverInfo> current) -> DriverComparisonResult`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class WindowsImageDriverServiceTests
    {
        private static WindowsImageDriverInfo MakeDriver(
            string published, string original, string provider, string version, bool inBox = false)
        {
            return new WindowsImageDriverInfo
            {
                PublishedName = published,
                OriginalFileName = original,
                ProviderName = provider,
                Version = version,
                InBox = inBox
            };
        }

        [Fact]
        public void Compare_DetectsAdded()
        {
            var reference = new List<WindowsImageDriverInfo>();
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Added);
            Assert.Equal("oem1.inf", result.Added[0].PublishedName);
            Assert.Empty(result.Removed);
        }

        [Fact]
        public void Compare_DetectsRemoved()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo>();

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Removed);
            Assert.Empty(result.Added);
        }

        [Fact]
        public void Compare_DetectsSuperseded_SameOriginalFileNameAndProvider_HigherVersion()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem2.inf", "net.inf", "Acme", "2.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Single(result.Superseded);
            Assert.Equal("oem2.inf", result.Superseded[0].PublishedName);
        }

        [Fact]
        public void Compare_DetectsDuplicateOem_SameOriginalFileNameAndProvider_SamePublishedNameSet_DifferentEntries()
        {
            var driverA = MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0");
            var driverB = MakeDriver("oem2.inf", "net.inf", "Acme", "1.0.0.0");
            var current = new List<WindowsImageDriverInfo> { driverA, driverB };

            var result = new WindowsImageDriverService().Compare(new List<WindowsImageDriverInfo>(), current);

            Assert.Equal(2, result.DuplicateOem.Count);
        }

        [Fact]
        public void Compare_IdenticalLists_ReportsNoDifferences()
        {
            var reference = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };
            var current = new List<WindowsImageDriverInfo> { MakeDriver("oem1.inf", "net.inf", "Acme", "1.0.0.0") };

            var result = new WindowsImageDriverService().Compare(reference, current);

            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Superseded);
            Assert.Empty(result.DuplicateOem);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter WindowsImageDriverServiceTests`
Expected: FAIL (build error — types don't exist yet)

- [ ] **Step 3: Create the models**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Dism;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A driver package present inside a mounted (offline) Windows image, distinct from
    /// INFDriverInfo which represents loose .inf files on disk before injection.
    /// </summary>
    public class WindowsImageDriverInfo
    {
        public string PublishedName { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ClassDescription { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Version { get; set; } = string.Empty;
        public bool BootCritical { get; set; }
        public bool InBox { get; set; }
        public DismDriverSignature DriverSignature { get; set; }
        public string ImageName { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public string? CatalogFile { get; set; }

        public override string ToString() => $"{PublishedName} ({OriginalFileName}) v{Version} by {ProviderName}";
    }

    /// <summary>
    /// Result of comparing driver packages between two mounted images
    /// </summary>
    public class DriverComparisonResult
    {
        public string ReferenceName { get; set; } = string.Empty;
        public string CurrentName { get; set; } = string.Empty;
        public List<WindowsImageDriverInfo> Added { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> Removed { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> Superseded { get; set; } = new List<WindowsImageDriverInfo>();
        public List<WindowsImageDriverInfo> DuplicateOem { get; set; } = new List<WindowsImageDriverInfo>();

        public override string ToString() =>
            $"'{ReferenceName}' vs '{CurrentName}': +{Added.Count} -{Removed.Count} superseded:{Superseded.Count} duplicates:{DuplicateOem.Count}";
    }
}
```

- [ ] **Step 4: Implement the service's Compare method**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Enumerates, compares, exports, and removes drivers present inside a mounted (offline)
    /// Windows image
    /// </summary>
    public class WindowsImageDriverService
    {
        private const string ServiceName = "WindowsImageDriverService";
        private readonly ModuleCallbacks _callbacks;

        public WindowsImageDriverService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Compares two driver lists. Pure — operates only on already-captured WindowsImageDriverInfo,
        /// no DISM or filesystem access.
        /// </summary>
        public DriverComparisonResult Compare(List<WindowsImageDriverInfo> reference, List<WindowsImageDriverInfo> current)
        {
            var result = new DriverComparisonResult
            {
                ReferenceName = reference.FirstOrDefault()?.ImageName ?? string.Empty,
                CurrentName = current.FirstOrDefault()?.ImageName ?? string.Empty
            };

            var referenceByPublished = reference.ToDictionary(d => d.PublishedName, StringComparer.OrdinalIgnoreCase);
            var currentByPublished = current.ToDictionary(d => d.PublishedName, StringComparer.OrdinalIgnoreCase);

            foreach (var driver in current)
            {
                if (!referenceByPublished.ContainsKey(driver.PublishedName))
                {
                    result.Added.Add(driver);

                    var sameOriginInReference = reference.Any(r =>
                        string.Equals(r.OriginalFileName, driver.OriginalFileName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.ProviderName, driver.ProviderName, StringComparison.OrdinalIgnoreCase));

                    if (sameOriginInReference && IsHigherVersion(driver, reference))
                    {
                        result.Superseded.Add(driver);
                    }
                }
            }

            foreach (var driver in reference)
            {
                if (!currentByPublished.ContainsKey(driver.PublishedName))
                {
                    result.Removed.Add(driver);
                }
            }

            var duplicateGroups = current
                .GroupBy(d => (d.OriginalFileName.ToLowerInvariant(), d.ProviderName.ToLowerInvariant()))
                .Where(g => g.Select(d => d.PublishedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

            foreach (var group in duplicateGroups)
            {
                result.DuplicateOem.AddRange(group);
            }

            return result;
        }

        private static bool IsHigherVersion(WindowsImageDriverInfo candidate, List<WindowsImageDriverInfo> reference)
        {
            if (!Version.TryParse(candidate.Version, out var candidateVersion))
            {
                return false;
            }

            return reference
                .Where(r => string.Equals(r.OriginalFileName, candidate.OriginalFileName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(r.ProviderName, candidate.ProviderName, StringComparison.OrdinalIgnoreCase))
                .Any(r => Version.TryParse(r.Version, out var referenceVersion) && candidateVersion > referenceVersion);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter WindowsImageDriverServiceTests`
Expected: PASS (all 5 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Models/DriverModels.cs src/Services/WindowsImageDriverService.cs tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs
git commit -m "feat: add driver models and pure driver-comparison logic"
```

---

### Task 5: IWindowsImageService.GetDrivers/RemoveDriver + Get-WindowsImageDriver cmdlet

**Files:**
- Modify: `src/Services/Abstractions/IWindowsImageService.cs`
- Modify: `src/Services/WindowsImageService.cs`
- Modify: `src/Services/WindowsImageDriverService.cs`
- Create: `src/Cmdlets/WindowsImageDriverCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `DismApi.OpenOfflineSession(string)`, `DismApi.GetDrivers(DismSession, bool)` (confirmed present).
- Produces: `IWindowsImageService.GetDrivers(string mountPath, bool allDrivers) -> List<DismDriverPackage>`. `WindowsImageDriverService.GetDrivers(MountedWindowsImage, IWindowsImageService, bool all) -> List<WindowsImageDriverInfo>`.

This task wraps the real `Microsoft.Dism` API (non-public constructors block unit construction of `DismDriverPackage`), so it has no new xUnit test — it is exercised by the integration test in Step 4, consistent with `Global Constraints`.

- [ ] **Step 1: Add the interface members**

Add to `src/Services/Abstractions/IWindowsImageService.cs`, after `AddDriversFromDirectory`:

```csharp
        /// <summary>
        /// Lists driver packages present in a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="allDrivers">Include inbox (Windows-provided) drivers, not just third-party</param>
        /// <returns>Driver package information</returns>
        System.Collections.Generic.List<Microsoft.Dism.DismDriverPackage> GetDrivers(string mountPath, bool allDrivers = false);

        /// <summary>
        /// Removes a driver package from a mounted image by its published name (e.g. "oem12.inf")
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="publishedName">Published name of the driver to remove</param>
        void RemoveDriver(string mountPath, string publishedName);
```

- [ ] **Step 2: Implement in WindowsImageService**

Add to `src/Services/WindowsImageService.cs`, near `GetPackages`/`GetFeatures` (same file, same class), mirroring their exact pattern:

```csharp
        /// <inheritdoc />
        public List<DismDriverPackage> GetDrivers(string mountPath, bool allDrivers = false)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Getting drivers from mounted image at {mountPath} (allDrivers: {allDrivers})");

                using var session = DismApi.OpenOfflineSession(mountPath);
                var drivers = DismApi.GetDrivers(session, allDrivers).ToList();

                _callbacks.Verbose?.Invoke($"Found {drivers.Count} drivers");
                return drivers;
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to get drivers: {ex.Message}");
                throw;
            }
        }

        /// <inheritdoc />
        public void RemoveDriver(string mountPath, string publishedName)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Removing driver {publishedName} from mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.RemoveDriver(session, publishedName);

                _callbacks.Verbose?.Invoke($"Driver {publishedName} removed successfully");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to remove driver {publishedName}: {ex.Message}");
                throw;
            }
        }
```

- [ ] **Step 3: Add the mapping method to WindowsImageDriverService**

Add to `src/Services/WindowsImageDriverService.cs`:

```csharp
        /// <summary>
        /// Enumerates drivers present in a mounted image
        /// </summary>
        public List<WindowsImageDriverInfo> GetDrivers(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool all = false)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var drivers = imageService.GetDrivers(mountPath, all);

            return drivers.Select(d => new WindowsImageDriverInfo
            {
                PublishedName = d.PublishedName ?? string.Empty,
                OriginalFileName = d.OriginalFileName ?? string.Empty,
                ProviderName = d.ProviderName ?? string.Empty,
                ClassName = d.ClassName ?? string.Empty,
                ClassDescription = d.ClassDescription ?? string.Empty,
                Date = d.Date,
                Version = d.Version?.ToString() ?? string.Empty,
                BootCritical = d.BootCritical,
                InBox = d.InBox,
                DriverSignature = d.DriverSignature,
                ImageName = mountedImage.ImageName,
                MountPath = mountPath,
                CatalogFile = d.CatalogFile
            }).ToList();
        }
```

- [ ] **Step 4: Create the cmdlet**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Lists driver packages present in one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageDriver")]
    [OutputType(typeof(WindowsImageDriverInfo[]))]
    public class GetWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageDriver";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to enumerate drivers from")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Include inbox (Windows-provided) drivers, not just third-party")]
        public SwitchParameter All { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No mounted images provided for driver enumeration");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var driverService = new WindowsImageDriverService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var drivers = driverService.GetDrivers(mountedImage, imageService, All.IsPresent);
                    WriteObject(drivers.ToArray());
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to get drivers for {mountedImage.ImageName}: {ex.Message}", ex);
                }
            }
        }
    }
}
```

- [ ] **Step 5: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Get-WindowsImageDriver'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 6: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`:

```powershell
Describe "Integration: image drivers" -Tag Integration {

    It "lists drivers for a mounted image without error" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            { $mounted | Get-WindowsImageDriver } | Should -Not -Throw
            $allDrivers = $mounted | Get-WindowsImageDriver -All
            $allDrivers.Count | Should -BeGreaterThan 0
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add src/Services/Abstractions/IWindowsImageService.cs src/Services/WindowsImageService.cs src/Services/WindowsImageDriverService.cs src/Cmdlets/WindowsImageDriverCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsImageDriver cmdlet"
```

---

### Task 6: Remove-WindowsImageDriver cmdlet

**Files:**
- Modify: `src/Cmdlets/WindowsImageDriverCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `WindowsImageDriverInfo` (Task 4), `IWindowsImageService.RemoveDriver(string mountPath, string publishedName)` (Task 5).

- [ ] **Step 1: Add the cmdlet to WindowsImageDriverCmdlets.cs**

```csharp
    /// <summary>
    /// Removes a driver package from a mounted Windows image
    /// </summary>
    [Cmdlet(VerbsCommon.Remove, "WindowsImageDriver", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class RemoveWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Remove-WindowsImageDriver";
        private readonly List<WindowsImageDriverInfo> _allDrivers = new List<WindowsImageDriverInfo>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Driver(s) to remove, from Get-WindowsImageDriver")]
        [ValidateNotNull]
        public WindowsImageDriverInfo[] Driver { get; set; } = Array.Empty<WindowsImageDriverInfo>();

        [Parameter(HelpMessage = "Continue processing other drivers if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allDrivers.AddRange(Driver);
        }

        protected override void EndProcessing()
        {
            if (_allDrivers.Count == 0)
            {
                LoggingService.WriteWarning(this, "No drivers provided for removal");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);

            foreach (var driver in _allDrivers)
            {
                if (string.IsNullOrEmpty(driver.MountPath))
                {
                    LoggingService.WriteWarning(this, $"Driver {driver.PublishedName} has no mount path; skipping");
                    continue;
                }

                if (!ShouldProcess($"{driver.PublishedName} ({driver.OriginalFileName}) on {driver.MountPath}", "Remove driver"))
                {
                    continue;
                }

                try
                {
                    imageService.RemoveDriver(driver.MountPath, driver.PublishedName);
                    LoggingService.WriteVerbose(this, $"Removed driver {driver.PublishedName} from {driver.MountPath}");
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to remove driver {driver.PublishedName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
```

- [ ] **Step 2: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Remove-WindowsImageDriver'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 3: Add the integration test**

Append to the `Describe "Integration: image drivers"` block from Task 5:

```powershell
    It "removes a third-party driver from a mounted image" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $before = $mounted | Get-WindowsImageDriver
            if ($before.Count -gt 0) {
                $target = $before | Select-Object -First 1
                $target | Remove-WindowsImageDriver -Confirm:$false
                $after = $mounted | Get-WindowsImageDriver
                $after.PublishedName | Should -Not -Contain $target.PublishedName
            }
            else {
                Set-ItResult -Skipped -Because "synthetic baseline image has no third-party drivers to remove"
            }
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
```

- [ ] **Step 4: Commit**

```bash
git add src/Cmdlets/WindowsImageDriverCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Remove-WindowsImageDriver cmdlet"
```

---

### Task 7: Compare-WindowsImageDriver cmdlet

**Files:**
- Modify: `src/Cmdlets/WindowsImageDriverCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `WindowsImageDriverService.GetDrivers` (Task 5), `WindowsImageDriverService.Compare` (Task 4), `DriverComparisonResult` (Task 4).

- [ ] **Step 1: Add the cmdlet to WindowsImageDriverCmdlets.cs**

```csharp
    /// <summary>
    /// Compares driver packages between two mounted Windows images
    /// </summary>
    [Cmdlet(VerbsData.Compare, "WindowsImageDriver")]
    [OutputType(typeof(DriverComparisonResult))]
    public class CompareWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Compare-WindowsImageDriver";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Two mounted images: first is the reference, second is current")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Include inbox (Windows-provided) drivers, not just third-party")]
        public SwitchParameter All { get; set; }

        protected override void ProcessRecord()
        {
            _allMountedImages.AddRange(MountedImages);
        }

        protected override void EndProcessing()
        {
            if (_allMountedImages.Count != 2)
            {
                ThrowTerminatingError(new ErrorRecord(
                    new InvalidOperationException($"Compare-WindowsImageDriver requires exactly two mounted images, got {_allMountedImages.Count}"),
                    "InvalidImageCount",
                    ErrorCategory.InvalidArgument,
                    _allMountedImages.Count));
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var driverService = new WindowsImageDriverService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                var reference = driverService.GetDrivers(_allMountedImages[0], imageService, All.IsPresent);
                var current = driverService.GetDrivers(_allMountedImages[1], imageService, All.IsPresent);

                var result = driverService.Compare(reference, current);
                result.ReferenceName = _allMountedImages[0].ImageName;
                result.CurrentName = _allMountedImages[1].ImageName;

                WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "CompareWindowsImageDriverFailed", ErrorCategory.OperationStopped, ComponentName));
            }
        }
    }
```

- [ ] **Step 2: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Compare-WindowsImageDriver'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 3: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`:

```powershell
Describe "Integration: driver comparison" -Tag Integration {

    It "reports no differences between a mounted image and itself" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = Compare-WindowsImageDriver -MountedImages @($mounted, $mounted)
            $result.Added | Should -BeNullOrEmpty
            $result.Removed | Should -BeNullOrEmpty
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Cmdlets/WindowsImageDriverCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Compare-WindowsImageDriver cmdlet"
```

---

### Task 8: Export-WindowsImageDriver cmdlet

**Files:**
- Modify: `src/Services/WindowsImageDriverService.cs`
- Modify: `src/Cmdlets/WindowsImageDriverCmdlets.cs`
- Create: `tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs` (append)
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Produces: `WindowsImageDriverService.ResolveDriverSourceDirectory(string mountPath, string? catalogFilePath) -> string` (`internal static`, pure). `WindowsImageDriverService.Export(WindowsImageDriverInfo driver, DirectoryInfo destination)`.

`CatalogFile` as returned by DISM may be an absolute path or a path relative to the image root, depending on DISM version — `ResolveDriverSourceDirectory` handles both so the pure logic is verifiable without a real mounted image.

- [ ] **Step 1: Write the failing test for path resolution**

Append to `tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs`:

```csharp
        [Theory]
        [InlineData(@"C:\Mount", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme\net.cat", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme")]
        [InlineData(@"C:\Mount", @"Windows\System32\DriverStore\FileRepository\net_acme\net.cat", @"C:\Mount\Windows\System32\DriverStore\FileRepository\net_acme")]
        public void ResolveDriverSourceDirectory_HandlesAbsoluteAndRelativeCatalogPaths(string mountPath, string catalogFile, string expected)
        {
            var resolved = WindowsImageDriverService.ResolveDriverSourceDirectory(mountPath, catalogFile);
            Assert.Equal(expected, resolved, ignoreCase: true);
        }

        [Fact]
        public void ResolveDriverSourceDirectory_NullCatalogFile_ReturnsNull()
        {
            Assert.Null(WindowsImageDriverService.ResolveDriverSourceDirectory(@"C:\Mount", null));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter WindowsImageDriverServiceTests`
Expected: FAIL (`ResolveDriverSourceDirectory` not defined)

- [ ] **Step 3: Implement ResolveDriverSourceDirectory and Export**

Add to `src/Services/WindowsImageDriverService.cs`:

```csharp
        /// <summary>
        /// Resolves the on-disk directory containing a driver's files from its DISM-reported
        /// catalog path, handling both absolute paths and paths relative to the image root. Pure.
        /// </summary>
        internal static string? ResolveDriverSourceDirectory(string mountPath, string? catalogFilePath)
        {
            if (string.IsNullOrEmpty(catalogFilePath))
            {
                return null;
            }

            var fullCatalogPath = Path.IsPathRooted(catalogFilePath)
                ? catalogFilePath
                : Path.Combine(mountPath, catalogFilePath.TrimStart('\\', '/'));

            return Path.GetDirectoryName(fullCatalogPath);
        }

        /// <summary>
        /// Copies a driver's on-disk file repository folder to a destination directory
        /// </summary>
        public void Export(WindowsImageDriverInfo driver, DirectoryInfo destination)
        {
            var sourceDirectory = ResolveDriverSourceDirectory(driver.MountPath, driver.CatalogFile);

            if (sourceDirectory == null || !Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Could not resolve on-disk source directory for driver {driver.PublishedName} (catalog: {driver.CatalogFile ?? "none"})");
            }

            var driverDestination = Path.Combine(destination.FullName, Path.GetFileName(sourceDirectory));
            Directory.CreateDirectory(driverDestination);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = file.Substring(sourceDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
                var targetPath = Path.Combine(driverDestination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
            }

            _callbacks.Verbose?.Invoke($"Exported driver {driver.PublishedName} to {driverDestination}");
        }
```

Add `using System.IO;` to the top of `WindowsImageDriverService.cs` if not already present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter WindowsImageDriverServiceTests`
Expected: PASS

- [ ] **Step 5: Write the failing test for Export's file copy**

Append to `tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs`:

```csharp
        [Fact]
        public void Export_CopiesDriverFilesToDestination()
        {
            var mountPath = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + Guid.NewGuid().ToString("N"));
            var driverFolder = Path.Combine(mountPath, "Windows", "System32", "DriverStore", "FileRepository", "net_acme");
            var destination = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-Dest-" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(driverFolder);
            File.WriteAllText(Path.Combine(driverFolder, "net.inf"), "; fake inf");
            File.WriteAllText(Path.Combine(driverFolder, "net.cat"), "fake catalog");

            try
            {
                var driver = new WindowsImageDriverInfo
                {
                    PublishedName = "oem1.inf",
                    MountPath = mountPath,
                    CatalogFile = Path.Combine(driverFolder, "net.cat")
                };

                new WindowsImageDriverService().Export(driver, new DirectoryInfo(destination));

                var copiedInf = Path.Combine(destination, "net_acme", "net.inf");
                Assert.True(File.Exists(copiedInf));
            }
            finally
            {
                if (Directory.Exists(mountPath)) Directory.Delete(mountPath, true);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
            }
        }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter WindowsImageDriverServiceTests`
Expected: PASS (all tests including the two new ones)

- [ ] **Step 7: Add the cmdlet**

Add to `src/Cmdlets/WindowsImageDriverCmdlets.cs`:

```csharp
    /// <summary>
    /// Exports driver package files from a mounted Windows image to a destination directory
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImageDriver")]
    [OutputType(typeof(void))]
    public class ExportWindowsImageDriverCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImageDriver";
        private readonly List<WindowsImageDriverInfo> _allDrivers = new List<WindowsImageDriverInfo>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Driver(s) to export, from Get-WindowsImageDriver")]
        [ValidateNotNull]
        public WindowsImageDriverInfo[] Driver { get; set; } = Array.Empty<WindowsImageDriverInfo>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination directory for exported driver files")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        [Parameter(HelpMessage = "Continue processing other drivers if one fails")]
        public SwitchParameter ContinueOnError { get; set; }

        protected override void ProcessRecord()
        {
            _allDrivers.AddRange(Driver);
        }

        protected override void EndProcessing()
        {
            if (_allDrivers.Count == 0)
            {
                LoggingService.WriteWarning(this, "No drivers provided for export");
                return;
            }

            if (!DestinationPath.Exists)
            {
                DestinationPath.Create();
            }

            var driverService = new WindowsImageDriverService(ModuleCallbacks.FromCmdlet(this));

            foreach (var driver in _allDrivers)
            {
                try
                {
                    driverService.Export(driver, DestinationPath);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to export driver {driver.PublishedName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
```

- [ ] **Step 8: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Export-WindowsImageDriver'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 9: Add the integration test**

Append to the `Describe "Integration: image drivers"` block:

```powershell
    It "exports a driver's files to a destination directory" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite
        $exportDest = Join-Path $Workspace "driver-export"

        try {
            $drivers = $mounted | Get-WindowsImageDriver
            if ($drivers.Count -gt 0) {
                $drivers | Select-Object -First 1 | Export-WindowsImageDriver -DestinationPath $exportDest
                (Get-ChildItem $exportDest -Recurse -File).Count | Should -BeGreaterThan 0
            }
            else {
                Set-ItResult -Skipped -Because "synthetic baseline image has no third-party drivers to export"
            }
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
```

- [ ] **Step 10: Commit**

```bash
git add src/Services/WindowsImageDriverService.cs src/Cmdlets/WindowsImageDriverCmdlets.cs tests/PSWindowsImageTools.Tests/WindowsImageDriverServiceTests.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Export-WindowsImageDriver cmdlet"
```

---

### Task 9: Extend ImageSnapshot with driver inventory

**Files:**
- Modify: `src/Models/ImageComparisonModels.cs`
- Modify: `src/Services/ImageComparisonService.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ImageComparisonServiceTests.cs`

**Interfaces:**
- Consumes: `WindowsImageDriverService.GetDrivers` (Task 5).
- Modifies: `ImageSnapshot` gains `Drivers: List<SnapshotItem>`.

- [ ] **Step 1: Write the failing test**

Add to `tests/PSWindowsImageTools.Tests/ImageComparisonServiceTests.cs`, inside the `MakeSnapshot` helper's initializer, add a `Drivers` list matching the existing style:

```csharp
                Drivers = new List<SnapshotItem>
                {
                    new SnapshotItem { Name = "net.inf", State = "Acme", Detail = "1.0.0.0" }
                },
```

Add a new test:

```csharp
        [Fact]
        public void Compare_IncludesDriversCategory()
        {
            var reference = MakeSnapshot("A");
            var difference = MakeSnapshot("B", s => s.Drivers.Add(new SnapshotItem { Name = "gpu.inf", State = "Vendor", Detail = "2.0.0.0" }));

            var result = new ImageComparisonService().Compare(reference, difference);

            var driversDiff = result.Categories.Single(c => c.Category == "Drivers");
            Assert.Single(driversDiff.Added);
            Assert.Equal("gpu.inf", driversDiff.Added[0].Name);
        }
```

Add `using System.Linq;` to the test file's usings if not already present (needed for `.Single(...)`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageComparisonServiceTests`
Expected: FAIL (`Drivers` property doesn't exist on `ImageSnapshot`; `TotalItems` mismatch if referenced elsewhere)

- [ ] **Step 3: Add the Drivers property to ImageSnapshot**

In `src/Models/ImageComparisonModels.cs`, add to the `ImageSnapshot` class, alongside the existing `Packages`/`Features`/etc. properties:

```csharp
        /// <summary>
        /// Driver packages present in the image
        /// </summary>
        public List<SnapshotItem> Drivers { get; set; } = new List<SnapshotItem>();
```

Update `TotalItems`:

```csharp
        public int TotalItems => Packages.Count + Features.Count + Capabilities.Count + AppxPackages.Count + Software.Count + Drivers.Count;
```

- [ ] **Step 4: Wire driver capture into CaptureSnapshot and Compare**

In `src/Services/ImageComparisonService.cs`, add to `CaptureSnapshot` (after the AppX-packages `try`/`catch` block, before installed-software capture). `CaptureSnapshot`'s existing signature already receives the caller's `MountedWindowsImage mountedImage` and `IWindowsImageService imageService` parameters — reuse them directly:

```csharp
            try
            {
                var driverService = new WindowsImageDriverService(_callbacks);
                foreach (var driver in driverService.GetDrivers(mountedImage, imageService))
                {
                    snapshot.Drivers.Add(new SnapshotItem
                    {
                        Name = driver.OriginalFileName,
                        State = driver.ProviderName,
                        Detail = driver.Version
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to capture drivers: {ex.Message}");
            }
```

Add to `Compare`:

```csharp
            result.Categories.Add(CompareCategory("Drivers", reference.Drivers, difference.Drivers));
```

Add `using PSWindowsImageTools.Services;`... actually this file is already in `namespace PSWindowsImageTools.Services`, so `WindowsImageDriverService` is directly accessible — no new using needed.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ImageComparisonServiceTests`
Expected: PASS (all tests, including the pre-existing ones — confirms the extension didn't break existing behavior)

- [ ] **Step 6: Commit**

```bash
git add src/Models/ImageComparisonModels.cs src/Services/ImageComparisonService.cs tests/PSWindowsImageTools.Tests/ImageComparisonServiceTests.cs
git commit -m "feat: capture driver inventory in ImageSnapshot"
```

---

### Task 10: SbomReport model + Export-WindowsImageSBOM cmdlet

**Files:**
- Create: `src/Models/SbomModels.cs`
- Modify: `src/Services/ImageComparisonService.cs`
- Modify: `src/Cmdlets/ImageComparisonCmdlets.cs`
- Create: `tests/PSWindowsImageTools.Tests/SbomReportTests.cs`

**Interfaces:**
- Consumes: `ImageSnapshot` (Task 9), `ImageComparisonService.LoadSnapshot` (existing).
- Produces: `SbomReport { WindowsVersion, ImageName, ImagePath, GeneratedAt, Packages, Drivers, Features, Capabilities, Applications: List<SnapshotItem> }`. `ImageComparisonService.BuildSbom(ImageSnapshot) -> SbomReport` (pure — maps one POCO to another).

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class SbomReportTests
    {
        [Fact]
        public void BuildSbom_MapsSnapshotFieldsToSbomReport()
        {
            var snapshot = new ImageSnapshot
            {
                ImageName = "Windows 11 Pro",
                ImagePath = @"C:\images\install.wim",
                Packages = new List<SnapshotItem> { new SnapshotItem { Name = "Package-A" } },
                Drivers = new List<SnapshotItem> { new SnapshotItem { Name = "net.inf" } },
                Features = new List<SnapshotItem> { new SnapshotItem { Name = "Feature-1" } },
                Capabilities = new List<SnapshotItem> { new SnapshotItem { Name = "Cap.X" } },
                Software = new List<SnapshotItem> { new SnapshotItem { Name = "Tool" } }
            };

            var sbom = new ImageComparisonService().BuildSbom(snapshot);

            Assert.Equal("Windows 11 Pro", sbom.ImageName);
            Assert.Equal(@"C:\images\install.wim", sbom.ImagePath);
            Assert.Single(sbom.Packages);
            Assert.Single(sbom.Drivers);
            Assert.Single(sbom.Features);
            Assert.Single(sbom.Capabilities);
            Assert.Single(sbom.Applications);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter SbomReportTests`
Expected: FAIL (`SbomReport`/`BuildSbom` don't exist yet)

- [ ] **Step 3: Create the model**

```csharp
using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// Software Bill of Materials for a Windows image, built from a captured ImageSnapshot
    /// </summary>
    public class SbomReport
    {
        public string WindowsVersion { get; set; } = string.Empty;
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<SnapshotItem> Packages { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Drivers { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Features { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Capabilities { get; set; } = new List<SnapshotItem>();
        public List<SnapshotItem> Applications { get; set; } = new List<SnapshotItem>();

        public override string ToString() =>
            $"{ImageName}: {Packages.Count} packages, {Drivers.Count} drivers, {Applications.Count} applications";
    }
}
```

- [ ] **Step 4: Implement BuildSbom**

Add to `src/Services/ImageComparisonService.cs`:

```csharp
        /// <summary>
        /// Builds an SBOM report from a captured snapshot. Pure — no I/O.
        /// </summary>
        public SbomReport BuildSbom(ImageSnapshot snapshot)
        {
            return new SbomReport
            {
                ImageName = snapshot.ImageName,
                ImagePath = snapshot.ImagePath,
                Packages = snapshot.Packages,
                Drivers = snapshot.Drivers,
                Features = snapshot.Features,
                Capabilities = snapshot.Capabilities,
                Applications = snapshot.Software
            };
        }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter SbomReportTests`
Expected: PASS

- [ ] **Step 6: Add the cmdlet to ImageComparisonCmdlets.cs**

```csharp
    /// <summary>
    /// Builds a Software Bill of Materials (SBOM) from a captured Windows image snapshot
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImageSBOM")]
    [OutputType(typeof(SbomReport[]))]
    public class ExportWindowsImageSBOMCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImageSBOM";
        private readonly List<ImageSnapshot> _allSnapshots = new List<ImageSnapshot>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ParameterSetName = "BySnapshot", HelpMessage = "Snapshot(s) from Get-WindowsImageSnapshot")]
        [ValidateNotNull]
        public ImageSnapshot[] Snapshot { get; set; } = Array.Empty<ImageSnapshot>();

        [Parameter(Mandatory = true, ParameterSetName = "BySnapshotFile", HelpMessage = "Path to a saved snapshot JSON file")]
        [ValidateNotNullOrEmpty]
        public string SnapshotPath { get; set; } = null!;

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination directory for the SBOM JSON file(s)")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        protected override void ProcessRecord()
        {
            if (ParameterSetName == "BySnapshot")
            {
                _allSnapshots.AddRange(Snapshot);
            }
        }

        protected override void EndProcessing()
        {
            if (ParameterSetName == "BySnapshotFile")
            {
                var resolvedPath = GetUnresolvedProviderPathFromPSPath(SnapshotPath) ?? SnapshotPath;
                _allSnapshots.Add(ImageComparisonService.LoadSnapshot(resolvedPath));
            }

            if (_allSnapshots.Count == 0)
            {
                LoggingService.WriteWarning(this, "No snapshots provided for SBOM export");
                return;
            }

            if (!DestinationPath.Exists)
            {
                DestinationPath.Create();
            }

            var comparisonService = new ImageComparisonService(ModuleCallbacks.FromCmdlet(this));
            var reports = new List<SbomReport>();

            foreach (var snapshot in _allSnapshots)
            {
                var sbom = comparisonService.BuildSbom(snapshot);
                var fileName = $"sbom_{SanitizeFileName(snapshot.ImageName)}_{sbom.GeneratedAt:yyyyMMdd_HHmmss}.json";
                var filePath = Path.Combine(DestinationPath.FullName, fileName);
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(sbom, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(filePath, json);

                LoggingService.WriteVerbose(this, ComponentName, $"SBOM exported: {filePath}");
                reports.Add(sbom);
            }

            WriteObject(reports.ToArray());
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }
```

- [ ] **Step 7: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Export-WindowsImageSBOM'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 8: Commit**

```bash
git add src/Models/SbomModels.cs src/Services/ImageComparisonService.cs src/Cmdlets/ImageComparisonCmdlets.cs tests/PSWindowsImageTools.Tests/SbomReportTests.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Export-WindowsImageSBOM cmdlet"
```

---

### Task 11: HealthCheckReport model + status roll-up logic

**Files:**
- Create: `src/Models/HealthCheckModels.cs`
- Create: `tests/PSWindowsImageTools.Tests/HealthCheckModelsTests.cs`

**Interfaces:**
- Produces: `enum HealthStatus { Healthy, Warning, Unhealthy }`. `HealthFinding { Category: string, Severity: HealthStatus, Message: string }`. `HealthCheckReport { ImageName, ImagePath, MountPath, GeneratedAt, Findings: List<HealthFinding>, OverallHealth: HealthStatus (computed) }`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using PSWindowsImageTools.Models;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class HealthCheckModelsTests
    {
        [Fact]
        public void OverallHealth_NoFindings_IsHealthy()
        {
            var report = new HealthCheckReport();
            Assert.Equal(HealthStatus.Healthy, report.OverallHealth);
        }

        [Fact]
        public void OverallHealth_OnlyWarningFindings_IsWarning()
        {
            var report = new HealthCheckReport
            {
                Findings = new List<HealthFinding>
                {
                    new HealthFinding { Category = "MissingRegistryHive", Severity = HealthStatus.Warning, Message = "SYSTEM hive missing" }
                }
            };

            Assert.Equal(HealthStatus.Warning, report.OverallHealth);
        }

        [Fact]
        public void OverallHealth_AnyCorruptionFinding_IsUnhealthy()
        {
            var report = new HealthCheckReport
            {
                Findings = new List<HealthFinding>
                {
                    new HealthFinding { Category = "MissingRegistryHive", Severity = HealthStatus.Warning, Message = "SYSTEM hive missing" },
                    new HealthFinding { Category = "Corruption", Severity = HealthStatus.Unhealthy, Message = "Component store repairable" }
                }
            };

            Assert.Equal(HealthStatus.Unhealthy, report.OverallHealth);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter HealthCheckModelsTests`
Expected: FAIL (types don't exist yet)

- [ ] **Step 3: Create the model with computed OverallHealth**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace PSWindowsImageTools.Models
{
    public enum HealthStatus
    {
        Healthy,
        Warning,
        Unhealthy
    }

    /// <summary>
    /// A single health finding for an offline Windows image
    /// </summary>
    public class HealthFinding
    {
        /// <summary>
        /// One of: Corruption, MissingRegistryHive, OrphanedOrSupersededPackage, DriverIssue, PendingOperation
        /// </summary>
        public string Category { get; set; } = string.Empty;
        public HealthStatus Severity { get; set; }
        public string Message { get; set; } = string.Empty;

        public override string ToString() => $"[{Severity}] {Category}: {Message}";
    }

    /// <summary>
    /// Composite health assessment of an offline Windows image
    /// </summary>
    public class HealthCheckReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<HealthFinding> Findings { get; set; } = new List<HealthFinding>();

        /// <summary>
        /// Unhealthy if any Corruption finding exists; Warning if any other finding exists; else Healthy
        /// </summary>
        public HealthStatus OverallHealth =>
            Findings.Any(f => f.Category == "Corruption")
                ? HealthStatus.Unhealthy
                : Findings.Count > 0
                    ? HealthStatus.Warning
                    : HealthStatus.Healthy;

        public override string ToString() => $"{ImageName}: {OverallHealth} ({Findings.Count} findings)";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter HealthCheckModelsTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Models/HealthCheckModels.cs tests/PSWindowsImageTools.Tests/HealthCheckModelsTests.cs
git commit -m "feat: add HealthCheckReport model with overall-health roll-up"
```

---

### Task 12: WindowsImageHealthCheckService + Invoke-WindowsImageHealthCheck cmdlet

**Files:**
- Create: `src/Services/WindowsImageHealthCheckService.cs`
- Create: `src/Cmdlets/InvokeWindowsImageHealthCheckCmdlet.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`

**Interfaces:**
- Consumes: `ComponentStoreService.Analyze` (Task 2), `WindowsImageDriverService.GetDrivers` (Task 5), `RegistryHiveReader.GetSoftwareHivePath`/hive-presence (existing pattern from `ImageComparisonService`), `DismApi.CheckImageHealth`/`RestoreImageHealth` (confirmed present), `HealthCheckReport`/`HealthFinding`/`HealthStatus` (Task 11).
- Produces: `WindowsImageHealthCheckService.Run(MountedWindowsImage, IWindowsImageService, bool restoreHealth, PSCmdlet) -> HealthCheckReport`.

This task's DISM-facing composition (`DismApi.CheckImageHealth`) has no unit test — it is exercised by the integration test in Step 3, consistent with `Global Constraints`. The findings composed from `ComponentStoreService`/`WindowsImageDriverService` are already covered by those services' own unit tests (Tasks 1 and 4).

- [ ] **Step 1: Implement the service**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Composite health assessment of a mounted Windows image: corruption, missing registry
    /// hives, orphaned/superseded packages, and driver issues
    /// </summary>
    public class WindowsImageHealthCheckService
    {
        private const string ServiceName = "WindowsImageHealthCheckService";
        private readonly ModuleCallbacks _callbacks;

        public WindowsImageHealthCheckService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        public HealthCheckReport Run(MountedWindowsImage mountedImage, IWindowsImageService imageService, bool restoreHealth, PSCmdlet cmdlet)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var report = new HealthCheckReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            CheckCorruption(mountPath, restoreHealth, report);
            CheckRegistryHives(mountPath, report);
            CheckComponentStore(mountedImage, imageService, report);
            CheckDrivers(mountedImage, imageService, report);

            return report;
        }

        private void CheckCorruption(string mountPath, bool restoreHealth, HealthCheckReport report)
        {
            try
            {
                using var session = DismApi.OpenOfflineSession(mountPath);
                var healthState = DismApi.CheckImageHealth(session, scanImage: true);

                if (healthState != DismImageHealthState.Healthy)
                {
                    if (restoreHealth)
                    {
                        DismApi.RestoreImageHealth(session, limitAccess: false);
                        report.Findings.Add(new HealthFinding
                        {
                            Category = "Corruption",
                            Severity = HealthStatus.Warning,
                            Message = $"Component store was {healthState}; repair attempted"
                        });
                    }
                    else
                    {
                        report.Findings.Add(new HealthFinding
                        {
                            Category = "Corruption",
                            Severity = HealthStatus.Unhealthy,
                            Message = $"Component store is {healthState}; run with -RestoreHealth to repair"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check image health: {ex.Message}");
                report.Findings.Add(new HealthFinding { Category = "Corruption", Severity = HealthStatus.Warning, Message = $"Health check failed: {ex.Message}" });
            }
        }

        private void CheckRegistryHives(string mountPath, HealthCheckReport report)
        {
            var configDir = Path.Combine(mountPath, "Windows", "System32", "config");

            foreach (var hive in new[] { "SOFTWARE", "SYSTEM" })
            {
                var hivePath = Path.Combine(configDir, hive);
                if (!File.Exists(hivePath))
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "MissingRegistryHive",
                        Severity = HealthStatus.Warning,
                        Message = $"{hive} hive not found at {hivePath}"
                    });
                }
            }
        }

        private void CheckComponentStore(MountedWindowsImage mountedImage, IWindowsImageService imageService, HealthCheckReport report)
        {
            try
            {
                var componentStoreReport = new ComponentStoreService(_callbacks).Analyze(mountedImage, imageService);

                if (componentStoreReport.SupersededPackages > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "OrphanedOrSupersededPackage",
                        Severity = HealthStatus.Warning,
                        Message = $"{componentStoreReport.SupersededPackages} superseded package(s) present; consider Optimize-WindowsImageComponentStore"
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check component store: {ex.Message}");
            }
        }

        private void CheckDrivers(MountedWindowsImage mountedImage, IWindowsImageService imageService, HealthCheckReport report)
        {
            try
            {
                var drivers = new WindowsImageDriverService(_callbacks).GetDrivers(mountedImage, imageService);

                var unsignedCount = drivers.Count(d => d.DriverSignature == DismDriverSignature.Unsigned);
                if (unsignedCount > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "DriverIssue",
                        Severity = HealthStatus.Warning,
                        Message = $"{unsignedCount} unsigned driver(s) detected"
                    });
                }

                var duplicateCount = drivers
                    .GroupBy(d => (d.OriginalFileName.ToLowerInvariant(), d.ProviderName.ToLowerInvariant()))
                    .Count(g => g.Select(d => d.PublishedName).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

                if (duplicateCount > 0)
                {
                    report.Findings.Add(new HealthFinding
                    {
                        Category = "DriverIssue",
                        Severity = HealthStatus.Warning,
                        Message = $"{duplicateCount} duplicate OEM driver group(s) detected"
                    });
                }
            }
            catch (Exception ex)
            {
                _callbacks.Warning?.Invoke($"Failed to check drivers: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 2: Create the cmdlet**

```csharp
using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Runs a composite health check against one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsLifecycle.Invoke, "WindowsImageHealthCheck")]
    [OutputType(typeof(HealthCheckReport[]))]
    public class InvokeWindowsImageHealthCheckCmdlet : PSCmdlet
    {
        private const string ComponentName = "Invoke-WindowsImageHealthCheck";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to check")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Attempt to repair detected corruption via DISM RestoreHealth")]
        public SwitchParameter RestoreHealth { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided for health check");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var healthCheckService = new WindowsImageHealthCheckService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<HealthCheckReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    results.Add(healthCheckService.Run(mountedImage, imageService, RestoreHealth.IsPresent, this));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to health-check {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }

            WriteObject(results.ToArray());
        }
    }
}
```

- [ ] **Step 3: Build, register, and add the integration test**

Run: `dotnet build PSWindowsImageTools.sln` — expect success.
Add `'Invoke-WindowsImageHealthCheck'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`:

```powershell
Describe "Integration: health check" -Tag Integration {

    It "produces a health report with a computed OverallHealth" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Invoke-WindowsImageHealthCheck
            $report | Should -Not -BeNullOrEmpty
            $report.OverallHealth | Should -BeIn @("Healthy", "Warning", "Unhealthy")
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Services/WindowsImageHealthCheckService.cs src/Cmdlets/InvokeWindowsImageHealthCheckCmdlet.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Invoke-WindowsImageHealthCheck cmdlet"
```

---

### Task 13: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests`
Expected: PASS — all pre-existing tests plus the ~15 new ones added across Tasks 1–11.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build PSWindowsImageTools.sln`
Expected: PASS, no warnings-as-errors triggered.

- [ ] **Step 3: Verify the module manifest lists all 9 new cmdlets and PowerShell can discover them**

Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageComponentStore, Optimize-WindowsImageComponentStore, Get-WindowsImageDriver, Remove-WindowsImageDriver, Compare-WindowsImageDriver, Export-WindowsImageDriver, Export-WindowsImageSBOM, Invoke-WindowsImageHealthCheck"`
Expected: all 8 cmdlets found (the 9th change, `Drivers` on `ImageSnapshot`, is a model extension with no new cmdlet).

- [ ] **Step 4: Run the integration suite (requires an elevated Windows session)**

Run: `pwsh tests/integration/run-integration.ps1`
Expected: PASS — all `-Tag Integration` describe blocks added in Tasks 2, 3, 5, 6, 7, 8, 12.

- [ ] **Step 5: Commit any final cleanup**

```bash
git status
```

If the working tree is clean, no commit is needed — this task is verification-only.
