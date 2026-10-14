# Stage 8 Report: Executor Capabilities and Generic Catalog Editing

- Completed: 2026-10-04
- Scope: capability-driven executor discovery, generic create/edit UI and operational rollback window
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

The generic scan-check contract is now the primary application and frontend
vocabulary. Executor-specific behavior is declared by each registered Strategy,
not duplicated in a UI switch. Legacy HTTP, RabbitMQ and database fields remain
as boundary adapters until live readiness evidence satisfies the cleanup gate.

## Delivered

1. Added `ScanExecutorCapability` to the Strategy contract. Each registered
   executor declares:
   - stable executor identifier;
   - display name and description;
   - supported database engines;
   - whether administrators may create manual checks;
   - whether a check name may be changed;
   - whether JSON configuration schemas are supported.
2. Added an admin-only capability endpoint:

   ```text
   GET /api/scan-checks/executors
   ```

   The response is built from the DI-backed Strategy registry. An executor does
   not appear merely because its identifier has a valid shape; an implementation
   must actually be registered.
3. Declared capabilities for the four current strategies:

   | Executor | Engines | Manual creation | Configuration schema |
   | --- | --- | --- | --- |
   | `nmap-nse` | Generic and all supported databases | Yes | Yes |
   | `mysql-native` | MySQL | No | No |
   | `mssql-native` | Microsoft SQL Server | No | No |
   | `oracle-native` | Oracle | No | No |

4. Hardened generic catalog writes:
   - unregistered executor types are rejected;
   - manual creation is rejected when the Strategy does not advertise it;
   - executor type cannot be changed on an existing check;
   - name, engine and configuration changes are validated against capability;
   - unchanged legacy metadata can still be saved during the compatibility
     window.
5. Migrated the Rules catalog editor from legacy NSE mutations to generic
   scan-check mutations:
   - create, update, delete, enable and default-state changes use
     `/api/scan-checks`;
   - installed NSE synchronization remains on the intentionally NSE-specific
     sync endpoint;
   - executor choices come from `/api/scan-checks/executors`;
   - unsupported executors are visible for existing records but disabled for
     manual creation;
   - supported engines and configuration-schema controls react to capability;
   - credentials and intrusive flags are editable.
6. Renamed internal scan-service, processing and publisher parameters from NSE
   selections to scan-check selections. The old HTTP and RabbitMQ property names
   remain only at boundary compatibility adapters and continue to be dual-written.
7. Added the observable readiness gauge:

   ```text
   dbscanner.scan_check.compatibility_ready
   ```

   Values are `1` ready, `0` blocked and `-1` not audited.
8. Defined a seven-day continuous rollback window and added operational assets:
   - `ops/observability/scan-compatibility-dashboard.json`
   - `ops/observability/scan-compatibility-alerts.yaml`
   - `docs/architecture/scan-compatibility-operations.md`

## Extension path

Adding a future executor now follows one path:

```text
implement IScanExecutorStrategy
        |
        +-- ExecutorType
        +-- Capability
        +-- ExecuteAsync
        |
register strategy in DI
        |
        +--> capability API
        +--> generic catalog editor
        +--> strategy resolver/orchestrator
```

No frontend executor-name switch is required. A future Strategy can enable
manual creation only when its execution and configuration behavior is ready.

## Boundary compatibility

| Layer | Primary vocabulary | Retained adapter |
| --- | --- | --- |
| Application services | selected check IDs | none internally |
| Generic catalog API | scan checks and executor capabilities | legacy NSE routes remain |
| HTTP scan request | `selectedCheckIds` | `selectedNseScriptIds` accepted |
| RabbitMQ scan message | `SelectedCheckIds` | `SelectedNseScriptIds` read/written |
| Persistence | `ScanCheckId` read first | legacy columns still dual-written |
| Installed file discovery | NSE-specific by design | `/api/nse-scripts/sync` |

## Operational removal gate

The compatibility window is **seven continuous days** after every API and
worker instance is upgraded. The clock restarts when:

- any legacy fallback occurs;
- any old/new contract conflict occurs;
- readiness becomes false;
- a pre-Stage-8 instance returns to service.

Cleanup requires readiness `1`, zero fallback/conflict increases and a verified
deployment inventory throughout the entire window. The provided dashboard and
alerts encode those signals. The environment's OpenTelemetry/Prometheus pipeline
must subscribe to the `DbScanner.ScanCompatibility` meter.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 114 passed, 0 failed |
| Infrastructure tests | 126 passed, 0 failed, 4 environment-dependent container tests skipped |
| Capability endpoint authorization | Admin passed; viewer/operator forbidden |
| Manual-creation capability enforcement | Passed |
| Legacy and generic request compatibility | Passed |
| EF pending model changes | None |
| Frontend lint | Passed |
| Frontend production build | Passed |
| Grafana dashboard JSON validation | Passed |

The solution continues to report the 15 previously recorded dependency
vulnerability warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`.
Dependency versions were not changed in Stage 8.

## Environment note

The local Docker daemon remained unavailable, so AppHost, PostgreSQL, RabbitMQ
and Keycloak were not started for an interactive browser smoke test. The API
contracts, authorization, capability enforcement, frontend type checking and
production build are covered, but the operational seven-day clock has not
started and no live-readiness claim is made.

## Rollback

- No schema or data migration was added.
- Legacy API and queue fields remain operational.
- The frontend can be rolled back without losing generic catalog data because
  Stage 6 dual writes remain active.
- Strategy execution can still be rolled back with:

  ```text
  Scanner__UseStrategyOrchestrator=false
  ```

## Gate for Stage 9

Stage 9 should focus on proving production readiness, not deleting fields by
default. It may:

1. connect the compatibility meter to the selected telemetry exporter;
2. deploy/import the supplied dashboard and alert rules;
3. record per-environment readiness and deployment inventory for seven days;
4. add contract tests using an old producer against the Stage 8 consumer and a
   Stage 8 producer against the old consumer fixture;
5. produce a separately reviewed cleanup proposal. Physical renames or legacy
   removals require explicit approval after the observation window.

Stage 9 has not been started.
