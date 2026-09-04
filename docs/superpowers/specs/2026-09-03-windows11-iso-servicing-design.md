# Windows 11 ISO Servicing Pipeline — Design

**Date:** 2026-09-03
**Status:** Approved for planning

## Problem

The module already has strong primitives for editing mounted WIM images
(`Get-WindowsImageList`, `Mount-WindowsImageList`, `Dismount-WindowsImageList`,
driver/update/wallpaper/registry cmdlets, etc.), but there is no end-to-end path
from "the official Windows 11 ISO" to "a serviced Windows 11 ISO":

- `Get-WindowsImageList` explicitly throws `NotImplementedException` when given
  a `.iso` path (`GetWindowsImageListCmdlet.GetImageFilePath`) — ISO support was
  stubbed out, never finished.
- There is no cmdlet to fetch the latest official Windows 11 ISO.
- There is no cmdlet to extract an ISO's contents to a working folder.
- `winre.wim` is not a loose file on the media — it lives nested inside
  `install.wim` at `Windows\System32\Recovery\Winre.wim` per image index — and
  nothing in the module extracts, mounts, or re-embeds it.
- `ISOService.CreateBootableISO` (in `src/Services/ISOService.cs`) is fully
  implemented (prefers ADK's `oscdimg.exe`, falls back to `mkisofs`) but is
  never called from any cmdlet — it's dead code today.

This spec closes all four gaps so a user can go from "latest official Windows 11
release" to "customized bootable ISO" using only this module's cmdlets.

## Goals

1. Fetch the latest official Windows 11 ISO download URL and download it.
2. Extract ISO contents to a working folder that feeds the existing
   `Get-WindowsImageList` → `Mount-WindowsImageList` pipeline.
3. Make editing `winre.wim` (nested in `install.wim`) transparent: mounting
   `install.wim` read-write automatically mounts its embedded WinRE too;
   dismounting with `-Save` automatically re-embeds it.
4. Rebuild a bootable ISO from a serviced media folder, by finally wiring up
   the existing `ISOService`.

## Non-goals

- A raw PowerShell/IMAPI2 ISO-creation fallback (the existing stub in
  `ISOService.TryCreateISOWithPowerShell`) — out of scope; `oscdimg`/`mkisofs`
  are required, with `Install-ADK` as the documented way to get `oscdimg`.
- Editions/architectures beyond what Microsoft's own download page already
  supports (this design exposes the same choices, not new ones).
- Guaranteeing indefinite stability of Microsoft's undocumented download-page
  flow — see Risks below.

## Architecture

Four additions, all following the module's existing service + cmdlet + model
conventions (a `Services/*Service.cs` doing the work, a thin `Cmdlets/*Cmdlet.cs`
wrapping it in PowerShell semantics, `Models/*.cs` for any new output types).

### 1. ISO discovery & download

New `WindowsISODownloadService`, mirroring the existing Windows Update Catalog
pattern (`Search-WindowsUpdateCatalog` → `Get-WindowsUpdateDownloadUrl` →
`Save-WindowsUpdateCatalogResult`):

- **`Get-WindowsISODownloadInfo -Edition "Windows 11" [-Language "English International"] [-Architecture x64|arm64]`**
  Negotiates Microsoft's public consumer-download-page session flow (the same
  unauthenticated endpoints a browser uses when a person downloads Windows 11
  manually) to resolve a signed, time-limited direct ISO URL for the current
  latest official release. Returns an object with `Url`, `FileName`, `Edition`,
  `Architecture`, `Language`, `ExpiresAt`.

- **`Save-WindowsISO -DestinationPath <file> [-Url <uri>]`**
  Downloads with resume support, following the same pattern as
  `Save-WindowsUpdateCatalogResult`. Accepts the object from
  `Get-WindowsISODownloadInfo` via the pipeline, or a manually supplied `-Url`
  as a bypass if the automated discovery flow ever breaks.

### 2. ISO extraction

New `WindowsISOExtractionService` + **`Export-WindowsISO -IsoPath <file> -DestinationPath <dir>`**:

- Uses Windows-native `Mount-DiskImage` / `Dismount-DiskImage` (Storage module,
  built into Windows 10/11 — no bundled third-party tool required) to mount the
  ISO, copies the full tree to `DestinationPath`, then dismounts the ISO image
  in a `finally` block so it is never left mounted after a failure.
- Returns a new `WindowsInstallationMedia` model with resolved paths (`Root`,
  `InstallWim`, `BootWim`, and any other top-level files/folders needed later
  for rebuild) that feed directly into the existing `Get-WindowsImageList`.
- `GetWindowsImageListCmdlet.GetImageFilePath`'s existing `NotImplementedException`
  message is updated to point users at `Export-WindowsISO` — DISM cannot commit
  changes back into a WIM sitting on a read-only mounted ISO, so extraction to
  a real writable file is required regardless of how the ISO is opened.

