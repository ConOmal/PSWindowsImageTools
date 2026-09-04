# Edition Servicing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Set-WindowsImageEdition` — a `SupportsShouldProcess` cmdlet that changes the edition of a mounted (offline) Windows image via the managed DISM edition API (`DismApi.GetCurrentEdition` / `GetTargetEditions` / `SetEdition` / `SetEditionAndProductKey`), with `-Edition` + optional `-ProductKey`, or `-ServerEdition` for the server SKU path, emitting a `WindowsImageEditionResult` with `-PassThru`.

**Architecture:** Mirror the existing convention: `Models/WindowsImageEditionModels.cs` for the result object, `Services/WindowsImageEditionService.cs` for the work, `Cmdlets/SetWindowsImageEditionCmdlet.cs` for the surface, and pure `internal static` methods for every piece of logic a unit test can drive without a DISM session or a real image (parameter validation, edition-name normalization, product-key validation/masking, DISM-call selection, result mapping). The only thin, non-unit-tested surface is the `DismApi` calls themselves, which ride the existing `WindowsImageService` session pattern and require a mounted image.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), bundled `Microsoft.Dism.dll` (3.3.12) already referenced, xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-edition-servicing-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions, `??`, `??=`, `using var`, and nullable annotations exactly as `WindowsImageService.cs` / the existing cmdlets do.
- One new cmdlet → the orchestrator adds `Set-WindowsImageEdition` to `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` `CmdletsToExport` and regenerates the shipped MAML; do NOT touch the psd1 or `Module/PSWindowsImageTools/bin/`.
- One new help file `docs/help/Set-WindowsImageEdition.md` must keep `verify-help.ps1` checks 1–3 green (check 4, shipped MAML, is the orchestrator's expected failure — report it, don't fix it).
- No new NuGet/assembly dependencies. `Microsoft.Dism.dll` is the bundled 3.3.12 wrapper.
- Do NOT touch other agents' files (`ComponentStoreService.cs`, `RegistryDriftService.cs`, `RegistryDriftModels.cs`, `ImageComparisonService.cs`, `ImageComparisonModels.cs`, `WindowsImageDriverService.cs`, `WindowsImageHealthCheckService.cs`, `WinREImageService.cs`, `FormatUtilityService.cs`, `NativeRegistryService.cs`, anything Reserved-Storage/WinRE-intelligence).
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Local DISM `OpenOfflineSession` servicing is broken (documented in `docs/OpenCode-EngLog.md`) — real-image edition operations are manual/CI-only; everything testable locally is pure logic.
- Verification (only filtered tests, never the full suite):
  1. `dotnet build src/PSWindowsImageTools.csproj` — 0 errors (0 warnings for the new code); if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry.
  2. `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WindowsImageEditionServiceTests"` — pass.
  3. `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"` — confirm the new help file keeps checks 1–3 green; report check 4 as the orchestrator-only expected failure.

---

### Task 1: Edition result model

**Files:**
- Create: `src/Models/WindowsImageEditionModels.cs`

**Interfaces:**
- `WindowsImageEditionResult { ImagePath, CurrentEdition, RequestedEdition, IsServerEdition, ProductKeyProvided, ProductKeyMasked, AfterEdition, AvailableTargetEditions, Applied, Declined, IsSuccessful, ErrorMessage, CompletedAt, Duration }` plus computed `EditionChanged` and `Status` (`failed` / `declined` / `changed` / `unchanged` / `no change`), and `ToString()`.

- [x] **Step 1: Create `src/Models/WindowsImageEditionModels.cs`** — plain POCO with `= string.Empty` / `new List<...>()` initializers, XML doc comments, computed `EditionChanged` (case-insensitive, false when declined or `AfterEdition` null) and `Status`, mirroring `ComponentStoreModels.cs` style.
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: WindowsImageEditionService — pure logic + thin DISM path

**Files:**
- Create: `src/Services/WindowsImageEditionService.cs`

