# Edition Servicing — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** a new servicing cmdlet in the golden-image toolkit phase (after
component store cleanup, drivers, inventory/SBOM, validation, registry drift), which
declared "edition changes" a deferred concern.

## Problem

The DISM command line supports changing the edition of a mounted offline Windows image
(`dism /Image:<path> /Set-Edition:<edition> [/ProductKey:<key>]`, plus
`/Set-Edition:ServerEdition` for the server SKU path). The bundle's installed
`Microsoft.Dism` wrapper (3.3.12) **does** expose the full edition API surface
(`GetCurrentEdition`, `GetTargetEditions`, `SetEdition`, `SetEditionAndProductKey`,
`GetProductKeyInfo`, `SetProductKey`), but the module exposes **no cmdlet** that wraps
edition servicing. Operator workflows that must promote a golden image up-edition (for
example Home → Professional, or signing a Windows Server SKU) currently require dropping
out of the module to raw DISM.

The module already wraps the same session pattern for `Microsoft.Dism` in
`WindowsImageService`; edition servicing should ride the same managed API, not shell out
to `dism.exe`.

## Goals

1. Provide `Set-WindowsImageEdition` — a `SupportsShouldProcess` cmdlet that changes the
   edition of a mounted (offline) image via the managed DISM API, with `-Edition`
   (`Professional`, `Enterprise`, ...) optionally `-ProductKey`, or `-ServerEdition` for
   the server SKU path.
2. Keep the change safe and scriptable: `-WhatIf`/`-Confirm` supported, current-edition
   read before the change for an accurate confirm message, and a serializable
   `WindowsImageEditionResult` (before/after editions, status, masked key) emitted with
   `-PassThru` in the module's result-object convention.
3. Never print a full product key: the result carries only a masked form (last group).
4. Keep all decision logic — parameter-validity checks (mutual exclusion), edition-name
   normalization/validation, product-key format validation, DISM-call selection, result
   mapping — in `internal static` pure methods so it is unit-testable without a DISM
   session or a real image.
5. Reuse the module's convention everywhere: `ModuleCallbacks`, `LoggingService`
   start/complete timestamps, `ProgressService` progress, `WindowsImageService.ForCmdlet`
   for DISM initialization.

## Non-goals

- **Online edition change.** Offline mounted image only, matching the module's
  `OpenOfflineSession(mountPath)` servicing surface.
- **Edition upgrade/resupport actions** (fatal-error recovery, `/Get-TargetEditions`
  listing as a public cmdlet). Target-edition enumeration feeds a warning heuristic inside
  the service; there is no separate `Get-WindowsImageTargetEdition` cmdlet.
- **Product-key install/set outside an edition change.** `SetProductKey` /
  `GetProductKeyInfo` stay internal to the service surface decision; the cmdlet only
  accepts a key as part of a set-edition request.
- **EULA/TPM/policy prompts (interactive switches).** DISM's interactive `/Set-Edition`
  acceptance prompts apply to online un-attended flows and are out of scope for a
  scripted cmdlet.
- **CLI fallback to `dism.exe`.** The managed API exists in the bundled wrapper; there is
  no process spawning and no output-parsing surface to build or test.

## Architecture

All additions follow the existing service + model + cmdlet split. No manifest change (the
orchestrator adds the exported cmdlet to `CmdletsToExport`), one new help file, one new
model file, one new service file, one new cmdlet file, one new unit-test file. No new
NuGet/assembly dependencies (uses the bundled `Microsoft.Dism.dll`).

### New files

**`src/Models/WindowsImageEditionModels.cs`**

`WindowsImageEditionResult` — serializable before/after edition-change report:

- `ImagePath` (`DirectoryInfo`), `CurrentEdition`, `RequestedEdition` (nullable),
  `IsServerEdition`, `ProductKeyProvided`, `ProductKeyMasked` (masked form, never the
  full key), `AfterEdition` (nullable), `AvailableTargetEditions`
  (`List<string>`, empty when the query failed), `Applied`, `Declined`,
  `IsSuccessful`, `ErrorMessage`, `CompletedAt`, `Duration`.
- Computed `EditionChanged` (case-insensitive current vs after, false when declined or
  after null) and `Status` (`.ToString()` string: `failed` / `declined` / `changed` /
  `unchanged` / `no change`), mirroring the state-summary style of
  `ComponentStoreCleanupResult`.
- `ToString()` — `"<image>: current -> after|requested (status)"`.

**`src/Services/WindowsImageEditionService.cs`** (`_callbacks`, `ModuleCallbacks`-aware)

- `private const string ServiceName = "WindowsImageEditionService"`.
- `public const string ServerEditionId = "ServerEdition"` — the DISM edition id used by
  the server path.
- `public string GetCurrentEdition(string imagePath)` — thin: `OpenOfflineSession` +
  `DismApi.GetCurrentEdition`, nullable-coalesced to empty.
