# Stage 0 Report: Architecture Baseline and Safety Net

- Completed: 2026-10-03
- Scope: tests and architecture documentation only
- Production behavior changed: no

## Delivered

1. Added executable layer-dependency rules for Domain, Contracts, Application and
   Infrastructure.
2. Added scan-workflow characterization tests for legacy script-ID normalization,
   cancelled scans, unexpected runtime failures and requested cancellation.
3. Recorded the current modules, existing patterns, NSE/Nmap coupling points and
   migration constraints.
4. Accepted ADR-001 for an incremental executor-strategy migration with backward
   compatibility and security constraints.

## Verification

| Check | Result |
| --- | --- |
| Solution build | Passed, 0 errors |
| Application tests | 80 passed, 0 failed |
| Deterministic Infrastructure tests | 97 passed, 0 failed |
| Frontend lint | Passed |
| Frontend production build | Passed |
| Full Infrastructure suite | 99 passed, 1 skipped, 1 environment failure |

The full Infrastructure run reached the real SQL Server Testcontainers test. The
container exited with code 137 while its own log reported only 41 MB of available
memory. This is an environment resource failure, not an assertion or compilation
failure. All Infrastructure tests excluding container integration tests passed.

## Baseline findings

### Protected architecture boundaries

- Domain and Contracts have no dependencies on other DbScanner projects.
- Application depends only on Domain and Contracts.
- Infrastructure depends only on Application, Domain and Contracts and cannot
  depend on API or presentation projects.

### Primary stage 1-3 coupling risks

- Application ports and services use `NseScript` directly.
- API and queue messages expose `SelectedNseScriptIds`.
- Persistence and rules use `NseScriptId` relationships.
- Execution results assume Nmap XML through `RawXml`.
- Rule XPath matching reads a scan-level XML document.
- Presentation contracts and labels expose NSE-specific terminology.

### Existing extension seams worth preserving

- `IScannerRuntime` already isolates scan processing from execution details.
- Repository interfaces isolate persistence.
- RabbitMQ publisher/consumer interfaces isolate transport.
- Discovery fingerprint detectors are already registered as a collection of
  strategies.

## Known dependency warnings

The build reports existing vulnerability advisories for:

- `Microsoft.OpenApi` 2.0.0
- `SSH.NET` 2025.1.0
- `MessagePack` 2.5.192

Upgrading these packages was intentionally excluded from Stage 0 because package
changes can alter runtime behavior. They should be handled in a dedicated
dependency-hardening task with compatibility tests.

## Gate for Stage 1

Stage 1 may begin without changing public API, queue or database contracts. Its
generic models and adapters must preserve every invariant listed in the baseline
document and keep all Stage 0 tests passing.
