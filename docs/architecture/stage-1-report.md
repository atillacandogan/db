# Stage 1 Report: Generic Scan Contracts and NSE Compatibility

- Completed: 2026-10-03
- Scope: application models, compatibility adapters and tests
- Production behavior changed: no
- Public API, queue and database contracts changed: no

## Delivered

1. Added an executor-independent check definition (`ScanCheckDefinition`) with
   generic executor, engine, parser, matcher, severity and approval metadata.
2. Added a common execution context (`ScanExecutionContext`) that reuses the
   existing scan, target and protected authentication models.
3. Added executor-independent result contracts:
   - `CheckExecutionResult`
   - `ScanExecutionArtifact`
   - `CompositeScanExecutionResult`
   - `ScanPayloadFormat`
4. Added stable executor identifiers and identifier-shape validation. The model
   does not maintain a central allowlist; executability will be decided by the
   DI-backed strategy registry in Stage 2. This keeps new strategies open for
   extension without allowing paths, whitespace or command-like identifiers.
5. Added bidirectional NSE compatibility adapters for:
   - `NseScript` and `ScanCheckDefinition`
   - `ScanScriptExecutionResult` and `CheckExecutionResult`
   - `ScanExecutionResult` and `CompositeScanExecutionResult`

## Compatibility and safety rules implemented

- Existing NSE IDs and names are preserved.
- Supported database engines are parsed, de-duplicated and serialized in their
  canonical API form.
- Parser, matcher, severity, weight, remediation, approval and credential
  requirement metadata are preserved.
- Raw XML, normalized output, exit codes and probe result statuses are preserved.
- XML-only legacy contracts reject JSON, text and other payload formats instead
  of silently labeling them as XML.
- A mixed-executor composite cannot be collapsed into a single-executor legacy
  result.
- Unknown database engines are rejected instead of silently widening or
  narrowing scan scope.
- Credentials remain execution input and are not copied into result contracts.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 88 passed, 0 failed |
| New Stage 1 compatibility tests | 8 passed, 0 failed |
| Existing Stage 0 architecture and characterization tests | Passed |

The solution still reports the 15 previously recorded dependency vulnerability
warnings for `Microsoft.OpenApi`, `SSH.NET` and `MessagePack`. No dependency
versions were changed in this stage.

## What did not change

- `IScannerRuntime` and the current Nmap/native runtime chain
- scan workflow behavior
- API request/response contracts
- RabbitMQ messages
- database entities, mappings and migrations
- frontend behavior or labels

The new contracts currently sit beside the legacy contracts. Runtime traffic is
not switched to them yet, which makes this stage rollback-safe.

## Gate for Stage 2

Stage 2 may add `IScanExecutorStrategy`, a DI-backed strategy resolver and a
composite orchestrator on top of these contracts. The existing runtime must stay
behind an adapter, and all Stage 0-1 tests must continue to pass before any
production path is switched.
