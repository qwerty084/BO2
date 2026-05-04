# Read-BO2MonitorSharedMem verification

`Read-BO2MonitorSharedMem.ps1` reads the Event Monitor snapshot layout from
`contracts/EventMonitorSnapshotContract.v1.json`, then opens the process-scoped
memory map named by the contract shared-memory prefix plus process id.

## Live Event Monitor

1. Start BO2 Zombies (`t6zm.exe`) and the Event Monitor injection path.
2. Run:

   ```powershell
   .\tools\Read-BO2MonitorSharedMem.ps1
   ```

   Or pass a known process id:

   ```powershell
   .\tools\Read-BO2MonitorSharedMem.ps1 -ProcessId <t6zm-process-id>
   ```

3. Verify the output includes header state, counters, write sequence, and recent
   events. Current v6 event and weapon names should print from the contract
   `EventName` and `WeaponName` field offsets.

## Fixture-equivalent map

A lightweight fixture can create a read/write memory map named
`BO2MonitorSharedMem-<fixture-process-id>` using the contract
`snapshot.sharedMemorySize`, write the contract header fields and event records,
then run:

```powershell
.\tools\Read-BO2MonitorSharedMem.ps1 -ProcessId <fixture-process-id>
```

The fixture should populate `Magic`, `Version`, `EventCount`,
`EventWriteIndex`, `WriteSequence`, `EventName`, and `WeaponName` from the JSON
contract field offsets. This exercises the same reader path without requiring a
live game process.

## Static validation

Run this after editing the script or contract:

```powershell
$null = Get-Content .\contracts\EventMonitorSnapshotContract.v1.json -Raw | ConvertFrom-Json
$null = [scriptblock]::Create((Get-Content .\tools\Read-BO2MonitorSharedMem.ps1 -Raw))
```
