# Current Work Status

## Release State
- **v2026.09.04.2 tagged and released** at https://github.com/ConOmal/PSWindowsImageTools/releases/tag/v2026.09.04.2
  (fork; the stored credentials lack push access to upstream Grace-Solutions)
- **Published to PSGallery** (88 cmdlets) via the `release.yml` workflow using the
  `PSGALLERY_API_KEY` fork secret; release run green in 1m34s
- Backlog complete: every phase-1 spec Non-goals item implemented (see Phase 5-8 below);
  unit tests **695/695**; help guardrail green (88 commands, MAML in sync)
- CI fully green on main post-release (build + unit tests + help guardrails + integration
  suite), after three post-release fixes: honest skips for servicing-dependent integration
  tests on the CBS-less synthetic image, thread-safe cmdlet output during parallel mounting
  (buffered queues drained on the pipeline thread), and explicit pipeline enumeration of
  mount results (`WriteObject(x, true)` — the single-arg overload emits arrays as one item,
  which broke single-object parameter binding downstream)
- **CI now runs the integration suite on every push to main** (`integration` job); the
  real-servicing coverage comes from the manual-dispatch `integration-real` job, which uses
  a real Windows 11 24H2 install.wim baseline (downloaded, Pro index exported, cached)
- Upstream repo `origin` retained for history; `fork` remote points at the publish target

## Deferred
- **Module rename to `PSISOWIMTools`** (user-requested, deferred 2026-09-04): PSGallery has no
  rename primitive — the plan is to publish a NEW package named `PSISOWIMTools` and leave
  `PSWindowsImageTools` in place (optionally with one final "superseded" description release).
  Scoped plan: rename module dir + psd1 + bin DLL + MAML, `src/PSISOWIMTools.csproj` (AssemblyName
  + RootNamespace), test csproj dir, sln, C# namespaces (PSWindowsImageTools→PSISOWIMTools across
  ~200 files), help md front matter (`external help file`/`Module Name` drive the MAML filename),
  workflows (build/verify/Publish-Module paths), scripts, README, .gitignore; new manifest GUID;
  version 2026.09.04.2; tag v2026.09.04.2 → release workflow publishes the new package.
  Keep cmdlet noun names unchanged. GitHub repo name stays unless separately requested.

## Completed

### Phase 8 — Backlog Completion Batch (2026-09-04)
- **Unattend validation** (spec `docs/superpowers/specs/2026-09-04-unattend-validation-design.md`, plan
  `docs/superpowers/plans/2026-09-04-unattend-validation.md`): `Test-UnattendXMLConfiguration` — 21
  validation rules (well-formedness, namespace, pass validity incl. audit-mode-as-warning, duplicate
  components, RunSynchronous/Asynchronous ordering, CopyProfile pass placement, deprecated OOBE
  settings) with per-issue severity/pass/element-path reporting; pure XML logic; 58 tests.
- **Dynamic Update discovery** (spec `docs/superpowers/specs/2026-09-04-dynamic-update-discovery-design.md`,
  plan `docs/superpowers/plans/2026-09-04-dynamic-update-discovery.md`): `Get-WindowsDynamicUpdate` —
  discovers SU/SafeOS/CU/Setup Dynamic Updates for a build from the Update Catalog (label table,
  title classification, latest-per-type selection in apply order), reusing `WindowsUpdateCatalogService`
  untouched; completes the discover → download → `Invoke-MediaDynamicUpdate` workflow; 55 tests.
- **Compliance manifest** (spec `docs/superpowers/specs/2026-09-04-compliance-manifest-design.md`, plan
  `docs/superpowers/plans/2026-09-04-compliance-manifest.md`): `Export-WindowsImageComplianceManifest`
  — combines a snapshot with optional baseline-compliance and servicing-chain reports into a single
  versioned JSON audit artifact with tool/image provenance; inventory counts only (the generic
  inventory-export non-goal still stands); 17 tests.
