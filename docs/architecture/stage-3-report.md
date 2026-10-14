# Stage 3 Report: Executor-Specific Scanner Strategies

- Completed: 2026-10-03
- Scope: executor routing, Nmap/NSE and native database strategies, parity tests
- Production scan path changed: no
- Public API, RabbitMQ and database contracts changed: no

## Delivered

1. Replaced the temporary all-in-one legacy strategy registration from Stage 2
   with four executor-specific strategies:
   - `NmapNseScanExecutorStrategy`
   - `MySqlNativeScanExecutorStrategy`
   - `MsSqlNativeScanExecutorStrategy`
   - `OracleNativeScanExecutorStrategy`
2. Added the temporary migration port `INseScriptScanCheckRouter` and its
   Infrastructure implementation. Routing is server controlled:
   - ordinary NSE checks -> `nmap-nse`;
   - `mysql-empty-password` -> `mysql-native`;
   - MSSQL Browser and `mssql-auth-*` checks -> `mssql-native`;
   - `oracle-auth-*` checks -> `oracle-native`.
3. Preserved the existing MySQL compatibility behavior. When
   `mysql-empty-password` is selected with other compatible MySQL/Nmap checks,
   the router assigns the whole group to the MySQL compatibility strategy. The
   existing `mysql-info` preflight therefore runs once and the
   `mysql_native_password` NSE fallback remains available.
4. Preserved MSSQL behavior:
   - Nmap service discovery and `ms-sql-info` normalization stay in the Nmap
     strategy;
   - Browser discovery and authenticated checks run in the MSSQL native strategy;
   - missing credentials still produce explicit `Skipped` results.
5. Preserved Oracle behavior:
   - TNS/Nmap discovery stays in the Nmap strategy;
   - authenticated checks run in the Oracle native strategy;
   - missing credentials still produce explicit `Skipped` results.
6. Added shared native-result conversion and request guards. Native strategies
   reject checks owned by another executor and targets using the wrong database
   engine.
7. Extended the Stage 1 adapter with an explicitly named
   `ToLegacyNseScript` transition method. It allows native strategies to reuse
   the current catalog metadata entity without pretending the executor is Nmap.

## Current routing

```text
Legacy NSE catalog entries
        |
        v
NseScriptScanCheckRouter
        |
        +-- ordinary NSE --------------------> nmap-nse
        +-- MySQL empty password plan -------> mysql-native
        +-- MSSQL browser/auth --------------> mssql-native
        +-- Oracle auth ---------------------> oracle-native
        |
        v
ScanExecutionOrchestrator -> CompositeScanExecutionResult
```

Adding a new scanner no longer requires editing a central execution switch. The
integration supplies a strategy, a stable executor identifier, routing/catalog
metadata and tests, then registers the strategy in DI.

## Parity evidence

The new orchestrator path was compared with the existing hard-coded runtime
chain for:

- PostgreSQL Nmap/NSE execution;
- MySQL empty-password compatibility execution;
- combined MySQL `mysql-info` and empty-password execution;
- MSSQL Nmap discovery, Browser discovery and authenticated execution;
- Oracle Nmap discovery and authenticated execution.

The tests compare structured output and every persisted check-result invariant:
check ID, name, normalized output, exit code and result status. They also verify
that artifacts remain valid XML and credentials do not appear in normalized or
structured output.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 97 passed, 0 failed |
| New Stage 3 router/parity tests | 14 passed, 0 failed |
| Related Nmap/MySQL/MSSQL/Oracle Infrastructure tests | 34 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |

The solution still reports the 15 previously recorded dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. Dependency versions
were not changed.

## Compatibility and rollback

- `ScanProcessingService` still calls the existing `IScannerRuntime` chain.
- The new strategies are registered and tested but are not the production path.
- API requests, queue messages, database entities and frontend DTOs retain their
  legacy NSE fields.
- The Stage 2 `LegacyScannerRuntimeExecutorStrategy` was removed to prevent a
  duplicate `nmap-nse` registration; the current `IScannerRuntime` implementation
  itself remains untouched.

## Gate for Stage 4

Stage 4 may switch scan processing to the new orchestrator through a reversible
compatibility adapter or feature flag. Before enabling it by default, it must:

1. preserve zero-check baseline-scan behavior or reject that state explicitly;
2. convert the composite result back to the existing rule/persistence contract
   without losing XML artifacts or native results;
3. keep timeout, cancellation and failure-state transitions unchanged;
4. run the full scan-workflow characterization suite on both execution paths;
5. keep legacy API, RabbitMQ and database contracts operational.

Catalog, messaging, persistence and UI terminology should remain a later,
additive migration after the production execution path is proven.
