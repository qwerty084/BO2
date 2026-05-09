# Wiki Migration

The external GitHub wiki at `https://github.com/qwerty084/BO2/wiki` was migrated into repo docs on 2026-05-09 from wiki commit `f2ba659` (`2026-05-09 12:24:36 +0200`). The wiki clone contained nine Markdown pages and no images or other assets.

Local repo docs are now canonical. Future durable findings should be written under `docs/` or stored as raw evidence under `artifacts/reverse/`, not added first to the external wiki.

## Migrated Areas

| Wiki page | Repo destination |
| --- | --- |
| `Home` | [docs/index.md](../index.md), [docs/reverse/index.md](../reverse/index.md) |
| `Confirmed Memory Addresses` | [docs/reverse/address-ledger.md](../reverse/address-ledger.md), [docs/reverse/player-stats.md](../reverse/player-stats.md), [docs/reverse/timers.md](../reverse/timers.md), [docs/reverse/dvars-and-map-identity.md](../reverse/dvars-and-map-identity.md) |
| `VM Notify Production Events` | [docs/reverse/event-pipeline.md](../reverse/event-pipeline.md) |
| `In-Game And Round Timers` | [docs/reverse/timers.md](../reverse/timers.md) |
| `BO2 Box Weapon Tracking` | [docs/reverse/box-weapon-tracking.md](../reverse/box-weapon-tracking.md) |
| `BO2 Chat and Console Write Research` | [docs/reverse/chat-console-write-research.md](../reverse/chat-console-write-research.md) |
| `Upcoming Feature Recon` | [docs/reverse/dvars-and-map-identity.md](../reverse/dvars-and-map-identity.md), [docs/reverse/chat-console-write-research.md](../reverse/chat-console-write-research.md), [artifacts/reverse/open-questions.md](../../artifacts/reverse/open-questions.md) |
| `Ghidra and x32dbg Workflow` | [docs/reverse/ghidra-x32dbg-workflow.md](../reverse/ghidra-x32dbg-workflow.md) |

## Rewritten Or Discarded Claims

- External-wiki-as-canonical guidance was replaced with repo-local docs guidance.
- x64 and ARM64 build claims were removed. `BO2.csproj` supports only `Platform=x86`.
- `systemAIModels` capability claims were removed. The manifest declares `runFullTrust`.
- The old MainWindow description was replaced; `MainWindow.xaml.cs` owns substantial shell behavior.
- The stale Connect-gating note in `CONTEXT.md` was replaced with the current explicit Connect boundary.
- Box tracking wording was kept cautious: the broad owner-scoped `_zm` alias scan is current production behavior, but exact notify-owner alias lifetime still needs more event-boundary evidence.
- `BO2-CLI Current State` was not migrated as BO2 product documentation because this repository does not contain a standalone BO2-CLI project. Treat it as sibling-tool history unless that repo adopts its own docs.

## Intentionally Preserved External Reference

This migration note preserves the external wiki URL as provenance only. Product, developer, agent, and runtime docs should link to local repo docs.