**Interfaces:**
- `WindowsImageEditionService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default.
- `public const string ServerEditionId = "ServerEdition"`.
- `public string GetCurrentEdition(string imagePath)` — thin; `DismApi.OpenOfflineSession` + `GetCurrentEdition`, empty-coalesced.
- `public WindowsImageEditionResult SetImageEdition(string imagePath, string? edition, string? productKey, bool serverEdition, Action<int, string>? progressCallback = null)` — thin; validates, resolves the edition id, opens the session, reads current edition, warns when `IsEditionSupported` is false, short-circuits on already-on-edition, dispatches to the matching `DismApi` call, re-reads, builds the result. Never throws for anticipated failures (DISM errors → failed result).
- `internal static void ValidateEditionParameters(string? edition, string? productKey, bool serverEdition)`.
- `internal static string ResolveEditionId(string? edition, bool serverEdition)`.
- `internal static string NormalizeEditionName(string? edition)`.
- `internal static bool IsValidProductKeyFormat(string productKey)`.
- `internal static string MaskProductKey(string? productKey)`.
- `internal static bool EditionsMatch(string currentEdition, string? requestedEdition)`.
- `internal static bool IsEditionSupported(string editionId, IEnumerable<string>? targetEditions)`.
- `internal static string DescribeSetEditionCall(string editionId, string? productKey, bool serverEdition)`.
- `internal static WindowsImageEditionResult BuildResult(DirectoryInfo imagePath, string? requestedEdition, bool serverEdition, string? productKey, string currentEdition, string? afterEdition, bool applied, bool declined, bool isSuccessful, string? errorMessage, IReadOnlyList<string>? availableTargetEditions, DateTime completedAt, TimeSpan duration)`.

- [x] **Step 1: Write `WindowsImageEditionService.cs`** with all members above:
  - `_callbacks` from `ModuleCallbacks`; `ServiceName = "WindowsImageEditionService"`.
  - `ValidateEditionParameters`: server path → reject non-empty `edition`/`productKey`; client path → edition required, `IsValidProductKeyFormat` when key provided.
  - `NormalizeEditionName`: trim; reject blank and `\`/`/`.
  - `IsValidProductKeyFormat`: five dash-separated 5-char alphanumeric groups, or flat 25-char alphanumeric.
  - `MaskProductKey`: `XXXXX-XXXXX-XXXXX-XXXXX-<last5>`; empty for null/blank.
  - `EditionsMatch` / `IsEditionSupported`: case-insensitive; null target set = supported.
  - `DescribeSetEditionCall`: mirrors the dispatch branching (server / client+key / client) for unit-testable call selection.
  - `BuildResult`: maps inputs into `WindowsImageEditionResult`, always via `MaskProductKey` for `ProductKeyMasked` and `ToList()` for `AvailableTargetEditions` (empty list when null).
  - `SetImageEdition`: `using var session = DismApi.OpenOfflineSession(imagePath)`; wrap the `progressCallback` in a `DismProgressCallback` that never throws (mirror `WrapNativeProgress` in `WindowsImageService`); `SetEdition(session, ServerEditionId, ...)` for server, `SetEditionAndProductKey(session, edition, key, ...)` when key, else `SetEdition(session, edition, ...)`; `_callbacks.Warning`/`Error`/`Verbose` throughout.
- [x] **Step 2: Build** to confirm it compiles; resolve nullable warnings (0 warnings target).

### Task 3: SetWindowsImageEditionCmdlet

**Files:**
- Create: `src/Cmdlets/SetWindowsImageEditionCmdlet.cs`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Set, "WindowsImageEdition", SupportsShouldProcess = true)]`, `[OutputType(typeof(WindowsImageEditionResult))]`.
- Parameter sets `Edition` (`-ImagePath` DirectoryInfo mandatory position 0 ValueFromPipeline, `-Edition` mandatory, `-ProductKey` optional, `-PassThru`) and `ServerEdition` (`-ImagePath`, `-ServerEdition` mandatory switch, `-PassThru`).