- **Capability repository** (spec `docs/superpowers/specs/2026-09-04-capability-repository-design.md`,
  plan `docs/superpowers/plans/2026-09-04-capability-repository.md`): `Get-WindowsCapabilityRepository`
  — indexes a FoD payload source directory by cab-filename convention (name/arch/language/version,
  honest filename-derived limits documented), with regex filters and `-GroupByName`; 30 tests.
- **Nullable flow fixes**: `SecurityBaselineService.GetBaselineCompliance` dropped redundant
  `?? string.Empty` coalescing and `GetWindowsDynamicUpdateCmdlet` added a flow-breaking `return;`
  after `ThrowTerminatingError` — the .NET 11 preview SDK's nullable analyzer treats `??` on
  non-nullable params as a flow downgrade (visible only under `--no-incremental` builds); 5 warnings
  eliminated, `--no-incremental` build now 0 warnings / 0 errors.
- All 4 new cmdlets exported in the psd1 (no wildcards), help md + regenerated MAML in sync
  (88 commands), DLL rebuilt and synced to `Module/PSWindowsImageTools/bin/`.
- Unit tests now **695/695** (was 535); help guardrail green (4/4 checks). The phase-1 spec backlog
  is now fully implemented.

### Phase 7 — Registry Config Batch + Continued Session Phases (2026-09-04)
- **Services configuration** (spec `docs/superpowers/specs/2026-09-04-services-configuration-design.md`,
  plan `docs/superpowers/plans/2026-09-04-services-configuration.md`): `Get-WindowsImageService` /
  `Set-WindowsImageService` — service inventory and start-mode changes (Boot/System/Automatic/Manual/
  Disabled, `-DelayedAutoStart`) from the offline SYSTEM hive (`ControlSet001\Services`); writes
  delegate directly to `NativeRegistryService.ApplyRegistryOperations`; 51 tests.
- **OOBE configuration** (spec `docs/superpowers/specs/2026-09-04-oobe-configuration-design.md`, plan
  `docs/superpowers/plans/2026-09-04-oobe-configuration.md`): `Get-WindowsImageOOBE` /
  `Set-WindowsImageOOBE` — 7-entry OOBE catalog (SkipMachineOOBE, SkipUserOOBE, SkipPrivacyExperience,
  BypassNRO, HideOnlineAccountScreens, HideWirelessSetupInOOBE, ProtectYourPC) with tri-state
  switches (`-X` = 1, `-X:$false` = 0, omit = untouched) and catalog-validated `-Remove`; 33 tests.
- **Scheduled Tasks** (spec `docs/superpowers/specs/2026-09-04-scheduled-tasks-design.md`, plan
  `docs/superpowers/plans/2026-09-04-scheduled-tasks.md`): `Get-WindowsImageScheduledTask` —
  read-only TaskCache\Tree inventory (task path, GUID, state); the undocumented Tasks\<GUID> binary
  blob is honestly out of scope; 39 tests.
- **Security baselines** (spec `docs/superpowers/specs/2026-09-04-security-baselines-design.md`, plan
  `docs/superpowers/plans/2026-09-04-security-baselines.md`): `Get-WindowsImageSecurityBaseline` /
  `Set-WindowsImageSecurityBaseline` — 22-entry curated baseline across SOFTWARE (UAC, logon UX,
  AutoRun, RDP), SYSTEM (LSA/NTLM, SMB signing, SMB1 off, NLA, Remote Assistance) and the default-user
  NTUSER.DAT (screen-saver lock), each with documented rationale; compliance compare is numeric; 28 tests.
