# Chat And Console Write Research

This page preserves research from the former wiki for possible future text-sending features. It is not current BO2 product behavior.

## Current Product State

BO2 desktop does not expose chat, console, command-buffer, reliable-command sending, command registration, parser dispatch calls, or direct reliable-queue edits. BO2 remains a read-only inspection app at the product level.

## Research Summary

Migrated research notes say BO2's normal chat UI path formats command text such as `say "<text>"\n` or `say_team "<text>"\n`, then submits it through a reliable-command writer candidate at `0x00574360`. These addresses and `FUN_*` labels are not yet present in the repo function catalog, address ledger, tools, source, or tests, so treat them as unverified until provenance is added.

The same unverified migrated research listed:

| Area | Function / address | Finding |
| --- | ---: | --- |
| Chat command registration | `FUN_0046BD90` | Registers `say` and `say_team`. |
| Public chat mode handler | `0x007E80C0` | Opens public chat mode through normal UI flow. |
| Team chat mode handler | `0x007E80E0` | Opens team chat mode through normal UI flow. |
| Chat submit handler | `FUN_007EA760` | Formats `say` / `say_team` command text and submits it. |
| Reliable command writer candidate | `FUN_00574360` | Appends command text through the engine path. |
| Command parser | `FUN_00513290` | Dispatches `say` and `say_team`. |

The important design conclusion is negative: do not build a feature by writing reliable-queue fields directly, calling GSC dvar wrappers, or assuming another engine's command-buffer address.

## Future Design Requirements

Any future implementation must have a separate PRD and live local/offline validation. Minimum boundaries:

- Route through the explicit Connect lifecycle.
- Enable only for a supported active Steam Zombies session.
- Validate executable version, prologues, and required globals before enabling.
- Bound message length.
- Sanitize quotes and newlines.
- Rate-limit sends.
- Fail closed if game state, helper compatibility, or thread/context assumptions are uncertain.
- Validate whether calls must be marshaled to the game's main/client thread.

## Open Questions

- Whether `FUN_00574360` is safe from the monitor worker thread or requires game-thread marshalling.
- Behavior while paused, in menus, loading, host migration, or disconnected states.
- Whether team chat is meaningful in solo Zombies and other Zombies modes.
- Exact maximum safe message length after sanitization.
- Whether visible chat UI opening through `chatmodepublic` / `chatmodeteam` is preferable for user trust.
