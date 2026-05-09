# Ghidra And x32dbg Workflow

Use this workflow for scoped, evidence-driven runtime research against the current Steam Zombies `t6zm.exe` build.

## Scope And Safety

- Keep research read-only unless a future design explicitly authorizes a safe local/offline write path.
- Do not commit game binaries, raw memory dumps, large Ghidra projects, or uncurated generated live output logs.
- Curated, small runtime evidence can live under `artifacts/reverse` when it supports a durable repo finding.
- Do not document anti-cheat bypassing, process hiding, or generic injection workflows.
- Store temporary Ghidra projects and runtime captures outside the source tree or under ignored local folders such as `.reverse/`.

## Ghidra Static Workflow

Use Ghidra to recover stable layouts before changing production code.

```powershell
$repoRoot = '<repo-root>'
$ghidra = '<ghidra-install>\support\analyzeHeadless.bat'
$projectDir = Join-Path $repoRoot '.reverse'
$script = Join-Path $repoRoot 'tools\ghidra\BO2CatalogExport.java'
$target = '<private-copy-of-t6zm.exe>'
$outDir = Join-Path $repoRoot 'artifacts\reverse'
& $ghidra $projectDir BO2Recon -import $target -postScript $script $outDir
```

Keep `.reverse/` ignored. Do not commit imported binaries or Ghidra project files. Review generated catalog diffs before committing them.

## Confirmed Static Findings

| Item | Address / layout | Status |
| --- | ---: | --- |
| Local `vm_notify` entry | `0x008F31D0` | Confirmed for current Steam Zombies build |
| Public T6 `vm_notify` candidate | `0x008F3620` | Rejected for this build |
| `SL_GetStringOfSize` | `0x00418B40` | Confirmed by prologue and runtime use |
| Script string data pointer | `0x02BF83A4` | Confirmed |
| Child buckets pointer slot | `0x02DEFB00 + inst * 0x200` | Confirmed for tooling, not needed by current production box path |
| Child variables pointer slot | `0x02DEFB80 + inst * 0x200` | Confirmed and used |
| Child variable stride | `0x1C` | Confirmed |
| Child variable key | `+0x10` | `(parentId << 16) | (name >> 8)` |

## Live Read-Only Validation

Prefer read-only process-memory probes for repeatable evidence.

1. Start Zombies and reach the state being validated.
2. If monitor injection is already part of the feature under test, read the current snapshot:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Read-BO2MonitorSharedMem.ps1
```

3. For mystery-box alias validation, record the relevant event owner from normal monitor output or a controlled diagnostic build.
4. While the weapon is visible, optionally pause the game.
5. Run the Ghidra-layout owner scan:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Find-BO2BoxWeaponByGhidraLayout.ps1 -OwnerIds <owner> -DumpOwnerFields -ScanWeaponStringValues
```

A result is useful only when it ties a weapon alias to the same owner that emitted the relevant notify.

## x32dbg Guidance

x32dbg can help with small live inspection windows, but it destabilized BO2 during the 2026-05-09 owner-capture attempts. Prefer Ghidra plus read-only PowerShell probes whenever possible.

Use x32dbg only when:

- Static Ghidra analysis has narrowed the target to a small function or address set.
- A live breakpoint is genuinely needed.
- The session can be kept short and scoped.

Avoid:

- Long-running breakpoint scripts in the game thread.
- Reusing debugger flows that froze or crashed the game.
- Workflows that write to game memory or attempt to hide debugging.

## Evidence Standard

Before implementing a new runtime memory path, capture:

| Evidence | Required |
| --- | --- |
| Static source | Ghidra address/layout or known public T6 source reference |
| Runtime owner | Owner ID from the actual notify record when owner-scoped behavior matters |
| Runtime value | Decoded value tied to that owner |
| Repeat validation | At least one repeat or changed value proving the result is not stale |
| Failure behavior | Lookup failure preserves legacy events and avoids stale aliases |
