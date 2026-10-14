# Stage 9 Report: Operational Compatibility Proofing

- Completed: 2026-10-04
- Engineering scope: OTLP export, readiness monitoring, provisioned dashboard/alerts, cross-generation contracts and cleanup review
- Seven-day live observation complete: no
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

The Stage 8 compatibility instruments now leave the process through a real
OpenTelemetry OTLP exporter. API and worker instances continuously audit
database readiness and carry service/version/instance identity into telemetry.
The Grafana/Prometheus assets are now deployable as a provisioned stack rather
than manual JSON/YAML examples.

Engineering work for Stage 9 is complete. The operational removal gate remains
blocked until each real environment supplies seven continuous days of evidence.
The elapsed-time requirement was not simulated or declared complete.

## Delivered

1. Added OpenTelemetry `1.19.1` hosting and OTLP exporter integration to the
   shared infrastructure layer.
2. Registered `DbScanner.ScanCompatibility` in both:
   - `DbScanner.Api`;
   - `DbScanner.ScannerWorker`.
3. Connected both AppHost resources to Aspire's OTLP receiver with
   `WithOtlpExporter()`.
4. Added an optional named secondary OTLP exporter configured by:

   ```text
   Telemetry__CompatibilityOtlpEndpoint
   ```

   This permits simultaneous Aspire and operations-collector export without
   replacing the primary exporter.
5. Added service name, version and process instance identity as OpenTelemetry
   resource attributes for deployment inventory.
6. Added a five-minute background readiness audit to API and worker. It updates
   the readiness gauge without requiring a manual admin endpoint call. Audit
   failure is fail-closed: the process logs a warning and cleanup remains
   blocked.
7. Added a complete local operations stack:
   - OTLP Collector;
   - Prometheus with the compatibility rule file;
   - Grafana with an automatically provisioned datasource and dashboard;
   - 15-day Prometheus retention for the seven-day evidence window.
8. Added a deployment-inventory dashboard panel and a missing-telemetry alert.
9. Added Linux/macOS shell and Windows PowerShell snapshot commands. Snapshots
   record API version/instance/environment and database readiness in JSONL
   without persisting the admin token.
10. Added explicit cross-generation queue contract tests:
    - old producer message -> Stage 9 consumer;
    - Stage 9 dual-writing producer -> old consumer fixture;
    - new producer writes equivalent generic and legacy selections.
11. Centralized queue-message creation and selection resolution in
    `ScanRequestedMessageCompatibility`. The RabbitMQ publisher and consumer now
    use the same compatibility policy.
12. Produced a separately reviewable cleanup proposal. It contains ordering,
    preconditions and rollback rules but performs no deletion.

## Operational assets

| Asset | Purpose |
| --- | --- |
| `ops/observability/docker-compose.yml` | Collector, Prometheus and Grafana deployment |
| `ops/observability/otel-collector.yaml` | OTLP metrics receiver and Prometheus exporter |
| `ops/observability/prometheus.yml` | Scrape and alert-rule configuration |
| `ops/observability/grafana/provisioning/` | Automatic datasource/dashboard import |
| `ops/observability/scan-compatibility-dashboard.json` | Readiness, fallback, conflict, dual-write and inventory panels |
| `ops/observability/scan-compatibility-alerts.yaml` | Fallback, conflict, blocked-readiness and missing-telemetry alerts |
| `scripts/capture-scan-compatibility-readiness.sh` | POSIX observation snapshot |
| `scripts/capture-scan-compatibility-readiness.ps1` | Windows observation snapshot |
| `docs/architecture/scan-compatibility-observation-log.md` | Environment/window record |
| `docs/architecture/stage-9-cleanup-proposal.md` | Review-only future cleanup plan |

## Verification

| Check | Result |
| --- | --- |
| API build | Passed, 0 errors |
| Worker build | Passed, 0 errors |
| AppHost build | Passed, 0 errors |
| Application tests | 117 passed, 0 failed |
| Non-container infrastructure tests | 126 passed, 0 failed |
| Full infrastructure suite | 127 passed, 1 skipped, 2 environment failures |
| Old/new queue contract matrix | Passed |
| Docker Compose render/validation | Passed |
| Dashboard JSON validation | Passed |
| Collector/Prometheus/Grafana YAML parsing | Passed |

The two full-suite failures were real-database container startup failures under
the available Docker memory: SQL Server exited with code `137` and Oracle exited
with code `41` after startup. The same run completed 127 other tests. The
container-independent suite passed completely, so these failures are recorded
as environment capacity blockers rather than hidden or converted to successes.

The solution still reports the previously documented dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. Stage 9 did not
change those packages.

## Observation status

No environment has started a valid seven-day clock. Local deployment inventory
could not be completed because the database containers exceeded available
Docker memory. Development, staging and production deployment inventories were
not supplied to this workspace.

Consequently:

- readiness is not claimed continuously `1`;
- zero fallback/conflict growth is not claimed;
- no legacy removal is authorized;
- the observation clock remains at **not started**.

## Rollback

- No database migration or destructive cleanup was added.
- Removing `AddDbScannerTelemetry` stops the new exporter/monitor without
  affecting scan execution.
- Leaving `Telemetry__CompatibilityOtlpEndpoint` unset disables only the
  optional secondary exporter.
- Existing HTTP, RabbitMQ and database compatibility fields remain intact.

## Gate for Stage 10

Stage 10 must begin with rollout evidence, not deletion:

1. allocate sufficient Docker/runtime resources and deploy the Stage 9 API and
   worker to each target environment;
2. verify every expected API/worker instance in the deployment-inventory panel;
3. record observation start snapshots;
4. retain readiness `1` and zero fallback/conflict increases for seven
   continuous days;
5. record end snapshots and obtain explicit cleanup approval.

If Stage 10 is requested before seven live days have elapsed, it may improve
deployment verification and monitoring, but it must not remove legacy
boundaries or create the cleanup migration.

Stage 10 has not been started.
