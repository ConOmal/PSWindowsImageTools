# Reserved Storage — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** backlog phase after Phase 1 (component store, drivers,
inventory/SBOM, validation), which declared "Reserved storage" a non-goal.

## Problem

Windows Reserved Storage sets aside disk space on the system volume so Windows
Update and other servicing operations can apply updates even when free space is
low. For golden-image engineering it matters in both directions:

- **Enabled** by default on most client installs, it reserves roughly 7 GB per
  image — silent size cost that image builders frequently want to reclaim for
  compact image delivery.
- **Disabled** (or removed) on an image that later needs servicing, it can make
  feature updates fail on low-disk devices.

The module has no way to read or change this state in an offline image. The
DISM CLI exposes exactly two verbs — `dism /Image:<path> /Get-ReservedStorageState`
(state only) and `dism /Image:<path> /Set-ReservedStorageState:Enabled|Disabled`
(mutating) — but the bundled `Microsoft.Dism.dll` (3.3.12) managed wrapper has
no equivalent API surface (verified by reflection: no
`GetReservedStorageState`/`SetReservedStorageState` member exists), so the
module currently cannot service this setting at all.

## Goals

1. Report reserved-storage state (`Enabled`/`Disabled`) for a mounted image via
   a new `Get-WindowsImageReservedStorage` cmdlet, taking the same image-dir
   path DISM's `/Image:` switch consumes.
2. Enable/disable reserved storage in a mounted image via a new
   `Set-WindowsImageReservedStorage` cmdlet with mutually exclusive `-Enable` /
   `-Disable` switches, honoring `SupportsShouldProcess` / `ShouldProcess`.
3. Surface any size information DISM reports alongside the state (the current
   CLI reports state only; the model carries an optional size so a future DISM
   size line is surfaced instead of dropped).
4. Keep all pure logic — argument building, output parsing, state mapping,
   error-text extraction — `internal static` and unit-tested with no DISM
   dependency, mirroring the `ComponentStoreService` / `RegistryDriftService`
   split. The dism.exe invocation stays a thin shell.
5. Follow repo conventions exactly: `Services/ReservedStorageService.cs`,
   `Cmdlets/ReservedStorageCmdlets.cs`, `Models/ReservedStorageModels.cs`,
   `LoggingService`/`ModuleCallbacks`/`ProcessMonitoringService`-style logging,
   PlatyPS help under `docs/help/`.

## Non-goals

- **Online (running) images.** The cmdlets target offline mounted images only
  (`/Image:`), matching every other mounted-image cmdlet in the module.
- **Managed DISM API.** `Microsoft.Dism.dll` 3.3.12 exposes no reserved-storage
  member, so the implementation shells out to `dism.exe` — the same fallback
  path `ComponentStoreService.Cleanup` already uses for
  `/Cleanup-Image /StartComponentCleanup`. Even if a future wrapper added the
  API, the local host's `OpenOfflineSession` servicing limitation (documented
  in `OpenCode-EngLog.md`) means the CLI is the reliable path today.
- **Size guarantees or measurement.** DISM's `Get-ReservedStorageState` reports
  `Enabled`/`Disabled` only; the module does not compute actual on-disk
  reserved usage (that requires `ReserveManager` registry/Settings telemetry
  out of scope here).
- **Drive-freeing, cleanup, or sizing tools.** No `/Cleanup-Image` extensions,
  no reserved-shrink heuristics.
- **Multiple images per call / pipeline objects.** The DISM verbs are
  single-image; both cmdlets take one `-ImagePath`.

## Design

### New files

**`src/Models/ReservedStorageModels.cs`**

- `enum ReservedStorageState { Enabled, Disabled }` — the two DISM states.
- `WindowsImageReservedStorage` — `ImagePath` (mounted image directory),
  `State` (`ReservedStorageState`), computed `StateText` (`State.ToString()`),
  `SizeBytes` (`long?`, null when DISM reports no size), and computed
  `SizeMB`. `ToString()` returns `"{State} at {ImagePath}"`.
- `ReservedStorageOperationResult` — `ImagePath`, `Operation`
  (`EnableReservedStorage` / `DisableReservedStorage`), `RequestedState`,
  `Success`, `ExitCode`, `ErrorMessage` (`string?`), mirroring
  `ImageOperationResult`/`ComponentStoreCleanupResult` shape.

