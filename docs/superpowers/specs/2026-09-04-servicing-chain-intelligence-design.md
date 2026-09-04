# Servicing Chain Intelligence — Design

**Date:** 2026-09-04
**Status:** Approved for planning

## Problem

An offline Windows image can accumulate updates in any order DISM allows —
`Add-WindowsImagePackage` will happily install a cumulative update (LCU)
without its prerequisite servicing stack update (SSU) present, or leave a
stale SSU behind after installing a newer LCU. There is currently no way to
inspect a mounted image and tell whether its update chain is internally
consistent. This spec adds that capability: classify installed servicing
packages by role (SSU / LCU / other) and flag when the SSU looks too old
for the LCU present.

## Goals

1. Classify each installed package in a mounted image as a Servicing Stack
   Update (SSU), Latest Cumulative Update (LCU), or unclassified, using
   real, verified package-identity naming conventions.
2. Detect the practical real-world failure mode this module's own DISM
   servicing operations can produce: an LCU installed without an
   adequately current SSU.
3. Expose both a detailed report (`Get-WindowsImageServicingChain`) and a
   pass/fail wrapper (`Test-WindowsImageServicing`) over the same
   classification/check logic.

## Grounding: verified package-identity format

Confirmed against a real, live Windows 11 (build 26100) online image via
`dism /online /get-packages` during this session (not assumed from
memory):

```
Package_for_ServicingStack_9156~31bf3856ad364e35~amd64~~26100.9156.1.0
Installed | Security Update | 8/11/2026 8:08 PM

Package_for_RollupFix~31bf3856ad364e35~amd64~~26100.9168.1.19
Installed | Security Update | 8/14/2026 12:21 AM
```

Format: `<Name>~<PublicKeyToken>~<Architecture>~<Language>~<Version>`,
where `<Version>` is itself `<Build>.<Revision>.<Major>.<Minor>`.

- **SSU**: package name starts with `Package_for_ServicingStack`.
  `ReleaseType` is `SecurityUpdate` (confirmed via `DismPackage.ReleaseType`,
  the same enum this module already consumes in `ComponentStoreService`).
- **LCU**: package name starts with `Package_for_RollupFix`.
  `ReleaseType` is also `SecurityUpdate` — **`ReleaseType` alone cannot
  distinguish SSU from LCU**; the name prefix is the only reliable
  discriminator.
- Both packages' `<Build>` segment matches the image's own build (26100 in
  this example); the `<Revision>` segment (9156 for this SSU, 9168 for
  this LCU) is what actually varies release-to-release and is what a
  compatibility check must compare.

**Not independently verified this session** (no live example found on the
inspection machine): SafeOS Dynamic Update and standalone .NET Framework
update package name patterns. These are included as **best-effort,
lower-confidence** classifications only (see Non-goals) — no ordering
check is built on top of them.

## Non-goals

- **No authoritative "minimum SSU version for LCU X" lookup.** Microsoft
  does not expose this mapping in DISM package metadata, and this module
  has no network dependency on Microsoft's release-notes catalog for this
  feature. The compatibility check (below) is a documented heuristic
  derived from the packages' own revision numbers, not an authoritative
  Microsoft-published rule — the spec says this plainly so nobody mistakes
  a heuristic for ground truth.
- **No Setup Dynamic Update / installation-media detection.** Setup DU
  applies to installation media (`setup.exe`), not an installed offline
  image — nothing to classify from `Get-Packages` on a mounted WIM.
- **No SafeOS/.NET ordering validation** — classification only, flagged
  `Heuristic` confidence, no pass/fail claim built on them (see Grounding).
- **No mutation.** This subsystem is read-only reporting, matching
  `Get-WindowsImageComponentStore`'s scope — no `Optimize-*`-style cmdlet.
  Fixing a detected mismatch (e.g. installing a newer SSU) is already
  possible via the existing `Add-WindowsImagePackage` cmdlet.

## Architecture

Same service/cmdlet/model split as every other Phase 1 subsystem
(`ComponentStoreService`, `WindowsImageDriverService`), reusing
`IWindowsImageService.GetPackages` (existing, unchanged) as the sole data
source — no new DISM API surface needed.

### Models — `src/Models/ServicingChainModels.cs`

```csharp
public enum ServicingPackageRole
{
    ServicingStackUpdate,
    CumulativeUpdate,
    SafeOSUpdate,
    DotNetUpdate,
    Other
}

public enum ClassificationConfidence
{
    Verified,   // SSU / LCU — confirmed naming convention
    Heuristic   // SafeOS / DotNet — best-effort, unverified pattern
}

public class ServicingPackageInfo
{
    public string PackageName { get; set; } = string.Empty;
    public ServicingPackageRole Role { get; set; }
    public ClassificationConfidence Confidence { get; set; }
    public int Build { get; set; }
    public int Revision { get; set; }
    public DateTime? InstallTime { get; set; }
}

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
}
```

### Service — `src/Services/ServicingChainService.cs`

