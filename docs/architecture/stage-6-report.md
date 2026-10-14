# Stage 6 Report: Generic Rule and Result Relationships

- Completed: 2026-10-03
- Scope: generic scan-check identifiers, dual persistence, frontend adoption and data backfill
- Production scan behavior changed: no executor behavior was removed; persistence now writes both identifier generations
- Legacy API, fields and database columns removed: no

## Delivered

1. Added nullable `ScanCheckId` fields beside the existing identifiers on:
   - `RuleNseScript`
   - `ScanScriptResult`
2. Extended rule and scan-result contracts additively:
   - rule responses expose both `nseScripts` and `scanChecks`;
   - rule create/update requests accept either generation;
   - scan-result responses expose `scanCheckId` while retaining `nseScriptId`.
3. Implemented rule relationship compatibility:
   - legacy-only input is accepted;
   - generic-only input is accepted;
   - matching dual input is accepted;
   - conflicting dual input fails closed;
   - all new and updated relationships write the effective identifier to both columns.
4. Implemented scan persistence compatibility:
   - rule selection reads `ScanCheckId` first and falls back to `NseScriptId`;
   - execution results write both `ScanCheckId` and `NseScriptId`;
   - response mapping reads the generic identifier and falls back to the legacy value.
5. Added migration `20261003201450_GeneralizeRuleAndResultCheckIds`:
   - adds two nullable, indexed columns;
   - backfills rule mappings and stored results from the legacy identifiers;
   - backfills missing executor metadata using the established compatibility-routing rules;
   - deletes or renames no legacy data.
6. Made future catalog writes explicit:
   - manual NSE catalog creation stores `nmap-nse` or the resolved legacy executor;
   - installed-script synchronization stores `nmap-nse`;
   - existing installed rows are repaired when their executor metadata is missing.
7. Moved frontend catalog reads to `GET /api/scan-checks` on the dashboard,
   rule catalog and target scan picker. The current NSE mutation endpoints remain
   available for the NSE-specific create/edit/sync workflow.
8. Updated frontend terminology and result handling:
   - catalog and selection surfaces use **Scan Checks**;
   - scan requests send both `selectedCheckIds` and `selectedNseScriptIds`;
   - result screens and exported reports read `scanCheckId` first and fall back
     to `nseScriptId`;
   - the new frontend tolerates a response from an older backend that does not
     yet include `scanChecks`.

## Dual-read and dual-write path

```text
old rule/request                 new rule/request
nseScripts / NseScriptId        scanChecks / ScanCheckId
          \                         /
           +---- compatibility ----+
                       |
                       v
              effective check id
                       |
              +--------+--------+
              |                 |
              v                 v
        legacy column       generic column
```

The legacy column remains required for the current foreign key and mixed-version
deployment safety. No schema rename is attempted in this stage.

## Migration safety and idempotency

The backfill statements only modify rows where the new value is null:

```sql
UPDATE rule_nse_scripts
SET "ScanCheckId" = "NseScriptId"
WHERE "ScanCheckId" IS NULL;

UPDATE scan_script_results
SET "ScanCheckId" = "NseScriptId"
WHERE "ScanCheckId" IS NULL
  AND "NseScriptId" IS NOT NULL;

UPDATE nse_scripts
SET "ExecutorType" = CASE ... END
WHERE "ExecutorType" IS NULL;
```

Running the data updates again is therefore a no-op for already migrated rows.
Explicit executor choices and generic identifiers are not overwritten.

## Compatibility matrix

| Path | Result |
| --- | --- |
| Old rule request only | Accepted and dual-written |
| New rule request only | Accepted and dual-written |
| Matching old + new rule fields | Accepted |
| Conflicting old + new rule fields | Rejected |
| Legacy rule row with null `ScanCheckId` | Selected through `NseScriptId` fallback |
| Generic rule row | Selected through `ScanCheckId` |
| New stored scan result | Both identifiers populated |
| Old stored scan result | API/frontend fall back to `NseScriptId` |
| New frontend against an older rule response | Falls back to `nseScripts` |

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 105 passed, 0 failed |
| Infrastructure tests | 124 passed, 0 failed, 4 environment-dependent container tests skipped |
| Focused persistence/strategy tests | 40 passed, 0 failed |
| Legacy + generic rule-to-result workflow test | Passed |
| EF pending model changes | None |
| Generated Stage 6 migration SQL | Reviewed; additive columns, guarded backfills and indexes |
| Frontend lint | Passed |
| Frontend production build | Passed |

The solution still reports the 15 previously recorded dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. Dependency versions
were not changed in Stage 6.

## Rollout and rollback

- Apply Stage 5 before Stage 6. AppHost migration startup can apply both in order.
- Deploying the backend first is preferred, but the new frontend still handles
  the old rule response shape.
- Rolling the application back remains safe because all writes retain the legacy
  identifiers.
- Rolling the database migration down drops only the two generic identifier
  columns and their indexes. It deliberately does not erase the useful executor
  metadata backfill.
- The strategy rollback switch remains:

  ```text
  Scanner__UseStrategyOrchestrator=false
  ```

## Gate for Stage 7

Stage 7 may reduce legacy naming and compatibility code only after observing a
successful mixed-version rollout. Before removing anything it should:

1. verify production rows have non-null generic identifiers and executor types;
2. add telemetry for generic-versus-legacy fallback usage;
3. decide whether physical table/column renames justify their migration risk;
4. retain an explicit rollback window and avoid removing old API routes in the
   same deployment as schema cleanup;
5. update remaining NSE-specific create/edit UI only when non-NSE check creation
   is supported end to end.

Stage 7 has not been started.
