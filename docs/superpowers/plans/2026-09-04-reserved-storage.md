# Reserved Storage — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageReservedStorage` and `Set-WindowsImageReservedStorage` to PSWindowsImageTools — query and change Windows Reserved Storage state in a mounted image via `dism.exe`, with all pure logic unit-tested.

**Architecture:** Mirror the existing convention: `Models/ReservedStorageModels.cs` for output types, `Services/ReservedStorageService.cs` for the work, thin `Cmdlets/ReservedStorageCmdlets.cs` (`PSCmdlet` wrappers reusing `LoggingService`/`ModuleCallbacks`/`GetUnresolvedProviderPathFromPSPath`), and PlatyPS help under `docs/help/`. Because `Microsoft.Dism.dll` 3.3.12 exposes **no** reserved-storage API (reflection: no `GetReservedStorageState`/`SetReservedStorageState` members exist), the service shells out to `dism.exe` — the same pattern `ComponentStoreService.Cleanup` uses for `/Cleanup-Image /StartComponentCleanup` and `OptionalComponentService` uses for `/Get-Packages`. The only thin, non-unit-tested surface is the `Process.Start` invocation (`RunDism`); argument building, output parsing, state mapping and error-text extraction are `internal static` pure methods driven directly by xUnit tests.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), xUnit (`tests/PSWindowsImageTools.Tests/`), no new NuGet packages, no new DISM-API surface.

**Spec:** `docs/superpowers/specs/2026-09-04-reserved-storage-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ProcessStartInfo.ArgumentList`. Use classic statements and nullable annotations exactly as `ComponentStoreService.cs` / `OptionalComponentService.cs` do.
- New cmdlets `Get-WindowsImageReservedStorage` and `Set-WindowsImageReservedStorage` — do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (`CmdletsToExport` is updated by the orchestrator; report the exact cmdlet names). New help `.md` files ARE required (verify-help checks 1–2).
- No new NuGet/assembly dependencies. No edits to any file owned by the concurrent Edition-servicing / WinRE-intelligence / Registry-drift agents (see DO-NOT list in the task brief).
- Do NOT commit. Leave all changes in the working tree for the orchestrator.
- Do not run the full unit suite or the Pester integration suite locally. Verification is: `dotnet build src/PSWindowsImageTools.csproj` (0 errors; if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry), then `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~ReservedStorage"`.
- The local DISM API `OpenOfflineSession` limitation (documented in `docs/OpenCode-EngLog.md`), plus the fact that `Microsoft.Dism.dll` has no reserved-storage API, means reserved-storage operations are verified **manually/CI-only** on a real mounted image with `dism.exe`. Everything testable locally is pure logic.
- Use the existing xUnit style: plain `[Fact]`/`[Theory]`, no mocking framework, inline-string `[Theory]` cases like `ComponentStoreServiceTests.BuildCleanupArguments` (no temp dirs needed — argument and parsing tests are pure strings).

---

### Task 1: Spec

**Files:**
- Create: `docs/superpowers/specs/2026-09-04-reserved-storage-design.md`

- [x] **Step 1: Write the design spec** following the registry-drift design template (Problem, Goals, Non-goals, Design, Testing, Risks), reflecting the DISM-CLI approach and pure/impure split.

### Task 2: Reserved storage models

**Files:**
- Create: `src/Models/ReservedStorageModels.cs`

**Interfaces:**
- `enum ReservedStorageState { Enabled, Disabled }`.
- `WindowsImageReservedStorage { ImagePath, State, StateText (computed), SizeBytes (long?, computed SizeMB) }` with `ToString()`.
- `ReservedStorageOperationResult { ImagePath, Operation, RequestedState, Success, ExitCode, ErrorMessage }` with `ToString()`.

- [x] **Step 1: Create `src/Models/ReservedStorageModels.cs`** with the three types above (plain POCOs, `= string.Empty` / `DateTime`-free, XML doc comments, and `ToString()` overrides mirroring the existing `Models` style).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 3: ReservedStorageService — pure logic + thin dism.exe shell

**Files:**
- Create: `src/Services/ReservedStorageService.cs`