### 3. WinRE auto-handling

Extends the existing mount/dismount cmdlets and model, no new cmdlets:

- `MountedWindowsImage` (`src/Models/MountedWindowsImage.cs`) gains a nullable
  `WinRE` property of its own type (`MountedWindowsImage? WinRE`).
- In `MountWindowsImageListCmdlet.MountSingleImage`, after the parent image
  mounts successfully, check for `Windows\System32\Recovery\Winre.wim` inside
  the mount path:
  - If present, copy it out to a temp location under the same mount root, and
    mount it via `NativeDismService` exactly like the parent image is mounted
    (read-write if the parent was mounted read-write, read-only otherwise).
    Populate `.WinRE` with the resulting `MountedWindowsImage`.
  - If absent (some editions/SKUs don't carry one), `.WinRE` stays `null` —
    this is not an error or a warning-worthy condition.
- In `DismountWindowsImageListCmdlet` (with `-Save`): if `.WinRE` is populated
  and not read-only, dismount/commit it *first*, copy the resulting
  `winre.wim` back into the parent image at the same nested path, *then*
  dismount/commit the parent. If `-Save` is not specified (discard), the
  WinRE mount is discarded the same way the parent is.
- If the parent was mounted read-only, `.WinRE` mounts read-only too, for
  inspection only — no write-back is attempted.

### 4. ISO rebuild

**`New-WindowsISO -SourcePath <dir> -DestinationPath <file> [-VolumeLabel <string>] [-BootMode UEFI|BIOS|Both]`**

- Thin cmdlet wrapper around the existing, already-correct
  `ISOService.CreateBootableISO` (`src/Services/ISOService.cs`) — no changes
  needed to that service itself. It already prefers ADK's `oscdimg.exe` (which
  this module can already install via `Install-ADK -IncludeDeploymentTools`)
  and falls back to `mkisofs`.
- If neither tool is found, the cmdlet throws a clear terminating error
  pointing at `Install-ADK -IncludeDeploymentTools` rather than silently
  falling through to the unfinished PowerShell/IMAPI2 stub.

## End-to-end data flow

```powershell
Get-WindowsISODownloadInfo -Edition "Windows 11" -Architecture x64 |
    Save-WindowsISO -DestinationPath C:\ISO\Win11.iso |
    Export-WindowsISO -DestinationPath C:\Media\Win11

$images  = Get-WindowsImageList -ImagePath C:\Media\Win11\sources\install.wim
$mounted = $images | Mount-WindowsImageList -MountPath C:\Mount -ReadWrite
# $mounted.WinRE is already mounted -- edit files under $mounted.MountPath and $mounted.WinRE.MountPath
$mounted | Dismount-WindowsImageList -Save     # re-embeds WinRE, commits install.wim

$boot = Get-WindowsImageList -ImagePath C:\Media\Win11\sources\boot.wim |
    Mount-WindowsImageList -MountPath C:\MountBoot -ReadWrite
$boot | Dismount-WindowsImageList -Save

New-WindowsISO -SourcePath C:\Media\Win11 -DestinationPath C:\ISO\Win11-serviced.iso
```

## Error handling

- **Download flow**: throws a clear terminating error if Microsoft's endpoint
  shape or edition lookup fails to resolve; `-Url` on `Save-WindowsISO` is the
  documented escape hatch when the automated flow is broken.
- **Extraction**: verifies the ISO mounted successfully before copying;
  dismounts the ISO disk image in a `finally` block even if the copy fails,
  so a failed run never leaves a stray mounted ISO.
- **WinRE**: a missing embedded `winre.wim` is not an error — `.WinRE` is
  simply `null`.
- **Rebuild**: pre-flight check for `oscdimg`/`mkisofs` availability, with an
  actionable error message rather than silently degrading to the unfinished
  PowerShell fallback.

## Risks

- Microsoft's consumer download page is an undocumented, unversioned flow that
  can change shape at any time (this is the same territory open-source tools
  like Fido operate in; we implement the equivalent flow independently rather
  than depend on or copy any GPL-licensed code). The `-Url` bypass on
  `Save-WindowsISO` exists specifically so this feature degrades gracefully
  (manual URL) rather than becoming a hard blocker when Microsoft changes
  something.
- `oscdimg`/`mkisofs` must be present for `New-WindowsISO` to succeed; this is
  a real external dependency, mitigated by the module's existing `Install-ADK`
  cmdlet and a clear error message when neither tool is found.

## Testing

The download endpoint and real DISM mounts need administrator rights and real
Windows image files, so this isn't unit-testable in the traditional sense.
Verification is a manual end-to-end run against a real Windows 11 ISO:
extract → get image list → mount → make an edit → dismount with `-Save` →
rebuild → re-extract the rebuilt ISO → confirm the edit persisted in both
`install.wim` and, where applicable, the re-embedded `winre.wim`.
