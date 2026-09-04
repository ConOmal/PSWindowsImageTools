# Registry Drift Detection — Design

**Date:** 2026-09-04
**Status:** Ready for planning
**Parent deliverable:** first backlog phase after Phase 1 (component store, drivers,
inventory/SBOM, validation), which declared "Registry drift" a non-goal.

## Problem

A Windows golden image can drift from its baseline in ways the current snapshot
pipeline (`Get-WindowsImageSnapshot` / `Compare-WindowsImage`) cannot see.
Packages, features, capabilities, AppX, software and drivers are captured and
diffed, but **registry state is not**. Autostart entries (Run/RunOnce), security
policy (Policies keys), Windows Update configuration, Winlogon/autologon
settings, installed-software signatures (Uninstall) and the installed service
set (Services) are first-order drift signals for golden-image validation — a
machine that "passes" the inventory comparison can still differ in exactly the
places operations teams most care about.

The module already reads offline hives in memory (`RegistryHiveReader` via the
`Registry.dll` Registry package, no hive mounting, no persistent file handles),
but only on demand (`Get-RegistryHiveOnDemand`) or for software/version/update
enumeration. There is no way to capture a comparable, serializable registry
fingerprint per image and diff one image (or saved baseline) against another.

## Goals

1. Capture a defined, documented set of drift-relevant registry keys and values
   from a mounted image's offline hives as part of the existing
   `Get-WindowsImageSnapshot` output.
2. Report registry drift — added / removed / changed values per hive — through
   the existing `Compare-WindowsImage` path, including the category tally that
   drives `TotalDifferences`.
3. Keep the snapshot self-contained: registry data round-trips through the
   existing snapshot JSON export/load (`ImageComparisonService.SaveSnapshot` /
   `LoadSnapshot`) so a baseline can be captured once and diffed later without
   re-mounting.
4. Keep key selection, value normalization and diffing logic pure and
   unit-testable without hive files, DISM sessions or real images.
5. Reuse the in-memory hive-reading pattern exactly — no hive mounting, no
   persistent file handle, same `RegistryHiveOnDemand`/`RegistryKey` surface the
   module already uses.

## Non-goals

- **New cmdlets.** The existing snapshot/compare pipeline is the right surface:
  it already provides capture, JSON export, reload and per-category diff. Adding
  a parallel `Get-WindowsImageRegistrySnapshot` / `Compare-WindowsImageRegistry`
  pair would duplicate inventory plumbing and force manifest + help work for no
  user-facing gain. Registry drift is delivered by extending `ImageSnapshot`
  with a `Registry` category and `ImageComparisonResult` with a
  `RegistryDrift` object.
- **Whole-hive capture or triage.** Only a defined key set is captured (bounded,
  ~15 keys and 2 subkey-name signatures per image). No registry
  targeting/triage logic (that stays a separate `Get-RegistryHiveOnDemand`
  concern).
- **Writes / repair.** Detection only; no key/value creation, no drift
  reconciliation.
- **Online machines.** Offline mounted image hives only (`<mount>\Windows\
  System32\config\{SOFTWARE,SYSTEM}`), matching the existing
  `RegistryHiveReader` scope.
- **Drift "meaning" beyond compare.** We surface *what* changed per value
  (previous/current data, before/after type); interpreting *whether* a change
  is good/bad is the operator's call via the compare output.

## Architecture

All additions follow the existing service + model split. No cmdlet changes, no
manifest changes, no help-file changes, no new NuGet/assembly dependencies
(`Registry.dll` and the comparison models already exist). The capture block
mirrors `ImageComparisonService.CaptureSnapshot`'s existing
`RegistryHiveReader` usage, and the diff mirrors `CompareCategory`.

### New files

**`src/Models/RegistryDriftModels.cs`**

- `RegistrySnapshotValue` — one captured registry value:
  `Hive` (`HKLM\SOFTWARE` / `HKLM\SYSTEM`), `KeyPath` (definition key path,
  e.g. `Microsoft\Windows\CurrentVersion\Run`), `ValueName` (`(Default)` for the
  default value), `ValueType` (friendly `REG_*` string as returned by the
  registry package), `ValueData` (normalized string), and `FullPath`
  (`Hive\KeyPath\ValueName`) for a stable diff identity.
- `RegistryDriftKeyDefinition` — `Hive`, `KeyPath`, `RegistryKeyCaptureMode`
  (`Values` = direct value-name/value pairs of that key;
  `SubKeyNames` = sorted direct child subkey names as a signature), and a
  human-readable `Description`.
