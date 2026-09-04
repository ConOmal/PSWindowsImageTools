# Servicing Chain Intelligence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Classify installed servicing packages (SSU/LCU) in a mounted Windows image and detect SSU/LCU version mismatches, via two new cmdlets: `Get-WindowsImageServicingChain` and `Test-WindowsImageServicing`.

**Architecture:** One new subsystem following the exact service/cmdlet/model split used by every other subsystem in this module — `src/Models/ServicingChainModels.cs`, `src/Services/ServicingChainService.cs`, `src/Cmdlets/ServicingChainCmdlets.cs`. Reuses the existing `IWindowsImageService.GetPackages` — no new DISM API surface. Classification logic is pure/unit-testable; DISM enumeration is a thin wrapper, matching `ComponentStoreService`'s pure/impure split.

**Tech Stack:** C# / .NET (netstandard2.0), `Microsoft.Dism`, xUnit (`tests/PSWindowsImageTools.Tests/`), Pester (`tests/integration/`).

**Spec:** `docs/superpowers/specs/2026-09-04-servicing-chain-intelligence-design.md`

## Global Constraints

- Cmdlet naming: `Verb-WindowsImage<Noun>`. `Get-WindowsImageServicingChain` = `VerbsCommon.Get`; `Test-WindowsImageServicing` = `VerbsDiagnostic.Test` (confirmed approved verb during Phase 1 planning).
- **Verified package-identity format** (confirmed live against a real Windows 11 build-26100 image, not assumed): `<Name>~<PublicKeyToken>~<Architecture>~<Language>~<Build>.<Revision>.<Major>.<Minor>`. SSU packages: name starts with `Package_for_ServicingStack`. LCU packages: name starts with `Package_for_RollupFix`. Both have `ReleaseType == DismReleaseType.SecurityUpdate` — `ReleaseType` alone cannot distinguish them, the name prefix must be checked.
- **Precision refinement over the spec** (the spec's prose implies this but its declared pure-function signature didn't include it — this plan is where it becomes exact): `ClassifyPackage` only classifies packages whose `ReleaseType` is one of the actual update-like values (`CriticalUpdate`, `Hotfix`, `SecurityUpdate`, `SoftwareUpdate`, `Update`, `UpdateRollup`, `ServicePack`) — NOT every installed package in the image (which would include hundreds of irrelevant FeaturePack/LanguagePack/OnDemandPack/Driver/Foundation/Product entries, confirmed via live `dism /online /get-packages` output during spec research). Packages outside this set return `null` from `ClassifyPackage`, same as removed/superseded packages.
- `DismPackageFeatureState` values needing exclusion (a package no longer actually present): `Removed`, `Superseded`, `NotPresent` (confirmed real enum values from Phase 1 planning: `NotPresent, UninstallPending, Staged, Resolved, Removed, Installed, InstallPending, Superseded, PartiallyInstalled`).
- `Microsoft.Dism.DismPackage` has a non-public constructor (confirmed via reflection during Phase 1) — no test can construct it. `Analyze` (the DISM-facing wrapper) has no unit test; `ClassifyPackage`/`ValidateOrdering` (pure, operate on primitives/POCOs) are fully unit-tested.
- Mutating cmdlets: none in this plan — both cmdlets are read-only reporting.
- Multi-image cmdlets accept `-ContinueOnError`; per-image failures caught/recorded unless the switch is absent (established Phase 1 convention, e.g. `Get-WindowsImageComponentStore`).
- This repo commits its compiled binary module DLL (`Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll`) alongside source changes.
- **Working-tree note**: this checkout may have uncommitted work from OTHER concurrent sessions/automations present (other tools have been active on this machine). If `git status` shows modifications to files this plan doesn't list, do not touch them — only stage the exact files each task's steps name.

---

### Task 1: Models + pure package classification

**Files:**
- Create: `src/Models/ServicingChainModels.cs`
- Create: `src/Services/ServicingChainService.cs`
- Test: `tests/PSWindowsImageTools.Tests/ServicingChainServiceTests.cs`

