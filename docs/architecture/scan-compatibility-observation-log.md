# Scan Compatibility Observation Log

The local seven-day removal clock **started on 2026-10-05 at 08:28:00 UTC**.
On 2026-10-06 the user explicitly requested the POC Stage 15 cutover without
waiting for this window. The historical Stage 14 evidence remains untouched,
but is not a completed or sealed production-removal approval. Its monitor was
deleted; a Stage 15 deployment cannot continue this Stage 13 deployment window.
Development, staging and production have not started. A valid environment
window starts only after every API and worker instance runs the Stage 13 build,
reports compatibility telemetry and passes the continuous burn-in preflight.

| Environment | Deployment inventory verified | Window start (UTC) | Window end (UTC) | Readiness | Fallback/conflict increase | State |
| --- | --- | --- | --- | --- | --- | --- |
| Local (historical) | API `atillacan-MacBook-Air-11970` and Worker `4fc03b3879a7-1`, both `stage-13` | 2026-10-05 08:28:00 | 2026-10-12 08:28:00 (planned, not completed) | 5/5 at 2026-10-05 08:48:36; observing | 0 / 0 at last recorded admission check | POC waiver; monitor retired |
| Development | Not supplied | — | — | Unknown | Unknown | Not started |
| Staging | Not supplied | — | — | Unknown | Unknown | Not started |
| Production | Not supplied | — | — | Unknown | Unknown | Not started |

## Recording an observation

The API status response supplies the deployed API version, instance and
environment. The admin-only readiness endpoint supplies database compatibility
counts. Append a JSONL snapshot with one of these commands:

```bash
./scripts/capture-scan-compatibility-readiness.sh \
  staging https://dbscanner.example.test "$DBSCANNER_ADMIN_TOKEN"
```

```powershell
./scripts/capture-scan-compatibility-readiness.ps1 `
  -EnvironmentName staging `
  -ApiBaseUrl https://dbscanner.example.test `
  -AdminToken $env:DBSCANNER_ADMIN_TOKEN
```

Tokens are used only as request headers and are not written to the observation
file. Worker versions and instances are read from the dashboard's deployment
inventory panel, which is populated from OpenTelemetry resource labels.

Record at least the start and end of the window and after every deployment.
The window is invalid unless the dashboard retains continuous samples between
those records.

The authoritative clock state and automatic decision are produced by
`scripts/manage-scan-compatibility-window.py`. This Markdown table is the
human-readable sign-off record; it cannot override a blocked machine result.

## Local Stage 14 evidence

- Signing key id: `dbscanner-local-stage-13`
- State digest:
  `88ad710475eead511973e123dd81a95f313bbf5b690af26964054de434b481c7`
- Archived manifest SHA-256:
  `1990467d3a3c80dc6c3b27061c98ca5cfdb01dc815433da3dee379948c206d8c`
- Archived state-file SHA-256:
  `405ca9050a505f4f63a11605a55f38103a1bd3c2296e29dd3db238e220486c55`
- First signed evaluation: `observing`, fallback increase `0`, conflict
  increase `0`
- Stage 15 admission check at `2026-10-05T08:48:36.863361Z`: `observing`,
  API 5/5, Worker 5/5, sealed bundle absent
- Hourly monitor: deleted on 2026-10-06 (`stage-14-g-zlemi`); no automatic
  seal or cleanup will run from that monitor

The key, authoritative state and rolling evaluation remain outside the
repository under the user's protected configuration directory. Their contents
must not be copied into this log. Any continuity gap or counter increase
invalidates the window; this table must then be marked blocked rather than
editing the signed start time.
