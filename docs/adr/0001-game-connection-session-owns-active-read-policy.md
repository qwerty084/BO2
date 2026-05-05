# ADR 0001: Game Connection Session Owns Active Read Policy

Status: Accepted

## Context

The app has one central **Game Connection Session** for the runtime relationship between the current **Detected Game** and the optional **Event Monitor**. It observes **Detected Game** changes, handles connect and disconnect commands, owns the **Event Monitor** start/stop relationship, performs active-read gating, and publishes the current **Game Connection Snapshot**.

The **Current Game Page**, shell connection display, and **Box Tracker Widget Runtime** consume the **Game Connection Snapshot** and projected display state. They should not decide whether a **Player Stats Read** or **Event Monitor** read is allowed. That policy depends on session lifecycle state, supported **Detected Game** identity, connect/disconnect transitions, monitor readiness, and cleanup after failures.

`GameConnectionSessionLifecycle` exists to keep lifecycle state transitions and command availability local and testable. It does not own process memory reads, **Event Monitor** IO, or snapshot publication.

## Decision

**Game Connection Session** remains the central module for **Detected Game** lifecycle, connect/disconnect behavior, active-read gating, **Event Monitor** ownership, and **Game Connection Snapshot** publication.

Active reads are gated inside **Game Connection Session**. A **Player Stats Read** and **Event Monitor** read happen only when the current **Detected Game** still matches the session state and the session lifecycle says the monitor is connected for that game. Callers consume snapshots or projections; they do not duplicate session lifecycle checks or trigger direct reads.

Internal helper modules such as `GameConnectionSessionLifecycle` are appropriate when they improve locality, reduce complexity, or make lifecycle behavior easier to test without moving IO policy into callers. Broad future architecture reviews should not propose splitting active-read or session lifecycle policy out of **Game Connection Session** unless there is new feature or bug friction that this ownership model cannot handle cleanly.

## Consequences

- **Game Connection Session** stays the single coordination point for **Detected Game** changes, explicit connect/disconnect, monitor cleanup, and active-read eligibility.
- **Current Game Page**, shell projectors, and **Box Tracker Widget Runtime** remain snapshot consumers rather than owners of **Player Stats Read** or **Event Monitor** policy.
- Lifecycle helper code can grow or be refactored behind **Game Connection Session**, but direct memory/monitor IO and **Game Connection Snapshot** publication remain in the session boundary.
- Tests should continue to cover session-level behavior for active-read gating, connect/disconnect transitions, monitor readiness, and cleanup after **Detected Game** changes or read failures.
