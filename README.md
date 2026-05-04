# BO2

WinUI 3 desktop app for read-only Black Ops II Zombies stat and event inspection.

The app targets the current Steam Zombies `t6zm.exe` build, runs as a 32-bit Windows desktop app, and packages native 32-bit monitor/helper binaries alongside the managed UI. Steam Zombies is the only variant with full stat and event-monitor support; other detected BO2 variants are shown as unsupported.

## Prerequisites

- Windows 10 1809 or newer.
- .NET 8 SDK.
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

## Runtime Notes

- The app reads process memory and monitor snapshots but does not write game memory.
- Event capture depends on the native monitor reaching a compatible snapshot state.
- Mystery-box weapon tracking intentionally uses the proven `randomization_done` path and snapshot v6 weapon-name field.
- Widget settings are stored under `%LocalAppData%\BO2\widgets.json`; invalid settings are moved to a timestamped backup and defaults are restored.

## Packaging

Packaging is single-project MSIX. `Package.appxmanifest` declares `runFullTrust` because the app needs desktop process access. Package identity, publisher, signing, and distribution metadata should be reviewed before external release.

## Reference Notes

Repo-maintained runtime findings live in the GitHub wiki:

- [Confirmed Memory Addresses](https://github.com/qwerty084/BO2/wiki/Confirmed-Memory-Addresses)
- [BO2 Box Weapon Tracking](https://github.com/qwerty084/BO2/wiki/BO2-Box-Weapon-Tracking)
- [Ghidra and x32dbg Workflow](https://github.com/qwerty084/BO2/wiki/Ghidra-and-x32dbg-Workflow)
