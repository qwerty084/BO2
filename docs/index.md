# BO2 Documentation

This directory is the canonical documentation set for BO2. The former external GitHub wiki has been migrated into repo docs; future durable findings should be added here or under `artifacts/reverse` when they are raw evidence.

## Start Here

- [README](../README.md): prerequisites, build, test, runtime, and packaging overview.
- [Project context](../CONTEXT.md): domain language and ownership boundaries.
- [Architecture decisions](adr/0001-game-connection-session-owns-active-read-policy.md): accepted design decisions.
- [Reverse-engineering index](reverse/index.md): current Steam Zombies runtime knowledge.
- [Validation index](validation/index.md): local and CI validation commands.
- [Wiki migration note](migration/wiki-migration.md): what moved from the external wiki and what was rewritten.

## Documentation Boundaries

- `AGENTS.md` and `.github/` instruction files are for agent workflow and coding conventions.
- `CONTEXT.md` is for project language, relationships, and domain ambiguities.
- `docs/reverse` is for durable runtime and reverse-engineering knowledge.
- `artifacts/reverse` is for raw or generated evidence, ledgers, runtime captures, and open research queues.
- `.scratch` is for active PRDs and implementation issues, not long-lived reference docs.