- `RegistryHiveDifference` — one hive's diff: `Hive`, plus `Added`,
  `Removed` (each `List<RegistrySnapshotValue>`) and `Changed`
  (`List<RegistryValueChange>`); `Count => Added + Removed + Changed`.
- `RegistryValueChange` — `Hive`, `KeyPath`, `ValueName`, `ValueType`
  (current type), `PreviousData`, `CurrentData`, and `FullPath`.
- `RegistryDriftResult` — `ReferenceName`, `DifferenceName`,
  `ReferenceValueCount`, `DifferenceValueCount`, `Hives:
  List<RegistryHiveDifference>`, `HasRegistryData` (any captured value on either
  side), `TotalDifferences` and `AreIdentical` (true when both sides have no
  data *or* zero differences).

**`src/Services/RegistryDriftService.cs`** (`_callbacks`, `ModuleCallbacks`-
aware, mirroring `RegistryHiveReader`)

- `private const string ServiceName = "RegistryDriftService"`.
- `public const string SoftwareHiveName = "HKLM\\SOFTWARE"` and
  `public const string SystemHiveName = "HKLM\\SYSTEM"`.
- `public static List<RegistryDriftKeyDefinition> GetDefaultDriftKeyDefinitions()`
  — the documented 16-entry default key set (see below).
- `public List<RegistrySnapshotValue> CaptureDriftValues(
  IRegistryHiveReader reader, string mountPath,
  IReadOnlyList<RegistryDriftKeyDefinition>? definitions = null)` —
  thin hive-reading path. Groups definitions by hive, calls
  `registryReader.OpenHive(hivePath)` + `registryReader.GetKey(hive, keyPath)`,
  and funnels each key's `Values` (as `ValueName`, `ValueType`, normalized
  `ValueData`) or `SubKeys` (as `KeyName` strings) into the pure
  `AppendCapture`. Missing hive → verbose note + skip; missing key → skip;
  per-key exceptions → `_callbacks.Warning` + continue (matching the
  `CollectSoftwareEntries` pattern).
- `internal static string ResolveHivePath(string mountPath, string hive)` —
  `Path.Combine(mountPath, "Windows", "System32", "config", hive ==
  "HKLM\SOFTWARE" ? "SOFTWARE" : hive == "HKLM\SYSTEM" ? "SYSTEM" :
  hive.Replace('\\', '_'))` (fallback keeps the helper total for arbitrary
  hive names; the default set only uses the two real names).
- `internal static List<RegistrySnapshotValue> CaptureValues(
  string hive, RegistryDriftKeyDefinition definition, IEnumerable<
  (string ValueName, string ValueType, string ValueData)> values)` — pure;
  projects the definition-mode `Values` capture into `RegistrySnapshotValue`s.
- `internal static List<RegistrySnapshotValue> CaptureSubKeyNames(
  string hive, RegistryDriftKeyDefinition definition, IEnumerable<string>
  subKeyNames)` — pure; projects the `SubKeyNames`-mode capture (each child
  name becomes a value entry with `ValueName = name`, `ValueType = "SubKey"`,
  data empty). Sorting by `FullPath` here keeps snapshot output deterministic.
- `internal static void AppendCapture(string hive,
  RegistryDriftKeyDefinition definition, IEnumerable<
  (string ValueName, string ValueType, string ValueData)> values,
  IEnumerable<string> subKeyNames, List<RegistrySnapshotValue> output)` — pure;
  routes by `definition.Mode` to the two capturers above. Unit-testable with
  hand-built tuples — the tests never need hive files.
- `internal static string NormalizeValueData(string? data)` — pure; collapses
  `\r\n`/`\r` to `\n`, trims, and returns `string.Empty` for null. Determinism
  for `REG_MULTI_SZ`/binary relies on the registry package's `ValueData`
  decoding being stable for identical bytes (same parse path the existing
  `RegistryHiveReader` already trusts).
- `internal static RegistryDriftResult CompareRegistry(string referenceName,
  string differenceName, List<RegistrySnapshotValue> reference,
  List<RegistrySnapshotValue> difference)` — pure diff. Groups by hive
  (ordinal-ignore-case), builds `FullPath` → value dictionaries per side
  (first wins on duplicates, like `CompareCategory`), emits `Added`
  (difference-only paths), `Changed` (same path, different data or type) and
  `Removed` (reference-only paths), each sorted by `FullPath`. Sets
  `ReferenceValueCount` / `DifferenceValueCount` / `HasRegistryData`.