**`src/Services/ReservedStorageService.cs`** (`_callbacks`, `ModuleCallbacks`-
aware, mirroring `ComponentStoreService`)

- `private const string ServiceName = "ReservedStorageService"`.
- Pure, unit-tested `internal static` members:
  - `BuildGetReservedStorageStateArguments(string imagePath)` →
    `/Image:"<path>" /Get-ReservedStorageState`.
  - `BuildSetReservedStorageStateArguments(string imagePath, bool enable)` →
    `/Image:"<path>" /Set-ReservedStorageState:Enabled|Disabled`.
  - `ParseReservedStorageState(string? output)` → `ReservedStorageState?`;
    scans for the DISM `Reserved Storage is:` line (ordinal-ignore-case) and
    maps `Enabled`/`Disabled`; null when absent/unknown/blank.
  - `ParseReservedStorageSizeBytes(string? output)` → `long?`; defensive
    parser that reads a size line containing "size" + `:` and converts the
    numeric token with KB/MB/GB suffix to bytes; null when none. DISM emits no
    size today, so this is future-proofing that keeps the "size info available"
    goal honest and testable.
  - `ExtractErrorMessage(string? output, int exitCode)` → last output line
    mentioning `error`, else last non-empty line, suffixed with the exit code.
- Thin public surface (not unit-tested, consistent with
  `ComponentStoreService.Cleanup`):
  - `ctor(ModuleCallbacks? callbacks = null)`.
  - `GetState(string imagePath, PSCmdlet? cmdlet = null)` →
    `WindowsImageReservedStorage`; validates the directory exists, runs
    `dism.exe` with `BuildGetReservedStorageStateArguments`, parses state +
    size, throws `InvalidOperationException` on non-zero exit or unparseable
    state (Get semantics: no partial/blank result).
  - `SetState(string imagePath, bool enable, PSCmdlet? cmdlet = null)` →
    `ReservedStorageOperationResult`; always returns a result, `Success =
    exitCode == 0`, `ErrorMessage` via `ExtractErrorMessage` on failure (Set
    semantics: report outcome, matching `AddWindowPackage`-style results).
  - `private (int ExitCode, string Output) RunDism(string arguments,
    PSCmdlet? cmdlet)` — thin Process.Start shell (dism.exe,
    `UseShellExecute=false`, redirected stdout/stderr, `CreateNoWindow=true`,
    3-minute wait then kill), verbose-logging the command/output, warning on
    stderr, following `OptionalComponentService`'s process pattern. Uses
    `LoggingService.LogOperationStartWithTimestamp` /
    `LogOperationCompleteWithTimestamp` around each operation.

**`src/Cmdlets/ReservedStorageCmdlets.cs`**

