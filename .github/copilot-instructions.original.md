# Copilot Instructions

## Build, test, and lint

- Build the solution with `dotnet build .\BO2.slnx`.
- Build a specific architecture with `dotnet build .\BO2.csproj -p:Platform=x64` (supported platforms are `x86`, `x64`, and `ARM64`).
- There is no test project in the repository yet, so there is no full-suite or single-test command to run.
- There is no repository-specific lint or formatting command/config checked in yet.

## Repository knowledge

- For complex tasks, consult the GitHub wiki for additional project knowledge before designing or implementing changes: https://github.com/qwerty084/BO2/wiki.
- If the task involves memory reading, address discovery, CDB/WinDbg usage, or BO2 Zombies runtime data, check wiki pages such as `Confirmed-Memory-Addresses` for confirmed addresses and validation notes.
- Store durable findings that future agents should reuse in the repo wiki rather than in ad-hoc local notes. Use the `repo-wiki-notes` skill for wiki updates.
- Do not store secrets, credentials, raw sensitive memory dumps, anti-cheat bypass techniques, process hiding techniques, code injection workflows, or memory-writing instructions in the wiki.

## Architecture

- This repository is a single-project WinUI 3 desktop app. `BO2.csproj` targets `net8.0-windows10.0.19041.0`, uses Windows App SDK, and has single-project MSIX tooling enabled.
- `App.xaml` sets up application-wide WinUI resources by merging `XamlControlsResources`.
- `App.xaml.cs` is the launch entry point. `OnLaunched` creates `MainWindow`, stores it in a private `_window` field, and then activates it.
- `MainWindow.xaml` is currently the entire UI shell: one `Window` with a `MicaBackdrop` and an empty root `Grid`. `MainWindow.xaml.cs` only initializes the partial class.
- Packaging is part of the project structure, not a separate packaging project. `Package.appxmanifest` defines identity, logos, and capabilities, while `app.manifest` carries unpackaged compatibility and DPI settings.
- `Properties\launchSettings.json` defines two local run profiles: packaged (`MsixPackage`) and unpackaged (`Project`).

## Key conventions

- Keep XAML `x:Class`, code-behind partial class names, and the `BO2` namespace in sync when renaming or moving UI types.
- Preserve the `_window` field pattern in `App.xaml.cs`; the app keeps a window reference at the application level instead of creating a transient local variable during launch.
- Add shared styles, brushes, and other app-wide resources in `App.xaml` rather than directly in `MainWindow.xaml`.
- When editing packaging assets, keep the manifest asset basenames stable. `Package.appxmanifest` references names like `Assets\Square150x150Logo.png`, while the physical files in `Assets\` use scale-qualified filenames such as `.scale-200.png`.
- Be careful when changing manifest capabilities or packaging settings. The app currently declares both `runFullTrust` and `systemAIModels`.
- If a change depends on output architecture or native/runtime packaging behavior, specify the `Platform` explicitly because the project is set up to build for `x86`, `x64`, and `ARM64`.