### Modified files

**`src/Models/ImageComparisonModels.cs`**

- `ImageSnapshot.Registry: List<RegistrySnapshotValue>` (new category).
- `TotalItems` gains `+ Registry.Count`. (The existing `ToString` /
  `SBOM` mapping are unaffected.)

**`src/Services/ImageComparisonService.cs`**

- `CaptureSnapshot`: new try/catch block after the software block —
  `using var registryReader = new RegistryHiveReader(_callbacks)` then
  `new RegistryDriftService(_callbacks).CaptureDriftValues(registryReader,
  mountPath)` appended to `snapshot.Registry`. Failure path: warning + continue
  (snapshot still returned), matching every other capture block.
- `Compare`: after the six existing `CompareCategory` calls adds
  `result.Categories.Add(CompareRegistryCategory(reference, difference))` and
  `result.RegistryDrift = RegistryDriftService.CompareRegistry(...)`.
- `private static CategoryDifference CompareRegistryCategory(ImageSnapshot
  reference, ImageSnapshot difference)` — pure; maps both `Registry` lists to
  `SnapshotItem`s (`Name = FullPath`, `State = ValueType`,
  `Detail = ValueData`) and reuses the private `CompareCategory`, so registry
  diffs also flow into the existing `TotalDifferences` /
  `AreIdentical` totals without changing their semantics.

### Default drift key set (`GetDefaultDriftKeyDefinitions`)

`Values` mode captures the direct **values** of the key; `SubKeyNames` mode
captures the **sorted direct child subkey names** as a signature (bounded — no
recursion).

| Hive | KeyPath | Mode | Why it matters for drift |
| --- | --- | --- | --- |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Run` | Values | autostart entries |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\RunOnce` | Values | one-shot autostart |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\System` | Values | UAC / logon / shutdown policy |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Policies\Explorer` | Values | shell / Explorer policy |
| HKLM\SOFTWARE | `Policies\Microsoft\Windows\WindowsUpdate` | Values | WSUS / Update policy |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update` | Values | AU configuration |
| HKLM\SOFTWARE | `Microsoft\Windows NT\CurrentVersion\Winlogon` | Values | autologon, shell, logon UI |
| HKLM\SOFTWARE | `Microsoft\Windows\CurrentVersion\Uninstall` | SubKeyNames | installed-software signature (native) |
| HKLM\SOFTWARE | `WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` | SubKeyNames | installed-software signature (WOW64) |
| HKLM\SYSTEM | `ControlSet001\Control\ComputerName\ComputerName` | Values | computer name |
| HKLM\SYSTEM | `ControlSet001\Control\Session Manager` | Values | boot execute / memory config |
| HKLM\SYSTEM | `ControlSet001\Control\Lsa` | Values | LSA / security policy |
| HKLM\SYSTEM | `ControlSet001\Control\Session Manager\Environment` | Values | system environment variables |
| HKLM\SYSTEM | `ControlSet001\Services\Tcpip\Parameters` | Values | DHCP / DNS suffix / hostname |
| HKLM\SYSTEM | `ControlSet001\Control\Terminal Server` | Values | RDP state (`fDenyTSConnections`) |
| HKLM\SYSTEM | `ControlSet001\Services` | SubKeyNames | installed service set signature |

Drift-relevant and stable:

- `Run`/`RunOnce` and `Winlogon` are the classic golden-image autostart drift
  signal; their value **names** (e.g. a staged `MyAgent`) are the diff's
  identity, data compared as well.
- `Uninstall`/`Services` signatures use subkey **names** so the capture is
  compact and stable, and changes (a driver installing a service, a program
  uninstalling) surface as add/remove of names rather than a noise storm.
- Policies keys are included by direct **values**; policy subkeys are not
  enumerated (they blow up verbosity for little signal).
- `SYSTEM\ControlSet001` is the canonical control set; `ControlSet002`/`003`
  backups intentionally mirror it and are excluded to avoid duplicate diffs.

> Capture correctness of the real hive files is verified manually on a real
> image (task 4 below); everything cross-checks offline against `SOFTWARE` on a
> mounted image.

## Data Flow

```
Get-WindowsImageSnapshot
   └─► ImageComparisonService.CaptureSnapshot
         └─► RegistryDriftService.CaptureDriftValues
               ├─► IRegistryHiveReader.OpenHive(<mount>\Windows\System32\config\SOFTWARE)
               ├─► IRegistryHiveReader.OpenHive(<mount>\Windows\System32\config\SYSTEM)
               └─► pure AppendCapture / NormalizeValueData
         └─► ImageSnapshot.Registry (+ TotalItems)
   └─► ImageComparisonService.SaveSnapshot  (Registry round-trips via JSON)

