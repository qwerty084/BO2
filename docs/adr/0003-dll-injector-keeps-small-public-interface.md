# ADR 0003: DllInjector Keeps A Small Public Interface

Status: Accepted

## Context

`Services\DllInjector.cs` is dense because it centralizes security-sensitive Event Monitor injection work: supported-game checks, monitor payload path resolution, PE machine and export validation, remote `LoadLibraryW` startup, `StartMonitor` launch, WOW64 helper launch, bounded waits, and handle cleanup.

The public surface used by Game Connection Session callers is intentionally much smaller than that implementation. Callers pass an optional `DetectedGame` to `Inject` and receive a `DllInjectionResult`; they do not coordinate native handles, payload validation, PE/export parsing, or helper process details.

The lower-level helper path already has its own orchestration module in `BO2InjectorHelper\InjectorOrchestration.*`, with native tests covering remote thread sequencing, export address calculation, cleanup, and failure cases. The C# tests cover the app-facing injection contract, payload validation, PE/export parsing, architecture routing, and result mapping.

## Decision

Keep `DllInjector` as the small public interface for app-side Event Monitor startup. Its implementation may remain dense when that density preserves locality across validation, architecture selection, native injection, helper launch, and result mapping.

Broad future architecture reviews should not propose splitting `DllInjector` only because the file is large. Split it only when changing injection behavior, PE/export parsing, helper launch, or payload validation creates a real deeper module with stronger locality than the current centralized implementation.

The public seam is intentionally small for Game Connection Session callers: one injection request in, one result out.

## Consequences

- Game Connection Session code stays insulated from native injection details and security-sensitive branching.
- Tests can continue to exercise the app-facing contract in C# and the helper orchestration behavior in native tests.
- `DllInjector` remains a careful, high-attention file; changes should favor explicit validation, bounded native waits, and cleanup clarity over cosmetic decomposition.
- New modules are justified by cohesive behavior boundaries, not line count.
