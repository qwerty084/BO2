# Event Monitor Snapshot Fixture

This tool refreshes the committed v6 native-to-managed Event Monitor snapshot fixture used by `BO2.Tests`.

Run from the repository root:

```powershell
.\tools\Refresh-EventMonitorSnapshotFixture.ps1
```

The refresh script builds this Win32 native console project in `Release`, then runs it to write:

```text
BO2.Tests\Fixtures\EventMonitorSnapshot.v6.wrapped.bin
```

The generator uses `BO2Monitor\SharedSnapshot.h` and `SharedSnapshot.cpp`, specifically the same native layout and mutation functions used by the Event Monitor writer adapter. The fixture intentionally writes:

- snapshot version 6 with `Compatible` compatibility state
- non-zero dropped event, dropped notify, and published notify counters
- more than `MaxEventCount` events so the native event ring wraps
- a Box Tracker Widget-relevant `randomization_done` event with `ray_gun_zm`
- round/session events such as `start_of_round` and `end_game`
- deterministic native ticks so managed timestamp conversion stays stable

When the snapshot contract changes, update the generator expectations first, run the refresh script, and review the binary fixture diff together with the managed decoder test changes.
