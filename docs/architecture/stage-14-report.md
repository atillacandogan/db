# Stage 14 Report: Live Rollout and Observation Start

- Started: 2026-10-05 08:28:00 UTC (11:28:00 Europe/Istanbul)
- Required completion: 2026-10-12 08:28:00 UTC (11:28:00 Europe/Istanbul)
- Current status: observing
- Stage 15 authorized: no
- Legacy compatibility removed: no

## Outcome

The Stage 13 build is running locally and the real signed seven-day
compatibility observation has started. The required 15-minute preflight passed
with four continuous samples for both API and Worker, exactly one current
instance of each service, readiness `1`, and zero fallback/conflict growth.

No time was simulated. Stage 14's rollout/start work is complete, but its
calendar-bound evidence window remains in progress until 2026-10-12 08:28:00
UTC. Stage 15 has not started and cannot be authorized by this report.

## Actions completed

1. Started the local OTLP Collector, Prometheus and Grafana stack.
2. Created a random 32-byte HMAC key outside the repository with POSIX mode
   `0600`. Only its non-secret identifier is recorded in project files.
3. Gracefully stopped the old AppHost and started the successfully built
   Stage 13 AppHost.
4. Confirmed old Stage 12 Prometheus series became stale and disappeared from
   current inventory.
5. Observed the full 15-minute burn-in without bypassing the time gate.
6. Passed preflight with:
   - API: 4/4 continuous ready samples;
   - Worker: 4/4 continuous ready samples;
   - fallback increase: 0;
   - conflict increase: 0.
7. Created the signed schema 3 observation state exactly once.
8. Copied the exact manifest, state and start result to a protected persistent
   directory outside the repository and verified the copies byte-for-byte.
9. Ran the first signed evaluation; it returned exit code `2` (`observing`).
10. Created an hourly thread heartbeat that stays quiet while the window is
    clean, reports a block/error, and seals/verifies the evidence when it
    becomes genuinely `ready`. It cannot start Stage 15.

## Start evidence

| Property | Value |
| --- | --- |
| Environment | `local` |
| Required deployment | `stage-13` |
| API instance | `atillacan-MacBook-Air-11970` |
| Worker instance | `4fc03b3879a7-1` |
| Start time | `2026-10-05T08:28:00.851721Z` |
| Required end time | `2026-10-12T08:28:00.851721Z` |
| Signing key id | `dbscanner-local-stage-13` |
| State digest | `88ad710475eead511973e123dd81a95f313bbf5b690af26964054de434b481c7` |
| Manifest SHA-256 | `1990467d3a3c80dc6c3b27061c98ca5cfdb01dc815433da3dee379948c206d8c` |
| State-file SHA-256 | `405ca9050a505f4f63a11605a55f38103a1bd3c2296e29dd3db238e220486c55` |
| Initial evaluation | `observing` |
| Initial fallback/conflict increase | `0 / 0` |
| Hourly automation | `stage-14-g-zlemi`, active |

The signing key content is intentionally absent from this report and from the
repository.

## Runtime notes

- Aspire continues to warn that no trusted local development certificate is
  installed. This affects browser trust presentation, not the compatibility
  telemetry or signed rollout evidence.
- AppHost, API, Worker, Collector, Prometheus and Grafana were running when the
  window was started.
- The first signed evaluation showed both services ready and approximately
  seven days remaining, which is the expected result immediately after start.
- The observation state is never overwritten or restarted automatically.

## Failure policy

Any of the following blocks Stage 15 and requires investigation; a new window
may begin only after the cause is repaired and the invalid window is archived:

- missing API or Worker continuity sample;
- a pre-Stage-13/currently unexpected deployment instance;
- readiness other than `1`;
- fallback or contract-conflict counter growth;
- changed manifest, state, key identifier or invalid signature;
- unavailable/insufficient Prometheus history at final evaluation.

The hourly monitor may evaluate, report, seal and verify. It may not modify the
state, shorten time, remove adapters, stop dual writes or create a cleanup
migration.

## Gate for Stage 15

Do not start Stage 15 before all conditions are true:

1. current UTC time is at or after `2026-10-12T08:28:00.851721Z`;
2. all required continuity samples remain present for the complete window;
3. fallback and conflict increases remain zero;
4. final evaluation returns signed status `ready`;
5. `seal` creates the non-overwritten evidence bundle;
6. offline `verify-bundle` succeeds against the archived manifest/state/result;
7. the user gives an explicit Stage 15 command after reviewing that evidence.

Until then, Stage 14 remains `observing`. Stage 15 has not been started.
