# Stage 13 Report: Tamper-Evident Release Evidence Bundle

- Completed: 2026-10-05
- Engineering scope: sealed evidence bundle, offline release gate and portable CI wrappers
- Seven-day live observation complete: no
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

Stage 13 turns the separately signed rollout files from Stage 12 into one
self-contained, tamper-evident release bundle. A cleanup or release job can now
consume a single directory, verify it without Prometheus access and fail closed
unless the original evaluation is authentic and `ready`.

Engineering work for Stage 13 is complete. The local Prometheus endpoint was
not running during the final preflight, so no observation clock was started.
No real signing key, state, evaluation result or release bundle was generated.
No legacy HTTP, RabbitMQ, persistence or Strategy compatibility boundary was
removed.

## Delivered

1. Added `seal` to the rollout manager. It:
   - re-verifies the manifest, state and signed evaluation before packaging;
   - accepts only an authentic `ready` evaluation;
   - preserves the exact input bytes;
   - refuses to overwrite an existing bundle;
   - builds the bundle in a sibling temporary directory and publishes it with
     an atomic rename;
   - makes the directory/files read-only on POSIX as an accident guard.
2. Added a signed `evidence-index.json` containing:
   - environment and deployment version;
   - manifest, state and evaluation identities;
   - exact byte length and SHA-256 digest of every source file;
   - signing-key identifier and HMAC-SHA256 attestation.
3. Added `verify-bundle`, an offline command that rejects:
   - missing or extra files;
   - symbolic links and non-regular files;
   - any changed byte or mismatched size/digest;
   - wrong keys or invalid HMAC signatures;
   - mismatched manifest/state/evaluation identities;
   - an evaluation status other than `ready`.
4. Added CI/CD entry points for both platform families:
   - `scripts/verify-scan-compatibility-release.sh`;
   - `scripts/verify-scan-compatibility-release.ps1`.
5. Updated API and scanner-worker telemetry identity to `stage-13` and changed
   the local/example manifests to require exactly that version.
6. Documented the full seal/upload/verify flow and its trust boundary.

## Storage and trust model

The sealed directory is tamper-evident, not intrinsically immutable. POSIX
read-only permissions prevent common accidental edits but an owner can change
them. The release pipeline must upload the complete directory to an external
artifact store with overwrite disabled and retention/object-lock controls.

The signing key remains external to both source control and the evidence
bundle. HMAC proves that a key holder created the evidence; it does not identify
an individual human approver. The bundle must never contain the key.

## Verification

| Check | Result |
| --- | --- |
| AppHost build | Passed, 0 errors |
| Application tests | 117 passed, 0 failed |
| Full infrastructure suite | 129 passed, 1 skipped, 0 failed |
| Stage 13 rollout/evidence tests | 32 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |
| Python syntax validation | Passed |
| Shell syntax validation | Passed |
| Stage 13 manifest JSON validation | Passed |
| Authentic ready bundle seal/verify | Passed |
| Existing-bundle overwrite rejection | Passed |
| Tampered-bundle rejection | Passed |
| Authentic non-ready artifact rejection | Passed |
| Offline bundle CLI verification | Passed without Prometheus |
| Live Stage 13 preflight | Failed closed; Prometheus connection refused |

The skipped infrastructure assertion is the existing host-dependent real Nmap
test. SQL Server and Oracle container integration suites executed and passed.

The existing vulnerability warnings for `Microsoft.OpenApi`, `SSH.NET` and
`MessagePack` remain. Stage 13 did not modify dependency versions.

PowerShell was not installed on the macOS verification host, so the PowerShell
wrapper could not be executed locally. It uses only standard PowerShell
argument forwarding and invokes the same tested Python `verify-bundle` command.

## Live result

The Stage 13 local manifest was queried against `http://localhost:9090`.
Prometheus was not running, so the command returned exit code `5` with:

```text
Prometheus request failed: <urlopen error [Errno 61] Connection refused>
```

This is the intended fail-closed behavior. The observability stack and AppHost
were not restarted automatically, `/private/tmp/dbscanner-stage13-rollout-state.json`
was not created, and no elapsed time was simulated.

## Compatibility and rollback

- Schema 1 and 2 manifests remain parseable for older operational evidence.
- Stage 13 continues to use signed schema 3 state/evaluation files.
- Existing scan contracts, adapters, database columns and dual writes are
  unchanged.
- The bundle feature is operational tooling only; rolling application binaries
  back does not require a database rollback.
- A failed bundle verification blocks cleanup but does not affect scan
  execution.

## Gate for Stage 14

Stage 14 is the real rollout and observation-start stage:

1. provision the Stage 13 signing key outside the repository;
2. start the observability stack;
3. deploy/restart every API and worker instance on `stage-13`;
4. pass the continuous 15-minute preflight;
5. start the signed seven-day state without replacing an existing state;
6. schedule `evaluate` and retain its outputs;
7. keep all legacy compatibility paths enabled during the window.

Stage 14 must stop and report `blocked` if telemetry is incomplete, counters
increase, an old instance appears or the environment cannot safely be
restarted. It must not shorten or simulate the seven-day interval.

## Remaining stages

The earlier gate documents intentionally described the next safe action rather
than declaring a numeric final stage. The closure roadmap is now fixed to **two
stages after Stage 13**:

- **Stage 14 — Live rollout and signed observation:** deploy Stage 13, pass
  burn-in and start/operate the real seven-day evidence window.
- **Stage 15 — Approved compatibility cleanup:** only after a verified sealed
  `ready` bundle and explicit approval, remove legacy boundaries in reversible
  deployment slices and add the separately reviewed database migration last.

Calendar time is unavoidable: Stage 15 cannot be authorized until seven real
continuous days have been observed. Stage 14 has not been started.
