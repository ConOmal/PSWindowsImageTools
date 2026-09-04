# WinRE Intelligence — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Add `Get-WindowsImageWinRE`, a pure file-inspection cmdlet that reports on the embedded WinRE image (`Windows\System32\Recovery\Winre.wim`) inside a mounted Windows image: presence, size, last-modified, and version/identity fields parsed from the WIM's own 208-byte header (plus, under `-Detailed`, the first image's display name from the raw XML metadata).

**Architecture:** Mirror the module convention — `Models/*.cs` for the report types, `Services/WinREIntelligenceService.cs` for the work, `Cmdlets/GetWindowsImageWinRECmdlet.cs` for the command surface. Every piece of logic (path resolution, WIM header parsing, byte→MB formatting, XML decode/element extraction) is `internal static` and unit-testable with a synthetic byte layout and temp directories. The only thin, non-unit-asserted surface is the bounded `FileStream` read wrappers, which a synthetic-WIM `Inspect` test still covers end-to-end.

**Tech Stack:** C# / .NET (netstandard2.0, LangVersion 8.0, nullable enabled per existing `.csproj`), no new NuGet/assembly dependencies, xUnit (`tests/PSWindowsImageTools.Tests/`).

**Spec:** `docs/superpowers/specs/2026-09-04-winre-intelligence-design.md`

## Global Constraints

- C# 8 only (LangVersion 8.0): no `is not`, no records, no `init`, no target-typed `new`, no `ArgumentList`. Use switch expressions and nullable annotations exactly as `ComponentStoreService.cs` / the existing services do.
- The local machine's DISM API servicing is BROKEN (`OpenOfflineSession` fails — `OpenCode-EngLog.md`). This feature NEVER calls DISM (pure file inspection), so it is not affected, but all real-operation verification stays manual/CI-only.
- Do NOT touch `src/Services/WinREImageService.cs` (read-only reference — we reuse its `EmbeddedWinREPath` constant). Do NOT touch any service/model owned by other concurrent phases (Reserved Storage, Edition servicing, component store, drivers, health check, registry drift, format/native registry).
- Do NOT touch `Module/PSWindowsImageTools/PSWindowsImageTools.psd1` (orchestrator adds the `CmdletsToExport` entry after this phase — report the exact cmdlet name `Get-WindowsImageWinRE`).
- Do NOT commit; leave everything in the working tree.
- Do not run the full unit suite or the Pester integration suite locally (parallel builders — a full run can hit file-lock/`obj` errors). Verification is the filtered test command below; if MSBuild `.obj`/file-lock errors appear from a concurrent build, wait ~30s and retry.
- Do NOT sync/copy rebuilt DLLs into `Module/PSWindowsImageTools/bin/`.
- Temp-dir fixture pattern from `tests/PSWindowsImageTools.Tests/WinREImageServiceTests.cs` (`Path.Combine(Path.GetTempPath(), "<Name>_" + Guid.NewGuid().ToString("N"))`) for the synthetic-WIM tests.

---

### Task 1: WinRE intelligence models

**Files:**
- Create: `src/Models/WinREIntelligenceModels.cs`

**Interfaces:**
- `WinREIntelligenceReport { ImagePath, WinREPresent, WinREPath, SizeBytes, SizeMB, LastModifiedUtc, WimHeaderParsed, WimVersion, ImageCount, CompressionType, WimHeader, XmlImageDisplayName }` — flatteners `WimVersion` / `ImageCount` / `CompressionType` mirror `WimHeader` for quick tabular output; `XmlImageDisplayName` set only under `-Detailed`.
- `WimHeaderInfo { IsValid, HeaderSize, Version, VersionMajor, VersionMinor, VersionText, Flags, CompressionType, CompressionTypeName, WimGuid, PartNumber, NumberOfParts, ImageCount, BootIndex, MetadataOffset, TotalBytes }`.

- [x] **Step 1: Create `src/Models/WinREIntelligenceModels.cs`** with the two POCO types above (plain properties with `string.Empty`/default initializers, XML doc comments, `ToString()` overrides mirroring the existing `Models` style).
- [x] **Step 2: Build** `dotnet build src/PSWindowsImageTools.csproj` to confirm it compiles.

### Task 2: WinREIntelligenceService — pure logic + thin file reads

**Files:**
- Create: `src/Services/WinREIntelligenceService.cs`

**Interfaces:**
- `WinREIntelligenceService(ModuleCallbacks? callbacks = null)` — public ctor, `ModuleCallbacks.Silent` default.
- `public WinREIntelligenceReport Inspect(string mountPath, bool detailed)` — thin orchestration; structural path errors throw, content failures degrade to warning + partial report.
- `internal static string ResolveWinREPath(string mountPath)`.
- `internal static double BytesToMB(long bytes)`.
- `internal static WimHeaderInfo? TryParseWimHeader(byte[] bytes)`.
- `internal static string MapCompressionType(uint type)`.
- `internal static string? TryExtractXmlImageDisplayName(byte[] xmlBytes)`.
- `internal static string? ExtractFirstElementText(string xml, string elementName)`.
- `internal static string UnescapeXml(string value)`.
- Private thin wrappers `TryReadWimHeader` / `TryReadXmlMetadataBlock`.

