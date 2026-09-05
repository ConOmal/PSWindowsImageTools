# App Provisioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageProvisionedApp`, `Add-WindowsImageProvisionedApp`, and `Export-WindowsImageWinGetConfiguration` — completing the offline-image-compatible AppX provisioning set (alongside the existing `Remove-AppXProvisionedPackageList`) and adding a WinGet Configuration/DSC artifact generator for first-boot application.

**Architecture:** One new subsystem (`src/Models/AppProvisioningModels.cs`, `src/Services/AppProvisioningService.cs`, `src/Cmdlets/AppProvisioningCmdlets.cs`). Reuses `IWindowsImageService.GetProvisionedAppxPackages` (existing) and adds one new interface member, `AddProvisionedAppxPackage`, wrapping the confirmed-real `DismApi.AddProvisionedAppxPackage`. The WinGet Configuration exporter is pure file templating with no DISM/image dependency at all.

**Tech Stack:** C# / .NET (netstandard2.0), `Microsoft.Dism`, xUnit, Pester.

**Spec:** `docs/superpowers/specs/2026-09-04-app-provisioning-design.md`

## Global Constraints

- Cmdlet naming: `Verb-WindowsImage<Noun>`. `Get`/`Add` = `VerbsCommon.Get`/`VerbsCommon.Add`; `Export` = `VerbsData.Export`.
- **Confirmed real DISM API this session via reflection against the module's bundled `Microsoft.Dism.dll`** (do not re-derive): `DismApi.AddProvisionedAppxPackage(DismSession session, string appPath, List<string> dependencyPackages, string licensePath, string customDataPath)` — the 5-argument overload this plan uses; two other overloads exist with additional parameters (`List<string> optionalPackages` and a `DismStubPackageOption`) that this plan does not use — do not accidentally call one of those instead.
- **Confirmed existing methods this plan reuses**: `IWindowsImageService.GetProvisionedAppxPackages(string mountPath) -> List<DismAppxPackage>`. `DismAppxPackage` (confirmed via `ImageComparisonService.CaptureSnapshot`'s existing AppX capture code) exposes `.PackageName`, `.DisplayName`.
- `Microsoft.Dism.DismAppxPackage` has a non-public constructor (same constraint as every other DISM type used in this codebase, confirmed repeatedly across prior Phase 1/2 plans) — no test constructs it directly; `GetProvisionedApps`'s DISM-facing mapping has no unit test.
- `Add-WindowsImageProvisionedApp` requires `SupportsShouldProcess = true` + per-image `ShouldProcess` (mutating).
- `Export-WindowsImageWinGetConfiguration` does NOT touch DISM/the mount at all — pure file generation from a caller-supplied package list, no `SupportsShouldProcess` needed (not a destructive operation on the image).
- Multi-image cmdlets accept `-ContinueOnError`, matching established convention.
- WinGet Configuration YAML must use the real, documented schema:
  `# yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2`
  header, top-level `properties.resources[]`, each entry
  `resource: Microsoft.WinGet.DSC/WinGetPackage`, `directives: {description: <string>, allowPrerelease: true}`,
  `settings: {id: <PackageIdentifier>, source: <Source>}`.
- This repo commits its compiled binary module DLL alongside source changes.
- **Working-tree note**: shared checkout with other concurrent automations. Only `git add` files this plan's tasks explicitly name.
- Do NOT create or modify any file under `*OOBE*`/`*FirstLogon*` naming — that surface belongs to a different concurrently active session's work per this plan's spec Non-goals.

---

### Task 1: ProvisionedAppInfo model + GetProvisionedApps + Get-WindowsImageProvisionedApp cmdlet

**Files:**
- Create: `src/Models/AppProvisioningModels.cs`
- Create: `src/Services/AppProvisioningService.cs`
- Create: `src/Cmdlets/AppProvisioningCmdlets.cs`

**Interfaces:**
- Produces: `ProvisionedAppInfo { PackageName: string, DisplayName: string, Publisher: string, Version: string, InstallLocation: string }`
- Produces: `AppProvisioningService.GetProvisionedApps(MountedWindowsImage, IWindowsImageService) -> List<ProvisionedAppInfo>`

This task's mapping method is DISM-facing (wraps `GetProvisionedAppxPackages`) — no unit test, matching established convention for this class of method throughout the codebase.

- [x] **Step 1: Create the model**

```csharp
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// A provisioned AppX package in a mounted Windows image
    /// </summary>
    public class ProvisionedAppInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;

        public override string ToString() => $"{DisplayName} ({PackageName})";
    }
}
```

- [x] **Step 2: Create the service**

```csharp
using System.Collections.Generic;
using System.Linq;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Lists and provisions AppX packages in a mounted Windows image
    /// </summary>
    public class AppProvisioningService
    {
        private const string ServiceName = "AppProvisioningService";
        private readonly ModuleCallbacks _callbacks;

        public AppProvisioningService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Lists provisioned AppX packages in a mounted image
        /// </summary>
        public List<ProvisionedAppInfo> GetProvisionedApps(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new System.InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            var packages = imageService.GetProvisionedAppxPackages(mountPath);

            return packages.Select(p => new ProvisionedAppInfo
            {
                PackageName = p.PackageName ?? string.Empty,
                DisplayName = p.DisplayName ?? string.Empty,
                Publisher = string.Empty,
                Version = string.Empty,
                InstallLocation = string.Empty
            }).ToList();
        }
    }
}
```

Note for the implementer: `DismAppxPackage` may expose additional fields beyond `.PackageName`/`.DisplayName` (e.g. a publisher or install-path property) — check the actual type via the same reflection approach used in prior Phase 1/2 plans (`Add-Type -Path 'Module/PSWindowsImageTools/bin/Microsoft.Dism.dll'; [Microsoft.Dism.DismAppxPackage].GetProperties() | Select-Object Name`) before finalizing this mapping, and populate `Publisher`/`Version`/`InstallLocation` from real properties if they exist rather than leaving them permanently empty. If no such properties exist on the type, leave them as `string.Empty` and note this in your self-review — don't invent property names that don't exist.

- [x] **Step 3: Create the cmdlet**

```csharp
using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Lists provisioned AppX packages in one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageProvisionedApp")]
    [OutputType(typeof(ProvisionedAppInfo[]))]
    public class GetWindowsImageProvisionedAppCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageProvisionedApp";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to query")]
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
                LoggingService.WriteWarning(this, "No mounted images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var apps = appProvisioningService.GetProvisionedApps(mountedImage, imageService);
                    WriteObject(apps.ToArray());
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to get provisioned apps for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 4: Build the module and smoke-test the cmdlet is registered**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Get-WindowsImageProvisionedApp'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (targeted single-line insert).
Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageProvisionedApp"` — expect the cmdlet to be found.

- [ ] **Step 5: Commit**

```bash
git add src/Models/AppProvisioningModels.cs src/Services/AppProvisioningService.cs src/Cmdlets/AppProvisioningCmdlets.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsImageProvisionedApp cmdlet"
```

Rebuild and commit the DLL as a follow-up commit:

```bash
dotnet build PSWindowsImageTools.sln
cp Artifacts/bin/PSWindowsImageTools.dll Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git add Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git commit -m "build: rebuild PSWindowsImageTools.dll for Get-WindowsImageProvisionedApp"
```

---

### Task 2: IWindowsImageService.AddProvisionedAppxPackage + Add-WindowsImageProvisionedApp cmdlet

**Files:**
- Modify: `src/Services/Abstractions/IWindowsImageService.cs`
- Modify: `src/Services/WindowsImageService.cs`
- Modify: `src/Services/AppProvisioningService.cs`
- Modify: `src/Cmdlets/AppProvisioningCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`

**Interfaces:**
- Consumes: `DismApi.OpenOfflineSession(string)`, `DismApi.AddProvisionedAppxPackage(DismSession, string, List<string>, string, string)` (confirmed real, see Global Constraints).
- Produces: `IWindowsImageService.AddProvisionedAppxPackage(string mountPath, string appPath, List<string> dependencyPackages, string? licensePath, string? customDataPath)`. `AppProvisioningService.AddProvisionedApp(MountedWindowsImage, IWindowsImageService, FileInfo appPackagePath, List<FileInfo>? dependencyPackages, FileInfo? licensePath)`.

No unit test for this task — real DISM API call, matching every other DISM-facing method's established no-unit-test convention.

- [x] **Step 1: Add the interface member**

Add to `src/Services/Abstractions/IWindowsImageService.cs`, after `RemoveProvisionedAppxPackage`:

```csharp
        /// <summary>
        /// Provisions an AppX package into a mounted image
        /// </summary>
        /// <param name="mountPath">Path where the image is mounted</param>
        /// <param name="appPath">Path to the .appx/.appxbundle/.msix package file</param>
        /// <param name="dependencyPackages">Paths to any dependency packages the app requires</param>
        /// <param name="licensePath">Path to the app's license file, if required</param>
        /// <param name="customDataPath">Path to a custom data file for the app, if any</param>
        void AddProvisionedAppxPackage(string mountPath, string appPath, System.Collections.Generic.List<string> dependencyPackages, string? licensePath = null, string? customDataPath = null);
```

- [x] **Step 2: Implement in WindowsImageService**

Add to `src/Services/WindowsImageService.cs`, near `RemoveProvisionedAppxPackage`, mirroring the exact session-lifecycle pattern every other DISM-facing method in this file uses:

```csharp
        /// <inheritdoc />
        public void AddProvisionedAppxPackage(string mountPath, string appPath, List<string> dependencyPackages, string? licensePath = null, string? customDataPath = null)
        {
            Initialize();

            try
            {
                _callbacks.Verbose?.Invoke($"Provisioning AppX package {appPath} into mounted image at {mountPath}");

                using var session = DismApi.OpenOfflineSession(mountPath);
                DismApi.AddProvisionedAppxPackage(session, appPath, dependencyPackages, licensePath ?? string.Empty, customDataPath ?? string.Empty);

                _callbacks.Verbose?.Invoke($"AppX package {appPath} provisioned successfully");
            }
            catch (Exception ex)
            {
                _callbacks.Error?.Invoke(ex, $"Failed to provision AppX package {appPath}: {ex.Message}");
                throw;
            }
        }
```

- [x] **Step 3: Add the mapping method to AppProvisioningService**

Add to `src/Services/AppProvisioningService.cs`:

```csharp
        /// <summary>
        /// Provisions a new AppX package into a mounted image
        /// </summary>
        public void AddProvisionedApp(MountedWindowsImage mountedImage, IWindowsImageService imageService, FileInfo appPackagePath, List<FileInfo>? dependencyPackages, FileInfo? licensePath)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var dependencyPaths = (dependencyPackages ?? new List<FileInfo>()).Select(f => f.FullName).ToList();

            imageService.AddProvisionedAppxPackage(
                mountedImage.MountPath.FullName,
                appPackagePath.FullName,
                dependencyPaths,
                licensePath?.FullName);
        }
```

Add `using System;` and `using System.IO;` to the top of `AppProvisioningService.cs` if not already present.

- [x] **Step 4: Add the cmdlet**

Add to `src/Cmdlets/AppProvisioningCmdlets.cs`:

```csharp
    /// <summary>
    /// Provisions a new AppX package into one or more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Add, "WindowsImageProvisionedApp", SupportsShouldProcess = true)]
    [OutputType(typeof(void))]
    public class AddWindowsImageProvisionedAppCmdlet : PSCmdlet
    {
        private const string ComponentName = "Add-WindowsImageProvisionedApp";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to provision the app into")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Path to the .appx/.appxbundle/.msix package file")]
        [ValidateNotNull]
        public FileInfo PackagePath { get; set; } = null!;

        [Parameter(HelpMessage = "Paths to any dependency packages the app requires")]
        public FileInfo[]? DependencyPackagePath { get; set; }

        [Parameter(HelpMessage = "Path to the app's license file, if required")]
        public FileInfo? LicensePath { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));
            var dependencyPackages = DependencyPackagePath?.ToList();

            foreach (var mountedImage in _allMountedImages)
            {
                var target = mountedImage.MountPath?.FullName ?? mountedImage.ImageName;
                if (!ShouldProcess(target, $"Provision app {PackagePath.Name}"))
                {
                    continue;
                }

                try
                {
                    appProvisioningService.AddProvisionedApp(mountedImage, imageService, PackagePath, dependencyPackages, LicensePath);
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to provision app for {mountedImage.ImageName}: {ex.Message}", ex);
                    if (!ContinueOnError.IsPresent)
                    {
                        throw;
                    }
                }
            }
        }
    }
```

Add `using System.Linq;` to the top of `AppProvisioningCmdlets.cs` if not already present (needed for `DependencyPackagePath?.ToList()`).

- [ ] **Step 5: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Add-WindowsImageProvisionedApp'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 6: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`:

```powershell
Describe "Integration: app provisioning" -Tag Integration {

    It "lists provisioned apps for a mounted image without error" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            { $mounted | Get-WindowsImageProvisionedApp } | Should -Not -Throw
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

(No `Add-WindowsImageProvisionedApp` integration case — the synthetic baseline image has no real `.appx` package file to provision, and fabricating one is out of proportion to this test's value; the cmdlet's plumbing is exercised by the unit-testable `AddProvisionedApp` mapping and manual verification.)

- [ ] **Step 7: Commit**

```bash
git add src/Services/Abstractions/IWindowsImageService.cs src/Services/WindowsImageService.cs src/Services/AppProvisioningService.cs src/Cmdlets/AppProvisioningCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Add-WindowsImageProvisionedApp cmdlet"
```

Rebuild and commit the DLL as a follow-up commit, same pattern as Task 1.

---

### Task 3: WinGet Configuration export

**Files:**
- Modify: `src/Models/AppProvisioningModels.cs`
- Modify: `src/Services/AppProvisioningService.cs`
- Modify: `src/Cmdlets/AppProvisioningCmdlets.cs`
- Test: `tests/PSWindowsImageTools.Tests/AppProvisioningServiceTests.cs`

**Interfaces:**
- Produces: `WinGetConfigurationEntry { PackageIdentifier: string, Version: string?, Source: string }`. `WinGetConfigurationExportResult { ConfigPath: FileInfo, ScheduledTaskPath: FileInfo, Packages: List<WinGetConfigurationEntry> }`. `AppProvisioningService.ExportWinGetConfiguration(List<WinGetConfigurationEntry> packages, DirectoryInfo destination) -> WinGetConfigurationExportResult` — pure file templating, no DISM/image access.

- [x] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.IO;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class AppProvisioningServiceTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public AppProvisioningServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "PSWIT-Tests-" + System.Guid.NewGuid().ToString("N"));
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
        public void ExportWinGetConfiguration_WritesYamlWithSchemaHeaderAndPackages()
        {
            var packages = new List<WinGetConfigurationEntry>
            {
                new WinGetConfigurationEntry { PackageIdentifier = "Microsoft.PowerToys", Version = "0.87.0", Source = "winget" },
                new WinGetConfigurationEntry { PackageIdentifier = "7zip.7zip", Source = "winget" }
            };

            var result = new AppProvisioningService().ExportWinGetConfiguration(packages, new DirectoryInfo(_tempDirectory));

            Assert.True(result.ConfigPath.Exists);
            var yaml = File.ReadAllText(result.ConfigPath.FullName);
            Assert.Contains("yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2", yaml);
            Assert.Contains("Microsoft.WinGet.DSC/WinGetPackage", yaml);
            Assert.Contains("Microsoft.PowerToys", yaml);
            Assert.Contains("7zip.7zip", yaml);
            Assert.Equal(2, result.Packages.Count);
        }

        [Fact]
        public void ExportWinGetConfiguration_WritesWellFormedScheduledTaskXml()
        {
            var packages = new List<WinGetConfigurationEntry>
            {
                new WinGetConfigurationEntry { PackageIdentifier = "7zip.7zip", Source = "winget" }
            };

            var result = new AppProvisioningService().ExportWinGetConfiguration(packages, new DirectoryInfo(_tempDirectory));

            Assert.True(result.ScheduledTaskPath.Exists);
            // Must parse as well-formed XML — throws if malformed
            var doc = new System.Xml.XmlDocument();
            doc.Load(result.ScheduledTaskPath.FullName);
            Assert.Equal("Task", doc.DocumentElement!.Name);
        }

        [Fact]
        public void ExportWinGetConfiguration_EmptyPackageList_StillWritesValidFiles()
        {
            var result = new AppProvisioningService().ExportWinGetConfiguration(new List<WinGetConfigurationEntry>(), new DirectoryInfo(_tempDirectory));

            Assert.True(result.ConfigPath.Exists);
            Assert.Empty(result.Packages);
        }
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter AppProvisioningServiceTests`
Expected: FAIL (`WinGetConfigurationEntry`/`ExportWinGetConfiguration` don't exist yet)

- [x] **Step 3: Add the models**

Add to `src/Models/AppProvisioningModels.cs`:

```csharp
    /// <summary>
    /// One desired package entry for a WinGet Configuration export
    /// </summary>
    public class WinGetConfigurationEntry
    {
        public string PackageIdentifier { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string Source { get; set; } = "winget";

        public override string ToString() => $"{PackageIdentifier} ({Source})";
    }

    /// <summary>
    /// Result of exporting a WinGet Configuration artifact for first-boot application
    /// </summary>
    public class WinGetConfigurationExportResult
    {
        public System.IO.FileInfo ConfigPath { get; set; } = null!;
        public System.IO.FileInfo ScheduledTaskPath { get; set; } = null!;
        public System.Collections.Generic.List<WinGetConfigurationEntry> Packages { get; set; } = new System.Collections.Generic.List<WinGetConfigurationEntry>();

        public override string ToString() => $"{Packages.Count} package(s) -> {ConfigPath.FullName}";
    }
```

- [x] **Step 4: Implement ExportWinGetConfiguration**

Add to `src/Services/AppProvisioningService.cs`:

```csharp
        /// <summary>
        /// Generates a WinGet Configuration (DSC v3) YAML file describing desired package state,
        /// plus a Scheduled Task XML definition that applies it via `winget configure` on first
        /// boot. Pure file templating — no DISM/image access, since WinGet cannot target an
        /// offline mounted image.
        /// </summary>
        public WinGetConfigurationExportResult ExportWinGetConfiguration(List<WinGetConfigurationEntry> packages, DirectoryInfo destination)
        {
            if (!destination.Exists)
            {
                destination.Create();
            }

            var configPath = new FileInfo(Path.Combine(destination.FullName, "winget-configuration.yaml"));
            var taskPath = new FileInfo(Path.Combine(destination.FullName, "Apply-WinGetConfiguration.xml"));

            var yaml = new StringBuilder();
            yaml.AppendLine("# yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2");
            yaml.AppendLine("properties:");
            yaml.AppendLine("  resources:");

            foreach (var package in packages)
            {
                yaml.AppendLine("  - resource: Microsoft.WinGet.DSC/WinGetPackage");
                yaml.AppendLine("    directives:");
                yaml.AppendLine($"      description: Install {package.PackageIdentifier}");
                yaml.AppendLine("      allowPrerelease: true");
                yaml.AppendLine("    settings:");
                yaml.AppendLine($"      id: {package.PackageIdentifier}");
                if (!string.IsNullOrEmpty(package.Version))
                {
                    yaml.AppendLine($"      version: {package.Version}");
                }
                yaml.AppendLine($"      source: {package.Source}");
            }

            yaml.AppendLine("  configurationVersion: 0.2.0");

            File.WriteAllText(configPath.FullName, yaml.ToString());

            var taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Actions Context=""Author"">
    <Exec>
      <Command>winget</Command>
      <Arguments>configure --file ""{configPath.FullName}"" --accept-configuration-agreements</Arguments>
    </Exec>
  </Actions>
</Task>";

            File.WriteAllText(taskPath.FullName, taskXml);

            _callbacks.Verbose?.Invoke($"WinGet configuration exported: {configPath.FullName} ({packages.Count} package(s))");

            return new WinGetConfigurationExportResult
            {
                ConfigPath = configPath,
                ScheduledTaskPath = taskPath,
                Packages = packages
            };
        }
```

Add `using System.IO;` and `using System.Text;` to the top of `AppProvisioningService.cs` if not already present.

- [x] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter AppProvisioningServiceTests`
Expected: PASS (all 3 tests)

- [x] **Step 6: Add the cmdlet**

Add to `src/Cmdlets/AppProvisioningCmdlets.cs`:

```csharp
    /// <summary>
    /// Generates a WinGet Configuration artifact for first-boot application (WinGet cannot
    /// target an offline mounted image directly)
    /// </summary>
    [Cmdlet(VerbsData.Export, "WindowsImageWinGetConfiguration")]
    [OutputType(typeof(WinGetConfigurationExportResult))]
    public class ExportWindowsImageWinGetConfigurationCmdlet : PSCmdlet
    {
        private const string ComponentName = "Export-WindowsImageWinGetConfiguration";
        private readonly List<WinGetConfigurationEntry> _allPackages = new List<WinGetConfigurationEntry>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Desired package entries")]
        [ValidateNotNull]
        public WinGetConfigurationEntry[] Package { get; set; } = Array.Empty<WinGetConfigurationEntry>();

        [Parameter(Mandatory = true, Position = 1, HelpMessage = "Destination directory for the generated configuration files")]
        [ValidateNotNull]
        public DirectoryInfo DestinationPath { get; set; } = null!;

        protected override void ProcessRecord()
        {
            _allPackages.AddRange(Package);
        }

        protected override void EndProcessing()
        {
            if (_allPackages.Count == 0)
            {
                LoggingService.WriteWarning(this, "No packages provided for WinGet configuration export");
            }

            var appProvisioningService = new AppProvisioningService(ModuleCallbacks.FromCmdlet(this));

            try
            {
                var result = appProvisioningService.ExportWinGetConfiguration(_allPackages, DestinationPath);
                WriteObject(result);
            }
            catch (Exception ex)
            {
                ThrowTerminatingError(new ErrorRecord(ex, "ExportWinGetConfigurationFailed", ErrorCategory.WriteError, DestinationPath));
            }
        }
    }
```

- [ ] **Step 7: Build and register the cmdlet**

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Export-WindowsImageWinGetConfiguration'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 8: Commit**

```bash
git add src/Models/AppProvisioningModels.cs src/Services/AppProvisioningService.cs src/Cmdlets/AppProvisioningCmdlets.cs tests/PSWindowsImageTools.Tests/AppProvisioningServiceTests.cs Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Export-WindowsImageWinGetConfiguration cmdlet"
```

Rebuild and commit the DLL as a follow-up commit, same pattern as prior tasks.

---

### Task 4: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests`
Expected: PASS — all pre-existing tests plus the 3 new ones from Task 3.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build PSWindowsImageTools.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Verify the module manifest lists all 3 new cmdlets and PowerShell can discover them**

Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageProvisionedApp, Add-WindowsImageProvisionedApp, Export-WindowsImageWinGetConfiguration"`
Expected: all 3 cmdlets found.

- [ ] **Step 4: Run the integration suite (requires an elevated Windows session with real DISM)**

Run: `pwsh tests/integration/run-integration.ps1`
Expected: PASS — including the `-Tag Integration` describe block added in Task 2.

- [ ] **Step 5: Commit any final cleanup**

```bash
git status
```

If the working tree is clean (aside from unrelated files belonging to other concurrent sessions — do not touch those), no commit is needed.
