# Scan Compatibility Operations

## Rollback window

Legacy HTTP fields, RabbitMQ fields and database columns must remain available
for at least **7 continuous days** after all API and worker instances have been
upgraded. The window restarts whenever any of the following occurs:

- a legacy fallback counter increases;
- a contract conflict is observed;
- `readyForLegacyRemoval` becomes false;
- an old API or worker version is returned to service.

Cleanup requires all of the following for the full window:

1. `GET /api/scan-checks/compatibility-readiness` returns
   `readyForLegacyRemoval=true` in every environment;
2. `dbscanner.scan_check.compatibility_ready` is continuously `1`;
3. the increase of `compatibility_fallbacks` is zero;
4. the increase of `compatibility_conflicts` is zero;
5. API and worker deployment inventories contain no pre-Stage-8 instances.

## Dashboard and alerts

- Grafana dashboard: `ops/observability/scan-compatibility-dashboard.json`
- Prometheus rules: `ops/observability/scan-compatibility-alerts.yaml`
- .NET meter: `DbScanner.ScanCompatibility`

Both the API and scanner worker register the meter with the OpenTelemetry OTLP
exporter. AppHost's `WithOtlpExporter()` connection sends the meter to the
Aspire dashboard automatically. A five-minute background audit in both
processes refreshes the readiness gauge; the admin endpoint no longer has to be
called manually for the metric to exist.

The production-shaped local stack provisions an OTLP Collector, Prometheus,
the alert rules, the Prometheus Grafana datasource and the dashboard:

```text
docker compose -f ops/observability/docker-compose.yml up -d
```

To dual-export from Aspire to this stack, set the optional secondary endpoint
for both API and worker deployments:

```text
Telemetry__CompatibilityOtlpEndpoint=http://localhost:4317
```

For a worker running inside a container, use an address that resolves the
collector from that container network (for Docker Desktop this is commonly
`http://host.docker.internal:4317`). Production must use the collector's
internal service name. The normal `OTEL_EXPORTER_OTLP_*` variables still control
the primary exporter.

For local AppHost development, `appsettings.Development.json` supplies separate
host-process and container endpoints. The API uses `localhost`; the scanner
worker uses `host.docker.internal`. Override or remove these values when the
collector uses a different network topology:

```text
Telemetry__CompatibilityOtlpHostEndpoint=http://localhost:4317
Telemetry__CompatibilityOtlpContainerEndpoint=http://host.docker.internal:4317
```

After startup:

- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3001`, folder `DbScanner`
- Collector Prometheus endpoint: `http://localhost:9464/metrics`

The Compose and provisioning files are platform-neutral and work on Docker
Desktop for Windows/macOS and Docker Engine on Linux. Only the endpoint used by
a containerized worker differs by network topology.

Metric exporters commonly normalize dots to underscores and append `_total` to
counters. If the selected OpenTelemetry/Prometheus pipeline uses a different
normalization, adapt only the dashboard/rule query names; do not rename the
application instruments during the rollback window.

## Response procedure

When a fallback alert fires:

1. keep all compatibility adapters and dual writes enabled;
2. inspect the bounded `surface` label;
3. correlate the time range with API/worker deployment versions;
4. upgrade or repair the old producer/data row;
5. run the readiness endpoint again;
6. restart the seven-day window only after counters stop increasing.

Database cleanup is a separately approved deployment. It must not be bundled
with executor feature delivery or frontend changes.

## Automated rollout gate

Copy the example manifest once per environment and commit the expected service
inventory and deployment version:

```text
cp ops/observability/compatibility-rollout.example.json \
  ops/observability/compatibility-rollout.staging.json
```

Local AppHost development already has a matching manifest at
`ops/observability/compatibility-rollout.local.json`.

Run preflight before starting the clock:

```text
python3 scripts/manage-scan-compatibility-window.py preflight \
  --manifest ops/observability/compatibility-rollout.staging.json \
  --state output/compatibility-window.staging.json \
  --prometheus-url http://localhost:9090
```

The Stage 13 manifest uses `schemaVersion: 3`, declares `preflightMinutes` and
names a non-secret `evidenceSigningKeyId`. Preflight requires every expected
API/worker instance to report readiness `1` and exactly the manifest's
deployment version for that entire burn-in interval. Any missing sample,
fallback increase or conflict increase blocks `start`.

Provision a random key of at least 32 bytes through the deployment secret store
and mount it as an owner-readable file. Do not commit it to the repository. On
POSIX hosts the file must have mode `0600`:

