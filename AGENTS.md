# Copilot Instructions

## Build, test, and lint

- Build the solution with `dotnet build .\BO2.slnx`.
- Build the 32-bit Windows app with `dotnet build .\BO2.csproj -p:Platform=x86`.
- Run non-UI unit tests with: `dotnet test BO2.Tests\BO2.Tests.csproj`
- Tests are pure xUnit (no WinAppSDK runtime); target `net8.0-windows10.0.19041.0` but do NOT reference WinUI.
- No repo-specific lint or format command/config checked in.

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
- Be careful with manifest capabilities and packaging settings. App declares the restricted `runFullTrust` capability for desktop process access.
- If change depends on output architecture or native/runtime packaging behavior, specify `Platform=x86`; the app only supports 32-bit Windows builds.

## Agent skills

### Issue tracker

Issues and PRDs are tracked as local markdown files under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

This repo uses the default five-label triage vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

This repo uses a single-context domain docs layout. See `docs/agents/domain.md`.
