# Image Checkpoint / Rollback — Design

**Date:** 2026-09-04
**Status:** Approved for planning

## Problem

Servicing a mounted image is a sequence of destructive, in-place edits
(add packages, remove drivers, apply registry changes) with no way to
undo one step short of discarding the entire mount
(`Dismount-WindowsImageList -Discard`) and starting over from source. This
spec adds a lightweight checkpoint/restore mechanism scoped to a single
mount directory, so a caller can snapshot before a risky operation and
revert just that operation without a full remount.

## Goals

1. `Checkpoint-WindowsImage` — snapshot a mounted image's current on-disk
   state to a named checkpoint.
2. `Restore-WindowsImageCheckpoint` — revert the mount directory to a
   previously taken checkpoint.
3. `Get-WindowsImageCheckpoint` — list checkpoints taken for a given mount.

## Non-goals

- **No VSS (Volume Shadow Copy).** VSS shadow copies operate at the volume
  level and require restoring an entire volume snapshot, not a single
  subdirectory — a poor fit for "checkpoint one mount directory." (The
  other concurrently active session used VSS for a different purpose —
  capturing a whole-volume baseline for its own CI fixture — which is a
  legitimately different use case, not something this spec should copy.)
  This subsystem uses a plain recursive directory mirror instead: simpler,
  portable, no admin-only volume APIs, and scoped to exactly the mount
  directory.
- **No incremental/differential checkpoints.** Each checkpoint is a full
  copy of the mount directory at that point in time. Windows image mount
  directories are typically tens of GB; this spec accepts that cost
  explicitly (see Risks) rather than building an incremental-diff engine,
  which is a substantially larger undertaking out of proportion to this
  feature's scope.
- **No automatic checkpointing before every mutating cmdlet.** Callers
  opt in explicitly by calling `Checkpoint-WindowsImage` — this spec does
  not hook into `Optimize-WindowsImageComponentStore` or any other
  existing cmdlet to auto-checkpoint.
- **No cross-machine/cloud checkpoint storage.** Checkpoints live in a
  local directory (default: alongside the mount, matching this module's
  existing "artifacts live near the source" convention seen in
  `MountSessionService`'s `%TEMP%\PSWindowsImageTools\` location).

## Architecture

- **Model** — `src/Models/ImageCheckpointModels.cs`:
  `ImageCheckpointInfo { CheckpointId: string (GUID), MountId: string
  (ties back to MountedWindowsImage.MountId, existing field), Label:
  string?, CreatedAt: DateTime, SizeBytes: long, CheckpointPath:
  DirectoryInfo }`.
- **Service** — `src/Services/ImageCheckpointService.cs`, persisting a
  JSON index following the exact pattern `MountSessionService` already
  established (flat DTO list in `%TEMP%\PSWindowsImageTools\checkpoints.json`,
  since `ImageCheckpointInfo` holding a `DirectoryInfo` has the same
  Newtonsoft-can't-serialize-`DirectoryInfo` problem `MountSessionService`
  already solved once — reuse that solution, don't re-derive it):
  `Create(MountedWindowsImage, string? label) -> ImageCheckpointInfo` —
  recursive file copy (`Directory` walk + `File.Copy`, NOT
  `robocopy.exe` — keeps this dependency-free, matching how `Export-WindowsImageDriver`
  already does its own recursive copy rather than shelling out) from the
  mount path to a new `<TEMP>\PSWindowsImageTools\checkpoints\<CheckpointId>\`
  directory; records the entry in the JSON index.
  `Restore(ImageCheckpointInfo, MountedWindowsImage) -> void` — deletes
  the current mount directory's contents and recursively copies the
  checkpoint directory back over it. **Requires the image to be mounted
  read-write** (checked before starting) since restoring is inherently a
  mutation.
  `List(string? mountId) -> List<ImageCheckpointInfo>` — reads the JSON
  index, optionally filtered to one mount.
  `Delete(ImageCheckpointInfo)` — removes the checkpoint directory and its
  index entry (needed so checkpoints don't accumulate unboundedly across
  a long servicing session — exposed as `-RemoveAfterRestore` on
  `Restore-WindowsImageCheckpoint`, not a separate cmdlet, since standalone
  deletion without restoring is a rare enough case not to warrant its own
  top-level cmdlet per YAGNI).
- **Cmdlets** — `src/Cmdlets/ImageCheckpointCmdlets.cs`:
  `Checkpoint-WindowsImage` (`VerbsData.Checkpoint` — confirmed a real
  approved PowerShell verb via `Get-Verb`), `-Label` optional string,
  outputs `ImageCheckpointInfo`. `Restore-WindowsImageCheckpoint`
  (mutating, `SupportsShouldProcess`), pipeline of `ImageCheckpointInfo[]`
  (from `Get-WindowsImageCheckpoint` or `Checkpoint-WindowsImage`'s own
  output) plus the `MountedWindowsImage` to restore into, `-RemoveAfterRestore`
  switch. `Get-WindowsImageCheckpoint`, `-MountedImage` optional filter
  parameter.

## Data Flow

```
Mount-WindowsImageList -ReadWrite
        │
        ▼
Checkpoint-WindowsImage -Label "before-driver-update" ──► ImageCheckpointInfo
        │
        ▼
[ risky servicing operation, e.g. Remove-WindowsImageDriver ]
        │
        ▼ (if it went wrong)
Get-WindowsImageCheckpoint | Restore-WindowsImageCheckpoint
        │
        ▼
[ mount directory back to pre-operation state ]
```

## Error Handling

`Restore` verifies the target `MountedWindowsImage.Status == Mounted` and
the mount was opened read-write before proceeding — a read-only mount or
an already-dismounted target throws a clear terminating error rather than
silently no-op'ing or corrupting a read-only mount. `SupportsShouldProcess`
on `Restore-WindowsImageCheckpoint` given it discards current mount state.
`Checkpoint-WindowsImage`/`Get-WindowsImageCheckpoint` are non-mutating;
standard `-ContinueOnError` on any multi-item pipeline path.

## Testing

- **Unit (xUnit)**: `Create`/`Restore`/`List`/`Delete` against real
  temp-directory fixtures (no DISM/mount involved — this subsystem only
  ever touches plain directories once it has a `MountPath`, matching the
  `ImageComparisonServiceTests`/`WindowsImageDriverServiceTests` pattern
  of real filesystem I/O in temp dirs). Covers: checkpoint captures file
  content correctly, restore reverts a modified/deleted/added file back to
  checkpoint state, restoring onto a non-writable target throws, JSON
  index round-trips through save/load (mirroring
  `MountSessionServiceTests`, if that file exists — check during
  implementation and match its pattern if so).
- **Integration (Pester)**: full round-trip against a real mount — mount
  read-write, modify a file, checkpoint, modify again, restore, assert the
  file matches the checkpoint's content not the latest edit.

## Risks

- **Disk space**: a full-copy checkpoint of a multi-GB mount directory
  doubles (or more, with multiple checkpoints) the space consumed during a
  servicing session. `Get-WindowsImageCheckpoint`'s `SizeBytes` field and
  `-RemoveAfterRestore` are the mitigation this spec provides; a caller
  who checkpoints repeatedly without cleanup is responsible for the disk
  cost, same as any snapshot mechanism.
- **Time cost**: a full recursive copy of a Windows image mount (which can
  include hundreds of thousands of small files under `WinSxS`) is
  materially slower than a metadata-only mechanism would be — explicitly
  accepted per Non-goals rather than building the more complex
  incremental alternative.