Compare-WindowsImage (mounted pair or two snapshot JSON files)
   └─► ImageComparisonService.Compare
         └─► CompareRegistryCategory ──► CategoryDifference ("Registry")
         └─► RegistryDriftService.CompareRegistry ──► RegistryDriftResult
               └─► ImageComparisonResult.Categories / .RegistryDrift / .TotalDifferences
```

## Error Handling

- Capture never throws out of `CaptureSnapshot`: the registry block is wrapped
  like every other capture block (`catch` → `_callbacks.Warning` → snapshot
  still returned). Missing `SOFTWARE`/`SYSTEM` hives produce an empty `Registry`
  list and a verbose note, matching `RegistryHiveReader`'s missing-file
  behavior.
- Per-key read failures inside `CaptureDriftValues` are caught (warning +
  continue) so one bad key never drops the whole category; this mirrors
  `CollectSoftwareEntries`.
- `Compare` additively builds `RegistryDriftResult`; if both snapshots have no
  registry data, the result is `AreIdentical = true` with `HasRegistryData =
  false` — an older pre-registry snapshot JSON stays comparable without errors.

## Testing

- **Unit (xUnit, `tests/PSWindowsImageTools.Tests/RegistryDriftServiceTests.cs`,
  plus additions to `ImageComparisonServiceTests.cs`)** — all pure, no hives,
  no DISM, no registry files:
  - `AppendCapture` / `CaptureValues` / `CaptureSubKeyNames` routes by
    definition mode and produces expected `FullPath` (`(Default)` default
    name, `SubKey` type for name signatures).
  - `NormalizeValueData` collapse rules (`\r\n`/`\r` → `\n`, trim, null →
    empty).
  - `GetDefaultDriftKeyDefinitions` is non-empty, mode-valid, and references
    only the two known hive names.
  - `ResolveHivePath` maps `HKLM\SOFTWARE`/`HKLM\SYSTEM` to the real config
    file names under a temp `mountPath` (temp-dir fixture pattern from
    `ImageComparisonServiceTests.cs`).
  - `CompareRegistry`: empty-vs-empty → identical, no data; equal values →
    identical; added / removed / changed per hive are reported and sorted by
    `FullPath`; a same-path value with a different type is `Changed` not
    `Added`/`Removed`.
  - `ImageComparisonService.Compare`: registry changes flow into the
    `Registry` category and `RegistryDrift`, and an identical pair (with
    matching registry) is still `AreIdentical`.
  - Snapshot JSON round-trip preserves `Registry` (add a registry value to the
    `MakeSnapshot` fixture, `SaveSnapshot` → `LoadSnapshot`).
- **Integration (Pester, `tests/integration/PSWindowsImageTools.Integration.Tests.ps1`)**:
  only a manual/CI note — a real mounted image is required to exercise the
  actual `SOFTWARE`/`SYSTEM` files. The local DISM
  `OpenOfflineSession` servicing limitation documented in `docs/OpenCode-EngLog.md`
  means real-image snapshot/compare already runs manually/CI-only; registry
  capture rides the same path and stays manual.

## Risks

- **Value decoding differences.** `ValueData` for `REG_BINARY`/`REG_MULTI_SZ`
  is the registry package's stable string decode; equal bytes must decode
  equally across captures on the same image. The package drives the existing
  `RegistryHiveReader`, so this risk is pre-existing and unchanged.
- **Snapshot size.** Uninstall (native + WOW64) and the `Services` signature
  add roughly 1–2k `RegistrySnapshotValue` entries per image. Bounded and
  documented; acceptable for a drift fingerprint.
- **`ControlSet001` assumption.** If an image's active control set is not
  `ControlSet001` the SYSTEM captures could be stale; the key set documents
  `ControlSet001` as canonical. Low likelihood for standard installs.
- **No new env/test surface.** Because no cmdlets, manifest entries or help
  files change, the existing `verify-help.ps1` guardrail and psd1
  `CmdletsToExport` stay untouched — drift is additive to serialized
  snapshots and compare output only.