# Native Smoke Test

Use this checklist for the live BO2 path that CI cannot exercise.

## Setup

- Build the app with `dotnet build .\BO2.csproj -p:Platform=x86`.
- Launch the packaged app profile.
- Start Steam Black Ops II Zombies so `t6zm.exe` is running.

## Checklist

- The app detects Steam Zombies and does not report an unsupported variant.
- Connect succeeds and the event monitor reaches a compatible state.
- Player stats update while in a match.
- A mystery-box roll produces a box event rendered as `randomization_done: <weapon alias>` in the app and widget.
- Disconnect requests monitor shutdown, returns the UI to disconnected state, and does not crash.
- Closing BO2 while connected returns the UI to disconnected state on the next refresh.

## Notes

- The app should remain read-only: do not add memory writes or BO2 process modification as part of this smoke test.
- Preserve the snapshot v6 `randomization_done` weapon-name path unless new wiki-backed evidence proves it changed.