- [x] **Step 1: Write `SetWindowsImageEditionCmdlet.cs`** — `EndProcessing`:
  - image-path `DirectoryInfo` existence check → `ThrowTerminatingError` (`ImagePathNotFound`) on failure;
  - `ValidateEditionParameters` / `ResolveEditionId` → `ThrowTerminatingError` (`InvalidEditionParameters`) on failure;
  - `LoggingService.LogOperationStartWithTimestamp` (`ComponentName = "Set-WindowsImageEdition"`);
  - `using var imageService = WindowsImageService.ForCmdlet(this); imageService.Initialize();`
  - best-effort `GetCurrentEdition` before `ShouldProcess` (warning + null on failure) to build `"change image edition from '<cur>' to '<ed>'"`;
  - `ShouldProcess` false → decline path: `BuildResult(..., declined: true, isSuccessful: false)` on `-PassThru`, verbose note, op-complete log, return;
  - `ProgressService.CreateProgressCallback(this, "Setting Windows Image Edition", "Setting edition '<ed>'", 1, 1)`;
  - `SetImageEdition(...)` → `WriteObject(result)` on `-PassThru`; op-complete log with the right summary (changed / already / failed);
  - outer catch → `LoggingService.WriteError` + on `-PassThru` a failed result, else `ThrowTerminatingError` (`SetEditionFailed`); op-complete log.
- [x] **Step 2: Build** to confirm it compiles.

### Task 4: Help file

**Files:**
- Create: `docs/help/Set-WindowsImageEdition.md`

- [x] **Step 1: Create `docs/help/Set-WindowsImageEdition.md`** — copy the PlatyPS skeleton from `docs/help/Optimize-WindowsImageComponentStore.md` (front matter, SYNTAX/DESCRIPTION/PARAMETERS/EXAMPLES/INPUTS/OUTPUTS). Document both SYNTAX parameter sets; PARAMETERS sections for `-ImagePath`, `-Edition`, `-ProductKey`, `-ServerEdition`, `-PassThru`, plus `-Confirm`/`-WhatIf`/`-ProgressAction`; INPUTS `System.IO.DirectoryInfo`; OUTPUTS `PSWindowsImageTools.Models.WindowsImageEditionResult`; carry the `-ServerEdition`-without-key advisory caveat in DESCRIPTION.

### Task 5: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/WindowsImageEditionServiceTests.cs`

- [x] **Step 1: Create `WindowsImageEditionServiceTests.cs`** — pure xUnit `[Fact]`/`[Theory]`, no mock framework:
  - `ValidateEditionParameters`: mutual exclusion (server + edition, server + key), client requires edition, valid client combos accepted.
  - `NormalizeEditionName` / `ResolveEditionId`: trim; blank + `\`/`/` rejection; `ServerEdition` mapping.
  - `IsValidProductKeyFormat`: valid dashed + flat 25; bad group count/length/spaces rejected.
  - `MaskProductKey`: dashed + flat → masked tail; null/blank → empty.
  - `EditionsMatch`: case-insensitive equal/not-equal, empty/null sides.
  - `IsEditionSupported`: null targets supported, matching supported, missing not supported.
  - `DescribeSetEditionCall`: server / client+key (masked) / client.
  - `BuildResult`: changed / unchanged / declined / failed / server-path semantics.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~WindowsImageEditionServiceTests"`) and confirm they pass.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors, 0 warnings from the new code).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 5 / Step 2).
- [x] **Step 3: Run help guardrail** `powershell -NoProfile -Command "& .\Scripts\verify-help.ps1 -SkipCompile"`; confirm the new help file keeps checks 1–3 green and report check 4 (shipped MAML / psd1 export) as the orchestrator-only expected items.
- [x] **Step 4: Integration note** — real-image edition change (`GetCurrentEdition` / `SetEdition`) is verified manually/CI on a mounted image; the local DISM `OpenOfflineSession` limitation keeps this out of the local suite. (No Pester changes made.)
- [x] **Step 5: Final report** — spec + plan paths, exact cmdlet name, API-vs-CLI decision + why, test counts, verify-help outcome, deviations. Leave working tree uncommitted.