- `public WindowsImageEditionResult SetImageEdition(string imagePath, string? edition,
  string? productKey, bool serverEdition, Action<int, string>? progressCallback = null)`
  — the only thin, non-unit-tested surface. Validates via the pure helpers, resolves the
  edition id, opens the session, reads the current edition, warns when the requested
  edition is not in `GetTargetEditions` (heuristic only — a valid product key can still
  succeed), short-circuits when already on that edition, then dispatches to the matching
  DISM call (`SetEdition` or `SetEditionAndProductKey` for the client path,
  `SetEdition(ServerEditionId)` for the server path), re-reads the edition, and builds
  the result. Never throws for anticipated failures — DISM errors are mapped into a
  failed `WindowsImageEditionResult` after a `LoggingService` error.
- Pure `internal static` methods (unit-tested, no DISM):
  - `ValidateEditionParameters(string? edition, string? productKey, bool serverEdition)`
    — `-ServerEdition` is mutually exclusive with `-Edition` and `-ProductKey`; the
    client path requires an edition and validates a provided product key's format.
  - `ResolveEditionId(string? edition, bool serverEdition)` — `ServerEdition` for the
    server path, else `NormalizeEditionName`.
  - `NormalizeEditionName(string? edition)` — trim; reject blank and path-separator
    (`\`, `/`) values.
  - `IsValidProductKeyFormat(string productKey)` — five dash-separated 5-character
    alphanumeric groups, or a flat 25-character alphanumeric key.
  - `MaskProductKey(string? productKey)` — `"XXXXX-XXXXX-XXXXX-XXXXX-<last5>"`, empty
    for null/blank.
  - `EditionsMatch(string currentEdition, string? requestedEdition)` —
    case-insensitive equality used for the already-on-edition short-circuit.
  - `IsEditionSupported(string editionId, IEnumerable<string>? targetEditions)` — whether
    the requested edition appears in the image's reported target set; null/empty target
    set is treated as supported (unknown must not warn).
  - `DescribeSetEditionCall(string editionId, string? productKey, bool serverEdition)` —
    describes which DISM call will be issued (used for verbose + the longest pole of
    command-line fidelity; kept pure so the branching logic is testable).
  - `BuildResult(DirectoryInfo imagePath, string? requestedEdition, bool serverEdition,
    string? productKey, string currentEdition, string? afterEdition, bool applied,
    bool declined, bool isSuccessful, string? errorMessage,
    IReadOnlyList<string>? availableTargetEditions, DateTime completedAt,
    TimeSpan duration)` — pure mapper into `WindowsImageEditionResult`.

**`src/Cmdlets/SetWindowsImageEditionCmdlet.cs`**

- `[Cmdlet(VerbsCommon.Set, "WindowsImageEdition", SupportsShouldProcess = true)]`,
  `[OutputType(typeof(WindowsImageEditionResult))]`.
- Parameter set `Edition`: `-ImagePath` (`DirectoryInfo`, mandatory, position 0,
  `ValueFromPipeline`), `-Edition` (string, mandatory), `-ProductKey` (optional),
  `-PassThru` (optional).
- Parameter set `ServerEdition`: `-ImagePath` + `-ServerEdition` (switch, mandatory),
  `-PassThru`.
- `EndProcessing`: resolves/validates the image path exists (terminal error otherwise),
  validates the edition parameters, logs operation start, `using var imageService =
  WindowsImageService.ForCmdlet(this)` + `imageService.Initialize()`, reads the current
  edition (warning + null on failure) so `ShouldProcess` can show `from '<current>' to
  '<edition>'`, honors `ShouldProcess` (`declined` result when false), runs with
  `ProgressService.CreateProgressCallback`, writes the result only when `-PassThru`,
  and logs the operation complete. `SupportsShouldProcess` gives `-WhatIf`/`-Confirm`
  for free.

### Modified files

None. (The orchestrator registers the cmdlet in `Module/PSWindowsImageTools/
PSWindowsImageTools.psd1` `CmdletsToExport` after this phase lands; the module copy under
`Module/PSWindowsImageTools/bin/` is refreshed by the orchestrator release step.)

### Help file

**`docs/help/Set-WindowsImageEdition.md`** — PlatyPS format copied from
`docs/help/Optimize-WindowsImageComponentStore.md` (front matter: `external help file:
PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`; SYNTAX block for
both parameter sets; DESCRIPTION; PARAMETERS documenting `-ImagePath`, `-Edition`,
`-ProductKey`, `-ServerEdition`, `-PassThru`, plus `-Confirm`/`-WhatIf`/`-ProgressAction`
and CommonParameters; INPUTS `System.IO.DirectoryInfo`; OUTPUTS
`PSWindowsImageTools.Models.WindowsImageEditionResult`). `Scripts/verify-help.ps1`
validates the new help file the same way it validates every exported cmdlet.

## Data Flow

```
Set-WindowsImageEdition -ImagePath <mounted> -Edition Professional [-ProductKey <key>] [-PassThru]
   └─► WindowsImageService.ForCmdlet(this).Initialize()        (DISM API lifecycle)
   └─► WindowsImageEditionService.GetCurrentEdition(mountPath) (DismApi.OpenOfflineSession + GetCurrentEdition)
   └─► ShouldProcess(mountPath, "change edition from '<cur>' to '<ed>'")
        ├─► {false} → declined WindowsImageEditionResult
        └─► {true}  → WindowsImageEditionService.SetImageEdition(...)
              ├─► ValidateEditionParameters / ResolveEditionId  (pure)
              ├─► DismApi.OpenOfflineSession(mountPath)
              ├─► DismApi.GetTargetEditions ──► IsEditionSupported warning
              ├─► (server) DismApi.SetEdition(session, "ServerEdition", progress)
              ├─► (client+key) DismApi.SetEditionAndProductKey(session, edition, key, progress)
              ├─► (client) DismApi.SetEdition(session, edition, progress)
              ├─► DismApi.GetCurrentEdition  (after)
              └─► BuildResult ──► WindowsImageEditionResult  [PassThru → pipeline]
```

## Error Handling

- The cmdlet treats a missing `-ImagePath` directory as a terminal error
  (`DirectoryNotFoundException`, category `ObjectNotFound`) before any DISM work.
- Invalid parameter combinations (edition missing, `-ServerEdition` mixed with
  `-Edition`/`-ProductKey`, malformed product key) are terminal errors
  (`ArgumentException`, category `InvalidArgument`) — fail fast, before any mutation.
- The service never throws `SetImageEdition` out for anticipated failures: DISM
  exceptions are caught, `LoggingService.WriteError` is raised, and a failed
  `WindowsImageEditionResult` (with `ErrorMessage`) is returned. The cmdlet,
  with `-PassThru`, writes the failed result; without it and on an unexpected
  exception, it throws a terminal `ErrorRecord` (`SetEditionFailed`).
- A failed/declined current-edition read degrades to a warning + null so
  `ShouldProcess` can still proceed with a "from" hint omitted.
- Already-on-edition short-circuits with a warning and an `unchanged` result — no DISM
  mutation call.
- A `GetTargetEditions` failure degrades to a warning; the change still proceeds.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/WindowsImageEditionServiceTests.cs`)** —
  all pure, no DISM sessions, no images:
  - `ValidateEditionParameters`: mutual exclusion of `-ServerEdition` vs
    `-Edition`/`-ProductKey`; client path requires an edition; malformed keys rejected.
  - `NormalizeEditionName` / `ResolveEditionId`: trimming, blank rejection,
    path-separator rejection, `ServerEdition` mapping.
  - `IsValidProductKeyFormat`: valid dashed + flat 25 forms; over-length/short groups,
    wrong group count, spaces rejected.
  - `MaskProductKey`: dashed and flat keys mask to the tail; null/blank → empty.
  - `EditionsMatch`: case-insensitive equal/not-equal, empty/null sides.
  - `IsEditionSupported`: null target list supported, matching target supported,
    missing target not supported.
  - `DescribeSetEditionCall`: server path, client+key path (masked key), client path.
  - `BuildResult`: successful change (`changed`), already-matching (`unchanged`),
    declined (`declined`), failure (`failed`), server path flag + masked-key semantics.
