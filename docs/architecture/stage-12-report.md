# Stage 12 Report: Signed Rollout Evidence

- Completed: 2026-10-04
- Engineering scope: HMAC-authenticated state, signed evaluation artifacts, offline verification and key-handling rules
- Seven-day live observation complete: no
- Legacy boundary contracts removed: no
- Cleanup migration created: no

## Outcome

Stage 12 upgrades rollout evidence from an unkeyed integrity checksum to
HMAC-SHA256 authentication with an externally managed secret. A valid release
artifact is now cryptographically bound to the exact manifest and observation
state. Recomputing the public SHA-256 checksum after modifying a state file can
no longer bypass the gate.

The new offline `verify` command validates the state signature, evaluation
signature, manifest digest, state digest and signing-key identifier without
contacting Prometheus. When an evaluation artifact is supplied, verification
succeeds only when the authentic artifact also has status `ready`.

Engineering work for Stage 12 is complete. No real signing key was generated or
stored in the repository, the running AppHost was not interrupted and the live
observation clock has not started. No compatibility boundary was removed.

## Delivered

1. Introduced rollout manifest `schemaVersion: 3` with a non-secret
   `evidenceSigningKeyId`.
2. Updated API and worker deployment identity to `stage-12`.
3. Added external signing-key loading with these controls:
   - key material is read only from `--signing-key-file`;
   - at least 32 bytes are required;
   - POSIX files must be owner-only (`0600`);
   - key contents are never printed or persisted in state/results.
4. Added signed state schema version 3 containing:
   - existing atomic-write and SHA-256 integrity properties;
   - `HMAC-SHA256` algorithm identifier;
   - signing-key identifier;
   - state HMAC signature.
5. Bound the state signature to the environment, exact manifest digest, start
   time and complete preflight evidence.
6. Added HMAC-authenticated evaluation artifacts. Their attestation binds:
   - the complete evaluation result;
   - manifest digest;
   - state digest;
   - signing-key identifier.
7. Added the offline command:

   ```text
   verify --manifest ... --state ... --artifact ... --signing-key-file ...
   ```

8. Made verification fail closed for:
   - missing or short key files;
   - unsafe POSIX key permissions;
   - wrong signing key;
   - wrong signing-key identifier;
   - modified state or result contents;
   - result bound to a different manifest/state;
   - missing evaluation artifact;
   - authentic but `observing` or `blocked` evaluation status.
9. Preserved `not_started` exit code `4` when no state exists, without requiring
   a key that has not yet been provisioned.
10. Documented key generation, secret-store ownership, rotation behavior and
    the symmetric trust semantics of HMAC.

## Security model

`evidenceSigningKeyId` is safe to commit; the key material is not. The key must
come from a deployment secret store and be readable only by the trusted rollout
pipeline and authorized verifier.

HMAC proves that a holder of the shared secret produced the artifact. It does
not provide public verification or identify an individual human signer. Key
rotation changes the manifest/key identifier and therefore requires a fresh
observation window.

## Verification

| Check | Result |
| --- | --- |
| AppHost build | Passed, 0 errors |
| Application tests | 117 passed, 0 failed |
| Full infrastructure suite | 129 passed, 1 skipped, 0 failed |
| Stage 12 rollout/evidence tests | 25 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |
| Python syntax validation | Passed |
| Stage 12 manifest JSON validation | Passed |
| Correct-key state verification | Passed |
| Wrong-key state rejection | Passed |
| Recomputed-checksum bypass rejection | Passed |
| Signed evaluation verification | Passed |
| Modified evaluation rejection | Passed |
| Authentic blocked-result release rejection | Passed |
| Offline CLI verification | Passed without Prometheus |
| Live Stage 12 preflight | Correctly blocked; no state created |
| Live start without key | Correctly failed closed; no state created |

The single skipped infrastructure test is the existing host-dependent real
Nmap assertion. SQL Server and Oracle container integration tests executed and
passed.

The existing `Microsoft.OpenApi`, `SSH.NET` and `MessagePack` package
vulnerability warnings remain visible. Stage 12 did not change dependency
versions.

## Live result

The local Stage 12 manifest was queried against the running Prometheus stack.
Because the already-running AppHost has not been restarted onto Stage 12,
preflight returned exit code `3`:

```text
DbScanner.Api has 0 instance(s); expected at least 1.
DbScanner.ScannerWorker has 0 instance(s); expected at least 1.
DbScanner.Api has 0/4 continuous ready samples.
DbScanner.ScannerWorker has 0/4 continuous ready samples.
```

Invoking `start` without a signing key returned exit code `5` and:

```text
A Stage 12 manifest requires --signing-key-file.
```

Offline verification against the absent local state returned exit code `4`
(`not_started`). No `/private/tmp/dbscanner-stage12-rollout-state.json` was
created.

## Compatibility and rollback

- Schema 1 and 2 manifests remain parseable for non-Stage-12 compatibility.
- A schema 3 manifest requires a signed schema 3 state for evaluation.
- Existing HTTP, RabbitMQ, persistence and Strategy compatibility adapters
  remain unchanged.
- No database migration or destructive cleanup was added.
- The optional secondary OTLP exporter remains independent of scan execution.
- A previous build can be restored without database rollback because Stage 12
  changes only deployment identity and operational evidence tooling.

## Gate for Stage 13

Before compatibility cleanup can be considered:

1. provision a random 32-byte-or-longer signing key in the target environment's
   secret store and restrict its file permissions;
2. deploy/restart every API and worker instance on `stage-12`;
3. complete the 15-minute burn-in and pass `preflight`;
4. run signed `start` and archive the exact manifest plus state;
5. collect seven real days of continuous readiness and zero fallback/conflict
   growth;
6. run `evaluate` to produce the signed `ready` artifact;
7. run offline `verify` against the manifest, state and result;
8. retain all three files as immutable release evidence;
9. obtain explicit approval before stopping dual writes, removing adapters or
   creating a destructive migration.

If Stage 13 is requested before this evidence exists, it may add asymmetric
signing, external artifact-store integration, CI policy examples or other
non-destructive release hardening. It must not remove the legacy compatibility
layer.

Stage 13 has not been started.
