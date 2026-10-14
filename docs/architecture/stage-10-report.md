# Stage 10 Report: Automated Rollout Evidence Gate

- Completed: 2026-10-04
- Engineering scope: deployment inventory, immutable observation windows, continuity evaluation and production-shaped observability validation
- Seven-day live observation complete: no
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

Stage 10 turns the Stage 9 monitoring assets into a fail-closed, machine-
evaluated rollout gate. A deployment cannot start or complete the compatibility
window unless all expected API and worker instances report the required build,
readiness remains continuously healthy and neither fallback nor conflict
counters increase.

The engineering scope is complete. The seven-day clock has deliberately not
been started: the local Collector, Prometheus and Grafana stack is running, but
the API and worker have not been continuously deployed to that collector as a
complete Stage 10 inventory. No elapsed time was simulated and no legacy field
was deleted.

## Delivered

1. Added explicit deployment identity to API and worker telemetry:
   - `service.name`;
   - `service.version` from `Telemetry:DeploymentVersion`;
   - unique `service.instance.id`;
   - `deployment.environment.name` from `Telemetry:Environment`.
2. Aligned `/api/status` with the same deployment version and environment
   values used by telemetry.
3. Added a versioned rollout manifest that declares the environment, required
   deployment version, expected service counts, sample interval and seven-day
   window.
4. Added `scripts/manage-scan-compatibility-window.py` with three commands:
   - `preflight`: verifies current deployment inventory, build version and
     readiness;
   - `start`: stores the immutable manifest digest and clean counter baseline;
   - `evaluate`: verifies real elapsed time, sample continuity, current
     inventory and counter deltas.
5. Made the decision fail closed when:
   - an expected instance is missing;
   - an old or unknown build reports telemetry;
   - readiness is missing or not `1`;
   - a five-minute sample is absent;
   - fallback or conflict counters increase;
   - the manifest changes after the window starts;
   - seven actual days have not elapsed.
6. Expanded the Grafana dashboard with deployment environment, version and
   instance inventory plus seven-day readiness/fallback/conflict panels.
7. Added an alert for an individually missing API or worker service, in
   addition to the existing all-telemetry-missing alert.
8. Updated the operations runbook and observation log with the automated gate
   commands and exit-code contract.
9. Serialized SQL Server and Oracle container integration collections. This
   prevents the two memory-heavy databases from starting concurrently and
   removed the Stage 9 Docker memory failures without skipping either suite.
10. Updated the local observability stack to the available OpenTelemetry
    Collector `0.161.0` image and replaced the deprecated all-resource label
    conversion with an explicit low-cardinality resource-label allowlist.

## Rollout gate contract

| Exit code | Meaning |
| --- | --- |
| `0` | Preflight passed, window started, or completed window is ready |
| `2` | Window is healthy but still observing real elapsed time |
| `3` | Gate is blocked and the window is invalid |
| `4` | No window state exists |
| `5` | Manifest, state, Prometheus or command error |

The state records the manifest SHA-256 digest. Editing the expected inventory,
required version or window definition after `start` invalidates evaluation
instead of silently redefining success.

## Verification

| Check | Result |
| --- | --- |
| AppHost build | Passed, 0 errors |
| Application tests | 117 passed, 0 failed |
| Non-container infrastructure tests | 126 passed, 0 failed |
| Full infrastructure suite | 129 passed, 1 skipped, 0 failed |
| Rollout-manager unit tests | 7 passed, 0 failed |
| Python syntax validation | Passed |
| Rollout/dashboard JSON validation | Passed |
| Docker Compose render/validation | Passed |
| Collector startup | Passed; OTLP gRPC/HTTP and Prometheus exporter ready |
| Prometheus health | Passed |
| Prometheus alert rules | All five rules loaded with health `ok` |
| Grafana health | Passed; database `ok` |
| Grafana provisioning | `DbScanner Scan Compatibility` dashboard found through the API |
| Live fail-closed preflight | Correctly blocked: API `0/1`, worker `0/1` |

The one skipped infrastructure test is the existing host-dependent Nmap test.
SQL Server and Oracle container integration tests both executed and passed.

The solution still reports the previously documented dependency vulnerability
warning for `Microsoft.OpenApi`. Stage 10 did not alter that package.

## Live observation status

The local operations stack is running on:

- Prometheus: `http://localhost:9090`;
- Grafana: `http://localhost:3001`;
- OTLP gRPC: `http://localhost:4317`;
- OTLP HTTP: `http://localhost:4318`.

The live preflight returned `blocked` with these expected reasons:

```text
DbScanner.Api has 0 instance(s); expected at least 1.
DbScanner.ScannerWorker has 0 instance(s); expected at least 1.
```

This is the correct result. Starting monitoring infrastructure alone must not
start the removal clock. Both application services must first export Stage 10
telemetry to the operations collector.

## Rollback and safety

- No destructive schema migration was added.
- Existing generic/legacy dual writes, fallback reads and queue fields remain.
- No observation state was created after the blocked preflight.
- Stopping the optional observability stack does not change scan execution.
- Omitting `Telemetry__CompatibilityOtlpEndpoint` disables only the secondary
  operations exporter.

## Gate for Stage 11

Before legacy removal can be considered:

1. copy the example manifest per target environment and set the real expected
   instance counts;
2. deploy both API and worker with deployment version `stage-10` and configure
   `Telemetry__CompatibilityOtlpEndpoint` for the environment collector;
3. run `preflight`, then `start` only after it passes;
4. keep every expected instance reporting readiness `1` at five-minute
   continuity for seven real days;
5. require zero fallback and conflict counter increase;
6. run `evaluate` and retain the generated evidence;
7. obtain explicit approval before any cleanup migration or boundary removal.

If Stage 11 is requested before this live window succeeds, it may continue
non-destructive architecture and rollout hardening, but it must not remove the
legacy compatibility layer.

Stage 11 has not been started.
