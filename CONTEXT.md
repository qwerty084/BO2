# BO2

This context covers the WinUI desktop app that inspects Black Ops II Zombies runtime state through read-only process memory and a native event monitor.

## Language

**Detected Game**:
A running BO2 process classified by variant, support state, process metadata, and confirmed memory addresses.
_Avoid_: Process, game process

**Game Connection Session**:
The app-owned runtime relationship between the current **Detected Game** and an optional **Event Monitor**.
_Avoid_: connection manager, stats refresh service

**Game Connection Snapshot**:
A point-in-time read-only view of a **Game Connection Session** for app pages and widgets.
_Avoid_: refresh result, UI state

**Event Monitor**:
The native `BO2Monitor` payload loaded into Steam Zombies to publish read-only game events through a shared snapshot.
_Avoid_: injector, hook

**Player Stats Read**:
A read-only sample of confirmed player stat and candidate addresses from the current **Detected Game**.
_Avoid_: polling update, refresh

## Relationships

- A **Game Connection Session** has zero or one current **Detected Game**.
- A **Game Connection Session** owns zero or one **Event Monitor** for the current **Detected Game**.
- A **Game Connection Session** exposes one current **Game Connection Snapshot** at a time.
- A **Player Stats Read** belongs to exactly one **Detected Game** when Steam Zombies is supported.
- An **Event Monitor** can provide event data only after the **Game Connection Session** connects to Steam Zombies.

## Example Dialogue

> **Dev:** "When the **Detected Game** changes, should the **Game Connection Session** keep the old **Event Monitor** alive?"
> **Domain expert:** "No. The session should stop the old monitor and wait for the user to connect to the new Steam Zombies process."

## Flagged Ambiguities

- "refresh" can mean UI timer work, process memory reads, or monitor snapshot reads. Use **Player Stats Read** for memory sampling and **Game Connection Session** for lifecycle coordination.