```text
openssl rand -out /secure/dbscanner-compatibility.key 32
chmod 600 /secure/dbscanner-compatibility.key
```

The key identifier in the manifest identifies the secret-store version; it is
not the key itself. Changing the key identifier changes the manifest digest and
therefore requires a fresh observation window.

Start writes the manifest digest, burn-in evidence and UTC start time to a
schema-versioned state file. The state is written with an atomic replace and
contains a canonical SHA-256 integrity digest:

```text
python3 scripts/manage-scan-compatibility-window.py start \
  --manifest ops/observability/compatibility-rollout.staging.json \
  --state output/compatibility-window.staging.json \
  --signing-key-file /secure/dbscanner-compatibility.key \
  --prometheus-url http://localhost:9090
```

Evaluate the same state from a scheduled pipeline:

```text
python3 scripts/manage-scan-compatibility-window.py evaluate \
  --manifest ops/observability/compatibility-rollout.staging.json \
  --state output/compatibility-window.staging.json \
  --signing-key-file /secure/dbscanner-compatibility.key \
  --prometheus-url http://localhost:9090 \
  --output output/compatibility-window-result.staging.json
```

`evaluate` signs its result and binds it to both the exact manifest digest and
state digest. Verify the state and evaluation artifact without
connecting to Prometheus:

```text
python3 scripts/manage-scan-compatibility-window.py verify \
  --manifest ops/observability/compatibility-rollout.staging.json \
  --state output/compatibility-window.staging.json \
  --artifact output/compatibility-window-result.staging.json \
  --signing-key-file /secure/dbscanner-compatibility.key
```

`verify` requires the evaluation artifact and returns success only if its
signature is valid **and** its evaluation status is `ready`. A valid signature on an
`observing` or `blocked` artifact authenticates the result but never authorizes
cleanup.

After verification succeeds, seal the exact inputs into a release-evidence
bundle. `seal` refuses to package non-ready evidence and never overwrites an
existing bundle:

```text
python3 scripts/manage-scan-compatibility-window.py seal \
  --manifest ops/observability/compatibility-rollout.staging.json \
  --state output/compatibility-window.staging.json \
  --artifact output/compatibility-window-result.staging.json \
  --signing-key-file /secure/dbscanner-compatibility.key \
  --bundle output/compatibility-evidence.staging
```

The bundle contains only the exact manifest, observation state, evaluation
result and `evidence-index.json`. The index records each file's byte length and
SHA-256 digest, binds the environment/deployment/state/result identities and is
itself HMAC-authenticated. On POSIX, the created directory and files are made
read-only. That local permission is an accident guard, not durable immutable
storage; the release pipeline must upload the directory to an artifact store
with overwrite disabled and retention/object-lock policy enabled.

Release or cleanup jobs consume only the sealed directory and verify it without
Prometheus access:

```text
./scripts/verify-scan-compatibility-release.sh \
  output/compatibility-evidence.staging \
  /secure/dbscanner-compatibility.key \
  output/compatibility-evidence-verification.staging.json
```

```powershell
./scripts/verify-scan-compatibility-release.ps1 `
  -EvidenceBundleDirectory output/compatibility-evidence.staging `
  -SigningKeyFile C:\secure\dbscanner-compatibility.key `
  -VerificationOutput output/compatibility-evidence-verification.staging.json
```

Both wrappers return the manager's exit code, so a CI/CD job fails closed on a
missing/extra/symlinked file, changed byte, wrong key, invalid signature or a
result other than `ready`.

Exit codes are stable for CI use:

| Code | Meaning |
| --- | --- |
| `0` | preflight/start/evaluation passed, or evidence was verified/sealed |
| `2` | window is valid but still observing |
| `3` | blocked by inventory, readiness, continuity, fallback or conflict evidence |
| `4` | window has not started |
| `5` | invalid configuration or Prometheus request failure |

The evaluator checks every configured sampling point through Prometheus's range
API. A missing point, old version, insufficient instance count, readiness other
than `1`, fallback increase, conflict increase or manifest change blocks the
window. A modified, unsigned, wrong-key, legacy-format or future-dated state
also blocks evaluation. It never deletes or silently overwrites an existing
state file.

Stage 13 uses HMAC-SHA256 to authenticate state, evaluation artifact and sealed
bundle index.
HMAC is symmetric: only the trusted deployment pipeline and authorized
verifiers may read the key. A valid signature proves that the holder of the
shared key produced the artifact; it does not identify an individual human
signer. Never expose the key in a pipeline argument, log or uploaded artifact.
