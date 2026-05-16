# Copilot Instructions

## Commands

- Build: `dotnet build .\BO2.slnx`.
- Build app/MSIX payload path: `dotnet build .\BO2.csproj -p:Platform=x86`.
- Test C#: `dotnet test .\BO2.Tests\BO2.Tests.csproj`.
- Test native: `.\tools\Run-NativeTests.ps1 -Configuration Release`.
- Test native production hook: `.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook`.
- Check generated event monitor contract: `.\tools\Generate-EventMonitorSnapshotContract.ps1 -Check`.
- Format C#: `dotnet format .\BO2.slnx -v detailed --severity info`.
- Check formatting: `dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info`.
- Formatter is separate from behavior validation: format/check style, then build/test.
- Refresh formatter baseline with `dotnet new editorconfig` only when adopting/updating repo formatter policy.
- C# tests are pure xUnit under `net8.0-windows10.0.19041.0`; no WinAppSDK runtime refs.

## CI/Workflows

- When editing `.github\workflows\`, test affected workflows with `act` before handoff when practical.
- For Windows build workflow, prefer host runner: `act workflow_dispatch -W .github\workflows\build.yml -j windows -P windows-latest=-self-hosted`.
- Clean `.act-*` folders after local workflow runs.

## Architecture

- WinUI 3 desktop app in `BO2.csproj`; solution also carries C# tests, native tests, `BO2Monitor`, and `BO2InjectorHelper`.
- App targets `net8.0-windows10.0.19041.0`, uses Windows App SDK + single-project MSIX, and supports only `Platform=x86` / `win-x86`.
- `App.xaml` owns app-wide resources and merges `XamlControlsResources`.
- `App.xaml.cs` launch path keeps private `_window`, creates `MainWindow`, activates it.
- `MainWindow.xaml` is shell: `MicaBackdrop`, navigation, connection footer, settings, current game, game history, widget settings.
- `MainWindow.xaml.cs` wires shell events, refresh queue/resources, connect/disconnect, theme prefs, widget runtime, and cleanup.
- Packaging stays in app project. `Package.appxmanifest` defines identity/assets/capabilities; `app.manifest` covers unpackaged compatibility + DPI.
- Launch profiles: packaged `MsixPackage`, unpackaged `Project`.

## Conventions

- Keep tracked text CRLF, except `*.sh` LF. Respect `.editorconfig` and `.gitattributes`.
- After any patch tool edit, immediately normalize touched non-`*.sh` text files back to CRLF.
- Keep XAML `x:Class`, code-behind partial class, and `BO2` namespace aligned.
- Preserve `App.xaml.cs` `_window` field pattern.
- Put shared styles/brushes/resources in `App.xaml`, not `MainWindow.xaml`.
- Keep manifest asset basenames stable; `Package.appxmanifest` references unscaled names, physical files use scale-qualified names under `Assets\`.
- Treat manifest capabilities/package settings carefully; app declares restricted `runFullTrust`.
- Specify `Platform=x86` for architecture/native/package-sensitive work.

## UIA Tools

- Use `winapp ui` against running BO2 for rendered UI inspect/click/type/screenshot checks.
- Start with `winapp ui status -a BO2` and `winapp ui inspect -a BO2 --interactive`.
- Screenshot with `winapp ui screenshot -a BO2 --output <path>`; add `--capture-screen` for popups/flyouts.
- Prefer stable AutomationId selectors. Re-run inspect when slug selectors go stale.
- If `-a BO2` matches multiple windows, run `winapp ui list-windows -a BO2`, then target with `-w <HWND>`.
- Chain related `winapp ui` commands in PowerShell with `;`.

## Agent Skills

- Issues/PRDs live as local markdown under `.scratch/`; see `docs/agents/issue-tracker.md`.
- Triage uses the default five-label vocabulary; see `docs/agents/triage-labels.md`.
- Domain docs use single-context layout; read `CONTEXT.md` and relevant `docs/adr/` files, see `docs/agents/domain.md`.