- [x] **Step 1: Write `WinREIntelligenceService.cs`**:
  - `ResolveWinREPath` = `Path.Combine(mountPath, WinREImageService.EmbeddedWinREPath)` (reuses the constant — never a hardcoded second string).
  - `BytesToMB` = `Math.Round(bytes / 1048576.0, 2)`.
  - `TryParseWimHeader(byte[] bytes)`: `null` for a short buffer (<208) or non-`MSWIM\0\0\0` signature; otherwise a `BinaryReader` over the byte buffer reading the fixed offsets (signature, HeaderSize@8, Version@12, Flags@16, CompressionType@20, reserved@24..30, WIMGuid@31, PartNumber@47, NumberOfParts@49, ImageCount@51, BootIndex@59, boot metadata offset (skip)@67, MetadataOffset@75, TotalBytes@83). `VersionMajor = Version >> 16`, `VersionMinor = Version & 0xFFFF`, `VersionText = $"{major}.{minor}"`; `CompressionTypeName = MapCompressionType(...)`. Never throws.
  - `MapCompressionType`: `1`→`LZX`, `2`→`XPRESS`, `3`→`LZMS`, else `"Unknown (n)"`.
  - `TryExtractXmlImageDisplayName(byte[] xmlBytes)`: detect `FF FE` (UTF-16LE) / `EF BB BF` (UTF-8) BOM, else UTF-8 fallback; then `ExtractFirstElementText(xml, "DISPLAYNAME")`.
  - `ExtractFirstElementText`: case-insensitive `IndexOf("<NAME>")` → inner text until `"</NAME>"`, `Trim()`, empty → null.
  - `UnescapeXml`: `&amp; &lt; &gt; &quot; &apos;`.
  - `Inspect`: validate `mountPath` (null/empty → `ArgumentException`, missing dir → `DirectoryNotFoundException`); resolve path; absent → negative report + verbose; present → fill `FileInfo` fields; `TryReadWimHeader` → `WimHeader` + flatteners + `WimHeaderParsed`; `detailed` → `TryReadXmlMetadataBlock` at `MetadataOffset` → display name. Each read caught → `_callbacks.Warning` + downgrade.
- [x] **Step 2: Build** to confirm it compiles.

### Task 3: Get-WindowsImageWinRE cmdlet

**Files:**
- Create: `src/Cmdlets/GetWindowsImageWinRECmdlet.cs`

**Interfaces:**
- `[Cmdlet(VerbsCommon.Get, "WindowsImageWinRE")]` / `[OutputType(typeof(WinREIntelligenceReport))]`.
- `-ImagePath` `DirectoryInfo` (mandatory, position 0, `ValueFromPipeline` + `ValueFromPipelineByPropertyName`), `-Detailed` switch.
- `ProcessRecord`: validate dir exists (non-terminating error + return), `LogOperationStartWithTimestamp`, `new WinREIntelligenceService(ModuleCallbacks.FromCmdlet(this)).Inspect(...)`, `WriteObject`, `LogOperationCompleteWithTimestamp`; catch → `LoggingService.WriteError` + rethrow.

- [x] **Step 1: Write `GetWindowsImageWinRECmdlet.cs`** following the `GetINFDriverListCmdlet` / `AddSetupCompleteActionCmdlet` parameter + logging conventions exactly (`ComponentName` const, `LoggingService.*`, `ModuleCallbacks.FromCmdlet`).
- [x] **Step 2: Build** to confirm it compiles.

### Task 4: Unit tests

**Files:**
- Create: `tests/PSWindowsImageTools.Tests/WinREIntelligenceServiceTests.cs`

- [x] **Step 1: Create `WinREIntelligenceServiceTests.cs`** — plain xUnit `[Fact]`s, no mock framework, temp-dir `IDisposable` fixture (pattern from `WinREImageServiceTests.cs`):
  - `ResolveWinREPath` canonical nested path.
  - `BytesToMB` rounding cases.
  - `TryParseWimHeader`: null/short/bad-signature → null; synthetic 208-byte header → round-trip raw + interpreted fields (`0x000D0000` → VersionText `"13.0"`, CT `2` → `"XPRESS"`, image count, part geometry, GUID, offsets).
  - `MapCompressionType` 1/2/3/other.
  - `TryExtractXmlImageDisplayName` UTF-16LE + UTF-8, missing element → null, entity unescaping.
  - `Inspect` end-to-end with a synthetic WIM file (header + padding + XML at MetadataOffset): `detailed` populates `XmlImageDisplayName`; non-detailed leaves it null; absent `Winre.wim` → `WinREPresent = false`; structural path validation.
- [x] **Step 2: Run the filtered unit tests** `dotnet test tests/PSWindowsImageTools.Tests/PSWindowsImageTools.Tests.csproj --filter "FullyQualifiedName~WinREIntelligence"` and confirm they pass.

### Task 5: Help file

**Files:**
- Create: `docs/help/Get-WindowsImageWinRE.md`

- [x] **Step 1: Write `docs/help/Get-WindowsImageWinRE.md`** in PlatyPS format using `docs/help/Get-WindowsImageComponentStore.md` as the template (front matter `external help file: PSWindowsImageTools.dll-Help.xml`, `Module Name: PSWindowsImageTools`), documenting `-ImagePath`, `-Detailed`, and `-ProgressAction`, with SYNOPSIS/DESCRIPTION/EXAMPLES/INPUTS/OUTPUTS.

### Task 6: Final verification

Files: none.

- [x] **Step 1: Build** `dotnet build src/PSWindowsImageTools.csproj` (0 errors; retry once if concurrent `.obj`/file-lock errors).
- [x] **Step 2: Run filtered unit tests** (same filter as Task 4 / Step 2).
- [x] **Step 3: Integration note** — real-image `Get-WindowsImageWinRE` verification is manual/CI-only (requires a mounted image with a genuine `Winre.wim`); this feature never calls DISM, so the local `OpenOfflineSession` limitation does not block it, but real-mount assertions belong on a healthy host/CI. (No Pester changes made.)
- [x] **Step 4: Final report** — spec + plan paths, exact cmdlet name, report model fields (especially WIM-header-derived version/identity + parse method), test counts, deviations. Leave working tree uncommitted.