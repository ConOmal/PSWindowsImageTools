# App Provisioning (DISM AppX + WinGet Configuration Export) — Design

**Date:** 2026-09-04
**Status:** Approved for planning

## Problem

The module can already remove provisioned AppX packages
(`RemoveAppXProvisionedPackageListCmdlet.cs`, via
`IWindowsImageService.RemoveProvisionedAppxPackage`) and read them
(`GetProvisionedAppxPackages`, used internally by `Get-WindowsImageSnapshot`).
There is no way to (a) list them as a first-class cmdlet, (b) add a new one,
or (c) describe desired app state for first-boot application, since WinGet
itself cannot target an offline mounted image — it only operates against a
running system.

## Goals

1. `Get-WindowsImageProvisionedApp` — first-class listing cmdlet (today
   this data is only reachable indirectly via `Get-WindowsImageSnapshot`).
2. `Add-WindowsImageProvisionedApp` — provision a new AppX package into a
   mounted image via `DismApi.AddProvisionedAppxPackage` (confirmed real
   API this session via reflection: `AddProvisionedAppxPackage(DismSession,
   string appPath, List<string> dependencyPackages, string licensePath,
   string customDataPath)`), completing the Get/Add/Remove set alongside
   the existing `Remove-AppXProvisionedPackageList`.
3. `Export-WindowsImageWinGetConfiguration` — since WinGet can't run
   against an offline image, this generates a WinGet Configuration (DSC
   v3) YAML file describing desired package state, plus a scheduled task
   definition that applies it via `winget configure` on first boot.

## Non-goals

- **No `winget configure` execution against the image.** WinGet requires a
  running Windows environment with network access and the WinGet client
  installed — neither is true of an offline mounted WIM. This subsystem
  only *authors* the configuration artifact; applying it is a first-boot
  concern.
