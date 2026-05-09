# Validation

Use this page to choose the right validation path after changes.

## Local Commands

| Purpose | Command |
| --- | --- |
| Build solution | `dotnet build .\BO2.slnx` |
| Build 32-bit app payload | `dotnet build .\BO2.csproj --configuration Release -p:Platform=x86` |
| Run managed non-UI tests | `dotnet test .\BO2.Tests\BO2.Tests.csproj` |
| Run native tests | `.\tools\Run-NativeTests.ps1 -Configuration Release` |
| Run native tests with hook flag | `.\tools\Run-NativeTests.ps1 -Configuration ReleaseWithVmNotifyHook` |
| Check C# formatting/style | `dotnet format .\BO2.slnx -v detailed --verify-no-changes --severity info` |
| Verify snapshot contract generation | `.\tools\Generate-EventMonitorSnapshotContract.ps1 -Check` |

Managed tests are pure xUnit and avoid WinAppSDK runtime startup. Native tests cover deterministic repo-owned Event Monitor and injector-helper behavior without a live BO2 process.

## CI Coverage

The Windows build workflow restores with `Platform=x86`, verifies generated snapshot contracts, checks formatting at `info` severity, runs managed tests, runs native tests in both configurations, builds the solution, creates a test signing certificate, builds a test-signed x86 MSIX, and verifies the native payloads are inside the MSIX.

## Live Validation

Use [native-smoke-test.md](../native-smoke-test.md) for behavior that requires a real Steam Zombies `t6zm.exe` process, such as live detection, Connect, monitor compatibility, stat refresh, box-event capture, disconnect cleanup, and game-exit recovery.

## Workflow Changes

When changing `.github/workflows`, test affected workflows locally with `act` before handoff when feasible. For Windows jobs, prefer:

```powershell
act workflow_dispatch -W .github\workflows\build.yml -j windows -P windows-latest=-self-hosted
```

Clean `.act-*` folders after local workflow runs.
