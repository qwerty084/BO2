# t6zm.exe Open Questions

This file records what remains unresolved after the static Ghidra pass and repo-first inventory on 2026-05-09.

## Highest Priority

1. Confirm live hook installation on Steam build 65428.
   Static evidence strongly supports `0x008F31D0`, but this run did not inject into a live `t6zm.exe` process or validate real MinHook patching.

2. Confirm box weapon alias lifetime.
   Production reads the owner child variables after original `vm_notify` returns. A live mystery-box roll should confirm the alias is still attached to the notify owner at that point for `randomization_done` and `user_grabbed_weapon`.

3. Recover field-specific box weapon path.
   The tool path knows `weapon_string`, `grab_weapon_name`, and `zbarrier`, but production currently scans all owner string/istring child variables and accepts the first likely `_zm` alias. A field-specific lookup would be less broad if the owner/field relationship is confirmed.

4. Fix or retire `tools/Search-BO2ScriptFieldBytes.ps1`.
   The script opens `$handle` but `Read-ProcessBytes` reads `$script:handle`; as written it likely fails unless that script-scoped variable exists externally.

5. Deepen `scr_var_glob` structure.
   `0x02DEA400` is a useful region anchor, but the exported catalog has no direct xrefs to the exact address. Nearby pointer slots (`0x02DEFB00`, `0x02DEFB80`) have better evidence.

## Player-State Gaps

6. Validate local-player candidate fields against live state changes.
   Position has the best structural evidence. Velocity, gravity, speed, ADS, view angles, height, and ammo are still mostly repo-map assumptions.

7. Recover the local-player state type from static xrefs.
   The address block around `0x02346AA0` should be typed from code references rather than only from contiguous manually discovered addresses.

8. Recover GEntity array semantics.
   `0x021C56C0`, `0x021C9B28`, and stride `0x31C` are plausible, but current production does not iterate entities. Confirm health/team/class offsets before building features on them.

9. Confirm scoreboard stat ownership.
   Points/kills/downs/revives/headshots are app-critical and stable in current repo assumptions, but the distinction between primary, alternate, and secondary stat copies needs a named structure.

10. Validate timing pointers in live transitions.
   `sv_running`, `cl_paused`, and `client_active + 0x50/+0x58` are unit-tested through fakes but should be watched in menu, loading, paused, and in-game states.

## Static Analysis Follow-ups

11. Name the three callers of `local_vm_notify_entry`.
   Ghidra found callers at `0x006787f9`, recursive `0x008f3220`, and `0x008f5d04`. The caller purposes should be recovered before expanding notify coverage.

12. Rename and type the script VM helpers in the Ghidra project.
   Start with `scr_find_variable`, `scr_get_variable_value`, `scr_get_variable_value_address`, `scr_set_variable_field`, and `scr_find_object`.

13. Resolve notify remap globals.
   `0x024BB4CC`, `0x024BB4CE`, and `0x024BB4D0` are compared when `inst == 0`; identify the script strings they represent at runtime.

14. Determine why `SL_GetStringOfSize` is called with type `6`.
   Current code treats that as the correct mode for notify name resolution. The enum meaning is still unnamed.

15. Build a live string-id capture log.
   Record resolved IDs for all notify targets on build 65428 and note whether IDs are stable between process launches.

## Runtime Confirmation Boundary

Dynamic confirmation should stay read-only:

- Ask the user to start BO2 and control the game.
- Use x32dbg only for passive breakpoints, registers, stack, and memory inspection.
- Do not add anti-cheat bypass, stealth, process hiding, or arbitrary memory-writing behavior.
