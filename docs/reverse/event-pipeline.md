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

## Runtime Validation, 2026-05-09

Read-only live validation was performed against Steam Zombies Town on build `65428`.

Confirmed executable provenance:

- Executable: Steam app `202970` `t6zm.exe` from a local Steam install. The local install path is intentionally omitted.
- MD5: `68C62BE753DE8ADF2C2C7B28DB769B99`.
- SHA256: `3645528D61EF0FB0591D5195E111718235EF3C75F2211BD3633A2DB4DE7C67AC`.

Confirmed live bytes before monitor injection:

| Address | Meaning | Live bytes |
|---:|---|---|
| `0x008F31D0` | local `vm_notify` entry | `55 8B EC 83 E4 F8 83 EC 44 53 56 8B 75 08 57 8B` |
| `0x008F361F` | containing instruction for rejected public candidate | `E8 8C 8B D8 FF 57 56 89` |
| `0x008F3620` | rejected public candidate interior bytes | `8C 8B D8 FF 57 56 89 44 24 30 E8 81 AB DC FF 8B` |
| `0x00418B40` | `SL_GetStringOfSize` candidate | `83 EC 0C 8B 54 24 10 53 8B 5C 24 1C 55 56 57 8B` |

Confirmed live pointers and values:

| Address | Meaning | Live value |
|---:|---|---:|
| `0x02BF83A4` | script string table pointer slot | `0x02BF8880` |
| `0x02DEFB00` | instance 0 child bucket pointer slot | `0x2EE30000` |
| `0x02DEFD00` | instance 1 child bucket pointer slot | `0x2F8D0000` |
| `0x02DEFB80` | instance 0 child variable pointer slot | `0x2E730000` |
| `0x02DEFD80` | instance 1 child variable pointer slot | `0x2F1D0000` |

Live notify string IDs in this Town process:

| Name | String ID |
|---|---:|
| `user_grabbed_weapon` | `7429` |
| `chest_accessed` | `7438` |
| `zbarrier` | `7453` |
| `randomization_done` | `7492` |
| `weapon_fly_away_start` | `7510` |
| `weapon_fly_away_end` | `7513` |
| `box_moving` | `7537` |
| `end_game` | `7545` |
| `start_of_round` | `8042` |
| `end_of_round` | `13095` |

These IDs are runtime values and changed across process launches during this pass. Do not hard-code them in production.

x32dbg headless attach was attempted once for passive validation and the game crashed after the headless process failed to detach cleanly. No further debugger attach was used in that initial validation pass. The stable confirmed runtime facts come from `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION)` and `ReadProcessMemory` only.

Because monitor injection is not read-only, MinHook installation was not performed in this pass. No `BO2MonitorSharedMem-<pid>` map existed before injection, so discovery events were not observed in a live snapshot.

A later supervised GUI x32dbg retry was attempted in an elevated debugger. The target view was verified at `t6zm.exe:0x008F31D0`, and a hardware execute breakpoint was set for the current launch's `randomization_done` and `user_grabbed_weapon` IDs. BO2 raised another access violation during resume before either target notify was captured. This confirms that debugger-based notify owner capture is not a reliable validation path in the current setup.

Read-only baseline from that retry still confirmed:

| Item | Live value |
|---|---:|
| `randomization_done` | `7491` |
| `user_grabbed_weapon` | `7436` |
| `zbarrier` | `7452` |
| `weapon_string` | not found |
| `grab_weapon_name` | not found |
| script string table pointer slot | `0x02BF83A4 -> 0x02BF8880` |
| instance 0 child bucket base | `0x2EE30000` |
| instance 0 child variable base | `0x2E730000` |

A later read-only continuation used a paused Town state without x32dbg. The user spun the box, reported the visible weapon as `python_zm`, then picked it up. No monitor shared-memory map existed for that process, so the normal monitor event queue was not available for this session. Passive snapshots found candidate owner `901` with string fields `town_chest`, `treasure_chest_use`, and `python_zm` under `tag_knob` both before pickup and after pickup. This is useful post-state evidence, but it is not equivalent to the normal monitor event record because no `vm_notify` owner argument was captured.

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

The production alias scan is not field-name specific. It scans child entries for the current notify owner and accepts the first string/istring child value that passes `_zm` alias validation. Current runtime evidence still supports this broad strategy: `weapon_string` and `grab_weapon_name` were absent from the live Town string table, while a box-looking owner candidate carried `python_zm` under `tag_knob`.

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
