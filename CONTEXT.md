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

**Event Monitor**:
The native `BO2Monitor` payload loaded into Steam Zombies to publish read-only game events through a shared snapshot.
_Avoid_: injector, hook

**Player Stats Read**:
A read-only sample of confirmed player stat and candidate addresses from the current **Detected Game**.
_Avoid_: polling update, refresh

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
- A **Player Stats Read** belongs to exactly one **Detected Game** when Steam Zombies is supported.
- An **Event Monitor** can provide event data only after the **Game Connection Session** connects to Steam Zombies.
- A **Box Tracker Widget** displays recent box events from the current **Event Monitor**.
- A **Box Tracker Widget Runtime** owns the lifecycle of zero or one visible **Box Tracker Widget**.

## Example Dialogue

> **Dev:** "When the **Detected Game** changes, should the **Game Connection Session** keep the old **Event Monitor** alive?"
> **Domain expert:** "No. The session should stop the old monitor and wait for the user to connect to the new Steam Zombies process."

## Flagged Ambiguities

- "refresh" can mean UI timer work, process memory reads, or monitor snapshot reads. Use **Player Stats Read** for memory sampling and **Game Connection Session** for lifecycle coordination.
- Today, **Player Stats Read** can occur before the user connects the **Game Connection Session**. Planned direction: connect should become the user's explicit initiation point for active game reads, including **Player Stats Read** and **Event Monitor** data.
