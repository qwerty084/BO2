# Runtime Validation Notes, 2026-05-09

Scope: read-only validation against Steam Zombies `t6zm.exe` build `65428` on Town. The user controlled the game.

## Process Provenance

- Live PID used for the final checks: `29172`.
- Path: `C:\Program Files (x86)\Steam\steamapps\common\Call of Duty Black Ops II\t6zm.exe`.
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

x32dbg headless attach was attempted once for passive attach/detach validation. It did not exit cleanly, and the game crashed after the headless process was stopped. No further debugger work was performed.

MinHook installation was not tested because it requires monitor injection and code patching, which is outside the read-only constraint for this pass.
