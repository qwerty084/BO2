# Dvars And Map Identity

This page records read-only dvar and map-identity evidence from the migrated wiki and local reverse docs. It is research support for future features, not current product UI behavior.

## Current Product State

- BO2 desktop does not expose map identity UI, FOV/FPS configurators, movement presets, arbitrary dvar reads, or dvar writes.
- Managed production code should prefer read-only process memory and validated app/session gates.
- Future dvar-backed features need a separate design, compatibility checks, user-facing bounds, and live local validation.

## Map Identity

Green Run map identity is split across dvars:

| Dvar | Type | Observed value | Meaning |
| --- | --- | --- | --- |
| `mapname` | string | `zm_transit` | Base map token. |
| `ui_mapname` | string | `zm_transit` | UI-facing base map token. |
| `ui_zm_mapstartlocation` | string | `town`, `farm`, or `transit` | Green Run start-location/submap token. `transit` is shared by TranZit and Bus Depot Survival. |
| `ui_gametype` | string | `zclassic` or `zstandard` | Green Run mode discriminator observed live; separates TranZit from Bus Depot Survival when base/start tokens are shared. |
| `g_gametype` | string | `zclassic` or `zstandard` | Active game type, observed matching `ui_gametype` for the validated TranZit and Bus Depot Survival captures. |
| `party_gametype` | string | `TranZit` or `Survival` | Party-facing mode label observed live; useful corroborating evidence. |
| `ui_zm_gamemodegroup` | enum | `0=zclassic`, `1=zsurvival`, `2=zencounter` | Type `7` enum dvar. Decode the current value as an enum index, not as a string pointer. |

For Green Run, derive the internal token as:

```text
<base map> + "_gump_" + <start location>
```

Examples:

| Base map | Start location | Derived token |
| --- | --- | --- |
| `zm_transit` | `town` | `zm_transit_gump_town` |
| `zm_transit` | `farm` | `zm_transit_gump_farm` |

Do not apply this derivation blindly to `ui_zm_mapstartlocation=transit`. Live validation showed both TranZit and Bus Depot Survival use `mapname=zm_transit`, `ui_mapname=zm_transit`, and `ui_zm_mapstartlocation=transit`. They require mode-aware identity:

| Target | Required observed identity |
| --- | --- |
| TranZit | `mapname=zm_transit`, `ui_zm_mapstartlocation=transit`, `ui_gametype=zclassic`, `ui_zm_gamemodegroup=zclassic`, `party_gametype=TranZit` |
| Bus Depot Survival | `mapname=zm_transit`, `ui_zm_mapstartlocation=transit`, `ui_gametype=zstandard`, `ui_zm_gamemodegroup=zsurvival`, `party_gametype=Survival` |

For maps without a start-location split, fall back to the base `mapname` value. Buried validation on 2026-05-15 proved the first standalone case: active gameplay observed `mapname=zm_buried` and `ui_mapname=zm_buried`, while `ui_zm_mapstartlocation` remained present as `processing` and did not identify a submap. Die Rise, Mob of the Dead, and Nuketown later confirmed the same standalone pattern with active `mapname=zm_highrise` / `ui_mapname=zm_highrise`, active `mapname=zm_prison` / `ui_mapname=zm_prison`, and active `mapname=zm_nuked` / `ui_mapname=zm_nuked`; their observed start-location values `rooftop`, `prison`, and `nuked` are corroborating UI/lobby values, not required identity inputs.

Do not use stale lobby-only fields as standalone identity. In the same Buried run, the lobby had `ui_mapname=zm_buried` but `mapname` still held stale `zm_transit` until the match spawned, and `party_gametype` remained `TranZit` throughout the run. Active `mapname=zm_buried` is the supported promotion evidence for Buried.

Mob of the Dead validation on 2026-05-15 repeated that lobby caveat: before spawn, `ui_mapname=zm_prison` and `ui_zm_mapstartlocation=prison` identified the target while `mapname` still held stale `zm_highrise` from the prior Die Rise run. After spawn, active `mapname=zm_prison` became the promotion evidence. `party_gametype` again remained stale as `TranZit`, so it must not be used as a standalone map discriminator.