- `GetWindowsImageReservedStorageCmdlet` —
  `[Cmdlet(VerbsCommon.Get, "WindowsImageReservedStorage")]`,
  `[OutputType(typeof(WindowsImageReservedStorage))]`; mandatory `-ImagePath`
  (`string`, resolved via `GetUnresolvedProviderPathFromPSPath`). Calls
  `ReservedStorageService(ModuleCallbacks.FromCmdlet(this)).GetState(...)`;
  on exception logs `LoggingService.WriteError` + throws terminating error
  (matching `GetWindowsImageComponentStoreCmdlet`'s Get path).
- `SetWindowsImageReservedStorageCmdlet` —
  `[Cmdlet(VerbsCommon.Set, "WindowsImageReservedStorage", SupportsShouldProcess
  = true)]`, `[OutputType(typeof(ReservedStorageOperationResult))]`;
  mandatory `-ImagePath`, mandatory `-Enable` (parameter set `Enable`) and
  mandatory `-Disable` (parameter set `Disable`) — PowerShell-level mutual
  exclusivity, with an in-code guard for neither-present. `EndProcessing`
  calls `ShouldProcess("Set reserved storage state on <path>", "Enable|Disable
  reserved storage")`, then `SetState`, and writes the result object.

**Help files** `docs/help/Get-WindowsImageReservedStorage.md` and
`docs/help/Set-WindowsImageReservedStorage.md` — PlatyPS format copied from
`docs/help/Get-WindowsImageComponentStore.md` (front matter:
`external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name:
PSWindowsImageTools`; document every parameter incl. `-ProgressAction`).

## Data Flow

```
Get-WindowsImageReservedStorage -ImagePath <mount>
   └─► ReservedStorageService.GetState
         ├─► BuildGetReservedStorageStateArguments  (pure)
         ├─► RunDism ──► dism.exe /Image:"<mount>" /Get-ReservedStorageState   (thin shell)
         └─► ParseReservedStorageState / ParseReservedStorageSizeBytes  (pure)
         └─► WindowsImageReservedStorage

Set-WindowsImageReservedStorage -ImagePath <mount> -Enable|-Disable
   └─► ShouldProcess (SupportsShouldProcess)
   └─► ReservedStorageService.SetState
         ├─► BuildSetReservedStorageStateArguments  (pure)
         ├─► RunDism ──► dism.exe /Image:"<mount>" /Set-ReservedStorageState:Enabled|Disabled
         └─► ReservedStorageOperationResult
```

## Error Handling

- Missing/blank `-ImagePath` → PowerShell mandatory binding error; non-existent
  directory → `DirectoryNotFoundException` from `GetState`/`SetState` (logged
  + terminating error in the cmdlets).
- You can't both `-Enable` and `-Disable`: separate parameter sets make that a
  binding error; neither switch → clear terminating guard.
- `GetState` throws on non-zero DISM exit or unparseable state (no silent
  "unknown" result), error text via `ExtractErrorMessage`.
- `SetState` returns `Success = exitCode == 0` with `ErrorMessage` populated —
  it never throws for a DISM-reported failure, only for process-start/timeout
  failures.
- DISM stderr is surfaced as a warning; stdout is verbose. `RunDism` kills the
  process and throws `TimeoutException` if it exceeds 3 minutes.
- `-WhatIf`/`-Confirm` are honored through `ShouldProcess` before any mutation.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/ReservedStorageServiceTests.cs`)** —
  all pure, no DISM, no image, no process:
  - Argument builders: correct `/Image:` + verb strings for Get and
    Set(:Enabled/:Disabled) — `[Theory]` on quoting/verb combinations, same
    inline-string style as `ComponentStoreServiceTests.BuildCleanupArguments`.
  - `ParseReservedStorageState`: full real-style DISM output → `Enabled`;
    `: Disabled` → `Disabled`; case-insensitivity; null on blank/null/unknown.
  - `ParseReservedStorageSizeBytes`: KB/MB/GB/raw-byte parsing, null when no
    size line, ignores non-size output lines.
  - `ExtractErrorMessage`: picks the last `error` line, falls back to last
    non-empty line, and to exit-code-only when output is empty.
- **Integration (Pester / real DISM): manual/CI-only note** — reserved-storage
  verbs require a real mounted image. The local host's DISM API
  `OpenOfflineSession` limitation documented in `OpenCode-EngLog.md` makes
  real-image verification manual/CI-only; the dism.exe CLI (which always works
  on this host) is exercised end-to-end there, not in the local suite.
- `dotnet build src/PSWindowsImageTools.csproj` (0 errors) and filtered
  `dotnet test` for the new test class are the local verification gates.

## Risks

- **Localized DISM output.** `Reserved Storage is:` is the en-US line; parsing
  is ordinal-ignore-case on that marker, so non-English DISM builds may not
  parse (state returns null → Get throws with the raw exit/output text). This
  matches the module's existing en-US DISM parsing posture
  (`OptionalComponentService`, `WindowsUpdateCatalogService`).
- **CLI dependency.** `dism.exe` must exist (it does on every Windows host the
  module targets) and running under an elevated context — reserved-storage
  mutation needs admin, same as every other servicing cmdlet.
- **No size data today.** `SizeBytes` is null for real DISM output; the field
  exists so the "size info available" goal is honored and future DISM size
  lines are surfaced. Callers must not assume a value.
- **Single-image, write path.** The Set cmdlet mutates a mounted image like
  every other mutating cmdlet in the module; `ShouldProcess` + `-WhatIf`
  protect against accidental toggles.