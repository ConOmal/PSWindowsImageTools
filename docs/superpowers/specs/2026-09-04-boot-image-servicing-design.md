# Boot Image Servicing — Design

**Date:** 2026-09-04
**Status:** Approved for planning

## Problem

`boot.wim` (the WinPE-based Setup/PE image on installation media, index 2 of
`sources\boot.wim`) is a normal serviceable WIM — it can already be mounted
via the module's generic `Get-WindowsImageList`/`Mount-WindowsImageList`,
and `WindowsInstallationMedia.BootWim` (existing model, from the ISO
servicing pipeline) already locates it on extracted media. There is no
boot.wim-specific convenience layer: a caller wanting to inject a driver or
run component cleanup against WinPE has to know it's `boot.wim` and use the
generic cmdlets by hand. This spec adds that convenience layer — the same
kind of thin, purpose-named wrapper `Optimize-WindowsImageComponentStore`
already is over generic DISM operations.

## Goals

1. `Get-WindowsBootImage` — locate and report on `boot.wim` given an
   extracted media root or an explicit path, reusing
   `WindowsInstallationMedia.BootWim`.
2. `Add-WindowsBootDriver` — inject drivers into a mounted boot.wim,
   reusing the existing `IWindowsImageService.AddDriversFromDirectory`
   (confirmed present, used by INF driver injection elsewhere in the
   module) — no new DISM surface.
3. `Optimize-WindowsBootImage` — run component cleanup against a mounted
   boot.wim, reusing `ComponentStoreService.Cleanup` (from the Phase 1
   Component Store subsystem) unchanged — boot.wim is serviced through the
   same DISM component-store mechanism as any other WIM.

## Non-goals

- **No new mount/export logic.** `Get-WindowsBootImage` returns a path +
  metadata; mounting is the existing `Mount-WindowsImageList` (the caller
  pipes `Get-WindowsBootImage`'s output's `.Path` into the existing
  `Get-WindowsImageList -ImagePath` → `Mount-WindowsImageList` chain,
  exactly like any other WIM). This avoids duplicating the mount lifecycle
  the module already owns.
- **No WinPE optional-component wrapping.** `Add-WinPEOptionalComponent`/
  `Get-WinPEOptionalComponent` already exist and already operate on a
  mounted WinPE image — this spec doesn't re-wrap them.
- **No boot.wim creation from scratch.** Only servicing an existing
  boot.wim extracted from media (or produced by
  `ESDConversionService`/`Export-WindowsISO`, both existing) is in scope.

## Architecture

- **Model** — `src/Models/BootImageModels.cs`: `BootImageInfo { Path:
  FileInfo, SourceMediaRoot: string?, ImageCount: int, Images:
  List<WindowsImageInfo> }` (reuses the existing `WindowsImageInfo` model
  from `Get-WindowsImageList`, no new per-image type needed).
- **Service** — `src/Services/BootImageService.cs`:
  `Locate(DirectoryInfo mediaRoot) -> BootImageInfo?` (thin wrapper over
  `WindowsInstallationMedia.FromRoot` — reuse, don't reimplement — plus
  `imageService.GetImageInfo(bootWimPath)` for `Images`).
  `AddDriver(MountedWindowsImage mountedImage, IWindowsImageService
  imageService, DirectoryInfo driverDirectory, bool forceUnsigned) -> void`
  — direct pass-through to `imageService.AddDriversFromDirectory`.
  `Optimize(MountedWindowsImage, IWindowsImageService, PSCmdlet) ->
  ComponentStoreCleanupResult` — direct pass-through to
  `new ComponentStoreService(...).Cleanup(...)`, `resetBase` hardcoded
  `false` (ResetBase on a boot/PE image is not a meaningful operation —
  WinPE has no update history to reset).
- **Cmdlets** — `src/Cmdlets/BootImageCmdlets.cs`: `Get-WindowsBootImage`
  (read-only, takes `-MediaRoot DirectoryInfo` or `-Path FileInfo`
  directly), `Add-WindowsBootDriver` (mutating, `SupportsShouldProcess`,
  pipeline of `MountedWindowsImage[]` + `-DriverPath DirectoryInfo`,
  matching the existing driver-injection cmdlets' parameter shape),
  `Optimize-WindowsBootImage` (mutating, `SupportsShouldProcess`, pipeline
  of `MountedWindowsImage[]`, same shape as
  `Optimize-WindowsImageComponentStore`).

## Data Flow

```
Get-WindowsBootImage -MediaRoot <extracted ISO root>  ──►  BootImageInfo (.Path = boot.wim)
        │
        ▼
Get-WindowsImageList -ImagePath <BootImageInfo.Path> | Mount-WindowsImageList -ReadWrite
        │
        ├─► Add-WindowsBootDriver -DriverPath <dir>       ──► mutates mount, no new report type
        └─► Optimize-WindowsBootImage                      ──► ComponentStoreCleanupResult (reused type)
        │
        ▼
Dismount-WindowsImageList -Save
```

## Error Handling

Matches established convention: `SupportsShouldProcess` + per-image
`ShouldProcess` gate on both mutating cmdlets; `-ContinueOnError` on
multi-image pipelines; `Get-WindowsBootImage` returns `$null`/emits a
warning (not a terminating error) when no `boot.wim` is found under the
given media root, since "no boot.wim here" is a normal, expected outcome
for some media layouts, not a failure.

## Testing

- **Unit (xUnit)**: `BootImageService.Locate` against a temp-directory
  fixture with/without a `sources\boot.wim` present (pure filesystem
  logic, no DISM). `AddDriver`/`Optimize` are thin pass-throughs to
  already-tested services (`AddDriversFromDirectory` has no direct test
  today, matching how driver injection elsewhere in this module is
  untested — DISM-facing; `ComponentStoreService.Cleanup` is already
  tested from Phase 1) — no new unit tests needed for the pass-through
  bodies themselves, only for `Locate`.
- **Integration (Pester)**: `Get-WindowsBootImage` against a synthetic
  media root fixture (create a temp dir with `sources\boot.wim` copied
  from the existing integration suite's baseline WIM, reusing it as a
  stand-in). `Add-WindowsBootDriver`/`Optimize-WindowsBootImage` against a
  mounted copy, matching the existing integration test conventions.

## Risks

- `ResetBase` being hardcoded off for boot images is a design opinion, not
  a DISM limitation — worth a one-line doc-comment so a future maintainer
  understands it's deliberate, not an oversight.
