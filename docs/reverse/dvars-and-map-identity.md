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
| `ui_zm_mapstartlocation` | string | `town` or `farm` | Green Run start-location/submap token. |

For Green Run, derive the internal token as:

```text
<base map> + "_gump_" + <start location>
```

Examples:

| Base map | Start location | Derived token |
| --- | --- | --- |
| `zm_transit` | `town` | `zm_transit_gump_town` |
| `zm_transit` | `farm` | `zm_transit_gump_farm` |

For maps without a start-location split, fall back to the base `mapname` value. Validate at least one non-Green-Run map before treating this as complete map support.

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
