# Copilot Instructions

## Build, test, and lint

- Build solution: `dotnet build .\BO2.slnx`.
- Build 32-bit Windows app: `dotnet build .\BO2.csproj -p:Platform=x86`.
- Run non-UI unit tests: `dotnet test BO2.Tests\BO2.Tests.csproj`.
- Run native C++ unit tests: `.\tools\Run-NativeTests.ps1 -Configuration Release`.
- Run native C++ unit tests with the production hook flag: `.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook`.
- Create the standard .NET editor configuration with `dotnet new editorconfig` when adopting or refreshing the formatter baseline.
- Apply C# formatter fixes with `dotnet format .\BO2.slnx -v detailed --severity info`.
- Check C# formatting without editing files with `dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info`.
- Treat formatter commands as separate from build and test validation: use the fix command to apply supported formatting and code-style changes, then use the check-only command to verify no formatting changes remain. Build and tests remain the behavior validation path.
- Formatter checks should run at `info` severity going forward so information-level C# style suggestions are included in repo policy.
- When changing GitHub Actions workflows under `.github\workflows\`, test affected workflow locally with `act` before handoff. For Windows jobs, prefer host-backed run: `act workflow_dispatch -W .github\workflows\build.yml -j windows -P windows-latest=-self-hosted`; clean `.act-*` folders after.
- Tests pure xUnit, no WinAppSDK runtime; target `net8.0-windows10.0.19041.0`, do NOT reference WinUI.

## Architecture

- WinUI 3 desktop app with native payloads and tests in `BO2.slnx`. `BO2.csproj` targets `net8.0-windows10.0.19041.0`, uses Windows App SDK + single-project MSIX tooling, and supports only `Platform=x86`.
- `App.xaml` merges `XamlControlsResources` for app-wide WinUI resources.
- `App.xaml.cs`: launch entry. `OnLaunched` creates `MainWindow`, stores private `_window`, activates it.
- `MainWindow.xaml`: app shell window with `MicaBackdrop`, navigation, connection controls, settings UI, and page host. `MainWindow.xaml.cs` owns shell wiring, refresh resources, connection handlers, widget runtime coordination, theme persistence, and cleanup.
- Packaging in project, no separate packaging project. `Package.appxmanifest` defines identity, logos, capabilities; `app.manifest` carries unpackaged compatibility + DPI.
- `Properties\launchSettings.json`: two local run profiles: packaged (`MsixPackage`) and unpackaged (`Project`).

## Key conventions

- Line endings are repository policy: keep all tracked text files CRLF on checkout and after edits, except `*.sh` files which must stay LF. Respect `.editorconfig` and `.gitattributes`; do not introduce LF-only churn in Windows project files, XAML, C#, markdown, JSON, XML, manifests, or PowerShell scripts.
- When using a patch/editing tool that may write LF, immediately normalize each touched non-`*.sh` text file back to CRLF before continuing. Treat line-ending normalization as part of the edit itself, not a late cleanup step.
- Keep XAML `x:Class`, code-behind partial class names, and `BO2` namespace in sync when renaming/moving UI types.
- Preserve `_window` field pattern in `App.xaml.cs`; app keeps window reference at application level, not transient launch local.
- Add shared styles, brushes, app-wide resources in `App.xaml`, not directly in `MainWindow.xaml`.
- Packaging assets: keep manifest asset basenames stable. `Package.appxmanifest` references `Assets\Square150x150Logo.png`; physical files in `Assets\` use scale-qualified names like `.scale-200.png`.
- Treat manifest capabilities + packaging settings carefully. App declares restricted `runFullTrust` capability for desktop process access.
- If change depends on output architecture or native/runtime packaging behavior, specify `Platform=x86`; app supports only 32-bit Windows builds.

## Agent tools

- Use `winapp ui` for Windows UI Automation (UIA) against a running app when agents need to inspect rendered UI, find controls, click buttons, read or set values, take screenshots, or verify UI state.
- Start with `winapp ui status -a BO2; winapp ui inspect -a BO2 --interactive` to confirm the target window and discover invokable controls.
- Use `winapp ui screenshot -a BO2 --output <path>` for visual verification, and add `--capture-screen` when popups, dropdowns, or flyouts must be included.
- Prefer stable AutomationId selectors from `inspect` output; otherwise use the generated slug selector. Re-run `inspect` when slug selectors go stale after UI changes.
- If `-a BO2` matches multiple windows, run `winapp ui list-windows -a BO2` and target the intended window with `-w <HWND>`.
- Chain related `winapp ui` commands with PowerShell `;` rather than `&&`.

## Agent skills

### Issue tracker

Issues + PRDs tracked as local markdown files under `.scratch/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Repo uses default five-label triage vocabulary. See `docs/agents/triage-labels.md`.

### Domain docs

Repo uses single-context domain docs layout. See `docs/agents/domain.md`.
