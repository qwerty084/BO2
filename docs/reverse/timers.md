# In-Game And Round Timers

This page documents the v1 timer model for the current Steam Zombies `t6zm.exe` build. The implementation is read-only and scoped to solo behavior.

## Scope

| Field | Value |
| --- | --- |
| Process | `t6zm.exe` |
| Mode | Zombies |
| Build | Current Steam 32-bit build |
| Supported timer scope | Solo v1 |
| Access | Read-only process memory plus Event Monitor lifecycle events |

V1 does not promise co-op pause correctness, timer widgets, timer history, pause badge UI, previous-round history UI, or native Event Monitor shared snapshot timer fields.

Unsupported, invalid, stale, or temporarily unavailable timing affects only timer display. Player stats, Event Monitor status, game events, and widgets should continue to work independently.

## Timing Sources

| Source | Address / path | Read type | Purpose |
| --- | ---: | --- | --- |
| `sv_running` current value | `0x02A09F00` | byte bool | Coarse active-match gate. |
| `cl_paused` current value | `0x02A09DE0` | `int32` bool | Solo pause evidence. |
| Client active pointer | `uint32[0x0119DC04]` | `uint32` pointer | Base pointer for client timing fields. |
| Snapshot valid | `clientActivePtr + 0x50` | `int32` | Must be `1` before timing is trusted. |
| Game time milliseconds | `clientActivePtr + 0x58` | `int32` ms | Primary memory-backed game/level time source. |

`sv_running` must be read as a byte. A previous runtime regression came from reading `0x02A09F00` as `int32`, which can include neighboring bytes and produce a non-boolean value while a match is active. `Services/GameTimingReader.cs` now uses `ReadByte` for this value.

## App Model

`Game Timing Read` is separate from `Player Stats Read`. It reports one timer-only result state:

| Result | Meaning |
| --- | --- |
| Supported timing | A valid game-time sample is available. |
| Unsupported timing | The detected game has no timing map. |
| Invalid timing source state | Timing state is internally inconsistent or not ready. |
| Inactive lobby state | `sv_running` indicates the game is not in an active match. |
| Generic read failure | Process memory could not be read. |

`Game Timer State` belongs to `Game Connection Session`. It combines timing reads with Event Monitor lifecycle events and publishes placeholder, active, or frozen timer display state through the `Game Connection Snapshot`.

## Lifecycle Events

| Event | Timer behavior |
| --- | --- |
| `start_of_round` round 1 | Arms or captures the in-game timer baseline and starts the round timer baseline when a valid timing read is available. |
| `start_of_round` later rounds | Starts or resets the round timer baseline. It does not invent a missing in-game timer if round 1 was missed. |
| `end_of_round` | Freezes the current round timer at the last known good value and leaves it visible between rounds. |
| `end_game` | Freezes any known in-game and round timer values. Missing timers remain placeholders. |
| Inactive/lobby timing state | Clears timer state after inactive/lobby state is confirmed. |
| Disconnect or Detected Game change | Clears timer state. |

If a lifecycle event arrives before a valid timing read, `Game Timer State` keeps a pending baseline and captures it on the first valid timing read. Event Monitor sequence gaps fail closed: timers preserve only trustworthy known values or placeholders until a reset boundary is observed.

## Display Rules

- The in-game timer remains a placeholder until round 1 start is observed for the current match.
- The round timer can start on any observed round start, even when the in-game timer is still a placeholder because round 1 was missed.
- Timers display whole seconds through the shared duration formatter.
- Negative or backward game-time samples are invalid/stale and are not clamped into normal output.
- Generic read failures do not clear final or in-progress values.
- Missing or unsupported timer state projects placeholders.

## Validation Status

Town solo validation on 2026-05-09 confirmed pre-match placeholders, round 1 timer start, active advancement, pause freeze, resume, and round transition freeze/reset after the `sv_running` byte-read fix.

Remaining validation gaps before broader support claims:

- Game-over freeze in a complete live match.
- Lobby clear after game exit or post-game transition.
- Full process restart repeat.
- Co-op and non-host pause behavior.
- Degraded timing-state behavior under read failures or event sequence gaps.
