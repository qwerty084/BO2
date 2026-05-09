# Event Pipeline And Snapshot Bridge

The Event Monitor has two capture paths:

- Preferred path: hook the local `vm_notify` entry and publish selected script notify events.
- Fallback path: poll a small set of stat globals and publish change events when the hook is unsupported.

The managed app reads a repo-owned shared-memory snapshot written by `BO2Monitor.dll`.

## Startup Path

1. Managed code detects supported Steam Zombies process `t6zm`.
2. `Services/DllInjector.cs` validates the helper and monitor DLL are PE32/I386 payloads and locates the `StartMonitor` export.
3. The injector loads `BO2Monitor.dll` into the target process and starts `StartMonitor`.
4. `BO2Monitor/dllmain.cpp` creates the monitor worker thread.
5. The worker initializes shared memory and update/stop events.
6. The worker calls `TryInstallNotifyHook`.
7. If hook compatibility returns `UnsupportedVersion`, the worker uses polling fallback; otherwise it drains the notify queue.

## Hook Compatibility

Hook target request:

- Target: `0x008F31D0`.
- Expected bytes: `55 8B EC 83 E4 F8 83 EC 44`.
- Hook support flag: `BO2MONITOR_ENABLE_VM_NOTIFY_HOOK`.

Compatibility state machine:

1. Missing target/prologue data -> `UnsupportedVersion`.
2. Non-executable target or mismatched prologue -> `UnsupportedVersion`.
3. Hook support disabled -> `CaptureDisabled`.
4. No notify target string resolves through `SL_GetStringOfSize` -> `UnsupportedVersion`.
5. MinHook install failure -> `UnsupportedVersion`.
6. Successful install -> `Compatible`.

Production Win32 builds enable VM notify hook support. Debug/Release builds can be built with hook support disabled, which intentionally yields `CaptureDisabled` after the address/prologue checks pass.

## Discovery Evidence

The monitor publishes discovery events into the same snapshot:

| Event name | Type | Meaning |
|---|---|---|
| `vm_notify_candidate_rejected` | `NotifyCandidateRejected` | Public candidate `0x008F3620` is rejected for this build. |
| `vm_notify_entry_candidate` | `NotifyEntryCandidate` | Local entry `0x008F31D0` prologue matched. |
| `sl_string_data_candidate` | `StringResolverCandidate` | `0x02BF83A4` is readable. |
| `sl_get_string_of_size_candidate` | `StringResolverCandidate` | `0x00418B40` prologue matched. |

## Notify Targets

The native monitor resolves these names to string IDs at startup:

| Name | Event type | Publishes live round value |
|---|---|---|
| `start_of_round` | `StartOfRound` | yes |
| `end_of_round` | `EndOfRound` | yes |
| `end_game` | `EndGame` | no |
| `randomization_done` | `BoxEvent` | no |
| `user_grabbed_weapon` | `BoxEvent` | no |
| `chest_accessed` | `BoxEvent` | no |
| `box_moving` | `BoxEvent` | no |
| `weapon_fly_away_start` | `BoxEvent` | no |
| `weapon_fly_away_end` | `BoxEvent` | no |
| `arrived` | `BoxEvent` | no |
| `left` | `BoxEvent` | no |
| `closed` | `BoxEvent` | no |

The detour ignores unresolved or untracked notify string IDs.

## Detour Behavior

`VmNotifyDetour(inst, ownerId, stringValue, top)`:

1. Finds a resolved target by `stringValue`.
2. If not tracked, calls original `vm_notify` and returns.
3. For ordinary tracked events, calls original first, then enqueues a record.
4. For `randomization_done` and `user_grabbed_weapon`, calls original first, scans for a weapon alias under the owner object, then enqueues the record with optional weapon name.

Calling original first preserves game behavior and lets the original notify update script state before the monitor reads owner fields.

## Native Notify Queue

The detour does not write shared memory directly. It writes a lightweight record to an internal queue:

- Capacity: `256`.
- Enqueue uses `try_to_lock`.
- Contention drops are counted.
- Overwrite detection uses sequence numbers.
- Worker drains up to `64` records per loop.
- Worker publishes dropped and published counters separately.

This keeps the hook path short and moves shared-memory work to the monitor thread.

## Shared Snapshot ABI

Snapshot v6 constants:

| Field | Value |
|---|---:|
| Magic | `0x45324F42` |
| Version | `6` |
| Max events | `128` |
| Max event-name bytes | `64` |
| Max weapon-name bytes | `64` |
| Header size | `36` |
| Event record size | `148` |
| Shared memory size | `18980` |

Object names:

- Shared memory: `BO2MonitorSharedMem-<pid>`.
- Update event: `BO2MonitorEvent-<pid>`.
- Stop event: `BO2MonitorStopEvent-<pid>`.

Header layout:

| Offset | Field |
|---:|---|
| `0` | `Magic` |
| `4` | `Version` |
| `8` | `CompatibilityState` |
| `12` | `EventWriteIndex` |
| `16` | `DroppedEventCount` |
| `20` | `EventCount` |
| `24` | `DroppedNotifyCount` |
| `28` | `PublishedNotifyCount` |
| `32` | `WriteSequence` |

Event record layout:

| Offset | Field |
|---:|---|
| `0` | `EventType` |
| `4` | `LevelTime` |
| `8` | `OwnerId` |
| `12` | `StringValue` |
| `16` | `Tick` |
| `20` | `EventName[64]` |
| `84` | `WeaponName[64]` |

## Stable Read Rules

Native mutations increment `WriteSequence` before and after writes, with memory barriers. Odd values mean a write is in progress. Managed reader behavior:

1. Read `WriteSequence`.
2. If odd, sleep and retry.
3. Copy the full snapshot.
4. Read `WriteSequence` again.
5. Accept only if both sequence values are equal and even.
6. Retry up to four times.

When `EventCount < 128`, events are read from slot `0..EventCount-1`. When full, the oldest event is at `EventWriteIndex % 128`.

## Polling Fallback

Fallback initializes only if all four reads succeed:

| Stat | Address | Event | Publish rule |
|---|---:|---|---|
| Round | `0x0233FA10` | `round_changed` | `2..255`, increasing only |
| Points | `0x0234C068` | `points_changed` | `0..2000000`, any change |
| Kills | `0x0234C080` | `kills_changed` | `0..100000`, increasing only |
| Downs | `0x0234C084` | `downs_changed` | `0..1000`, increasing only |

Fallback is intentionally less rich than notify capture. It gives the UI useful changes without relying on script VM hooks.