Pure, unit-testable classification and validation (mirrors
`ComponentStoreService.ClassifyPackages`'s pure/impure split):

- `internal static ServicingPackageInfo? ClassifyPackage(string packageName, DismPackageFeatureState state, DateTime? installTime)`
  — string-prefix matching (`Package_for_ServicingStack` → SSU,
  `Package_for_RollupFix` → LCU; `Package_for_SafeOS`/`Package_for_KB...NetFramework`-style
  substring checks → SafeOS/DotNet at `Heuristic` confidence — see
  Ambiguity note below); parses `Build`/`Revision` from the trailing
  version segment via `Version.TryParse` on the last two dot-separated
  components. Returns `null` (not `Other`) for packages whose state is
  `Removed`/`Superseded` — only currently-installed packages count toward
  the chain.
- `internal static void ValidateOrdering(ServicingChainReport report)` —
  pure, operates on the already-classified `report.Packages`. Sets
  `ServicingStackUpdate`/`CumulativeUpdate` to the classified packages
  matching those roles (if multiple of the same role exist — unusual but
  possible mid-servicing — picks the highest `Revision`). Rule: if a
  `CumulativeUpdate` is present, `OrderingValid = false` when either (a)
  no `ServicingStackUpdate` is present at all, or (b) the SSU's `Revision`
  is more than `MaxRevisionLag` (default 200 — chosen as a conservative
  multiple of typical month-to-month revision deltas observed in the
  grounding example, documented in code as a tunable heuristic, not a
  Microsoft-published constant) behind the LCU's `Revision`. Either
  failure adds a matching entry to `report.Issues`.
- `public ServicingChainReport Analyze(MountedWindowsImage mountedImage, IWindowsImageService imageService)`
  — DISM-facing wrapper: calls `imageService.GetPackages(mountPath)`,
  maps each to `(PackageName, PackageState, InstallTime)`, calls
  `ClassifyPackage` per package, calls `ValidateOrdering`. Same
  try/catch-into-`Issues` pattern as `ComponentStoreService.Analyze`.

**Ambiguity note (to resolve during implementation, not blocking this
spec):** the exact SafeOS/.NET name-prefix patterns are unverified. The
implementation task will note this explicitly and ship them as
best-effort substring checks (`Contains("SafeOS")`, `Contains("NetFramework")`)
behind the `Heuristic` confidence tag, so an incorrect guess degrades to
"unclassified as Other" rather than a wrong confident claim — false
negatives (missed SafeOS/.NET packages, shown as `Other`) are the
acceptable failure mode, not false positives.

### Cmdlets — `src/Cmdlets/ServicingChainCmdlets.cs`

- **`Get-WindowsImageServicingChain`** (`VerbsCommon.Get`) — pipeline of
  `MountedWindowsImage[]`, `-ContinueOnError` switch (matches
  `Get-WindowsImageComponentStore`'s pattern exactly), outputs
  `ServicingChainReport[]`.
- **`Test-WindowsImageServicing`** (`VerbsDiagnostic.Test`) — same
  pipeline shape, outputs `bool` per image (`report.OrderingValid`) via
  `WriteObject`, OR the full `ServicingChainReport` with a `-Detailed`
  switch for callers who want the reasoning, not just true/false (avoids
  needing two separate service calls for the common "just tell me
  pass/fail" case per the spec's Goal 3).

## Data Flow

```
Mount-WindowsImageList
        │
        ├─► Get-WindowsImageServicingChain ──► ServicingChainReport
        │         (Packages, SSU, LCU, OrderingValid, Issues)
        │
        └─► Test-WindowsImageServicing ──► bool (or ServicingChainReport with -Detailed)
                  (internally calls ServicingChainService.Analyze, same as Get-)
```

## Error Handling

Matches established Phase 1 convention: `-ContinueOnError` switch on both
cmdlets, per-image try/catch → `LoggingService.WriteError` + conditional
rethrow; `Analyze`'s internal package-enumeration failure is caught and
recorded to `report.Issues` rather than aborting (matching
`ComponentStoreService.Analyze`'s hardened pattern from Phase 1's own
final-review fix wave).

## Testing

- **Unit (xUnit)**: `ClassifyPackage` against the two verified real
  package-identity strings from Grounding (exact SSU/LCU cases), plus
  synthetic cases for `Other`, `Removed`-state exclusion, and malformed
  version strings (missing segments — must not throw). `ValidateOrdering`
  against synthetic `ServicingChainReport`s: no SSU + LCU present → invalid;
  SSU revision within tolerance → valid; SSU revision far behind → invalid;
  multiple SSUs present → highest-revision one selected. No DISM types
  constructed directly (same non-public-constructor constraint as every
  other Phase 1 service).
- **Integration (Pester)**: `Get-WindowsImageServicingChain`/
  `Test-WindowsImageServicing` against a real mounted image, matching the
  existing `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`
  conventions — the synthetic baseline WIM used by that file has no real
  servicing packages (confirmed during Phase 1's own live verification),
  so the integration case asserts the cmdlet runs without error and
  returns a report with `Packages` possibly empty, not a specific
  SSU/LCU pairing.

## Risks

- The `MaxRevisionLag` heuristic threshold (default 200) is a judgment
  call, not a verified Microsoft rule — documented in code and this spec
  so a future maintainer can tune it with better data rather than
  mistaking it for authoritative.
- SafeOS/.NET classification patterns are unverified guesses; shipped at
  `Heuristic` confidence specifically so callers can filter them out of
  any strict decision-making.