**Interfaces:**
- `ReservedStorageService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default (mirror `ComponentStoreService`).
- `public WindowsImageReservedStorage GetState(string imagePath, PSCmdlet? cmdlet = null)`.
- `public ReservedStorageOperationResult SetState(string imagePath, bool enable, PSCmdlet? cmdlet = null)`.
- `internal static string BuildGetReservedStorageStateArguments(string imagePath)`.
- `internal static string BuildSetReservedStorageStateArguments(string imagePath, bool enable)`.
- `internal static ReservedStorageState? ParseReservedStorageState(string? output)`.
- `internal static long? ParseReservedStorageSizeBytes(string? output)`.
- `internal static string ExtractErrorMessage(string? output, int exitCode)`.
- `private (int ExitCode, string Output) RunDism(string arguments, PSCmdlet? cmdlet)` — thin shell only.

- [x] **Step 1: Write `ReservedStorageService.cs`**:
  - Argument builders return `/Image:"<path>" /Get-ReservedStorageState` and `/Image:"<path>" /Set-ReservedStorageState:Enabled|Disabled` (pure, `[Theory]`-testable inline strings).
  - `ParseReservedStorageState` finds the `Reserved Storage is:` marker (ordinal-ignore-case), maps the post-colon token to the enum; null on blank/unknown.
  - `ParseReservedStorageSizeBytes` scans lines containing "size" + `:` and converts a numeric token with KB/MB/GB/raw-byte unit to bytes; null when none.
  - `ExtractErrorMessage` returns the last line mentioning `error` (else last non-empty line), suffixed `(exit code N)`; exit-code-only when output is empty.
  - `RunDism`: `Process.StartInfo` (dism.exe, `UseShellExecute=false`, redirected stdout/stderr, `CreateNoWindow=true`), read stdout+stderr, `WaitForExit(180000)` with kill-on-timeout → `TimeoutException`, verbose-log command+output, warn on stderr, return `(ExitCode, Output)`.
  - `GetState`: validate directory; verbose + `LogOperationStartWithTimestamp`/`LogOperationCompleteWithTimestamp`; throw `InvalidOperationException` (with `ExtractErrorMessage` text, `_callbacks.Error`) when exit != 0 or state null; return `WindowsImageReservedStorage` with parsed state/size.
  - `SetState`: always returns `ReservedStorageOperationResult`; `Success = exitCode == 0`; `ErrorMessage = ExtractErrorMessage(...)` on failure + `_callbacks.Warning`; never throws for DISM-reported failure.
- [x] **Step 2: Build** to confirm it compiles.

### Task 4: Cmdlets

**Files:**
- Create: `src/Cmdlets/ReservedStorageCmdlets.cs`

**Interfaces:**
- `GetWindowsImageReservedStorageCmdlet` — `[Cmdlet(VerbsCommon.Get, "WindowsImageReservedStorage")]`, `[OutputType(typeof(WindowsImageReservedStorage))]`, mandatory `-ImagePath` (string, `GetUnresolvedProviderPathFromPSPath`), `EndProcessing` → `ReservedStorageService(ModuleCallbacks.FromCmdlet(this)).GetState(resolved, this)` → `WriteObject`; on exception `LoggingService.WriteError` + terminating `ErrorRecord` (`ErrorCategory.OperationStopped`).
- `SetWindowsImageReservedStorageCmdlet` — `[Cmdlet(VerbsCommon.Set, "WindowsImageReservedStorage", SupportsShouldProcess = true)]`, `[OutputType(typeof(ReservedStorageOperationResult))]`, mandatory `-ImagePath`; `-Enable` (parameter set `Enable`) / `-Disable` (parameter set `Disable`) both `Mandatory = true` (PowerShell mutual-exclusivity); in-code guard for neither-switch → terminating error; `ShouldProcess(...)` before `SetState`; write the result.

- [x] **Step 1: Write `ReservedStorageCmdlets.cs`** with both cmdlets following the exact `WindowsImageDriverCmdlets.cs` / `ComponentStoreCmdlets.cs` shapes (ComponentName const, `LoggingService.WriteError`, ThrowTerminatingError on failure).
- [x] **Step 2: Build** to confirm it compiles (0 errors; no warnings from these files).

### Task 5: PlatyPS help

**Files:**
- Create: `docs/help/Get-WindowsImageReservedStorage.md`
- Create: `docs/help/Set-WindowsImageReservedStorage.md`

- [x] **Step 1: Write both help files** copying `docs/help/Get-WindowsImageComponentStore.md` as the template (same front matter, SYNTAX/DESCRIPTION/EXAMPLES/PARAMETERS structure, `-ProgressAction` section, CommonParameters/INPUTS/OUTPUTS/NOTES/RELATED LINKS). Document every parameter: `-ImagePath` (Get), `-ImagePath`, `-Enable`, `-Disable` (Set). `Set-*` mentions `SupportsShouldProcess`/`-WhatIf` in the description.

### Task 6: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/ReservedStorageServiceTests.cs`

- [x] **Step 1: Create `ReservedStorageServiceTests.cs`** — plain xUnit `[Fact]`/`[Theory]`, no mock framework:
  - `BuildGetReservedStorageStateArguments` / `BuildSetReservedStorageStateArguments` return the exact DISM arg strings (inline `[Theory]` cases, following `ComponentStoreServiceTests.BuildCleanupArguments`).
  - `ParseReservedStorageState` parses a full real-style DISM output block (`Reserved Storage is: Enabled`, `: Disabled`, case-insensitive), null for blank/unknown.
  - `ParseReservedStorageSizeBytes` parses KB/MB/GB/raw bytes and returns null without a size line.
  - `ExtractErrorMessage` last-error-line, last-line fallback, empty-output exit-code fallback.
- [x] **Step 2: Run the filtered unit tests** (`--filter "FullyQualifiedName~ReservedStorage"`) and confirm they pass.

### Task 7: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors; retry ~30s on MSBuild lock from a concurrent agent).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 6 / Step 2).
- [x] **Step 3: Help guardrail note** — verify-help checks 1–2 (markdown + parameter coverage) are satisfied by the new `.md` files; check 4 (shipped MAML) is the orchestrator's expected out-of-scope failure; not run locally to avoid disturbing the concurrent test/build agents.
- [x] **Step 4: Integration note** — real-image reserved-storage Get/Set is verified manually/CI on a mounted image via dism.exe; the local `Microsoft.Dism` `OpenOfflineSession` limitation plus the missing wrapper API keep this out of the local suite. (No Pester changes made.)
- [x] **Step 5: Final report** — spec + plan paths, exact cmdlet names, DISM-API-vs-CLI decision + rationale, test counts, deviations. Leave working tree uncommitted.