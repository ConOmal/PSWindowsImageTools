# WinRE Intelligence — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** phase-1 spec backlog item "WinRE intelligence beyond what exists".

## Problem

The module already knows how to **work with** an embedded WinRE image but not how to
**report on** it. `WinREImageService` extracts and re-embeds the nested
`Windows\System32\Recovery\Winre.wim` from inside a mounted image, and
`Mount-WindowsImageList` / `Dismount-WindowsImageList` auto-mount / re-embed a
`MountedWindowsImage.WinRE` companion. But there is nothing that answers the
first-order operations question: *does this golden image carry a WinRE image, how
big is it, when was it last touched, and what is it actually?*

Querying the embedded WinRE image via DISM servicing is unreliable on this host:
the local machine's DISM API servicing is broken (`OpenOfflineSession` fails —
documented in `OpenCode-EngLog.md`), so the command must not depend on `DismApi`.
The WIM file format itself is self-describing enough for a lightweight, pure
file-based inspection: a fixed 208-byte header carries the format version, image
count, boot index, part information, compression type and a GUID; the uncompressed
XML metadata (reachable via the header's metadata offset) carries the image-level
identity such as the `<DISPLAYNAME>`. Both are readable with pure `BinaryReader` /
`Encoding` code and a bounded file read — no servicing, no WIMGAPI, no admin.

## Goals

1. Add one query cmdlet, `Get-WindowsImageWinRE` (`-ImagePath` = mounted image
   directory), that reports on the embedded WinRE image: presence, full path, size
   (`SizeBytes` / `SizeMB`), `LastModifiedUtc`, plus version/identity fields parsed
   directly from the WIM file header (format version, image count, boot index,
   part geometry, compression type, WIM GUID).
2. Add a `-Detailed` switch that additionally reads the WIM XML metadata at the
   header's metadata offset (bounded, best-effort) to recover the first image's
   display name as a real identity field.
3. Keep every piece of logic pure I/O-free and unit-testable: path resolution, the
   WIM header parser, byte→MB formatting, XML decoding/element extraction are
   `internal static` and driven by a synthetic byte layout in temp-directory tests.
4. Honor the "no DISM servicing" constraint: the entire feature is pure file
   inspection over the mounted image's `Winre.wim`, reusing
   `WinREImageService.EmbeddedWinREPath` for path resolution.
5. No new dependencies, no manifest/help plumbing beyond the standard cmdlet, and
   no changes to any service owned by concurrent phases.

## Non-goals

- **No servicing / WIMGAPI.** No `DismApi`, no WIM mount, no image apply. The
  command reads the file and its header/XML only; anything that needs servicing is
  the existing `ExtractEmbeddedWinRE` / `Mount-WindowsImageList` surface.
- **No modification.** Inspection only — nothing is extracted, copied, re-embedded,
  or repaired.
- **No full WIM metadata parse.** We parse the fixed header fully and pull exactly
  one bounded string out of the XML metadata (first `<IMAGE>`'s `<DISPLAYNAME>`).
  No XML document model, no resource table, no per-file/overlay enumeration.
  Decompression of LZX/XPRESS resource data is out of scope; the XML metadata block
  is read raw and decoded as text (bounded best-effort).
- **No enumeration of a user-supplied `.winre.wim` for the source WIM's own image
  list** — `-ImagePath` is always a mounted image directory; the report covers the
  *embedded* WinRE image only.
- **No online/offline WinRE configuration queries** (e.g. `reagentc`
  enablement/`WinRE-WindowsRE` package status). This is a file-presence/intelligence
  command, not a configuration query.

## Architecture

Follows the existing service + model + cmdlet split. New files only; the only
existing type read is `WinREImageService.EmbeddedWinREPath` (constant, read-only).
No psd1 change (orchestrator appends `CmdletsToExport`), no new NuGet/assembly
dependencies (netstandard2.0, LangVersion 8.0, Nullable enable, no C# 9+ syntax).

### New files

**`src/Models/WinREIntelligenceModels.cs`**

- `WinREIntelligenceReport` — `ImagePath`, `WinREPresent`, `WinREPath`,
  `SizeBytes`, `SizeMB`, `LastModifiedUtc`, `WimHeaderParsed`, convenience identity
  flatteners (`WimVersion`, `ImageCount`, `CompressionType`), the full optional
  `WimHeader` object, and `XmlImageDisplayName` (populated only under `-Detailed`);
  `ToString()` summary.
- `WimHeaderInfo` — the parsed 208-byte header: `IsValid`, `HeaderSize`, `Version`
  (raw), `VersionMajor` (= `Version >> 16`), `VersionMinor` (= `Version & 0xFFFF`),
  `VersionText` (`"{major}.{minor}"`), `Flags`, `CompressionType` (raw),
  `CompressionTypeName`, `WimGuid`, `PartNumber`, `NumberOfParts`, `ImageCount`,
  `BootIndex`, `MetadataOffset`, `TotalBytes`; `ToString()`.

**`src/Services/WinREIntelligenceService.cs`** (`_callbacks`-aware, mirroring
`ComponentStoreService`)

- `private const string ServiceName = "WinREIntelligenceService"`.
- `public WinREIntelligenceReport Inspect(string mountPath, bool detailed)` — thin:
  resolves the embedded path, returns a negative report when absent, reads
  `FileInfo` metadata (size/timestamp), best-effort reads the 208-byte header, and
  under `detailed` best-effort reads the XML metadata block from the header's
  `MetadataOffset`. Never throws for file problems — each read is caught and
  downgraded (verbose/warning + partial report), mirroring the report-building
  style of `ComponentStoreService.Analyze`.
- `internal static string ResolveWinREPath(string mountPath)` — pure;
  `Path.Combine(mountPath, WinREImageService.EmbeddedWinREPath)`.
- `internal static double BytesToMB(long bytes)` — pure; `Math.Round(bytes /
  1048576.0, 2)`.
- `internal static WimHeaderInfo? TryParseWimHeader(byte[] bytes)` — pure; requires
  ≥208 bytes and signature `MSWIM\0\0\0`; reads the fixed header fields (offsets
  below); returns `null` for a non-WIM/short buffer (never throws).
- `internal static string MapCompressionType(uint type)` — pure; `1`→`LZX`, `2`→
  `XPRESS`, `3`→`LZMS`, else `"Unknown (n)"`.
- `internal static string? TryExtractXmlImageDisplayName(byte[] xmlBytes)` — pure;
  detects UTF-16LE (`FF FE`) / UTF-8 (`EF BB BF`) BOMs, decodes, finds the first
  `<DISPLAYNAME>... </DISPLAYNAME>`, trims and unescapes basic XML entities.
- `internal static string? ExtractFirstElementText(string xml, string elementName)`
  and `internal static string UnescapeXml(string value)` — pure element extraction.
- Private thin readers: `TryReadWimHeader(string path)` (open file, read first 208
  bytes) and `TryReadXmlMetadataBlock(string path, long metadataOffset, long
  maxLength)` (seek + bounded read).

**WIM header field layout (little-endian, offsets in bytes)** — fixed 208-byte
header, fields read via a packed layout with the well-known 7-byte reserved block
that puts the GUID at the misaligned offset `0x1F`:

| Offset | Size | Field |
| --- | --- | --- |
| 0x00 | 8 | Signature (`MSWIM\0\0\0`) |
| 0x08 | 4 | HeaderSize (expected 208) |
| 0x0C | 4 | Version (major = high 16 bits, minor = low 16 bits) |
| 0x10 | 4 | Flags |
| 0x14 | 4 | CompressionType (1=LZX, 2=XPRESS, 3=LZMS) |
| 0x18 | 7 | Reserved |
| 0x1F | 16 | WIMGuid |
| 0x2F | 2 | PartNumber |
| 0x31 | 2 | NumberOfParts |
| 0x33 | 8 | ImageCount |
| 0x3B | 8 | BootIndex |
| 0x43 | 8 | BootMetadataOffset (skipped) |
| 0x4B | 8 | MetadataOffset |
| 0x53 | 8 | TotalBytes |

> Version interpretation follows the wimlib/`file` convention: format version `13`
> is standard WIM (`Version` = `0x000D0000` → VersionMajor 13, VersionMinor 0 →
> `"13.0"`). `0x000F0000` (15.0) appears on newer WIMs/ESDs. The raw value is also
> exposed as `Version` so an unexpected encoding is never misreported.

**`src/Cmdlets/GetWindowsImageWinRECmdlet.cs`**

- `[Cmdlet(VerbsCommon.Get, "WindowsImageWinRE")]`, `[OutputType(typeof(
  WinREIntelligenceReport))]`.
- Parameters: `-ImagePath` (mandatory, position 0, `ValueFromPipeline` +
  `ValueFromPipelineByPropertyName`, `DirectoryInfo`, mirrors the
  `Get-WindowsImageComponentStore` image-path style and the
  `Install-WindowsImageUpdate`/`Add-SetupCompleteAction` `-ImagePath` convention);
  `-Detailed` (switch).
- `ProcessRecord`: validates the directory exists (non-terminating error +
  return), logs via `LoggingService.LogOperationStartWithTimestamp` /
  `LogOperationCompleteWithTimestamp`, builds `ModuleCallbacks.FromCmdlet(this)`,
  calls `new WinREIntelligenceService(...).Inspect(...)`, `WriteObject(report)`;
  catch → `LoggingService.WriteError` + rethrow (`-ErrorAction Stop`
  compatible, matching the existing cmdlets).

## Data Flow

```
Get-WindowsImageWinRE -ImagePath <mount> [-Detailed]
   └─► WinREIntelligenceService.Inspect
         ├─► ResolveWinREPath  ──► <mount>\Windows\System32\Recovery\Winre.wim
         ├─► FileInfo           ──► SizeBytes / SizeMB / LastModifiedUtc
         ├─► TryReadWimHeader   ──► TryParseWimHeader(byte[208])
         │                          └─► WimHeaderInfo (version, image count, ...)
         └─► [Detailed] TryReadXmlMetadataBlock ──► TryExtractXmlImageDisplayName
                                 └─► XmlImageDisplayName (first <IMAGE> name)
   └─► WinREIntelligenceReport ──► WriteObject
```

## Error Handling

- **Absent WinRE** is not an error: `WinREPresent = false` with `WinREPath` set to
  the resolved (non-existent) path; verbose note. The cmdlet still writes a report.
- **Missing/unreadable `-ImagePath`** is the caller's error: cmdlet writes a
  non-terminating `DirectoryNotFoundException`-category error and returns.
- **Unreadable file / too-short buffer / bad signature** never throw out of
  `Inspect`: `TryReadWimHeader` returns `null`, `WimHeaderParsed = false`, and the
  report still carries the file-metadata fields; a warning is raised (rather than
  treating an unexpected WinRE payload as a hard failure).
- **`-Detailed` XML read failures** are warning + `XmlImageDisplayName = null`; the
  header-derived fields remain authoritative. The read is bounded (never more than a
  fixed cap from `MetadataOffset`).
- `Inspect` still validates `mountPath`: null/empty → `ArgumentException`; missing
  directory → `DirectoryNotFoundException` (the thin path stays responsible for
  structural checks; file *content* problems degrade, structural *path* problems
  throw, matching `ComponentStoreService`).

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/WinREIntelligenceServiceTests.cs`)**
  — pure + temp-dir fixtures (the `WinREImageServiceTests` pattern; no mocking, no
  DISM):
  - `ResolveWinREPath` returns the canonical nested path under a temp mount root.
  - `BytesToMB` rounding (0, 1 MB, 350 MB, large).
  - `TryParseWimHeader`: null/short/non-signature → null; a synthetic 208-byte
    header round-trips raw + interpreted fields (version `0x000D0000` → `"13.0"`,
    compression `2` → `"XPRESS"`, image count, part geometry, GUID, metadata offset).
  - `MapCompressionType` for 1/2/3/other.
  - `TryExtractXmlImageDisplayName`: UTF-16LE and UTF-8 XML byte buffers returning
    the first `<DISPLAYNAME>`; missing element → null; entity-unescaped values.
  - `Inspect` end-to-end with a **synthetic WIM file**: temp mount dir, header
    bytes at offset 0, padding, XML bytes at `MetadataOffset`; asserts full report
    both with and without `detailed`, and the `WinREPresent = false` branch.
- **Integration (Pester)** — manual/CI-only note: a real mounted image is required
  to exercise a genuine `Winre.wim`; the local DISM `OpenOfflineSession` servicing
  limitation does not affect this feature (it never calls DISM), but real-mount
  verification still belongs on a healthy host/CI.

## Risks

- **Header layout assumptions.** The fixed offsets are from the published MS-WIM
  format and match the famous misaligned GUID at `0x1F`; the parser validates the
  signature and length first, so a structural surprise degrades to `WimHeaderParsed
  = false` (warn) rather than garbage output.
- **Compressed/odd XML blocks.** If the XML metadata block at `MetadataOffset` is
  compressed (uncommon — the metadata XML is normally stored raw) the decode yields
  no `<DISPLAYNAME>` and the field stays null under `-Detailed`; header identity
  fields are unaffected.
- **Version display.** We interpret `Version` as 16-bit major/minor (wimlib/`file`
  convention) and also expose the raw DWORD, so a future format change is visible
  rather than mislabeled.
- **Concurrent phases.** Only new files are touched; `WinREImageService.cs` and all
  other services/models are read-only, so Reserved-Storage and Edition-servicing
  work is unaffected.