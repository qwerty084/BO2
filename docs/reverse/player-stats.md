# Player Stats And Local Player State

This repo currently reads Steam Zombies stats through fixed virtual addresses in `Services/PlayerStatAddressMap.cs`. The process memory accessor opens the target with `QueryLimitedInformation | VirtualMemoryRead`; there is no Detected Game memory writing in the managed stat reader.

## Detection And Gating

- Steam Zombies process: `t6zm`.
- Supported map: `PlayerStatAddressMap.SteamZombies`.
- Unsupported detected variants: `t6zmv41`, Plutonium bootstrapper with `t6zm`/`t6mp`, `t6mp`, `t6mpv43`, and `t6sp`.
- Current live reads are connection-gated by the game connection session, not free-running before connect.

## Mandatory Score Reads

These reads are mandatory. If one fails, `GameMemoryReader` closes the accessor and propagates/wraps the failure.

| Stat | Address | Type | Notes |
|---|---:|---|---|
| Points | `0x0234C068` | `int32` | Also used by native polling fallback. |
| Kills | `0x0234C080` | `int32` | `points + 0x18`; also used by fallback. |
| Downs | `0x0234C084` | `int32` | `points + 0x1C`; also used by fallback. |
| Revives | `0x0234C088` | `int32` | `points + 0x20`. |
| Headshots | `0x0234C08C` | `int32` | `points + 0x24`. |

The address cluster suggests a scoreboard/stat block. The repo also carries alternate/secondary stat candidates:

- `0x0234C06C`: alternate kills.
- `0x0234C098`: alternate headshots.
- `0x0234C0C0`: secondary kills.
- `0x0234C0FC`: secondary headshots.

Those candidates are best-effort reads and still need named structural provenance.

## Local Player State Candidates

The map stores absolute addresses, but the values imply a local-player block at `0x02346AA0`.

| Field | Address | Inferred offset | Type |
|---|---:|---:|---|
| Position X | `0x02346AC8` | `+0x28` | `float` |
| Position Y | `0x02346ACC` | `+0x2C` | `float` |
| Position Z | `0x02346AD0` | `+0x30` | `float` |
| Velocity X | `0x02346AD4` | `+0x34` | `float` |
| Velocity Y | `0x02346AD8` | `+0x38` | `float` |
| Velocity Z | `0x02346ADC` | `+0x3C` | `float` |
| Gravity | `0x02346B2C` | `+0x8C` | `int32` |
| Speed | `0x02346B34` | `+0x94` | `int32` |
| Last jump height | `0x02346B64` | `+0xC4` | `float` |
| Legacy health | `0x02346C48` | `+0x1A8` | `int32` |
| ADS amount | `0x02346C84` | `+0x1E4` | `float` |
| View angle X | `0x02346C9C` | `+0x1FC` | `float` |
| View angle Y | `0x02346CA0` | `+0x200` | `float` |
| Height int | `0x02346CA4` | `+0x204` | `int32` |
| Height float | `0x02346CA8` | `+0x208` | `float` |
| Player-info health | `0x02346CD8` | `+0x238` | `int32` |
| Ammo slot 0 | `0x02346EC8` | `+0x428` | `int32` |
| Ammo slot 1 | `0x02346ECC` | `+0x42C` | `int32` |
| Lethal ammo | `0x02346ED0` | `+0x430` | `int32` |
| Ammo slot 2 | `0x02346ED4` | `+0x434` | `int32` |
| Tactical ammo | `0x02346ED8` | `+0x438` | `int32` |
| Ammo slot 3 | `0x02346EDC` | `+0x43C` | `int32` |
| Ammo slot 4 | `0x02346EE0` | `+0x440` | `int32` |

These are optional candidate reads. `GameMemoryReader` catches `Win32Exception` and `InvalidOperationException` for each candidate and returns `null`, so failure does not break score reads.

## Round And Timing

Round is shared by managed candidates and native event publication:

- Round: `0x0233FA10`.
- Native `TryReadLiveRoundValue` accepts `1..255`.
- Polling fallback publishes `round_changed` only for increasing values in `2..255`.

Timing uses a separate map:

| Field | Address or offset | Usage |
|---|---:|---|
| `sv_running` | `0x02A09F00` | Nonzero server-running gate. |
| `cl_paused` | `0x02A09DE0` | Pause state. |
| client-active pointer | `0x0119DC04` | Pointer followed by `GameTimingReader`. |
| snapshot valid | `+0x50` | Must be nonzero. |
| game time ms | `+0x58` | In-game elapsed time source. |

The timing path is covered by fake-memory unit tests, not by this static Ghidra pass.

## Passive Runtime Snapshot, 2026-05-09

A single read-only snapshot was captured from the running Town session while the game was paused. This is useful sanity evidence, not full transition validation.

Score and round values:

| Field | Address | Live value |
|---|---:|---:|
| Round | `0x0233FA10` | `1` |
| Points | `0x0234C068` | `18300` |
| Kills | `0x0234C080` | `0` |
| Downs | `0x0234C084` | `0` |
| Revives | `0x0234C088` | `0` |
| Headshots | `0x0234C08C` | `0` |
| Alternate `0x0234C06C` | `0x0234C06C` | `0` |
| Alternate `0x0234C098` | `0x0234C098` | `0` |
| Secondary `0x0234C0C0` | `0x0234C0C0` | `0` |
| Secondary `0x0234C0FC` | `0x0234C0FC` | `0` |

Timing values:

| Field | Address or offset | Live value |
|---|---:|---:|
| `sv_running` | `0x02A09F00` | `12573441` |
| `cl_paused` | `0x02A09DE0` | `1` |
| client-active pointer | `0x0119DC04` | `0x2D3ECEB0` |
| snapshot valid | `client + 0x50` | `1` |
| game time ms | `client + 0x58` | `40500` |

Local-player sample:

- Base: `0x02346AA0`.
- Position at `+0x28/+0x2C/+0x30`: `(2077.0027, 17.107466, 88.125)`.
- Velocity at `+0x34/+0x38/+0x3C`: `(0, 0, 0)`.

This snapshot supports the existing address map for the current build, but it does not prove scoreboard ownership, alternate stat semantics, movement deltas, or timing behavior across transitions.

## GEntity Hints

The map includes GEntity anchors:

- GEntity array: `0x021C56C0`.
- GEntity player health: `0x021C5868` (`array + 0x1A8`).
- Zombie 0 GEntity: `0x021C9B28` (`array + 22 * 0x31C`).
- GEntity size: `0x31C`.

Current production does not iterate the GEntity array. Treat these as weak candidates until static xrefs or live entity iteration confirms the structure.

## Revalidation Checklist

After a Steam update:

1. Recompute `t6zm.exe` hashes and build id.
2. Confirm score addresses in a live match.
3. Confirm `round` in menu, loading, and in-game states.
4. Confirm local-player position changes when moving.
5. Confirm timing pointer chain across menu, pause, and game transitions.
6. Promote only fields with code xrefs or live change evidence from candidate to confirmed.
