# ADR 0002: Hook.cpp Is The Native Live Adapter

Status: Accepted

## Context

The Event Monitor runs inside the current Detected Game and must interact with live BO2 memory, fixed Steam Zombies addresses, Windows memory protection APIs, SEH-guarded reads, MinHook setup, and worker-loop shutdown handles. Those responsibilities are tightly coupled to the loaded process and cannot be exercised as ordinary pure logic.

`BO2Monitor\Hook.cpp` is the current live adapter for that boundary. It owns the fixed addresses and byte-prologue checks, probes process memory, installs and removes the `vm_notify` hook, reads live script/string/stat memory, drains matched notify records, runs the polling fallback loop, and publishes discovery and event data into the shared snapshot.

The architecture already separates policy and algorithms that can be tested without a live Detected Game. `HookCompatibility`, `NotifyPublication`, `PollingFallback`, `SharedSnapshot`, and other native modules expose small interfaces and are covered by `BO2.NativeTests`. `Hook.cpp` wires those modules to the live process through concrete adapter implementations.

## Decision

`BO2Monitor\Hook.cpp` remains the native live adapter/orchestrator for the Event Monitor. It is allowed to stay large where the size comes from live-memory integration: fixed BO2 addresses, MinHook lifecycle, process-memory probing, SEH-protected reads, worker loop timing, and wiring together tested native modules.

New logic should prefer the existing direction: put pure policy, validation, publication rules, fallback state machines, snapshot contracts, and other deterministic algorithms in small native modules with focused interfaces and native tests. `Hook.cpp` should supply the live implementations of those interfaces and keep direct access to the Detected Game process boundary.

Broad future architecture reviews should not propose extracting `Hook.cpp` only because it is large. Extraction should happen opportunistically while changing live-memory or hook behavior, and only when a new module would concentrate a real policy or algorithm behind a small interface. Mechanical splits that merely move fixed addresses, MinHook calls, or live probing into another file do not improve the architecture.

## Consequences

`Hook.cpp` remains the place to inspect when debugging Event Monitor compatibility, hook installation, discovery evidence, live memory reads, polling fallback orchestration, or worker shutdown behavior.

The native test surface stays centered on pure modules and small interfaces. Changes that add or alter policy should usually add or update `BO2.NativeTests`; changes that only touch the live adapter may still require manual validation against a supported Detected Game.

Future extraction work is judged by behavior and testability, not by file size. Useful extractions should reduce live adapter complexity by isolating a coherent rule set; unhelpful extractions that scatter live-process details should be avoided.