- **Continued interrupted-session phases** (specs/plans committed by the parallel session, implemented
  here after it was rate-limited):
  - *Boot Image Servicing*: `Get-WindowsBootImage`, `Add-WindowsBootDriver`,
    `Optimize-WindowsBootImage` — thin wrappers over `WindowsInstallationMedia.FromRoot`,
    `AddDriversFromDirectory`, `ComponentStoreService.Cleanup` (ResetBase intentionally never
    offered for PE images); 2 tests.
  - *App Provisioning*: `Get-WindowsImageProvisionedApp`, `Add-WindowsImageProvisionedApp`,
    `Export-WindowsImageWinGetConfiguration` — completes the AppX provisioning set (new
    `IWindowsImageService.AddProvisionedAppxPackage` wrapping the confirmed 5-arg
    `DismApi.AddProvisionedAppxPackage`) plus a pure WinGet Configuration DSC v0.2 YAML +
    first-boot Scheduled Task XML generator; 3 tests.
  - *Image Checkpoint*: `Checkpoint-WindowsImage`, `Get-WindowsImageCheckpoint`,
    `Restore-WindowsImageCheckpoint` (`-RemoveAfterRestore`) — directory-mirror snapshots with a
    MountSessionService-style JSON index; restore guards mounted+read-write; 7 tests.
- **Interrupted session's refactor validated**: `NativeRegistryService` gained ModuleCallbacks cores
  (PSCmdlet overloads retained as thin wrappers), `RegistryApplicationService`/`RegistryService`/
  `WindowsUpdateCatalogService` de-coupled from PSCmdlet, `Export-WindowsImage -SplitSize` (SWM
  parts) and `Mount-WindowsImageList -MaxParallel` (parallel mounting) added; both new parameters
  documented in help. Their 25 new tests (NativeRegistryServiceCallbacks, RegistryApplicationService,
  WindowsUpdateCatalogService) pass.
- All 16 new cmdlets exported in the psd1 (no wildcards), help md + regenerated MAML in sync
  (84 commands), DLL rebuilt and synced to `Module/PSWindowsImageTools/bin/`.
- Unit tests now **535/535** (was 347); build 0 warnings/0 errors; help guardrail green (4/4 checks).

### Phase 6 — Backlog Batch: Reserved Storage, Edition Servicing, WinRE Intelligence, Servicing Chain (2026-09-04)
- **Reserved Storage** (spec `docs/superpowers/specs/2026-09-04-reserved-storage-design.md`, plan
  `docs/superpowers/plans/2026-09-04-reserved-storage.md`): `Get-WindowsImageReservedStorage` /
  `Set-WindowsImageReservedStorage` (SupportsShouldProcess, `-Enable`/`-Disable` parameter sets).
  Microsoft.Dism 3.3.12 has no reserved-storage API, so the service shells out to `dism.exe`
  (`/Get-ReservedStorageState`, `/Set-ReservedStorageState:Enabled|Disabled`) mirroring
  `ComponentStoreService.Cleanup`; arg-building/parsing is pure and unit-tested.
- **Edition Servicing** (spec `docs/superpowers/specs/2026-09-04-edition-servicing-design.md`, plan
  `docs/superpowers/plans/2026-09-04-edition-servicing.md`): `Set-WindowsImageEdition`
  (SupportsShouldProcess, `-Edition`/`-ProductKey` or `-ServerEdition` parameter sets, `-PassThru`).
  Uses the managed DISM API (`SetEdition`/`SetEditionAndProductKey` exist in 3.3.12); validation,
  edition-name normalization, key masking, and result building are pure and unit-tested.
- **WinRE Intelligence** (spec `docs/superpowers/specs/2026-09-04-winre-intelligence-design.md`, plan
  `docs/superpowers/plans/2026-09-04-winre-intelligence.md`): `Get-WindowsImageWinRE` reports the
  embedded WinRE image (presence, path, size, last-modified) plus WIM-header-derived identity
  (version, image count, compression type, GUID) parsed purely from the 208-byte MSWIM header —
  never calls DISM; `-Detailed` adds the first image's XML display name.
- **Servicing Chain Intelligence** (spec `docs/superpowers/specs/2026-09-04-servicing-chain-intelligence-design.md`,
  plan `docs/superpowers/plans/2026-09-04-servicing-chain-intelligence.md`): `Get-WindowsImageServicingChain`
  / `Test-WindowsImageServicing` classify installed servicing packages (SSU/LCU via verified
  `Package_for_ServicingStack`/`Package_for_RollupFix` identity prefixes, SafeOS/.NET heuristic) and
  flag stale SSU-vs-LCU pairings. Reuses `IWindowsImageService.GetPackages`; classification and
  ordering validation are pure. (Spec + plan + models + Task-1 tests authored in a parallel session;
  service, cmdlets, Task-2 tests, help, and integration tests completed here.)
