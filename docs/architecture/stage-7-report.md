# Stage 7 Report: Compatibility Observability and Removal Gate

- Completed: 2026-10-04
- Scope: measure legacy fallback usage and prove whether legacy identifiers are safe to remove
- Legacy API, table or column removed: no
- New database migration: no

## Outcome

Stage 7 establishes an evidence-based removal gate. Legacy names and columns are
not removed merely because an additive migration exists. The application can now
report incompatible rows, emit compatibility metrics and reject conflicting
dual-generation input while preserving the Stage 6 rollback window.

## Delivered

1. Added an admin-only readiness endpoint:

   ```text
   GET /api/scan-checks/compatibility-readiness
   ```

   It returns catalog, rule-mapping and scan-result totals together with missing
   or mismatched generic metadata counts and `readyForLegacyRemoval`.

2. Added a database-backed readiness service. Removal is blocked when any of
   these conditions exists:
   - a catalog row has no explicit `ExecutorType`;
   - a rule mapping has no `ScanCheckId`;
   - a rule mapping has conflicting legacy and generic identifiers;
   - a stored scan result has no `ScanCheckId`;
   - a stored scan result has conflicting legacy and generic identifiers.
3. The readiness check writes a structured information log when the gate passes
   and a structured warning containing all failing counts when it does not.
4. Added the `DbScanner.ScanCompatibility` meter with low-cardinality counters:

   | Instrument | Purpose |
   | --- | --- |
   | `dbscanner.scan_check.compatibility_fallbacks` | Counts reads that required a legacy fallback |
   | `dbscanner.scan_check.compatibility_dual_writes` | Counts compatibility writes to both identifier generations |
   | `dbscanner.scan_check.compatibility_conflicts` | Counts rejected conflicting old/new selections |

   Every instrument uses only the bounded `surface` tag. Check IDs, target IDs,
   usernames and other high-cardinality values are never metric tags.
5. Centralized identifier fallback in `ScanCheckIdCompatibility`. Rule catalog,
   rule evaluation, automatic scan selection and scan responses now share this
   behavior instead of duplicating null-coalescing logic.
6. Instrumented executor-name fallback in `LegacyScanCheckExecutorRouting` and
   contract fallback/conflict behavior in `ScanCheckSelectionCompatibility`.
7. Updated `RuleEngine` to prefer the generic rule mapping identifier. Legacy
   mappings still work and increment the fallback counter.
8. Protected the readiness endpoint with the admin policy. Viewer and operator
   roles receive `403 Forbidden`.

## Example readiness response

```json
{
  "catalogRows": 12,
  "catalogRowsMissingExecutorType": 0,
  "ruleMappings": 8,
  "ruleMappingsMissingScanCheckId": 0,
  "ruleMappingsWithMismatchedIds": 0,
  "scanResults": 42,
  "scanResultsMissingScanCheckId": 0,
  "scanResultsWithMismatchedIds": 0,
  "readyForLegacyRemoval": true
}
```

`readyForLegacyRemoval=true` is necessary but not sufficient for deletion. The
fallback metric must also remain at zero throughout the agreed rollback window.

## Physical rename decision

Physical renames are deferred. The current schema still uses legacy columns for
primary/foreign keys, and renaming the table and keys in the same deployment as
compatibility removal would combine several hard-to-reverse changes.

The safe sequence is:

```text
backfill complete
      -> readiness true
      -> fallback metric remains zero during rollback window
      -> clients/workers verified on generic contracts
      -> remove legacy contract writes
      -> later migration changes physical keys/names
```

## NSE-specific editor decision

The catalog reader and scan picker are generic, but the current editor still
creates and synchronizes local NSE definitions. It remains explicitly
NSE-oriented in this stage because arbitrary executor types are not all
registered with executable strategies. Presenting a fully generic creator now
would allow administrators to save a check that cannot execute.

The editor should become generic only after it can obtain the registered
executor capabilities from the backend and render executor-specific
configuration safely.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 110 passed, 0 failed |
| Infrastructure tests | 126 passed, 0 failed, 4 environment-dependent container tests skipped |
| Telemetry fallback/conflict tests | Passed |
| Readiness pass/fail database tests | Passed |
| Admin endpoint authorization tests | Passed for admin, viewer and operator |
| EF pending model changes | None |
| Frontend lint | Passed |
| Frontend production build | Passed |

The solution continues to report the 15 previously recorded dependency
vulnerability warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`.
Package versions were not changed in Stage 7.

## Environment note

The local Docker daemon was not running during the final audit, so no claim is
made that a live local or production database currently passes the readiness
gate. The endpoint and database queries are implemented and covered by both
passing and failing data tests. A real environment must return
`readyForLegacyRemoval=true` and show zero fallback events during its rollback
window before destructive cleanup is authorized.

## Rollback

- No legacy route, contract property, table or column was removed.
- No migration was added, so database rollback is unnecessary.
- Disabling or removing the new endpoint/service does not affect scan execution.
- Stage 4's execution rollback remains available:

  ```text
  Scanner__UseStrategyOrchestrator=false
  ```

## Gate for Stage 8

Stage 8 may prepare the generic contract as the primary internal vocabulary,
but destructive cleanup remains gated by live evidence. It should:

1. expose registered executor capabilities to administrators;
2. add executor-aware generic create/edit flows;
3. rename internal service parameters and models while retaining boundary
   adapters for old HTTP and RabbitMQ clients;
4. define the rollback-window duration and an operational dashboard/alert for
   compatibility fallbacks;
5. create a separate, explicitly approved cleanup migration only after live
   readiness is true and fallback usage remains zero.

Stage 8 has not been started.
