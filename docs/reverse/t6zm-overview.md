# t6zm.exe Reverse-Engineering Overview

This pack documents the Steam Zombies executable used by this repo's current live-stat and event-monitoring features. It is scoped to legitimate interoperability, structure recovery, and safer future feature work.

## Binary And Build

- Target binary: `C:\Program Files (x86)\Steam\steamapps\common\Call of Duty Black Ops II\t6zm.exe`
- Steam app id: `202970`
- Steam build id: `65428`
- File size: `13096504` bytes
- MD5: `68C62BE753DE8ADF2C2C7B28DB769B99`
- SHA256: `3645528D61EF0FB0591D5195E111718235EF3C75F2211BD3633A2DB4DE7C67AC`
- Ghidra image base: `0x00400000`
- Ghidra language/compiler: `x86:LE:32:default`, `windows`

All hard-coded game addresses in this pack are current-build virtual addresses. Treat them as build-specific unless a future analysis proves otherwise.

## Repo Entry Points

The repo supports live reads for Steam Zombies only:

- `Services/GameProcessDetector.cs` maps process name `t6zm` to `PlayerStatAddressMap.SteamZombies`.
- Redacted, Plutonium, multiplayer, and single-player processes are detected for UI messaging but do not receive supported address maps.
- `Services/DllInjector.cs` restricts monitor injection to supported Steam Zombies detections and validates Win32/I386 payloads.
- `BO2Monitor/dllmain.cpp` starts the native monitor worker inside the target process.

## Static Analysis Outputs

Primary outputs created by this pass:

- `artifacts/reverse/ghidra/bo2-ghidra-recon.txt`: original Ghidra post-script output with decompile snippets, xrefs, and ASCII searches.
- `artifacts/reverse/function-catalog.csv`: Ghidra-derived function catalog for the important code targets.
- `artifacts/reverse/globals-catalog.csv`: Ghidra-derived global/data catalog for important VM pointers.
- `artifacts/reverse/callgraph-notes.md`: readable callgraph notes exported from Ghidra.
- `artifacts/reverse/address-ledger.csv`: final provenance ledger.
- `artifacts/reverse/address-ledger.seed.csv`: repo-first seed ledger.
- `artifacts/reverse/open-questions.md`: remaining work queue.
- `artifacts/reverse/runtime-validation-2026-05-09.md`: read-only Town runtime notes.

The first Ghidra import completed and saved the project. Ghidra reported Java heap warnings in constant propagation and stack-variable analyzers, but the project, original post-script output, and companion catalogs were produced. The companion exporter was rerun with `-noanalysis` against the saved project.

The 2026-05-09 continuation pass reran the companion exporter with `-noanalysis`, applied durable names/comments and conservative helper signatures in the saved Ghidra project, and refreshed the catalogs. The function catalog now includes Ghidra prototypes, calling conventions, and bounded decompile snippets.

## Subsystem Map

| Subsystem | Current anchor | Main evidence | Notes |
|---|---:|---|---|
| Process/build assumptions | `t6zm`, build `65428` | Steam manifest, Ghidra import, repo detector | Steam Zombies only. |
| Score stats | `0x0234C068..0x0234C08C` | `PlayerStatAddressMap`, native fallback reuse | Points/kills/downs/revives/headshots. |
| Round | `0x0233FA10` | Managed candidate, native round reader, fallback | Used for notify round values and polling. |
| Local player state | base `0x02346AA0` | contiguous candidate map | Mostly optional/debug-level reads. |
| `vm_notify` | `0x008F31D0` | Ghidra function entry, prologue guard, native hook code | Public candidate `0x008F3620` is rejected. |
| String resolver | `0x00418B40` | Ghidra function entry, prologue guard | Used to resolve notify names live. |
| Script string table | pointer `0x02BF83A4` | Ghidra xrefs, native decoder | Entry stride `0x18`, text offset `0x04`. |
| Child variables | slots `0x02DEFB00`, `0x02DEFB80` | Ghidra xrefs, native struct, field tools | Entry size `0x1C`. |
| Box weapon tracking | notify owner child vars | native detour and alias validator | Production scans owner string fields, not field-specific lookup. |
| Snapshot bridge | v6 ABI | JSON contract, generated C#/C++, tests | 128-record ring, stable `WriteSequence`. |
| Polling fallback | round/points/kills/downs | native fallback rules and tests | Used when hook compatibility is unsupported. |

## Confirmed Findings

- `0x008F31D0` is a real function entry in `.text` and starts with the repo's expected 9-byte prologue: `55 8B EC 83 E4 F8 83 EC 44`.
- `0x008F3620` is not a function entry in this binary. Ghidra shows the containing instruction starts at `0x008F361F` as `CALL 0x0067C1B0`, so `0x008F3620` is inside the call immediate.
- `0x00418B40` is a function entry and matches the repo's expected `SL_GetStringOfSize` prologue: `83 EC 0C 8B 54 24 10`.
- `vm_notify` calls the candidate script variable helpers that are now cataloged: `scr_find_variable` (`0x006BFB30`), `scr_get_variable_value` (`0x00485950`), `scr_get_variable_value_address` (`0x0067C1B0`), `scr_set_variable_field` (`0x0058F9E0`), and `scr_find_object` (`0x00474EA0`).
- The shared-memory snapshot ABI is repo-owned and strongly validated by generated constants, C++ static assertions, and native/managed tests.

## Parallel Work Packages

Future agents can split work along these issue boundaries:

1. Executable inventory and seed address ledger.
2. Player stats and player-state structures.
3. Notify hook, compatibility checks, and event pipeline.
4. Script VM, script strings, and child-variable system.
5. Mystery-box and weapon alias flow.
6. Snapshot contract and native/managed bridge verification.
7. x32dbg runtime confirmation.
8. Final docs and wiki-ready knowledge consolidation.

## Dynamic Validation Status

Read-only live validation was performed on 2026-05-09 after the user launched Steam Zombies Town.

Confirmed:

- Running `t6zm.exe` matched build `65428` by MD5/SHA256.
- `0x008F31D0` matched the expected `vm_notify` prologue in live memory before monitor injection.
- `0x008F3620` was rejected again in live memory because it is inside the immediate bytes of the `CALL` at `0x008F361F`.
- `0x00418B40` matched the expected `SL_GetStringOfSize` prologue in live memory.
- Script string table and child variable pointer slots were readable and populated.
- `death`/`disconnect` remap globals resolve to `death_or_disconnect` in the observed process.

Not confirmed:

- MinHook installation and snapshot discovery events were not tested because monitor injection is not read-only.
- `vm_notify` execution reach was not breakpoint-confirmed. x32dbg headless attach failed to detach cleanly and the game crashed. A later elevated GUI x32dbg retry reached a hardware-breakpoint setup at `0x008F31D0`, but BO2 raised another access violation before target notify capture.
- Box weapon alias lifetime under the actual notify owner remains open because no event owner ID was captured. A later passive Town box spin did find candidate parent `901` with `town_chest`, `treasure_chest_use`, and the observed alias `python_zm`, but that is post-state evidence rather than notify-boundary evidence.