- **Integration/Pester:** no automated local integration — the local DISM
  `OpenOfflineSession` servicing limitation documented in `docs/OpenCode-EngLog.md`
  means real `GetCurrentEdition`/`SetEdition` against a mounted image is verified
  manually/CI on a real image. Everything testable locally is pure logic.

## Risks

- **DISM API semantics for `ServerEdition`.** `Set-Edition:ServerEdition` without a
  product key is only advisory (DISM does not change the edition until a server product
  key is supplied). The service passes the product key through when provided, and both
  the help file and the `-ServerEdition` description carry the caveat; the result's
  `AfterEdition` re-read still reflects whether DISM actually changed the edition.
- **`GetTargetEditions` disagreement.** DISM's reported target set can omit an edition
  that a valid product key still installs; the warning is heuristic and never blocks.
- **Edition-name fidelity.** `-Edition` values are opaque strings forwarded to DISM;
  unknown names surface as DISM errors mapped into the failed result, not a local
  catalog check (the module has no edition catalog).
- **After-edition read.** Reading the edition right after a change can return the new
  value only after the mount is re-enumerated; a transient read failure degrades to a
  warning and `AfterEdition = null` (`EditionChanged = false`, `Status` falls back to
  `unchanged`), which is accurate rather than fabricated.
- **No new env/test surface beyond pure logic.** One new cmdlet + help file means
  `verify-help.ps1` checks 1–3 must pass for the new file; the orchestrator owns the
  `CmdletsToExport` and shipped-MAML regeneration (check 4).