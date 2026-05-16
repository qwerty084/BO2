# BO2

WinUI 3 desktop app for read-only Black Ops II Zombies stat and event inspection.

The app targets the current Steam Zombies `t6zm.exe` build, runs as a 32-bit Windows desktop app, and packages native 32-bit monitor/helper binaries alongside the managed UI. Steam Zombies is the only variant with full stat and event-monitor support; other detected BO2 variants are shown as unsupported.

## Prerequisites

- Windows 10 1809 or newer.
- .NET 10 SDK, as pinned by `global.json`. The app still targets .NET 8.
- Visual Studio 2022 or newer with the .NET desktop, Windows App SDK/MSIX, and Desktop development with C++ workloads.
- Visual C++ MSBuild targets for Win32 native builds.

The solution is Windows-only. The app project defaults to `Platform=x86` and rejects other platforms.

## Build

```powershell
dotnet build .\BO2.slnx
```

Build the production 32-bit app payload:

```powershell
dotnet build .\BO2.csproj --configuration Release -p:Platform=x86
```

The managed project builds and copies these native payloads into the app output:

- `BO2Monitor.dll`
- `BO2InjectorHelper.exe`

If native MSBuild is not discoverable, install the Visual C++ workload or pass `NativeMSBuildExe` to a native `MSBuild.exe` path.

## Test

```powershell
dotnet test .\BO2.Tests\BO2.Tests.csproj
```

`BO2.Tests` is a non-UI xUnit project. It links testable service source files directly and uses fakes for process discovery, memory reads, and resource strings so tests stay deterministic and do not require WinAppSDK runtime startup or a live game process.

Run native C++ unit tests for repo-owned **Event Monitor** and injector-helper logic:

```powershell
.\tools\Run-NativeTests.ps1 -Configuration Release
.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook
```

`BO2.NativeTests` uses Microsoft Native Unit Test Framework through Visual Studio and covers deterministic native behavior for notify publication, shared snapshot writer OS contracts, polling fallback decisions, hook compatibility decisions, and injector helper export-resolution/orchestration logic. It does not require a live BO2 process and does not automate live DLL injection, validate MinHook internals, test third-party source, or write Detected Game memory.

Live Steam Zombies verification is covered by the [native smoke test](docs/native-smoke-test.md).

Run repo validation checks used by CI:

```powershell
dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info
.\tools\Generate-EventMonitorSnapshotContract.ps1 -Check
```

## Runtime Notes

- Player stats and timing reads use read-only process memory access.
- Event capture is explicit-connect live process access: the app injects `BO2Monitor.dll`, starts it in Steam Zombies, and the monitor may install hooks for supported event capture. The app does not write game stats or gameplay state.
- Event capture depends on the native monitor reaching a compatible snapshot state.
- Mystery-box event display uses the snapshot v6 weapon-name field. The native monitor attempts alias recovery for `randomization_done` and `user_grabbed_weapon`; treat only `randomization_done` as the roll-result source for future averages until more event-boundary evidence exists.
- User preferences are stored under `%LocalAppData%\BO2\preferences.json`.
- Widget settings are stored under `%LocalAppData%\BO2\widgets.json`; invalid settings are moved to a timestamped backup and defaults are restored.
- Game History is stored under `%LocalAppData%\BO2\game-history.sqlite`.

## Packaging

Packaging is single-project MSIX. `Package.appxmanifest` declares `runFullTrust` because the app needs desktop process access. Package identity, publisher, signing, and distribution metadata should be reviewed before external release.

## Documentation

Repo-maintained docs are canonical under [docs](docs/index.md). Runtime and reverse-engineering findings from the former external wiki were migrated into [docs/reverse](docs/reverse/index.md); see [docs/migration/wiki-migration.md](docs/migration/wiki-migration.md) for the migration note.

Useful entry points:

- [Project context](CONTEXT.md)
- [Validation commands](docs/validation/index.md)
- [Release and signing notes](docs/release-signing.md)
- [Reverse-engineering index](docs/reverse/index.md)
- [Runtime address ledger](docs/reverse/address-ledger.md)
- [Event pipeline and snapshot bridge](docs/reverse/event-pipeline.md)
- [Map validation and Game History support](docs/reverse/map-validation.md)
- [Dvars and map identity](docs/reverse/dvars-and-map-identity.md)
- [In-game and round timers](docs/reverse/timers.md)
- [Mystery-box weapon tracking](docs/reverse/box-weapon-tracking.md)
- [Ghidra and x32dbg workflow](docs/reverse/ghidra-x32dbg-workflow.md)
