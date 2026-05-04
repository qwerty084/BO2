# Native Smoke Test

Use this checklist for live Steam Zombies behavior that CI cannot exercise. Automated native tests already cover deterministic repo-owned native behavior; this smoke test is for behavior that requires a real Steam Zombies `t6zm.exe` process.

## Automated coverage

CI runs the native test suite in both supported configurations:

```powershell
.\tools\Run-NativeTests.ps1 -Configuration Release
.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook
```

Those tests cover deterministic **Event Monitor** and injector-helper behavior that does not need a live Detected Game:

- **Event Monitor** notify publication, including event type, level-time, owner, value, tick, weapon alias handling, and dropped-notify publication.
- Shared snapshot writer OS contract, including named mapping, update event, stop event, initialization, and cleanup.
- Polling fallback decisions for readable values, valid bounds, and change filtering.
- Hook compatibility decisions for hook-disabled, unsupported-version, capture-disabled, compatible, and string-resolution outcomes.
- Injector helper export-resolution and orchestration decisions using fakeable Windows API adapters.

These automated tests do not start Steam Zombies, attach to a live `t6zm.exe`, validate real MinHook patching, perform live DLL injection into BO2, or write Detected Game memory.

## Setup

- Build the solution with `dotnet build .\BO2.slnx`.
- Build the production 32-bit app payload with `dotnet build .\BO2.csproj --configuration Release -p:Platform=x86`.
- Run the native tests:

```powershell
.\tools\Run-NativeTests.ps1 -Configuration Release
.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook
```

- Launch the packaged app profile.
- Start Steam Black Ops II Zombies so `t6zm.exe` is running.

## Live Steam Zombies checklist

- Detected Game detection: the app detects Steam Zombies and does not report an unsupported variant.
- Successful connection: Connect succeeds against the live Detected Game.
- Monitor compatibility: the **Event Monitor** reaches a compatible state for the running Steam Zombies build.
- Live stat refresh: player stats update while in a match.
- Real box-event capture: a mystery-box roll produces a box event rendered as `randomization_done: <weapon alias>` in the app and widget.
- Disconnect cleanup: Disconnect requests monitor shutdown, returns the UI to disconnected state, and does not crash.
- Game-exit recovery: closing BO2 while connected returns the UI to disconnected state on the next refresh.

## Notes

- The app should remain read-only: do not add Detected Game memory writes or BO2 process modification as part of this smoke test.
- Preserve the snapshot v6 `randomization_done` weapon-name path unless new wiki-backed evidence proves it changed.