- **No scheduled-task *registration* cmdlet.** The other concurrently
  active session owns OOBE/first-boot cmdlet work
  (`Set-WindowsImageOOBE`/first-logon tasks per the earlier roadmap
  discussion). This subsystem writes a `.xml` Scheduled Task definition
  file and a README-style note on where it goes
  (`%WINDIR%\Setup\Scripts\` is the conventional first-logon location this
  module already uses for `SetupCompleteActionCmdlet`), but does NOT
  create/modify `src/Cmdlets/*OOBE*` or `*FirstLogon*` files — avoiding
  the exact collision this session already hit once. If the other
  session's OOBE work lands first, wiring the two together is a follow-up,
  not blocking this spec.
- **No AppX package *building*.** This provisions an existing `.appx`/
  `.appxbundle`/`.msix` file; authoring one is out of scope, same as
  `Add-WindowsImagePackage` doesn't build `.cab` files.
- **No dependency/license file resolution.** `Add-WindowsImageProvisionedApp`
  takes explicit `-DependencyPackagePath`/`-LicensePath` parameters,
  matching the real DISM API's own explicit-path requirement — no
  auto-discovery of a package's dependencies.

## Architecture

- **Model** — `src/Models/AppProvisioningModels.cs`:
  `ProvisionedAppInfo { PackageName, DisplayName, Publisher, Version,
  InstallLocation }` — reuses fields already read from `DismAppxPackage`
  elsewhere (`ImageComparisonService.CaptureSnapshot`'s AppX capture), now
  surfaced as a first-class type instead of the generic `SnapshotItem`.
  `WinGetConfigurationEntry { PackageIdentifier, Version?, Source }` — one
  desired-package entry. `WinGetConfigurationExportResult { ConfigPath:
  FileInfo, ScheduledTaskPath: FileInfo, Packages: List<WinGetConfigurationEntry> }`.
- **Service** — `src/Services/AppProvisioningService.cs`:
  `GetProvisionedApps(MountedWindowsImage, IWindowsImageService) ->
  List<ProvisionedAppInfo>` (maps `imageService.GetProvisionedAppxPackages`
  — existing, unchanged).
  `AddProvisionedApp(MountedWindowsImage, IWindowsImageService,
  FileInfo appPackagePath, List<FileInfo>? dependencyPackages, FileInfo?
  licensePath) -> void` — requires adding
  `IWindowsImageService.AddProvisionedAppxPackage(string mountPath, string
  appPath, List<string> dependencyPackages, string? licensePath, string?
  customDataPath)` to the existing interface (mirrors how Phase 1 added
  `GetDrivers`/`RemoveDriver` to this same interface) — wraps the confirmed
  real `DismApi.AddProvisionedAppxPackage` overload.
  `ExportWinGetConfiguration(List<WinGetConfigurationEntry> packages,
  DirectoryInfo destination) -> WinGetConfigurationExportResult` — pure
  string-templating (YAML + scheduled-task XML), no DISM/image access at
  all; takes the desired package list as a plain parameter rather than
  deriving it from the image, since "desired state for first boot" is
  inherently a caller-specified list, not something read off the offline
  image itself.
- **Cmdlets** — `src/Cmdlets/AppProvisioningCmdlets.cs`:
  `Get-WindowsImageProvisionedApp` (read-only), `Add-WindowsImageProvisionedApp`
  (mutating, `SupportsShouldProcess`), `Export-WindowsImageWinGetConfiguration`
  (read-only from the image's perspective — it doesn't touch the mount at
  all, pure file generation; takes `-Package` objects/hashtables via
  pipeline or parameter, `-DestinationPath`).

## WinGet Configuration format (grounded, not guessed)

DSC v3 / WinGet Configuration YAML has a stable, documented schema
(`# yaml-language-server: $schema=https://aka.ms/configuration-dsc-schema/0.2`,
top-level `properties.resources[]` with `resource:
Microsoft.WinGet.DSC/WinGetPackage`, `directives`, `settings.id`). The
implementation task will generate exactly this documented shape — this
spec does not invent a new format.

## Data Flow

```
Get-WindowsImageProvisionedApp ──► ProvisionedAppInfo[]
Add-WindowsImageProvisionedApp -PackagePath <appx> ──► mutates mount, no report object
Export-WindowsImageWinGetConfiguration -Package @(...) -DestinationPath <dir>
        ──► WinGetConfigurationExportResult (.yaml + a Scheduled Task .xml, both written to disk)
```

## Error Handling

`SupportsShouldProcess` on `Add-WindowsImageProvisionedApp`;
`-ContinueOnError` on both mount-touching cmdlets, matching established
convention. `Export-WindowsImageWinGetConfiguration` doesn't touch DISM at
all so has no `-ContinueOnError` need — file-write failures surface as a
normal terminating `ErrorRecord` (matches `Export-WindowsImageSBOM`'s
`-DestinationPath` unwritable-path behavior).

## Testing

- **Unit (xUnit)**: `ExportWinGetConfiguration`'s YAML/XML templating is
  pure — fully testable against known input, asserting the generated YAML
  parses back with the expected package list and the scheduled-task XML
  is well-formed. `GetProvisionedApps`'s mapping has no unit test (DISM
  types, same constraint as every other Phase 1 mapping method).
- **Integration (Pester)**: `Get-WindowsImageProvisionedApp` against a
  mounted image (structural pass/fail, matching the existing "runs without
  error" pattern for cmdlets whose synthetic baseline image won't have
  interesting provisioned apps). `Export-WindowsImageWinGetConfiguration`
  needs no mount at all — a plain unit-level Pester case suffices, no
  `Describe ... -Tag Integration` needed for it specifically.

## Risks

- The WinGet Configuration file this produces is never actually validated
  against a real `winget configure` run in this session (no live Windows
  Setup/OOBE environment available to test first-boot application) — the
  YAML schema conformance is the best available substitute for that,
  documented as a risk rather than silently assumed correct.