- All six cmdlets exported in the psd1 (no wildcards), help md + regenerated MAML in sync
  (68 commands), DLL rebuilt and synced to `Module/PSWindowsImageTools/bin/`.
- Unit tests now **347/347** (was 226); build 0 warnings/0 errors; help guardrail green (4/4 checks).

### Phase 5 — Registry Drift Detection + TODO Closure (2026-09-04)
- **Registry drift phase** (first backlog phase from the phase-1 spec): `Get-WindowsImageSnapshot`
  now captures a defined drift-relevant registry key set (Run/RunOnce, Policies, WindowsUpdate,
  Winlogon, Uninstall native+WOW64, Services, ComputerName/Session Manager/Lsa/Environment/Tcpip/
  Terminal Server) from offline hives via the in-memory `RegistryHiveReader` (no mounting), and
  `Compare-WindowsImage` reports per-hive added/removed/changed registry drift that feeds
  `TotalDifferences`/`AreIdentical`. Registry data round-trips through the snapshot JSON export.
  Spec: `docs/superpowers/specs/2026-09-04-registry-drift-detection-design.md`; plan:
  `docs/superpowers/plans/2026-09-04-registry-drift-detection.md` (17/17 steps).
- **TODO closure**: `FormatUtilityService.NormalizeWindowsVersion` now handles future kernel
  versions (major > 10 preserved as authoritative, 3-part padded to 4-part); the dead
  `NativeRegistryService.ModifyOfflineRegistry` stub is implemented via conversion to
  `RegistryOperation[]` delegating to the proven `ApplyRegistryOperations` hive-mounted write path.
- Unit tests now **226/226** (was 176); help guardrail green (62 cmdlets, MAML in sync);
  build 0 warnings/0 errors.

### Plan Checkbox Hygiene (2026-09-04)
- Both implementation plans verified against the code and marked complete:
  `docs/superpowers/plans/2026-09-03-windows11-iso-servicing.md` (53/53 steps) and
  `docs/superpowers/plans/2026-09-04-phase1-component-store-drivers-inventory-validation.md`
  (82/82 steps). All artifacts exist and all cmdlets are exported; two ISO-plan file names
  deviated from the plan as implemented (`NewWindowsImageISOCmdlet.cs` vs `NewWindowsISOCmdlet.cs`,
  `WindowsISODownloadUrlBuilder.cs` vs `WindowsISODownloadParser.cs`).

### Phase 0 — Baseline & Safety Net
- Solution builds clean (0 warnings); package downgrade fixed (`System.Runtime.CompilerServices.Unsafe` 6.1.2)
- `tests/PSWindowsImageTools.Tests` (xUnit): 93 tests — format parsers, `.reg` parsing, registry
  operation model, BuildRecipe/recipe round-trips, RegistryHiveReader (real hives)
- GitHub Actions CI (`.github/workflows/ci.yml`): build + test on windows-latest

### Phase 1 — Hygiene & Correctness
- **Manifest bug fixed**: `Get-RegistryHiveOnDemand` actually exported now (was listed as the
  non-existent `Read-RegistryHiveOnDemand`); phantom `Install-WindowsUpdateFile` removed
- All GC.Collect/Thread.Sleep handle hacks removed — verified `RegistryHiveOnDemand` holds no
  file handles (parses into memory)
- Identity aligned (sln renamed, ProjectUri, README = GPL-3.0); stale Windows-Image-Database doc
  references purged; unused System.Text.Json removed; debug scaffolding removed

### Phase 2 — Architecture Refactor
- **`ModuleCallbacks`** infrastructure (src/Services/ModuleCallbacks.cs): verbose/warning/error/
  progress callbacks; services no longer need PSCmdlet (new services are cmdlet-free + testable)
