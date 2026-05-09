# Runtime Validation Notes, 2026-05-09

Scope: read-only validation against Steam Zombies `t6zm.exe` build `65428` on Town. The user controlled the game.

## Process Provenance

- Executable: Steam app `202970` `t6zm.exe` from a local Steam install. The local install path is intentionally omitted.
- MD5: `68C62BE753DE8ADF2C2C7B28DB769B99`.
- SHA256: `3645528D61EF0FB0591D5195E111718235EF3C75F2211BD3633A2DB4DE7C67AC`.

## Read-Only Memory Checks

| Address | Label | Live bytes/value |
|---:|---|---|
| `0x008F31D0` | `local_vm_notify_entry` | `55 8B EC 83 E4 F8 83 EC 44 53 56 8B 75 08 57 8B` |
| `0x008F361F` | call containing rejected public candidate | `E8 8C 8B D8 FF 57 56 89` |
| `0x008F3620` | rejected public candidate | `8C 8B D8 FF 57 56 89 44 24 30 E8 81 AB DC FF 8B` |
| `0x00418B40` | `sl_get_string_of_size` | `83 EC 0C 8B 54 24 10 53 8B 5C 24 1C 55 56 57 8B` |
| `0x02BF83A4` | script string table pointer slot | `0x02BF8880` |
| `0x02DEFB00` | instance 0 child bucket pointer slot | `0x2EE30000` |
| `0x02DEFD00` | instance 1 child bucket pointer slot | `0x2F8D0000` |
| `0x02DEFB80` | instance 0 child variable pointer slot | `0x2E730000` |
| `0x02DEFD80` | instance 1 child variable pointer slot | `0x2F1D0000` |

## Live Script Strings

| Name | ID |
|---|---:|
| `user_grabbed_weapon` | `7429` |
| `chest_accessed` | `7438` |
| `zbarrier` | `7453` |
| `randomization_done` | `7492` |
| `weapon_fly_away_start` | `7510` |
| `weapon_fly_away_end` | `7513` |
| `box_moving` | `7537` |
| `end_game` | `7545` |
| `start_of_round` | `8042` |
| `end_of_round` | `13095` |
| `weapon_string` | not found |
| `grab_weapon_name` | not found |

Observed IDs moved between process launches during the pass. Treat IDs as runtime values only.

## Notify Remap Globals

| Global | Address | Live ID | Live string |
|---|---:|---:|---|
| `vm_notify_remap_a` | `0x024BB4CC` | `5351` | `death` |
| `vm_notify_remap_b` | `0x024BB4CE` | `5352` | `disconnect` |
| `vm_notify_remap_target` | `0x024BB4D0` | `5353` | `death_or_disconnect` |

## Box State

The user spun the mystery box and paused while the weapon was visible.

Findings:

- `weapon_string` and `grab_weapon_name` were still absent from the live string table.
- `zbarrier` remained available as ID `7453`.
- Exact field lookup found no target fields with the Ghidra-proven child table layout.
- Strict `_zm` child-value scanning found many global weapon aliases, but no notify owner was captured, so these do not prove the box owner alias path.

## Debugger Boundary

x32dbg headless attach was attempted once for passive attach/detach validation. It did not exit cleanly, and the game crashed after the headless process was stopped. No further debugger work was performed during the initial validation pass.

MinHook installation was not tested because it requires monitor injection and code patching, which is outside the read-only constraint for this pass.

## Debugger Retry Boundary

A second Town session was started later on 2026-05-09 to attempt owner-scoped box alias capture at the `vm_notify` boundary.

Read-only baseline:

| Item | Live value |
|---|---:|
| `randomization_done` | `7491` |
| `user_grabbed_weapon` | `7436` |
| `zbarrier` | `7452` |
| `weapon_string` | not found |
| `grab_weapon_name` | not found |
| script string table pointer slot | `0x02BF83A4 -> 0x02BF8880` |
| instance 0 child bucket base | `0x2EE30000` |
| instance 0 child variable base | `0x2E730000` |

`tools/Capture-BO2NotifyOwnerAliases.ps1` was added and parser-checked. A smoke run against owner `1` verified that it can resolve current notify IDs and read the same script string/child table pointers with `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION)` and `ReadProcessMemory`.

Elevated GUI x32dbg retry:

- x32dbg was run as administrator and attached to `t6zm.exe`.
- The disassembly view was verified at `t6zm.exe:0x008F31D0`.
- A hardware execute breakpoint was set at `0x008F31D0`.
- The condition targeted the current launch's box notify IDs: `randomization_done = 0x1D43`, `user_grabbed_weapon = 0x1D0C`.
- BO2 raised another `EXCEPTION_ACCESS_VIOLATION` during resume, before any target notify breakpoint was captured.

No `inst`, `ownerId`, pre-notify alias state, or post-notify alias state was captured. Debugger-based notify owner capture is therefore rejected for the current setup. Future validation should prefer either static recovery or an explicitly approved controlled diagnostic monitor path rather than x32dbg attach.

## Read-Only Box Spin Continuation

A later continuation used a paused Town session without x32dbg and without monitor injection. The lobby baseline had readable child table pointers but did not yet resolve the target event names. After Town loaded, the same process resolved:

| Name | Live value |
|---|---:|
| `randomization_done` | `7491` |
| `user_grabbed_weapon` | `7436` |
| `zbarrier` | `7452` |
| `weapon_string` | not found |
| `grab_weapon_name` | not found |

The user spun the box once, reported the visible weapon as `python_zm`, then later picked it up. `tools/Capture-BO2NotifyOwnerAliases.ps1` wrote local JSONL evidence during the run; only the curated findings are kept in the repo.

Strongest passive post-state evidence:

- Candidate parent `901` had `town_chest` and `treasure_chest_use` string fields.
- Candidate parent `901` had `python_zm` under field `tag_knob`.
- That same `python_zm` owner-scoped alias was still present after the Python was picked up.
- The normal monitor shared-memory map for that process was absent, so no production event record with the actual `ownerId` was available in this session.

This narrows the failure analysis: child tables were readable, target strings were present, and at least one box-looking parent had the expected alias. It still does not prove alias lifetime for the actual `randomization_done` or `user_grabbed_weapon` notify owner because no `vm_notify` boundary arguments were captured.