Nuketown validation on 2026-05-15 repeated the same active-match rule: before spawn, `ui_mapname=zm_nuked` and `ui_zm_mapstartlocation=nuked` identified the target while `mapname` still held stale `zm_prison` from the prior Mob of the Dead run. After spawn, active `mapname=zm_nuked` became the promotion evidence. `g_gametype=zstandard`, `ui_gametype=zstandard`, and `party_gametype=Survival` are corroborating values for the observed run, not required standalone discriminators.

Static local fastfile names provide candidate Green Run tokens, but they are not enough to promote support. On 2026-05-14, `zone\all` in the local Steam install contained `zm_transit_gump_busstation.ff`, `zm_transit_gump_diner.ff`, `zm_transit_gump_powerstation.ff`, `zm_transit_gump_tunnel.ff`, `zm_transit_gump_cornfield.ff`, `zm_transit_gump_labs.ff`, `zm_transit_gump_forest.ff`, `zm_transit_gump_forest2.ff`, and `zm_transit_gump_bridge.ff`. Live `ui_zm_mapstartlocation` capture still needs to prove which of those are selectable Game History map identities. Diner is Turned-only and is not a current support target for this app.

On 2026-05-14, a live Green Run / Survival / Bus Depot run observed `ui_zm_mapstartlocation=transit` in lobby, active rounds, and post-game. Do not infer Bus Depot support from `zm_transit_gump_busstation.ff`.

On 2026-05-15, focused live captures resolved the TranZit vs Bus Depot ambiguity. TranZit observed `g_gametype=zclassic`, `ui_gametype=zclassic`, and `party_gametype=TranZit`. Bus Depot Survival observed `g_gametype=zstandard`, `ui_gametype=zstandard`, and `party_gametype=Survival` in both lobby and active round 1. A type-aware read of `ui_zm_gamemodegroup` found type `7`, enum domain `0=zclassic`, `1=zsurvival`, `2=zencounter`, and Bus Depot Survival current index `1`.

## Read-Only Dvar Lookup Evidence

Migrated research notes name a native dvar-by-name helper at `0x006DC400` and a dvar bucket table at `0x029F4548`. These addresses are not yet present in the repo ledger, Ghidra catalogs, tools, source, or tests. Treat the lookup model below as unverified until source-backed artifacts or runtime notes are added.

Observed dvar struct layout:

| Offset | Meaning |
| ---: | --- |
| `+0x00` | Name pointer |
| `+0x08` | Lowercase name hash |
| `+0x0C` | Flags |
| `+0x10` | Type |
| `+0x18` | Current value |
| `+0x28` | Latched value |
| `+0x38` | Reset/default value |
| `+0x58` | Next pointer in hash bucket chain |

Migrated notes say direct helper calls reached `0x006DC400` and the type helper `0x0065EBC0` once, while formatter helpers such as `0x004188D0` and `0x006622C0` faulted for formatted string/int/float values from a foreign diagnostic thread. Until repo-backed provenance is added, prefer external read-only bucket traversal research and do not promote these helper addresses into product code.

## Observed Dvars

| Dvar | Type | Observed value | Notes |
| --- | --- | ---: | --- |
| `cg_fov` | float | `65` | FOV research; static range observed as `1..160`. |
| `cg_fovScale` | float | `1` | FOV scale research. |
| `cg_fovMin` | float | `10` | FOV minimum research. |
| `cg_fovExtraCam` | float | `30` | Extra camera FOV research. |
| `com_maxfps` | int | `0` | `0` appears to mean default/uncapped behavior. |
| `r_vsync` | bool | `0` | Active-match current value was off. |
| `g_speed` | int | `190` | Matches the local speed field in the observed match. |
| `player_sprintSpeedScale` | float | `1.5` | Movement tuning research. |
| `player_backSpeedScale` | float | `0.7` | Candidate for future backward movement speed work. |
| `player_strafeSpeedScale` | float | `0.8` | Movement comparison value. |

Heap dvar struct addresses observed during validation are not listed as product dependencies here. Use pointer slots or name lookup when available, and revalidate after any Steam update.

## Future Feature Boundaries

- Do not expose arbitrary dvar reads as a product surface without a design.
- Do not expose dvar writes or movement/FOV/FPS configurators from migrated research alone.
- Do not call script dvar wrappers from app threads just to read values.
- Use user-facing presets and strict validation if a future feature chooses a safe command/config path.