- **DISM consolidated 2→1**: `WindowsImageService` (src/Services/WindowsImageService.cs, interface
  `Abstractions/IWindowsImageService.cs`) — managed DISM queries + native mount/unmount with
  progress + export; single Initialize/Shutdown; mount/unmount THROW with real DISM errors
  (bool returns eliminated); DismService/NativeDismService deleted
- **Registry consolidated 6→2+parser**: `RegistryHiveReader` (interface
  `Abstractions/IRegistryHiveReader.cs`) for reads via RegistryHiveOnDemand with typed access
  (no reflection); RegistryPackageService + OfflineRegistryService (dead) deleted;
  RegistryApplicationService + NativeRegistryService remain as the write path

### Phase 3 — Finish Half-Built Features
- **The "HONEST ASSESSMENT" stubs were wrong**: ManagedDism 3.3.12 has the complete API. Implemented
  the full write set on WindowsImageService: AddPackage, RemovePackageByName, Enable/DisableFeature,
  Add/RemoveCapability, GetProvisionedAppxPackages/RemoveProvisionedAppxPackage, AddDriversFromDirectory
- **New cmdlets (12)**: `Get-WindowsImagePackageList`, `Get-WindowsImageFeatureList`,
  `Add-WindowsImagePackage`, `Enable-WindowsImageFeature`, `Disable-WindowsImageFeature`,
  `Add-WindowsImageCapability`, `Remove-WindowsImageCapability`,
  `New-WindowsImageRecipe`, `Test-WindowsImageRecipe`, `Invoke-WindowsImageRecipe`,
  `Export-WindowsImage`, `New-WindowsImageISO`
- **BuildRecipe executor** (src/Services/RecipeService.cs): loads JSON recipes, validates structure,
  selects images by regex, mounts read-write, applies 8 section types in deterministic order, saves.
  Registry modifications reuse the proven .reg application path
- **WimExportService TODOs finished**: index-by-name lookup, image count, boot flag
  (WIMSetBootImage), destination name/description (new WIMSetImageName/WIMSetImageDescription P/Invoke)
- **ISO support**: `Get-WindowsImageList -ImagePath x.iso` now mounts the ISO (Mount-DiskImage),
  locates install.wim/install.esd, keeps the ISO mounted for servicing; `New-WindowsImageISO`
  exposes ISOService (oscdimg path via installed ADK)

### Phase 4 — New Capabilities
- **`Get-MountedWindowsImage`**: cross-session mount registry (JSON state in %TEMP%\PSWindowsImageTools\
  mounts.json); Mount/Dismount/Get-WindowsImageList-SkipDismount auto-register/unregister; `-Prune` cleans stale entries
- **`Update-WindowsImageOnline`**: one-liner servicing — auto-discovers latest KB from release
  history, searches/downloads from the Update Catalog, installs into selected images. Supports
  pre-downloaded `-UpdatePackages` and explicit `-Query` modes
- **Image diffing**: `Get-WindowsImageSnapshot` (packages/features/capabilities/AppX/software,
  JSON export) + `Compare-WindowsImage` (two mounted images or two snapshot files →
  added/removed/changed per category)

## Module Totals
- 88 exported cmdlets · 695 unit tests passing · build clean (0 warnings)

## Known Remaining Tech Debt
- Remaining PSCmdlet-coupled services (catalog, ADK, wallpaper, unattend, autopilot) accept
  `PSCmdlet?` nullable params and only use them for null-guarded logging — safe as-is. The three
  services that previously REQUIRED non-null cmdlet (RegistryOperationService,
  RegistryApplicationService, INFDriverService) now have ModuleCallbacks overloads and no longer
  force `null!` in modern call paths
- PlatyPS help: regenerate via `Scripts/build-help.ps1` + `Scripts/build-help-examples.ps1`,
  then `New-ExternalHelp` into `Module\PSWindowsImageTools\en-US`
- Module bin refresh requires no other PowerShell session holding the DLLs (rename-swap used during dev)
