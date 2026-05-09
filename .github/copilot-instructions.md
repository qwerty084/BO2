# Copilot Instructions

Use [AGENTS.md](../AGENTS.md) as the primary agent instruction file. Local docs under [docs](../docs/index.md) are canonical; the external GitHub wiki is historical and has been migrated.

## Build, Test, And Lint

- Build solution: `dotnet build .\BO2.slnx`.
- Build the 32-bit Windows app payload: `dotnet build .\BO2.csproj -p:Platform=x86`.
- Run non-UI managed tests: `dotnet test BO2.Tests\BO2.Tests.csproj`.
- Run native tests: `.\tools\Run-NativeTests.ps1 -Configuration Release`.
- Run native tests with the production hook flag: `.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook`.
- Check C# formatting and code style: `dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info`.

The app supports only `Platform=x86` / `win-x86`. Do not document or rely on x64 or ARM64 builds unless the project file and native payloads are changed first.

## Repository Knowledge

- Runtime and reverse-engineering docs live under [docs/reverse](../docs/reverse/index.md).
- Build, test, and local validation guidance lives under [docs/validation](../docs/validation/index.md).
- Migration history for the former wiki lives in [docs/migration/wiki-migration.md](../docs/migration/wiki-migration.md).
- Keep future durable findings in repo docs or `artifacts/reverse`, not in the external wiki.
- Never store secrets, credentials, raw sensitive memory dumps, anti-cheat bypass techniques, process hiding techniques, or generic memory-writing procedures in docs.

## Architecture

- `BO2.slnx` includes the WinUI app, managed tests, native tests, `BO2Monitor`, and `BO2InjectorHelper`.
- `BO2.csproj` targets `net8.0-windows10.0.19041.0`, uses Windows App SDK + single-project MSIX tooling, and rejects non-x86 platforms.
- `Package.appxmanifest` declares `runFullTrust` for desktop process access. It does not declare `systemAIModels`.
- Managed stat and timer reads use read-only process handles. Event capture uses the native monitor loaded after explicit Connect.
- `MainWindow.xaml.cs` is substantial shell code: refresh resources, connect/disconnect handlers, theme persistence, widget runtime coordination, settings navigation, and cleanup.

## Key Conventions

- Preserve the explicit Connect boundary for live BO2 process access.
- Keep XAML `x:Class`, code-behind partial class names, and `BO2` namespace in sync.
- Preserve the `_window` field pattern in `App.xaml.cs`; the app keeps the window reference at application level.
- Add shared styles, brushes, and app-wide resources in `App.xaml`, not directly in page XAML.
- Keep manifest asset basenames stable. `Package.appxmanifest` references `Assets\Square150x150Logo.png`; physical files in `Assets\` use scale-qualified names.
