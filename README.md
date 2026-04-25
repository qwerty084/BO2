# BO2

WinUI 3 desktop app for read-only BO2 Zombies stat inspection.

## Build

```powershell
dotnet build .\BO2.slnx
```

## Test

```powershell
dotnet test .\BO2.Tests\BO2.Tests.csproj
```

`BO2.Tests` is a non-UI xUnit project. It links testable service source files directly and uses fakes for process discovery, memory reads, and resource strings so tests stay deterministic and do not require WinAppSDK runtime startup or a live game process.
