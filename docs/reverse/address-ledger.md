# Runtime Address Ledger

The authoritative machine-readable ledger is [artifacts/reverse/address-ledger.csv](../../artifacts/reverse/address-ledger.csv). This page gives the maintainable summary for source-backed documentation and migrated wiki references.

## Target Scope

| Field | Value |
| --- | --- |
| Process | `t6zm.exe` |
| Mode | Zombies |
| Build | Current local Steam build `65428` |
| Architecture | 32-bit / x86 |
| Address type | Current-build virtual addresses |
| Production support | Steam Zombies only |

Redacted, Plutonium, multiplayer, and single-player processes may be detected for UI messaging, but they do not receive supported stat, timing, or Event Monitor address maps in this repo.

## Production Anchors

| Label | Address | Status | Used by |
| --- | ---: | --- | --- |
| Local `vm_notify` entry | `0x008F31D0` | Runtime-confirmed entry bytes | Native Event Monitor hook compatibility |
| Public `vm_notify` candidate | `0x008F3620` | Rejected | Discovery evidence only |
| `SL_GetStringOfSize` | `0x00418B40` | Runtime-confirmed prologue | Notify target name resolution |
| Script string table pointer | `0x02BF83A4` | Runtime-confirmed pointer | Weapon alias decoding |
| Child bucket pointer slots | `0x02DEFB00 + inst * 0x200` | Runtime-confirmed pointer slots | Exact-field tooling |
| Child variable pointer slots | `0x02DEFB80 + inst * 0x200` | Runtime-confirmed pointer slots | Production broad owner alias scan |
| Child variable stride | `0x1C` | Runtime-supported layout | Alias scan and tools |

## Mandatory Managed Stat Reads

| Stat | Address | Type | Notes |
| --- | ---: | --- | --- |
| Points | `0x0234C068` | `int32` | Also used by native polling fallback. |
| Kills | `0x0234C080` | `int32` | Also used by native polling fallback. |
| Downs | `0x0234C084` | `int32` | Also used by native polling fallback. |
| Revives | `0x0234C088` | `int32` | Managed stat read only. |
| Headshots | `0x0234C08C` | `int32` | Managed stat read only. |

Candidate and optional local-player fields are documented in [player-stats.md](player-stats.md). Do not promote candidate fields without code xrefs or live change evidence.

## Timer Sources

| Source | Address / path | Read type | Notes |
| --- | ---: | --- | --- |
| `sv_running` current value | `0x02A09F00` | byte bool | Active-match gate; read exactly one byte. |
| `cl_paused` current value | `0x02A09DE0` | `int32` bool | Solo pause evidence only. |
| Client active pointer | `uint32[0x0119DC04]` | pointer | Base for client timing fields. |
| Snapshot valid | `clientActivePtr + 0x50` | `int32` | Must be `1`. |
| Game time milliseconds | `clientActivePtr + 0x58` | `int32` ms | Primary in-game elapsed-time source. |

Timer behavior is documented in [timers.md](timers.md).

## Dvar And Map Sources

Dvar pointer slots, bucket traversal, and map identity dvars are documented in [dvars-and-map-identity.md](dvars-and-map-identity.md). Treat heap dvar struct addresses as runtime evidence, not stable product dependencies, unless a static pointer slot or name lookup path is also validated.

## Revalidation Triggers

Revalidate this ledger after any Steam update, executable hash change, build-id change, target-process support expansion, or native monitor compatibility failure.
