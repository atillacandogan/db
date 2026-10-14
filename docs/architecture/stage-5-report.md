# Stage 5 Report: Additive Generic Scan Catalog

- Completed: 2026-10-03
- Scope: executor-neutral catalog API, persisted executor metadata and version-tolerant contracts
- Production scan behavior changed: yes, catalog rows may now select an executor explicitly
- Legacy API, RabbitMQ and database data removed: no

## Delivered

1. Added executor-neutral catalog contracts and an Application service:
   - `ScanCheckDto`
   - `CreateScanCheckCommand`
   - `UpdateScanCheckCommand`
   - `IScanCheckCatalogService`
   - `ScanCheckCatalogService`
2. Added authenticated generic catalog endpoints while retaining every existing
   `/api/nse-scripts` endpoint:
   - `GET /api/scan-checks`
   - `POST /api/scan-checks`
   - `PUT /api/scan-checks/{id}`
   - `DELETE /api/scan-checks/{id}`
3. Added nullable `ExecutorType` metadata to the current catalog entity and
   table. This is an additive bridge: the physical legacy table and all foreign
   keys remain in place while callers can describe Nmap, native, agent or API
   checks without encoding the executor in a switch outside the compatibility
   layer.
4. Added migration `20261003201426_AddScanCheckExecutorMetadata`. It:
   - adds only a nullable `ExecutorType` column;
   - assigns `nmap-nse` to the two EF seed rows;
   - deletes or rewrites no user catalog data.
5. Added one shared legacy-routing policy. Rows with explicit metadata use that
   executor. Older rows with a null executor continue to use the established
   MySQL, MSSQL, Oracle and Nmap name-routing behavior.
6. Updated the NSE compatibility adapter so generic checks preserve their
   executor metadata when temporarily represented by the legacy catalog entity.
7. Added additive scan request compatibility:
   - HTTP accepts the new `selectedCheckIds` field and the old
     `selectedNseScriptIds` field;
   - RabbitMQ messages retain `SelectedNseScriptIds` and append
     `SelectedCheckIds`;
   - the publisher writes both fields for old-worker/new-worker compatibility;
   - the consumer reads either generation;
   - conflicting old and new selections are rejected instead of silently
     choosing one.
8. Kept the Stage 4 rollback switch. The legacy path now fails closed when a
   check requests an executor that the old runtime cannot execute, preventing a
   future agent or external-API check from being misinterpreted as an NSE script.

## Dual-read model

```text
Existing catalog row                    New catalog row
ExecutorType = null                     ExecutorType = "vendor-scanner"
        |                                       |
        v                                       v
legacy compatibility routing            explicit validated identifier
        |                                       |
        +-------------------+-------------------+
                            |
                            v
                  ScanCheckDefinition
                            |
                            v
               registered strategy resolver
```

Executor identifiers remain data, not executable paths or shell commands. Their
shape is validated by `ScanExecutorTypes`, and execution is allowed only when a
matching strategy has been registered in dependency injection.

## Contract compatibility

| Producer / consumer | Behavior |
| --- | --- |
| Old HTTP client | `selectedNseScriptIds` still works |
| New HTTP client | `selectedCheckIds` works |
| New publisher -> old worker | Legacy field is still populated |
| Old publisher -> new worker | New worker falls back to the legacy field |
| New publisher -> new worker | Both fields are checked and resolved |
| Conflicting fields | Request/message fails closed |

The old DTOs, endpoints and JSON fields were not renamed or removed. The new
fields were appended to positional records so existing JSON messages remain
deserializable.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 104 passed, 0 failed |
| Related scanner and persistence Infrastructure tests | 55 passed, 0 failed |
| New Stage 5 compatibility tests | 11 passed, 0 failed |
| Migration SQL generation | Passed; additive column and seed updates only |
| EF pending model changes | None |
| Frontend lint | Passed |
| Frontend production build | Passed |

The solution still reports the 15 previously recorded dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. Dependency versions
were not changed during this stage.

## Rollout and rollback

- AppHost migration startup will apply the new nullable column on the next
  restart when `Persistence:ApplyMigrations` is enabled.
- Existing rows need no immediate backfill; null keeps legacy routing behavior.
- The strategy execution rollback remains:

  ```text
  Scanner__UseStrategyOrchestrator=false
  ```

- The legacy path accepts the current Nmap/MySQL/MSSQL/Oracle executor set and
  rejects newer executor types that it cannot represent.
- No frontend was switched to the generic API in this stage. The existing UI
  continues to use the legacy endpoints and remains operational.

## Remaining legacy names

This stage deliberately does not rename or remove:

- the `nse_scripts` physical table;
- `RuleNseScript` and `rule_nse_scripts`;
- `ScanScriptResult.NseScriptId`;
- legacy frontend NSE DTOs and labels.

Changing those in the same deployment would make rollback and mixed-version
operation harder. They remain behind the new catalog facade until their own
additive migration is proven.

## Gate for Stage 6

Stage 6 may generalize rule mappings and execution-result persistence. It
should:

1. add generic check identifiers beside `RuleNseScript` and
   `ScanScriptResult.NseScriptId` before removing anything;
2. dual-read and dual-write rule/check relationships and stored results;
3. expose the generic scan-check API in the frontend without breaking old
   bookmarks or requests;
4. backfill explicit executor metadata and prove the backfill is idempotent;
5. run an end-to-end scan through both legacy and generic catalog records before
   considering removal of name-based compatibility routing.

Stage 6 has not been started.