**Interfaces:**
- Produces: `ServicingPackageRole { ServicingStackUpdate, CumulativeUpdate, SafeOSUpdate, DotNetUpdate, Other }`
- Produces: `ClassificationConfidence { Verified, Heuristic }`
- Produces: `ServicingPackageInfo { PackageName: string, Role: ServicingPackageRole, Confidence: ClassificationConfidence, Build: int, Revision: int, InstallTime: DateTime? }`
- Produces: `ServicingChainReport { ImageName, ImagePath, MountPath: string, GeneratedAt: DateTime, Packages: List<ServicingPackageInfo>, ServicingStackUpdate: ServicingPackageInfo?, CumulativeUpdate: ServicingPackageInfo?, OrderingValid: bool (default true), Issues: List<string> }`
- Produces: `ServicingChainService.ClassifyPackage(string packageName, DismPackageFeatureState state, DismReleaseType releaseType, DateTime? installTime) -> ServicingPackageInfo?` — `internal static`, pure.
- Produces: `ServicingChainService.ParseBuildRevision(string packageName) -> (int Build, int Revision)` — `internal static`, pure.

- [ ] **Step 1: Write the failing tests using the verified real package identities**

```csharp
using System;
using Microsoft.Dism;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;
using Xunit;

namespace PSWindowsImageTools.Tests
{
    public class ServicingChainServiceTests
    {
        private const string RealSsuPackageName = "Package_for_ServicingStack_9156~31bf3856ad364e35~amd64~~26100.9156.1.0";
        private const string RealLcuPackageName = "Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9168.1.19";

        [Fact]
        public void ClassifyPackage_RealSsuIdentity_ClassifiedAsVerifiedSSU()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealSsuPackageName, DismPackageFeatureState.Installed, DismReleaseType.SecurityUpdate, new DateTime(2026, 8, 11));

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.ServicingStackUpdate, result!.Role);
            Assert.Equal(ClassificationConfidence.Verified, result.Confidence);
            Assert.Equal(26100, result.Build);
            Assert.Equal(9156, result.Revision);
        }

        [Fact]
        public void ClassifyPackage_RealLcuIdentity_ClassifiedAsVerifiedLCU()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Installed, DismReleaseType.SecurityUpdate, new DateTime(2026, 8, 14));

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.CumulativeUpdate, result!.Role);
            Assert.Equal(ClassificationConfidence.Verified, result.Confidence);
            Assert.Equal(26100, result.Build);
            Assert.Equal(9168, result.Revision);
        }

        [Fact]
        public void ClassifyPackage_RemovedState_ReturnsNull()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Removed, DismReleaseType.SecurityUpdate, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_SupersededState_ReturnsNull()
        {
            var result = ServicingChainService.ClassifyPackage(
                RealLcuPackageName, DismPackageFeatureState.Superseded, DismReleaseType.SecurityUpdate, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_NonUpdateReleaseType_ReturnsNull()
        {
            // A language pack or feature pack should never be classified, even if its name were unusual
            var result = ServicingChainService.ClassifyPackage(
                "Microsoft-Windows-Client-LanguagePack-Package~31bf3856ad364e35~amd64~en-US~10.0.26100.9168",
                DismPackageFeatureState.Installed, DismReleaseType.LanguagePack, null);

            Assert.Null(result);
        }

        [Fact]
        public void ClassifyPackage_UnrecognizedUpdateName_ClassifiedAsOtherHeuristic()
        {
            var result = ServicingChainService.ClassifyPackage(
                "Package_for_KB9999999~31bf3856ad364e35~amd64~~26100.9200.1.0",
                DismPackageFeatureState.Installed, DismReleaseType.Update, null);

            Assert.NotNull(result);
            Assert.Equal(ServicingPackageRole.Other, result!.Role);
            Assert.Equal(ClassificationConfidence.Heuristic, result.Confidence);
        }

        [Fact]
        public void ParseBuildRevision_RealLcuIdentity_ExtractsBuildAndRevision()
        {
            var (build, revision) = ServicingChainService.ParseBuildRevision(RealLcuPackageName);

            Assert.Equal(26100, build);
            Assert.Equal(9168, revision);
        }

        [Fact]
        public void ParseBuildRevision_MalformedName_ReturnsZeros()
        {
            var (build, revision) = ServicingChainService.ParseBuildRevision("not-a-real-package-identity");

            Assert.Equal(0, build);
            Assert.Equal(0, revision);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ServicingChainServiceTests`
