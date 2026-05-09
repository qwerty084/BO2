# Script VM, Strings, And Child Variables

This document captures the script VM pieces that matter to the Event Monitor and box weapon tracking.

## `vm_notify`

Production hook target:

- Address: `0x008F31D0`.
- Ghidra name: `FUN_008f31d0`.
- Expected prologue: `55 8B EC 83 E4 F8 83 EC 44`.
- Inferred signature: `void __cdecl vm_notify(int inst, uint ownerId, uint stringValue, void* top)`.

Ghidra evidence:

- The function starts at `0x008F31D0` in executable `.text`.
- Callers in the exported catalog: `0x006787f9`, recursive `0x008f3220`, and `0x008f5d04`.
- Early decompile logic checks `inst == 0`, compares `stringValue` against `0x024BB4CC` and `0x024BB4CE`, then recursively calls itself with replacement string id from `0x024BB4D0`.
- It checks a per-instance optional callback pointer at `0x02DF4170 + inst * 0x42A8` and calls it with `(ownerId, stringValue)` when present.
- It calls `scr_find_variable(inst, ownerId, 0x18000)`, then `scr_get_variable_value`, then looks up `stringValue` under that result.

Rejected public candidate:

- Address: `0x008F3620`.
- Status: rejected for this Steam build.
- Ghidra shows the containing instruction starts at `0x008F361F` as `CALL 0x0067C1B0`; `0x008F3620` is inside the call immediate bytes, not a function entry.

## Script String Resolution

Runtime resolver:

- Address: `0x00418B40`.
- Expected prologue: `83 EC 0C 8B 54 24 10`.
- Inferred signature: `uint __cdecl SL_GetStringOfSize(const char* name, int user, uint length, int type)`.
- Native monitor call pattern: `resolver(name, 0, strlen(name) + 1, 6)`.
- Live 2026-05-09 Town process bytes: `83 EC 0C 8B 54 24 10 53 8B 5C 24 1C 55 56 57 8B`.

The native monitor does not hard-code notify string IDs. It resolves each target name at startup after checking the resolver prologue. This is safer than storing IDs because script string IDs may be process-lifetime or load-order dependent.

Script string table:

- Pointer slot: `0x02BF83A4`.
- Entry stride: `0x18`.
- Text offset: `0x04`.
- Max string id guard in native code: `< 0x40000`.
- Live 2026-05-09 Town pointer value: `0x02BF8880`.

`CopyScriptStringValue` dereferences `0x02BF83A4`, computes `base + stringId * 0x18 + 0x04`, and copies printable ASCII into a 64-byte weapon-name buffer.

## Child Variable Layout

Pointer slots:

- Child bucket pointer slot base: `0x02DEFB00`.
- Child variable pointer slot base: `0x02DEFB80`.
- Per-instance slot stride: `0x200`.
- Supported instances in production: `0` and `1`.

Entry layout:

| Offset | Field | Notes |
|---:|---|---|
| `0x00` | value | `VariableUnion`; string values use script string id. |
| `0x04` | sibling or hash | Name from production struct. |
| `0x08` | next | Hash-chain next child id. |
| `0x0C` | type | Type masked with `0x7F`; string is `2`, istring is `3`. |
| `0x0D` | nameLo | Low 8 bits for field-name reconstruction. |
| `0x0E` | flags | 16-bit flags. |
| `0x10` | key | Packed parent/name-high key. |
| `0x14` | nextSibling | Sibling chain. |
| `0x18` | prevSibling | Sibling chain. |

Entry size is `0x1C`, asserted in native code.

Key and hash layout from the Ghidra-derived tool:

- `key = (parentId << 16) | (nameId >> 8)`.
- `bucketIndex = ((parentId * 0x65) + nameId) & 0x1ffff`.
- `bucketAddress = bucketBase + bucketIndex * 4`.
- `childAddress = childBase + childId * 0x1c`.

Production alias scanning does not currently use the exact hash lookup. It scans child entries and compares the parent id from `key` to the notify owner id.

## Cataloged Script VM Helpers

| Label | Address | Current inference | Evidence |
|---|---:|---|---|
| `scr_find_variable` | `0x006BFB30` | Finds child variable by instance, parent, name. | Ghidra function, multiply by `0x65`, many xrefs, vm_notify calls. |
| `scr_get_variable_value` | `0x00485950` | Reads variable value/object id. | Called immediately after `scr_find_variable` hits. |
| `scr_get_variable_value_address` | `0x0067C1B0` | Returns pointer to variable value storage. | Called from vm_notify; rejected public candidate lies inside one call to it. |
| `scr_set_variable_field` | `0x0058F9E0` | Assigns/links a script variable field. | Called in vm_notify field/object update branches. |
| `scr_find_object` | `0x00474EA0` | Resolves object metadata by id. | Called in vm_notify object traversal. |

See `artifacts/reverse/function-catalog.csv` for callers, callees, bytes, and xrefs.

The saved Ghidra project has durable names and conservative signatures for these helpers as of the 2026-05-09 continuation pass. `function-catalog.csv` now includes Ghidra prototypes, calling conventions, and bounded decompile snippets in addition to the manual inference columns. `scr_set_variable_field` still has an unknown return type; its parameters are named, but direct use should wait for deeper decompilation.

## Important Globals

| Label | Address | Meaning |
|---|---:|---|
| `script_string_data_pointer` | `0x02BF83A4` | Runtime script string table pointer. |
| `scr_var_glob_candidate` | `0x02DEA400` | Weak region anchor for script VM globals. |
| `child_bucket_pointer_slot_base` | `0x02DEFB00` | Per-instance child hash bucket pointer slots. |
| `child_variables_pointer_slot_base` | `0x02DEFB80` | Per-instance child variable table pointer slots. |
| `vm_notify_callback_table_base` | `0x02DF4170` | Per-instance optional notify callback pointer. |
| `vm_notify_remap_a` | `0x024BB4CC` | String id remap compare when `inst == 0`. |
| `vm_notify_remap_b` | `0x024BB4CE` | Second remap compare when `inst == 0`. |
| `vm_notify_remap_target` | `0x024BB4D0` | Replacement string id for recursive notify call. |

See `artifacts/reverse/globals-catalog.csv` for static xrefs.

## Runtime Notes, 2026-05-09

Read-only validation in a Town session confirmed the major VM anchors without injecting the monitor:

| Item | Live value |
|---|---:|
| `0x008F31D0` bytes | `55 8B EC 83 E4 F8 83 EC 44 53 56 8B 75 08 57 8B` |
| instance 0 child bucket base | `0x2EE30000` |
| instance 1 child bucket base | `0x2F8D0000` |
| instance 0 child variable base | `0x2E730000` |
| instance 1 child variable base | `0x2F1D0000` |

The three `vm_notify` remap globals resolved to script strings in this process:

| Global | Address | Live ID | Live string |
|---|---:|---:|---|
| `vm_notify_remap_a` | `0x024BB4CC` | `5351` | `death` |
| `vm_notify_remap_b` | `0x024BB4CE` | `5352` | `disconnect` |
| `vm_notify_remap_target` | `0x024BB4D0` | `5353` | `death_or_disconnect` |

The observed remap values are runtime string IDs, not portable constants. The portable concept is that `vm_notify(inst:0, death|disconnect, ...)` recursively normalizes to `death_or_disconnect`.

The reason `SL_GetStringOfSize` uses `type = 6` remains unresolved. Treat it as the empirically correct resolver mode for notify names until the string-type enum is recovered.
