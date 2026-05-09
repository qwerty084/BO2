# Mystery-Box And Weapon Alias Tracking

The current box tracker is built on script notify capture plus a best-effort read of weapon aliases from the notify owner object.

## Relevant Notify Names

Box-related notify targets:

- `randomization_done`
- `user_grabbed_weapon`
- `chest_accessed`
- `box_moving`
- `weapon_fly_away_start`
- `weapon_fly_away_end`
- `arrived`
- `left`
- `closed`

Only `randomization_done` and `user_grabbed_weapon` attempt weapon alias recovery in production.

## Production Alias Flow

For `randomization_done` and `user_grabbed_weapon`:

1. The detour matches the notify `stringValue` to a resolved target.
2. It calls original `vm_notify`.
3. It resolves child variables for the notify `inst`.
4. It scans child ids `1..0x1FFFF`.
5. It filters entries where:
   - entry type masked with `0x7F` is script string (`2`) or istring (`3`);
   - packed key parent id equals the notify `ownerId`;
   - decoded string is a likely Zombies weapon alias.
6. It publishes the first accepted alias in `GameEventRecord.WeaponName`.

This scan is intentionally broad. It does not currently rely on a specific field name such as `weapon_string`.

## Alias Decoding

Weapon alias strings are script string IDs stored in child-variable values.

String decode path:

- Read string table base from `0x02BF83A4`.
- Compute `base + stringId * 0x18 + 0x04`.
- Copy up to `MaxWeaponNameBytes - 1` printable ASCII characters.
- Reject empty, out-of-range, non-readable, or non-printable strings.

Alias validation:

- Lowercase ASCII letters, digits, and underscore only.
- Length greater than 3.
- Length less than 64 bytes.
- Must end with `_zm`.

Publication validates the alias again before writing it into shared memory.

## Managed Display Flow

Snapshot v6 stores weapon aliases in `GameEventRecord.WeaponName[64]` at record offset `84`.

Managed reader:

- Decodes the UTF-8/null-terminated weapon name.
- Stores it on `GameEvent.WeaponName` when nonblank.
- Uses `GameEventFormatter` and `WeaponDisplayNameResolver` to render display names.

Known aliases are mapped to readable names, for example `ray_gun_zm` -> `Ray Gun`. Unknown aliases are displayed as the raw alias.

## Field-Specific Tooling

The runtime tool `tools/Find-BO2BoxWeaponByGhidraLayout.ps1` supports a more exact lookup path:

- Resolves `weapon_string`, `grab_weapon_name`, and `zbarrier` through the live script string table.
- Encodes field names as `stringId + 0x10000`.
- Uses the child-variable hash formula to look for exact fields under owner objects.
- Can dump owner fields and scan weapon-like values.

This path is not used by production `BO2Monitor.dll` today. It is useful for future narrowing once live evidence proves the owner and field relationship for each box notify.

## Runtime Validation, 2026-05-09

Read-only Town validation was performed before a box spin and again while the box weapon was visible and the game was paused.

Live script string IDs in the visible-box state:

| Name | Result |
|---|---:|
| `weapon_string` | not found in the live string table |
| `grab_weapon_name` | not found in the live string table |
| `zbarrier` | `7453` |
| `randomization_done` | `7492` |
| `user_grabbed_weapon` | `7429` |

`tools/Search-BO2ScriptFieldBytes.ps1` and `tools/Find-BO2BoxWeaponByGhidraLayout.ps1` now tolerate missing field names and continue with available IDs. In this Town session they could only search the `zbarrier` exact path; no `weapon_string` or `grab_weapon_name` exact field path was available to test.

A strict child-variable scan found many existing `_zm` string/istring values, including normal weapon aliases such as `beretta93r_zm`, `m14_zm`, `mp5k_zm`, `python_zm`, and upgraded variants. These were global child-table observations, not notify-owner observations. Without the `vm_notify` owner ID for `randomization_done` or `user_grabbed_weapon`, they do not prove the box alias lifetime or an exact owner field path.

Current field-specific recommendation:

- Do not replace the production broad owner scan with exact lookup yet.
- Exact lookup is not viable as the only strategy on the observed Town process because `weapon_string` and `grab_weapon_name` were absent from the live script string table even while the box weapon was visible.
- If exact lookup is later implemented, use exact lookup first only when all required field IDs resolve, then fall back to the current broad owner scan.

## Static Evidence

Ghidra string search in `bo2-ghidra-recon.txt`:

- Did not find `randomization_done`, `user_grabbed_weapon`, `weapon_string`, `grab_weapon_name`, or `chest_accessed` as plain ASCII in the static binary.
- Found `zbarrier` in `.rdata` at multiple addresses.
- Found `giveweapon` and `takeweapon` strings.

This suggests the tracked notify names may come from script assets or runtime string tables rather than simple static ASCII in the executable. The runtime resolver path is therefore the right production strategy.

## Failure Modes

Alias recovery can fail while the event still publishes:

- Script alias tables are unreadable.
- Notify owner id is zero or outside `1..16383`.
- Child table pointer for the instance is unavailable.
- No owner child string/istring value looks like a Zombies weapon alias.
- The original notify moves or clears the alias before the post-call scan.

When alias recovery fails, the event is still useful for sequence/timing, but the UI cannot show the weapon name.

## Next Best Improvement

The next feature issue should capture `inst`, `ownerId`, and `stringValue` for `randomization_done` and `user_grabbed_weapon`, then run exact and broad owner scans against that owner before and after the original `vm_notify` returns. The x32dbg headless path crashed during this pass, so prefer a manually supervised GUI x32dbg session with hardware breakpoints, or add explicit monitor diagnostics in a controlled non-production build if write/injection validation is allowed.
