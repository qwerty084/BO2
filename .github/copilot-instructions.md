# Copilot Instructions

## Build, test, and lint

- Build the solution with `dotnet build .\BO2.slnx`.
- Build arch with `dotnet build .\BO2.csproj -p:Platform=x64`; platforms: `x86`, `x64`, `ARM64`.
- Run non-UI unit tests with: `dotnet test BO2.Tests\BO2.Tests.csproj`
- Tests are pure xUnit (no WinAppSDK runtime); target `net8.0-windows10.0.19041.0` but do NOT reference WinUI.
- No repo-specific lint or format command/config checked in.

## Repository knowledge

- Complex tasks: consult GitHub wiki before design and implementation: https://github.com/qwerty084/BO2/wiki.
- Memory reading, address discovery, CDB/WinDbg usage, BO2 Zombies runtime data: check wiki pages such as `Confirmed-Memory-Addresses` for confirmed addresses + validation notes.
- For mystery-box weapon tracking, use `BO2-Box-Weapon-Tracking`: https://github.com/qwerty084/BO2/wiki/BO2-Box-Weapon-Tracking.
- For Ghidra/x32dbg reverse-engineering workflows, use `Ghidra-and-x32dbg-Workflow`: https://github.com/qwerty084/BO2/wiki/Ghidra-and-x32dbg-Workflow.
- Durable future-agent findings go in repo wiki, not ad-hoc local notes. Use `repo-wiki-notes`.
- Never store secrets, credentials, raw sensitive memory dumps, anti-cheat bypass techniques, process hiding techniques, code injection workflows, or memory-writing instructions in wiki.

## Architecture

- Single-project WinUI 3 desktop app. `BO2.csproj` targets `net8.0-windows10.0.19041.0`, uses Windows App SDK + single-project MSIX tooling.
- `App.xaml` merges `XamlControlsResources` for app-wide WinUI resources.
- `App.xaml.cs`: launch entry. `OnLaunched` creates `MainWindow`, stores private `_window`, activates it.
- `MainWindow.xaml`: full UI shell; one `Window` with `MicaBackdrop` and the app's main UI content, including sections such as player stats and candidate details. `MainWindow.xaml.cs` only initializes partial class.
- Packaging lives in project, no separate packaging project. `Package.appxmanifest` defines identity, logos, capabilities; `app.manifest` carries unpackaged compatibility + DPI.
- `Properties\launchSettings.json`: two local run profiles: packaged (`MsixPackage`) and unpackaged (`Project`).

## Key conventions

- Keep XAML `x:Class`, code-behind partial class names, and `BO2` namespace in sync when renaming or moving UI types.
- Preserve `_window` field pattern in `App.xaml.cs`; app keeps window reference at application level, not transient local during launch.
- Add shared styles, brushes, app-wide resources in `App.xaml`, not directly in `MainWindow.xaml`.
- Packaging assets: keep manifest asset basenames stable. `Package.appxmanifest` references `Assets\Square150x150Logo.png`; physical files in `Assets\` use scale-qualified filenames such as `.scale-200.png`.
- Be careful with manifest capabilities and packaging settings. App declares both `runFullTrust` and `systemAIModels`.
- If change depends on output architecture or native/runtime packaging behavior, specify `Platform`; project builds for `x86`, `x64`, `ARM64`.
