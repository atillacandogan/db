# Stage 11 Report: Release Burn-In and Evidence Integrity

- Completed: 2026-10-04
- Engineering scope: continuous preflight burn-in, tamper detection, atomic state persistence and local rollout wiring
- Seven-day live observation complete: no
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

Stage 11 hardens the automated rollout gate before a seven-day observation is
allowed to start. A single healthy metric sample is no longer sufficient. Every
expected API and worker instance must now remain continuously ready on the exact
required build during a 15-minute burn-in, with no fallback or contract-conflict
growth.

New observation states are atomically persisted with their burn-in evidence and
a canonical SHA-256 integrity digest. Modified, legacy-format and future-dated
state files fail closed. Local AppHost configuration is prepared to dual-export
API and worker metrics to the operations Collector after the next AppHost
restart.

Engineering work for Stage 11 is complete. The currently running AppHost was
not interrupted, so it is still the pre-Stage-11 process and the observation
clock has not started. No time was simulated and no compatibility boundary was
removed.

## Delivered

1. Introduced rollout manifest `schemaVersion: 2` with an explicit
   `preflightMinutes` burn-in interval.
2. Set the Stage 11 release identity for API and worker telemetry.
3. Added a 15-minute preflight gate sampled every five minutes. It requires:
   - all expected services and minimum instance counts;
   - exact `stage-11` deployment version;
   - readiness `1` at every required point;
   - zero legacy fallback increase;
   - zero contract-conflict increase.
4. Reused the same hardened preflight inside `start`, preventing callers from
   bypassing burn-in by invoking `start` directly.
5. Added state schema version 2 containing:
   - environment;
   - immutable manifest digest;
   - UTC start time;
   - complete preflight evidence;
   - canonical state integrity digest.
6. Replaced direct state writes with a same-directory temporary file, file
   flush, `fsync` and atomic replace. An interrupted write cannot leave a
   partially valid state document.
7. Added fail-closed validation for:
   - modified state contents;
   - missing state digest;
   - legacy state format;
   - observation start after the evaluation time;
   - manifest changes after start.
8. Added separate configurable AppHost OTLP endpoints for:
   - the host-process API (`localhost`);
   - the containerized scanner worker (`host.docker.internal`).
9. Added a committed local rollout manifest matching AppHost's `local`
   deployment environment.
10. Documented that the SHA-256 checksum provides file-integrity detection but
    is not a digital signature. Cross-administrator evidence must additionally
    be retained in an immutable deployment artifact store.

## Compatibility

- Manifest schema 1 remains parseable for operational compatibility and uses no
  burn-in interval, but new Stage 11 state creation always uses schema 2.
- Legacy schema 1 state files cannot authorize Stage 11 completion; evaluation
  blocks and requires a new observed window.
- Existing HTTP fields, RabbitMQ fields, database columns, adapters and dual
  writes remain available.
- The secondary operations exporter remains optional and does not replace the
  primary Aspire OTLP exporter.

## Verification

| Check | Result |
| --- | --- |
| AppHost build | Passed, 0 errors |
| Application tests | 117 passed, 0 failed |
| Full infrastructure suite | 129 passed, 1 skipped, 0 failed |
| Stage 11 rollout-manager tests | 14 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |
| Python syntax validation | Passed |
| Stage 11 manifest JSON validation | Passed |
| Atomic state write and cleanup | Passed |
| Modified-state rejection | Passed |
| Future-dated-state rejection | Passed |
| Continuous preflight rejection | Passed when one sample is missing |
| Preflight fallback rejection | Passed |
| Live local fail-closed preflight | Correctly blocked; no state created |
| Live local fail-closed start | Correctly blocked; no state created |

The single skipped infrastructure test is the existing host-dependent real
Nmap assertion. SQL Server and Oracle container integration tests executed and
passed.

The existing `Microsoft.OpenApi`, `SSH.NET` and `MessagePack` package
vulnerability warnings remain visible. Stage 11 did not alter dependency
versions.

## Live result

The local observability stack remained available and the Stage 11 local
manifest was queried against Prometheus. Preflight returned exit code `3` with:

```text
DbScanner.Api has 0 instance(s); expected at least 1.
DbScanner.ScannerWorker has 0 instance(s); expected at least 1.
DbScanner.Api has 0/4 continuous ready samples.
DbScanner.ScannerWorker has 0/4 continuous ready samples.
```

This is expected because AppHost was already running before the Stage 11 build
and was intentionally not interrupted. The next AppHost restart will apply the
new API and worker version plus the secondary Collector endpoints. After four
continuous samples, preflight may pass if database readiness and compatibility
counters are clean.

No `/private/tmp/dbscanner-stage11-rollout-state.json` state was created by the
blocked check.

## Rollback and safety

- No database migration or destructive change was added.
- Removing the two AppHost endpoint settings disables only the secondary local
  export path.
- Existing scan execution is independent from the optional operations
  Collector.
- `start` never overwrites an existing observation state.
- A failed or interrupted atomic write leaves the previous complete state in
  place.

## Gate for Stage 12

Before compatibility cleanup can be considered:

1. restart or deploy all API and worker instances on the Stage 11 build;
2. wait for the full 15-minute burn-in;
3. require a successful `preflight`, then run `start`;
4. archive the manifest and state as immutable release artifacts;
5. collect seven real days of continuous readiness with zero fallback and
   conflict growth;
6. require `evaluate` to return `ready`;
7. obtain explicit approval before stopping dual writes, removing adapters or
   creating a destructive migration.

If Stage 12 is requested before the observation succeeds, it may add immutable
artifact publication, signing integration or other non-destructive release
hardening. It must not remove the legacy compatibility layer.

Stage 12 has not been started.
