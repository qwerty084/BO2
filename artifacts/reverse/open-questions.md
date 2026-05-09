# t6zm.exe Open Questions

This file records what remains unresolved after the static Ghidra pass and the 2026-05-09 read-only Town runtime checks.

## Highest Priority

1. Confirm live hook installation on Steam build 65428 when write/injection validation is explicitly allowed.
   Read-only runtime validation confirmed the live process hash and the original bytes at `0x008F31D0`, but MinHook installation requires monitor injection and code patching. No `BO2MonitorSharedMem-<pid>` map existed in the read-only run, so discovery events were not observed through the snapshot.

2. Confirm box weapon alias lifetime.
   Production reads owner child variables after original `vm_notify` returns. A later read-only Town continuation found candidate parent `901` with `town_chest`, `treasure_chest_use`, and `python_zm` under `tag_knob` both while the box weapon was visible and after pickup. This is strong post-state evidence for a box-looking owner carrying the expected alias, but it still did not capture `inst`/actual `ownerId` for `randomization_done` or `user_grabbed_weapon`. Elevated GUI x32dbg verified the view at `t6zm.exe:0x008F31D0` and set a hardware execute breakpoint, but BO2 raised another access violation during resume before either target notify was captured.

3. Recover field-specific box weapon path.
   In the observed Town processes, `zbarrier` resolved to `7453` or `7452`, but `weapon_string` and `grab_weapon_name` were absent from the live string table before the box spin, while the box weapon was visible, and after pickup. Candidate parent `901` stored the observed alias as `tag_knob -> python_zm`, which looks like a model/tag field rather than a semantic box weapon field. A field-specific lookup cannot replace the broad scan unless stable field names are recovered across events/maps.

4. Deepen `scr_var_glob` structure.
   `0x02DEA400` is a useful region anchor, but the exported catalog has no direct xrefs to the exact address. Nearby pointer slots (`0x02DEFB00`, `0x02DEFB80`) have better evidence.

## Player-State Gaps

5. Validate local-player candidate fields against live state changes.
   Position has the best structural evidence. Velocity, gravity, speed, ADS, view angles, height, and ammo are still mostly repo-map assumptions.

6. Recover the local-player state type from static xrefs.
   The address block around `0x02346AA0` should be typed from code references rather than only from contiguous manually discovered addresses.

7. Recover GEntity array semantics.
   `0x021C56C0`, `0x021C9B28`, and stride `0x31C` are plausible, but current production does not iterate entities. Confirm health/team/class offsets before building features on them.

8. Confirm scoreboard stat ownership.
   Points/kills/downs/revives/headshots are app-critical and stable in current repo assumptions, but the distinction between primary, alternate, and secondary stat copies needs a named structure.

9. Validate timing pointers in live transitions.
   `sv_running`, `cl_paused`, and `client_active + 0x50/+0x58` are unit-tested through fakes but should be watched in menu, loading, paused, and in-game states.

## Static Analysis Follow-ups

10. Name the three callers of `local_vm_notify_entry`.
   Ghidra found callers at `0x006787f9`, recursive `0x008f3220`, and `0x008f5d04`. The caller purposes should be recovered before expanding notify coverage.

11. Determine why `SL_GetStringOfSize` is called with type `6`.
   Current code treats that as the correct mode for notify name resolution. The enum meaning is still unnamed.

12. Build a multi-launch live string-id capture log.
   This pass recorded one Town process and observed that IDs can move between process launches. Capture more maps/process starts before making broader claims about stability.

## Resolved In 2026-05-09 Runtime Pass

- Live build provenance matched Steam build `65428` by MD5/SHA256.
- Live bytes at `0x008F31D0` matched the expected `vm_notify` prologue before hook installation.
- Live bytes at `0x008F3620` confirmed it is inside the `CALL 0x0067C1B0` immediate, not a function entry.
- Live bytes at `0x00418B40` matched the expected `SL_GetStringOfSize` prologue.
- Runtime script string table and child-variable pointer slots were readable and populated.
- `tools/Search-BO2ScriptFieldBytes.ps1` was fixed and validated in read-only mode against the Town process.
- `tools/Find-BO2BoxWeaponByGhidraLayout.ps1` now tolerates missing exact field names and continues with available IDs.
- Ghidra project functions were renamed and conservatively typed for `local_vm_notify_entry`, `sl_get_string_of_size`, `scr_find_variable`, `scr_get_variable_value`, `scr_get_variable_value_address`, `scr_set_variable_field`, and `scr_find_object`.
- Ghidra project globals were labelled/commented for the script string pointer, child table slots, callback table, and notify remap globals.
- `function-catalog.csv` now includes Ghidra prototypes, calling convention, callers, callees, and xrefs without raw decompiler snippets.

## Runtime Confirmation Boundary

Dynamic confirmation should stay read-only:

- Ask the user to start BO2 and control the game.
- The x32dbg headless path crashed during this pass. A later elevated GUI x32dbg hardware-breakpoint retry also produced an access violation before target notify capture. Treat x32dbg attach as rejected for this validation path unless a new, safer debugger plan is established.
- Prefer the app's normal monitor shared-memory event record or an explicitly approved diagnostic monitor build for future owner capture; passive after-the-fact scans can miss transient notify-only state.
- Use x32dbg only for passive breakpoints, registers, stack, and memory inspection.
- Do not add anti-cheat bypass, stealth, process hiding, or arbitrary memory-writing behavior.
