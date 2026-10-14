# Stage 4 Report: Production Strategy Cutover

- Completed: 2026-10-03
- Scope: production runtime cutover, compatibility bridge and rollback switch
- Production scan path changed: yes
- Public API, RabbitMQ and database contracts changed: no

## Delivered

1. Added `StrategyScannerRuntime` as the production `IScannerRuntime`
   implementation. It routes the current NSE-backed catalog through the Stage 3
   executor strategies and `ScanExecutionOrchestrator`.
2. Kept the previous runtime chain behind the internal
   `ILegacyScannerRuntime` rollback seam. No scan workflow caller had to change.
3. Added `Scanner:UseStrategyOrchestrator` and enabled it by default in the
   Scanner Worker. Setting it to `false` immediately restores the legacy runtime
   path without a code change:

   ```text
   Scanner__UseStrategyOrchestrator=false
   ```

4. Preserved baseline discovery behavior. A required executor can now run with
   an empty check group, so `nmap-nse` still produces the baseline XML for an
   empty or native-only plan.
5. Preserved the existing MySQL compatibility path. A plan owned by
   `mysql-native` does not schedule an additional Nmap baseline because that
   strategy already performs its compatible Nmap preflight.
6. Added a compatibility conversion from `CompositeScanExecutionResult` back to
   the existing `ScanExecutionResult`. The current rule engine, persistence
   flow and scan state machine therefore continue to consume their established
   contract.
7. Preserved XML evidence:
   - one XML artifact is returned unchanged;
   - multiple XML artifacts are wrapped in a deterministic
     `dbscanner-execution` document;
   - descendant XPath selectors such as `//script` continue to work.
8. Timeout and cancellation exceptions are allowed to propagate unchanged, so
   the existing workflow remains responsible for its current timeout,
   cancellation and failure transitions.

## Production flow

```text
ScanProcessingService
        |
        v
IScannerRuntime -> StrategyScannerRuntime
        |
        +-- flag false ---------------------> legacy runtime chain
        |
        +-- flag true
                |
                v
        NSE catalog compatibility router
                |
                v
        ScanExecutionOrchestrator
                |
                +-- nmap-nse
                +-- mysql-native
                +-- mssql-native
                +-- oracle-native
                |
                v
        CompositeScanExecutionResult
                |
                v
        legacy result compatibility bridge
                |
                v
        current rule engine and persistence flow
```

The strategy implementation is now the default production path. Adding another
scanner no longer requires changing `ScanProcessingService`; the scanner is
introduced as a new executor strategy, routed by stable metadata and verified
with contract/parity tests.

## Compatibility evidence

The Stage 4 tests verify that:

- the enabled runtime uses the orchestrator and does not invoke the legacy
  runtime;
- the disabled runtime invokes only the legacy path;
- empty plans still request the Nmap baseline;
- the MySQL compatibility plan does not run Nmap twice;
- structured and per-check results survive the compatibility bridge;
- merged XML retains rule-engine XPath evidence;
- timeout and cancellation propagate unchanged;
- production dependency injection resolves `StrategyScannerRuntime` and exactly
  the four registered Stage 3 executor types.

The complete Application characterization suite also remains green, including
the existing scan workflow state, persistence, rule evaluation, timeout and
cancellation tests.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 98 passed, 0 failed |
| New Stage 4 compatibility tests | 8 passed, 0 failed |
| Related scanner Infrastructure tests | 41 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |

The solution still reports the 15 previously recorded dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. Dependency versions
were not changed during this stage.

## Compatibility and rollback

- API request/response models are unchanged.
- RabbitMQ messages are unchanged.
- Database entities and migrations are unchanged by this stage.
- Frontend DTOs and legacy NSE catalog fields are unchanged.
- Rollback requires only `Scanner__UseStrategyOrchestrator=false` followed by a
  Scanner Worker/AppHost restart.
- No data migration is required to move between the new and legacy execution
  paths.

## Gate for Stage 5

Stage 5 may begin the additive catalog and contract generalization needed to
describe non-NSE scanners natively. It should:

1. introduce executor-neutral check metadata without deleting legacy NSE
   fields;
2. use dual-read/compatibility mapping while existing data is still present;
3. keep API, RabbitMQ and persistence changes additive and version tolerant;
4. prove old and new catalog records produce equivalent execution plans;
5. retain the Stage 4 rollback switch until the generalized catalog has been
   exercised end to end.

Stage 5 has not been started.
