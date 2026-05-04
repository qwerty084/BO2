# Copilot Instructions

## Build, test, and lint

- Build solution: `dotnet build .\BO2.slnx`.
- Build 32-bit Windows app: `dotnet build .\BO2.csproj -p:Platform=x86`.
- Run non-UI unit tests: `dotnet test BO2.Tests\BO2.Tests.csproj`.
- Run native C++ unit tests: `.\tools\Run-NativeTests.ps1 -Configuration Release`.
- Create the standard .NET editor configuration with `dotnet new editorconfig` when adopting or refreshing the formatter baseline.
- Apply C# formatter fixes with `dotnet format .\BO2.slnx -v detailed --severity info`.
- Check C# formatting without editing files with `dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info`.
- Treat formatter commands as separate from build and test validation: use the fix command to apply supported formatting and code-style changes, then use the check-only command to verify no formatting changes remain. Build and tests remain the behavior validation path.
- Formatter checks should run at `info` severity going forward so information-level C# style suggestions are included in repo policy.
- When changing GitHub Actions workflows under `.github\workflows\`, test affected workflow locally with `act` before handoff. For Windows jobs, prefer host-backed run: `act workflow_dispatch -W .github\workflows\build.yml -j windows -P windows-latest=-self-hosted`; clean `.act-*` folders after.
- Tests pure xUnit, no WinAppSDK runtime; target `net8.0-windows10.0.19041.0`, do NOT reference WinUI.

## Architecture

- Single-project WinUI 3 desktop app. `BO2.csproj` targets `net8.0-windows10.0.19041.0`, uses Windows App SDK + single-project MSIX tooling.
- `App.xaml` merges `XamlControlsResources` for app-wide WinUI resources.
- `App.xaml.cs`: launch entry. `OnLaunched` creates `MainWindow`, stores private `_window`, activates it.
- `MainWindow.xaml`: full UI shell; one `Window` with `MicaBackdrop` and app main UI content, including player stats + candidate details. `MainWindow.xaml.cs` only initializes partial class.
- Packaging in project, no separate packaging project. `Package.appxmanifest` defines identity, logos, capabilities; `app.manifest` carries unpackaged compatibility + DPI.
- `Properties\launchSettings.json`: two local run profiles: packaged (`MsixPackage`) and unpackaged (`Project`).

## Key conventions

- Keep XAML `x:Class`, code-behind partial class names, and `BO2` namespace in sync when renaming/moving UI types.
- Preserve `_window` field pattern in `App.xaml.cs`; app keeps window reference at application level, not transient launch local.
- Add shared styles, brushes, app-wide resources in `App.xaml`, not directly in `MainWindow.xaml`.
- Packaging assets: keep manifest asset basenames stable. `Package.appxmanifest` references `Assets\Square150x150Logo.png`; physical files in `Assets\` use scale-qualified names like `.scale-200.png`.
- Treat manifest capabilities + packaging settings carefully. App declares restricted `runFullTrust` capability for desktop process access.
- If change depends on output architecture or native/runtime packaging behavior, specify `Platform=x86`; app supports only 32-bit Windows builds.

## Agent skills

### Issue tracker

Issues + PRDs tracked as local markdown files under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Repo uses default five-label triage vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

Repo uses single-context domain docs layout. See `docs/agents/domain.md`.
