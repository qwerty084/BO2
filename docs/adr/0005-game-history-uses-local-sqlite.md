# ADR 0005: Game History Uses Local SQLite

Status: Accepted

## Context

**Game History** is moving from a whole-document JSON file to a local SQLite database because completed games have relational child data, the UI only needs summaries at startup, and future filtering or detail loading should not require loading every saved round and box event. The SQLite store will use a relational schema for entries, rounds, and box events; expose database-shaped async operations for appending a completed entry, listing summaries newest-first, and loading one detail by ID; and use hand-written SQL rather than an ORM because the persistence boundary is small.

Existing JSON **Game History** data will not be migrated because the app is still in early development. The store will create and upgrade its schema in code with an integer schema version, persist timestamps and durations as signed 64-bit millisecond values, keep `GameHistoryEntry.Id` as the app-facing identity, and treat failed saves as a distinct recording status rather than as a discarded game.
