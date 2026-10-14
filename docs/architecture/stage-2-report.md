# Stage 2 Report: Strategy Registry and Composite Orchestration

- Completed: 2026-10-03
- Scope: application strategy ports, resolver, orchestrator, legacy runtime adapter and tests
- Production scan path changed: no
- Public API, RabbitMQ and database contracts changed: no

## Delivered

1. Added the strategy port `IScanExecutorStrategy`. Each implementation owns one
   stable executor type and receives only the checks grouped for that executor.
2. Added `IScanExecutorStrategyResolver` and `ScanExecutorStrategyResolver` as a
   DI-backed registry. It:
   - resolves only server-registered strategies;
   - rejects invalid executor identifiers;
   - fails on duplicate executor registrations;
   - exposes registered identifiers for diagnostics.
3. Added `IScanExecutionOrchestrator` and `ScanExecutionOrchestrator`. The
   orchestrator:
   - rejects blank and duplicate check identifiers;
   - groups checks by executor in first-seen order;
   - runs executor groups sequentially for deterministic output and bounded
     database load;
   - propagates cancellation before and during execution;
   - merges structured output, check results and raw artifacts;
   - rejects results or artifacts attributed to another executor;
   - rejects non-null check IDs that were not requested.
4. Added `ScanExecutorResult`, the per-strategy result contract merged into the
   existing `CompositeScanExecutionResult` from Stage 1.
5. Added `LegacyScannerRuntimeExecutorStrategy`, an anti-corruption adapter that
   maps generic checks to the current `IScannerRuntime` chain and maps its XML
   result back to the generic result model.
6. Registered the resolver and orchestrator in Application DI and the temporary
   legacy strategy in Infrastructure DI.

## Execution model

```text
ScanCheckDefinition[]
        |
        v
ScanExecutionOrchestrator
        |
        +-- group: nmap-nse ----> registered strategy ----+
        +-- group: external-api -> registered strategy ----+--> CompositeScanExecutionResult
        +-- group: future-type --> registered strategy ----+
```

Executor names select only DI-registered strategy objects. They are not file
paths, process names or shell commands. Adding a future strategy requires a new
implementation and DI registration rather than a switch statement in the
orchestrator.

## Compatibility and rollback

- `ScanProcessingService` still calls `IScannerRuntime` directly.
- Current Nmap, compatibility, MSSQL and Oracle runtime behavior is untouched.
- The legacy strategy adapter is registered but no production workflow has been
  switched to the new orchestrator.
- Removing the Stage 2 registrations and types restores the pre-stage structure;
  no data migration or public contract rollback is necessary.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 97 passed, 0 failed |
| New Stage 2 Application tests | 9 passed, 0 failed |
| Legacy strategy adapter tests | 2 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |

The initial sandboxed Infrastructure test attempt was blocked because MSBuild and
VSTest could not open their local communication sockets. The focused adapter
tests were rerun outside that socket restriction and passed. The solution still
reports the 15 previously recorded dependency vulnerability warnings for
`Microsoft.OpenApi`, `SSH.NET` and `MessagePack`; dependency versions were not
changed.

## Gate for Stage 3

Stage 3 may replace the temporary legacy strategy with executor-specific
strategies for Nmap/NSE and the native database probes. It must:

1. preserve current Nmap, MySQL, MSSQL and Oracle routing behavior;
2. keep credentials and cancellation semantics unchanged;
3. add parity tests comparing new strategy results with the legacy runtime;
4. avoid duplicate executor registrations while the legacy adapter is removed;
5. keep the public API, RabbitMQ and database contracts backward compatible.

The production path should not be switched until those parity tests pass.
