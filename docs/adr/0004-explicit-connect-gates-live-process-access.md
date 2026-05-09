# ADR 0004: Explicit Connect Gates Live Process Access

Status: Accepted

## Context

The app can detect and display the current **Detected Game** before the user explicitly connects. That pre-connect state is useful for showing candidates, supported-game status, and connection availability, but it must remain passive.

The live **Black Ops 2** process boundary is different. Reading game memory, writing game memory, injecting the **Event Monitor** DLL, starting remote threads, installing hooks, or opening write-capable process handles are actions that affect or actively inspect the running game process. Those actions should require an explicit user command.

ADR 0001 already makes **Game Connection Session** the owner of active-read policy, connect and disconnect behavior, **Event Monitor** ownership, and **Game Connection Snapshot** publication. This ADR makes the user consent boundary explicit: the **Connect** command is the first point where live process access can begin.

## Decision

Before the user clicks **Connect**, the app may perform only passive game detection and UI projection work. It must not read from, write to, inject into, start remote execution in, hook, or otherwise modify the **Black Ops 2** process.

After the user clicks **Connect**, **Game Connection Session** owns the gate for live process access. Memory reads, memory writes, **Event Monitor** DLL injection, monitor startup, and hook installation can happen only through the connected session lifecycle for the current supported **Detected Game**.

Disconnect, **Detected Game** change, failed connection, monitor failure, or session cleanup returns the app to a no-live-access state for that game. The app must fail closed: callers should observe snapshots or projected state, not bypass **Game Connection Session** to perform direct live process access.

Pre-connect code may inspect ordinary process metadata needed for detection, such as process name, executable path, process ID, and architecture, but it must avoid APIs or handles that read or write the target process address space or prepare injection.

## Consequences

- **Connect** is the explicit consent boundary for live **Black Ops 2** process access.
- Candidate detection and shell UI can stay responsive before connection without touching game memory or injection paths.
- **Game Connection Session** remains the single coordination point for live process reads, writes, injection, monitor startup, cleanup, and snapshot publication.
- `DllInjector`, memory readers, **Event Monitor** startup, and native hook behavior should be reachable only from the connected session lifecycle.
- Tests should cover that pre-connect flows do not invoke memory readers, write paths, DLL injection, monitor startup, or event monitor reads.
- Future features that need live process access must route through **Connect** and the connected **Game Connection Session** lifecycle rather than adding background probing before user consent.
