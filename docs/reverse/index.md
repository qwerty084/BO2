# Reverse-Engineering Index

These docs describe the current Steam Zombies `t6zm.exe` build used by BO2. All absolute addresses are build-specific unless a page says otherwise.

## Runtime Docs

- [t6zm.exe overview](t6zm-overview.md): target binary, build provenance, subsystem map, and current validation status.
- [Runtime address ledger](address-ledger.md): high-level address map and links to the CSV evidence ledger.
- [Player stats and local player state](player-stats.md): managed read addresses and candidate field status.
- [In-game and round timers](timers.md): solo v1 timer model, timing addresses, lifecycle behavior, and validation limits.
- [Event pipeline and snapshot bridge](event-pipeline.md): native notify capture, polling fallback, snapshot v6, and stable read rules.
- [Script VM, strings, and child variables](script-vm.md): `vm_notify`, script string resolution, and child-variable layout.
- [Mystery-box and weapon alias tracking](box-weapon-tracking.md): current broad owner-scoped alias scan and remaining evidence gaps.
- [Dvars and map identity](dvars-and-map-identity.md): read-only dvar lookup evidence, map tokens, and future configurator boundaries.
- [Chat and console write research](chat-console-write-research.md): research-only notes for possible future text-sending features.
- [Ghidra and x32dbg workflow](ghidra-x32dbg-workflow.md): repeatable static workflow and live-validation guardrails.

## Evidence Labels

Use these labels consistently:

- `confirmed`: source and runtime evidence agree for the current build.
- `runtime-confirmed`: live read evidence exists, but the feature may still need transition or event-boundary proof.
- `observed`: seen in one validation pass; do not treat as broad support.
- `needs validation`: structurally plausible but not proven by live behavior.
- `research-only`: useful future design evidence, not product behavior.
- `rejected`: tested or analyzed and should not be used for this build.
