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

**Current Game Page**:
An app page that displays round-focused stats and event context from the current **Game Connection Snapshot** without debug or candidate-address details.
_Avoid_: home stats view, current game view

**Game History**:
A user-visible record of completed Zombies games that the app can show after a game ends.
_Avoid_: history feature, match history, saved game log

**Completed Zombies Game**:
A supported round-based Zombies run that ended cleanly and has enough final stats to appear in **Game History**.
_Avoid_: in-progress recording, partial game, abandoned session

**Supported Zombies Map**:
A round-based Black Ops II Zombies map that is eligible to appear in **Game History**.
_Avoid_: supported map table, standalone identity, base-map-token-only support

**Event Monitor**:
The native `BO2Monitor` payload loaded into Steam Zombies to publish read-only game events through a shared snapshot.
_Avoid_: injector, hook

**Player Stats Read**:
A read-only sample of confirmed player stat and candidate addresses from the current **Detected Game**.
_Avoid_: polling update, refresh

**Game Timing Read**:
A read-only sample of supported Steam Zombies timing facts, including the memory-backed game time and pause evidence used by timer state.
_Avoid_: timer poll, pause poll, timing refresh

**Game Timer State**:
The app-owned state machine that turns **Game Timing Read** samples and **Event Monitor** lifecycle events into placeholder, active, or frozen timer display state.
_Avoid_: UI timer, wall-clock timer, event timer

**Box Tracker Widget**:
A user-enabled overlay window that displays recent box events from the current **Event Monitor** outside the main app shell.
_Avoid_: overlay, native widget window

**Box Tracker Widget Runtime**:
The app-owned widget module that reconciles **Box Tracker Widget** settings, current **Event Monitor** state, settings persistence, and the native widget adapter. The app shell coordinates it through the widget window manager.
_Avoid_: widget manager, overlay service

## Relationships

- A **Game Connection Session** has zero or one current **Detected Game**.
- A **Game Connection Session** owns zero or one **Event Monitor** for the current **Detected Game**.
- A **Game Connection Session** exposes one current **Game Connection Snapshot** at a time.
- A **Current Game Page** displays round-focused state from the current **Game Connection Snapshot**.
- A **Current Game Page** does not own **Game Connection Session** commands; connect and disconnect controls live in the app shell footer/sidebar.
- A **Current Game Page** is the app's default first page; the old home stats surface no longer exists as a separate page.
- **Game History** records only **Completed Zombies Games** on **Supported Zombies Maps**.
- A **Player Stats Read** belongs to exactly one **Detected Game** when Steam Zombies is supported.
- An **Event Monitor** can provide event data only after the **Game Connection Session** connects to Steam Zombies.
- A **Game Timing Read** belongs to exactly one **Detected Game** when Steam Zombies timing support is available.
- **Game Timer State** belongs to the **Game Connection Session** and is reset when the session disconnects, the **Detected Game** changes, or timing confirms inactive/lobby state.
- The **Current Game Page** displays only projected timer text from the **Game Connection Snapshot**; it does not know timing addresses, event cursors, pause evidence, or stale-state reasons.
- A **Box Tracker Widget** displays recent box events from the current **Event Monitor**.
- A **Box Tracker Widget Runtime** owns the lifecycle of zero or one visible **Box Tracker Widget**.

## Example Dialogue

> **Dev:** "When the **Detected Game** changes, should the **Game Connection Session** keep the old **Event Monitor** alive?"
> **Domain expert:** "No. The session should stop the old monitor and wait for the user to connect to the new Steam Zombies process."

## Flagged Ambiguities

- "refresh" can mean UI timer work, process memory reads, or monitor snapshot reads. Use **Player Stats Read** for memory sampling and **Game Connection Session** for lifecycle coordination.
- Before the user connects the **Game Connection Session**, the app may perform passive **Detected Game** discovery and UI projection only. **Player Stats Read**, **Game Timing Read**, **Event Monitor** startup, and monitor snapshot reads are live-access work and must remain gated by the connected session lifecycle.
- In-game and round timers are v1 Steam Zombies solo behavior. Do not promise co-op pause correctness, timer widgets, timer history, pause badge UI, or native **Event Monitor** shared snapshot timer fields without a separate validation pass.