Expected: FAIL (build error — `ServicingChainService`/models don't exist yet)

- [ ] **Step 3: Create the models**

```csharp
using System;
using System.Collections.Generic;

namespace PSWindowsImageTools.Models
{
    /// <summary>
    /// The role a servicing package plays in the update chain
    /// </summary>
    public enum ServicingPackageRole
    {
        ServicingStackUpdate,
        CumulativeUpdate,
        SafeOSUpdate,
        DotNetUpdate,
        Other
    }

    /// <summary>
    /// How confident the classification of a package's role is. Verified = confirmed real
    /// naming convention (SSU/LCU); Heuristic = best-effort pattern match, may be wrong.
    /// </summary>
    public enum ClassificationConfidence
    {
        Verified,
        Heuristic
    }

    /// <summary>
    /// A single classified servicing package
    /// </summary>
    public class ServicingPackageInfo
    {
        public string PackageName { get; set; } = string.Empty;
        public ServicingPackageRole Role { get; set; }
        public ClassificationConfidence Confidence { get; set; }
        public int Build { get; set; }
        public int Revision { get; set; }
        public DateTime? InstallTime { get; set; }

        public override string ToString() => $"{Role} ({Confidence}): {PackageName} [{Build}.{Revision}]";
    }

    /// <summary>
    /// Servicing chain analysis for a mounted Windows image: classified update packages and
    /// whether the SSU/LCU pairing looks consistent
    /// </summary>
    public class ServicingChainReport
    {
        public string ImageName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string MountPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public List<ServicingPackageInfo> Packages { get; set; } = new List<ServicingPackageInfo>();
        public ServicingPackageInfo? ServicingStackUpdate { get; set; }
        public ServicingPackageInfo? CumulativeUpdate { get; set; }
        public bool OrderingValid { get; set; } = true;
        public List<string> Issues { get; set; } = new List<string>();

        public override string ToString() =>
            $"{ImageName}: {Packages.Count} servicing package(s), OrderingValid={OrderingValid}";
    }
}
```

- [ ] **Step 4: Create the service with pure classification logic**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Dism;
using PSWindowsImageTools.Models;

namespace PSWindowsImageTools.Services
{
    /// <summary>
    /// Classifies installed servicing packages (SSU/LCU/etc.) in a mounted Windows image and
    /// checks whether the SSU/LCU pairing looks version-consistent
    /// </summary>
    public class ServicingChainService
    {
        private const string ServiceName = "ServicingChainService";
        private readonly ModuleCallbacks _callbacks;

        private static readonly HashSet<DismReleaseType> ServicingReleaseTypes = new HashSet<DismReleaseType>
        {
            DismReleaseType.CriticalUpdate,
            DismReleaseType.Hotfix,
            DismReleaseType.SecurityUpdate,
            DismReleaseType.SoftwareUpdate,
            DismReleaseType.Update,
            DismReleaseType.UpdateRollup,
            DismReleaseType.ServicePack
        };

        public ServicingChainService(ModuleCallbacks? callbacks = null)
        {
            _callbacks = callbacks ?? ModuleCallbacks.Silent;
        }

        /// <summary>
        /// Classifies a single package by its identity string, state, and release type. Pure —
        /// no DISM/filesystem access. Returns null for packages that are no longer present
        /// (Removed/Superseded/NotPresent) or that aren't an update-like release type at all
        /// (feature packs, language packs, drivers, etc. are out of scope for this report).
        /// </summary>
        internal static ServicingPackageInfo? ClassifyPackage(
            string packageName, DismPackageFeatureState state, DismReleaseType releaseType, DateTime? installTime)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            if (state == DismPackageFeatureState.Removed ||
                state == DismPackageFeatureState.Superseded ||
                state == DismPackageFeatureState.NotPresent)
            {
                return null;
            }

            if (!ServicingReleaseTypes.Contains(releaseType))
            {
                return null;
            }

            ServicingPackageRole role;
            ClassificationConfidence confidence;

            if (packageName.StartsWith("Package_for_ServicingStack", StringComparison.OrdinalIgnoreCase))
            {
                role = ServicingPackageRole.ServicingStackUpdate;
                confidence = ClassificationConfidence.Verified;
            }
            else if (packageName.StartsWith("Package_for_RollupFix", StringComparison.OrdinalIgnoreCase))
            {
                role = ServicingPackageRole.CumulativeUpdate;
                confidence = ClassificationConfidence.Verified;
            }
            else if (packageName.IndexOf("SafeOS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                role = ServicingPackageRole.SafeOSUpdate;
                confidence = ClassificationConfidence.Heuristic;
            }
            else if (packageName.IndexOf("NetFramework", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                role = ServicingPackageRole.DotNetUpdate;
                confidence = ClassificationConfidence.Heuristic;
            }
            else
            {
                role = ServicingPackageRole.Other;
                confidence = ClassificationConfidence.Heuristic;
            }

            var (build, revision) = ParseBuildRevision(packageName);

            return new ServicingPackageInfo
            {
                PackageName = packageName,
                Role = role,
                Confidence = confidence,
                Build = build,
                Revision = revision,
                InstallTime = installTime
            };
        }

        /// <summary>
        /// Extracts the Build and Revision components from a DISM package identity string
        /// (format: Name~PublicKeyToken~Architecture~Language~Build.Revision.Major.Minor).
        /// Pure. Returns (0, 0) for anything that doesn't parse.
        /// </summary>
        internal static (int Build, int Revision) ParseBuildRevision(string packageName)
        {
            var segments = packageName.Split('~');
            if (segments.Length < 5)
            {
                return (0, 0);
            }

            var versionParts = segments[4].Split('.');
            if (versionParts.Length < 2)
            {
                return (0, 0);
            }

            int.TryParse(versionParts[0], out var build);
            int.TryParse(versionParts[1], out var revision);
            return (build, revision);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ServicingChainServiceTests`
Expected: PASS (all 7 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Models/ServicingChainModels.cs src/Services/ServicingChainService.cs tests/PSWindowsImageTools.Tests/ServicingChainServiceTests.cs
git commit -m "feat: add servicing chain models and pure package classification"
```

---

### Task 2: Ordering validation + Analyze() + Get-WindowsImageServicingChain cmdlet

**Files:**
- Modify: `src/Services/ServicingChainService.cs`
- Create: `src/Cmdlets/ServicingChainCmdlets.cs`
- Modify: `tests/PSWindowsImageTools.Tests/ServicingChainServiceTests.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`

**Interfaces:**
- Consumes: `ServicingPackageInfo`, `ServicingChainReport`, `ServicingChainService.ClassifyPackage` (Task 1); `IWindowsImageService.GetPackages(string mountPath) -> List<DismPackage>` (existing, unchanged); `MountedWindowsImage { MountPath: DirectoryInfo?, ImageName, SourceImagePath }` (existing); `WindowsImageService.ForCmdlet(PSCmdlet)` (existing).
- Produces: `ServicingChainService.ValidateOrdering(ServicingChainReport report, int maxRevisionLag = 200)` — `internal static`, pure. `ServicingChainService.Analyze(MountedWindowsImage, IWindowsImageService) -> ServicingChainReport`.

- [ ] **Step 1: Write the failing tests for ValidateOrdering**

Append to `tests/PSWindowsImageTools.Tests/ServicingChainServiceTests.cs`:

```csharp
        private static ServicingPackageInfo MakeInfo(ServicingPackageRole role, int build, int revision, string name = "test")
        {
            return new ServicingPackageInfo
            {
                PackageName = name,
                Role = role,
                Confidence = ClassificationConfidence.Verified,
                Build = build,
                Revision = revision
            };
        }

        [Fact]
        public void ValidateOrdering_LcuWithNoSsu_IsInvalid()
        {
            var report = new ServicingChainReport
            {
                Packages = { MakeInfo(ServicingPackageRole.CumulativeUpdate, 26100, 9168) }
            };

            ServicingChainService.ValidateOrdering(report);

            Assert.False(report.OrderingValid);
            Assert.Null(report.ServicingStackUpdate);
            Assert.NotNull(report.CumulativeUpdate);
            Assert.Single(report.Issues);
        }

        [Fact]
        public void ValidateOrdering_SsuWithinTolerance_IsValid()
        {
            var report = new ServicingChainReport
            {
                Packages =
                {
                    MakeInfo(ServicingPackageRole.ServicingStackUpdate, 26100, 9156),
                    MakeInfo(ServicingPackageRole.CumulativeUpdate, 26100, 9168)
                }
            };

            ServicingChainService.ValidateOrdering(report);

            Assert.True(report.OrderingValid);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void ValidateOrdering_SsuFarBehindLcu_IsInvalid()
        {
            var report = new ServicingChainReport
            {
                Packages =
                {
                    MakeInfo(ServicingPackageRole.ServicingStackUpdate, 26100, 8000),
                    MakeInfo(ServicingPackageRole.CumulativeUpdate, 26100, 9168)
                }
            };

            ServicingChainService.ValidateOrdering(report);

            Assert.False(report.OrderingValid);
            Assert.Single(report.Issues);
        }

        [Fact]
        public void ValidateOrdering_NoCumulativeUpdate_IsValidRegardlessOfSsu()
        {
            var report = new ServicingChainReport
            {
                Packages = { MakeInfo(ServicingPackageRole.ServicingStackUpdate, 26100, 9156) }
            };

            ServicingChainService.ValidateOrdering(report);

            Assert.True(report.OrderingValid);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void ValidateOrdering_MultipleSsus_PicksHighestRevision()
        {
            var report = new ServicingChainReport
            {
                Packages =
                {
                    MakeInfo(ServicingPackageRole.ServicingStackUpdate, 26100, 9000, "old-ssu"),
                    MakeInfo(ServicingPackageRole.ServicingStackUpdate, 26100, 9156, "new-ssu"),
                    MakeInfo(ServicingPackageRole.CumulativeUpdate, 26100, 9168)
                }
            };

            ServicingChainService.ValidateOrdering(report);

            Assert.Equal("new-ssu", report.ServicingStackUpdate!.PackageName);
            Assert.True(report.OrderingValid);
        }
```

Add `using System.Collections.Generic;` to the test file's usings if not already present (needed for the collection-initializer `{ ... }` syntax on `Packages`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ServicingChainServiceTests`
Expected: FAIL (`ValidateOrdering` not defined)

- [ ] **Step 3: Implement ValidateOrdering and Analyze**

Add to `src/Services/ServicingChainService.cs` (inside the `ServicingChainService` class, after `ParseBuildRevision`):

```csharp
        /// <summary>
        /// Selects the SSU/LCU from an already-classified package list and checks whether the
        /// SSU's revision is recent enough relative to the LCU's. Pure — operates only on
        /// report.Packages, no DISM/filesystem access.
        /// </summary>
        internal static void ValidateOrdering(ServicingChainReport report, int maxRevisionLag = 200)
        {
            report.ServicingStackUpdate = report.Packages
                .Where(p => p.Role == ServicingPackageRole.ServicingStackUpdate)
                .OrderByDescending(p => p.Revision)
                .FirstOrDefault();

            report.CumulativeUpdate = report.Packages
                .Where(p => p.Role == ServicingPackageRole.CumulativeUpdate)
                .OrderByDescending(p => p.Revision)
                .FirstOrDefault();

            if (report.CumulativeUpdate == null)
            {
                return;
            }

            if (report.ServicingStackUpdate == null)
            {
                report.OrderingValid = false;
                report.Issues.Add(
                    $"Cumulative update {report.CumulativeUpdate.PackageName} is present but no Servicing Stack Update was found");
                return;
            }

            var lag = report.CumulativeUpdate.Revision - report.ServicingStackUpdate.Revision;
            if (lag > maxRevisionLag)
            {
                report.OrderingValid = false;
                report.Issues.Add(
                    $"Servicing Stack Update revision {report.ServicingStackUpdate.Revision} appears stale relative to " +
                    $"Cumulative Update revision {report.CumulativeUpdate.Revision} (lag {lag} > {maxRevisionLag})");
            }
        }

        /// <summary>
        /// Analyzes the servicing chain of a mounted image (read-only)
        /// </summary>
        public ServicingChainReport Analyze(MountedWindowsImage mountedImage, IWindowsImageService imageService)
        {
            if (mountedImage.MountPath == null)
            {
                throw new InvalidOperationException($"Mount path is null for image {mountedImage.ImageName}");
            }

            var mountPath = mountedImage.MountPath.FullName;
            _callbacks.Verbose?.Invoke($"Analyzing servicing chain for {mountedImage.ImageName} at {mountPath}");

            var report = new ServicingChainReport
            {
                ImageName = mountedImage.ImageName,
                ImagePath = mountedImage.SourceImagePath,
                MountPath = mountPath
            };

            try
            {
                var packages = imageService.GetPackages(mountPath);
                foreach (var package in packages)
                {
                    var classified = ClassifyPackage(
                        package.PackageName ?? string.Empty, package.PackageState, package.ReleaseType, package.InstallTime);

                    if (classified != null)
                    {
                        report.Packages.Add(classified);
                    }
                }
            }
            catch (Exception ex)
            {
                report.Issues.Add($"Failed to enumerate packages: {ex.Message}");
                _callbacks.Warning?.Invoke($"Failed to enumerate packages for {mountedImage.ImageName}: {ex.Message}");
            }

            ValidateOrdering(report);

            _callbacks.Verbose?.Invoke($"Servicing chain analysis complete for {mountedImage.ImageName}: {report}");
            return report;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/PSWindowsImageTools.Tests --filter ServicingChainServiceTests`
Expected: PASS (12 tests total: 7 from Task 1 + 5 new)

- [ ] **Step 5: Create the cmdlet**

```csharp
using System;
using System.Collections.Generic;
using System.Management.Automation;
using PSWindowsImageTools.Models;
using PSWindowsImageTools.Services;

namespace PSWindowsImageTools.Cmdlets
{
    /// <summary>
    /// Analyzes the servicing chain (SSU/LCU classification and version consistency) of one or
    /// more mounted Windows images
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "WindowsImageServicingChain")]
    [OutputType(typeof(ServicingChainReport[]))]
    public class GetWindowsImageServicingChainCmdlet : PSCmdlet
    {
        private const string ComponentName = "Get-WindowsImageServicingChain";
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
                LoggingService.WriteWarning(this, "No mounted images provided for servicing chain analysis");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var servicingChainService = new ServicingChainService(ModuleCallbacks.FromCmdlet(this));
            var results = new List<ServicingChainReport>();

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    results.Add(servicingChainService.Analyze(mountedImage, imageService));
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to analyze servicing chain for {mountedImage.ImageName}: {ex.Message}", ex);
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

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Get-WindowsImageServicingChain'` to the `CmdletsToExport` array in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (targeted single-line insert, don't reorder or reformat the rest of the array).
Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageServicingChain"` — expect the cmdlet to be found.

- [ ] **Step 7: Add the integration test**

Append to `tests/integration/PSWindowsImageTools.Integration.Tests.ps1` (new `Describe` block, matching the file's existing style — read the file's `BeforeAll` block first for the exact `$BaselineWim`/`$MountRoot`/`$Workspace` variable names it defines):

```powershell
Describe "Integration: servicing chain" -Tag Integration {

    It "analyzes the servicing chain of a mounted image without error" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $report = $mounted | Get-WindowsImageServicingChain
            $report | Should -Not -BeNullOrEmpty
            $report.ImageName | Should -Be $mounted.ImageName
            # The synthetic baseline image has no real servicing packages, so Packages may be
            # empty — this asserts the cmdlet runs cleanly, not a specific SSU/LCU pairing.
            $report.OrderingValid | Should -BeOfType [bool]
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
}
```

- [ ] **Step 8: Commit**

```bash
git add src/Services/ServicingChainService.cs src/Cmdlets/ServicingChainCmdlets.cs tests/PSWindowsImageTools.Tests/ServicingChainServiceTests.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Get-WindowsImageServicingChain cmdlet"
```

Rebuild the DLL and commit it as a small follow-up commit (repo convention):

```bash
dotnet build PSWindowsImageTools.sln
cp Artifacts/bin/PSWindowsImageTools.dll Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git add Module/PSWindowsImageTools/bin/PSWindowsImageTools.dll
git commit -m "build: rebuild PSWindowsImageTools.dll for Get-WindowsImageServicingChain"
```

---

### Task 3: Test-WindowsImageServicing cmdlet

**Files:**
- Modify: `src/Cmdlets/ServicingChainCmdlets.cs`
- Modify: `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`
- Modify: `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`

**Interfaces:**
- Consumes: `ServicingChainService.Analyze` (Task 2), `ServicingChainReport.OrderingValid: bool` (Task 1).

This cmdlet is a thin wrapper reusing `Analyze` — no service changes needed, no new unit test (identical DISM-facing constraint as Task 2's cmdlet).

- [ ] **Step 1: Add the cmdlet to ServicingChainCmdlets.cs**

```csharp
    /// <summary>
    /// Tests whether one or more mounted Windows images have a version-consistent SSU/LCU
    /// servicing chain
    /// </summary>
    [Cmdlet(VerbsDiagnostic.Test, "WindowsImageServicing")]
    [OutputType(typeof(bool))]
    [OutputType(typeof(ServicingChainReport))]
    public class TestWindowsImageServicingCmdlet : PSCmdlet
    {
        private const string ComponentName = "Test-WindowsImageServicing";
        private readonly List<MountedWindowsImage> _allMountedImages = new List<MountedWindowsImage>();

        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, HelpMessage = "Mounted Windows images to test")]
        [ValidateNotNull]
        public MountedWindowsImage[] MountedImages { get; set; } = Array.Empty<MountedWindowsImage>();

        [Parameter(HelpMessage = "Return the full ServicingChainReport instead of just a boolean")]
        public SwitchParameter Detailed { get; set; }

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
                LoggingService.WriteWarning(this, "No mounted images provided for servicing test");
                return;
            }

            using var imageService = WindowsImageService.ForCmdlet(this);
            var servicingChainService = new ServicingChainService(ModuleCallbacks.FromCmdlet(this));

            foreach (var mountedImage in _allMountedImages)
            {
                try
                {
                    var report = servicingChainService.Analyze(mountedImage, imageService);
                    if (Detailed.IsPresent)
                    {
                        WriteObject(report);
                    }
                    else
                    {
                        WriteObject(report.OrderingValid);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.WriteError(this, ComponentName, $"Failed to test servicing for {mountedImage.ImageName}: {ex.Message}", ex);
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

Run: `dotnet build PSWindowsImageTools.sln` — expect success, 0 warnings.
Add `'Test-WindowsImageServicing'` to `CmdletsToExport` in `Module/PSWindowsImageTools/PSWindowsImageTools.psd1`.

- [ ] **Step 3: Add the integration test**

Append to the `Describe "Integration: servicing chain"` block from Task 2:

```powershell
    It "returns a boolean by default and a full report with -Detailed" {
        $mounted = Get-WindowsImageList -ImagePath $BaselineWim |
            Mount-WindowsImageList -MountRoot $MountRoot -ReadWrite

        try {
            $result = $mounted | Test-WindowsImageServicing
            $result | Should -BeOfType [bool]

            $detailed = $mounted | Test-WindowsImageServicing -Detailed
            $detailed.OrderingValid | Should -Be $result
        }
        finally {
            $mounted | Dismount-WindowsImageList -Discard -RemoveDirectories -ErrorAction SilentlyContinue
        }
    }
```

- [ ] **Step 4: Commit**

```bash
git add src/Cmdlets/ServicingChainCmdlets.cs tests/integration/PSWindowsImageTools.Integration.Tests.ps1 Module/PSWindowsImageTools/PSWindowsImageTools.psd1
git commit -m "feat: add Test-WindowsImageServicing cmdlet"
```

Rebuild and commit the DLL as a follow-up commit, same as Task 2's Step 8.

---

### Task 4: Full-suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full unit test suite**

Run: `dotnet test tests/PSWindowsImageTools.Tests`
Expected: PASS — all pre-existing tests plus the 12 new ones from Tasks 1-2.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build PSWindowsImageTools.sln`
Expected: PASS, 0 warnings, 0 errors.

- [ ] **Step 3: Verify the module manifest lists both new cmdlets and PowerShell can discover them**

Run: `powershell -NoProfile -Command "Import-Module ./Module/PSWindowsImageTools/PSWindowsImageTools.psd1 -Force; Get-Command Get-WindowsImageServicingChain, Test-WindowsImageServicing"`
Expected: both cmdlets found.

- [ ] **Step 4: Run the integration suite (requires an elevated Windows session with real DISM)**

Run: `pwsh tests/integration/run-integration.ps1`
Expected: PASS — including the `-Tag Integration` describe block added in Tasks 2-3.

- [ ] **Step 5: Commit any final cleanup**

```bash
git status
```

If the working tree is clean (aside from any unrelated files belonging to other concurrent sessions — do not touch those), no commit is needed — this task is verification-only.
