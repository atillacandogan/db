# DbScanner Architecture Baseline

This document records the production-code structure and the extension seams that
must remain stable while the scanner is migrated from an NSE-specific pipeline to
an executor-strategy pipeline.

## Layer dependency direction

```text
DbScanner.Domain       -> no DbScanner project
DbScanner.Contracts    -> no DbScanner project
DbScanner.Application  -> Domain, Contracts
DbScanner.Infrastructure -> Application, Domain, Contracts
DbScanner.Api          -> Application, Infrastructure, Contracts
DbScanner.ScannerWorker -> Application, Infrastructure, Contracts
DbScanner.AppHost      -> API, ScannerWorker
DbScanner.Web          -> HTTP contracts only
```

`ArchitectureDependencyTests` enforces the dependency-free core and prevents
Infrastructure from taking a dependency on API or presentation projects.

## Current modules

| Capability | Application entry point | Infrastructure adapter |
| --- | --- | --- |
| Targets | `TargetService` | `TargetRepository`, `TargetConnectionStringParser` |
| Scans | `ScanService`, `ScanProcessingService` | scanner runtimes, RabbitMQ, `ScanRepository` |
| Rules | `RuleCatalogService`, `RuleEngine` | `RuleRepository` |
| Findings | `FindingCatalogService` | `FindingRepository` |
| Discovery | `DiscoveryService`, `DiscoveryAllowlistService` | discovery agent, probes, RabbitMQ |
| NSE catalog | `NseScriptCatalogService` | NSE repository and installed-script sync |
| Access management | `AdminUserService` | Keycloak admin adapter and audit repository |

## Existing design patterns

- Ports and adapters: Application ports are implemented in Infrastructure.
- Repository: persistence is hidden behind repository interfaces.
- Strategy-like runtime seam: `IScannerRuntime` isolates scan processing from
  process and protocol details.
- Strategy collection: discovery fingerprint detectors are registered as
  multiple `IDatabaseFingerprintDetector` implementations.
- Publisher/consumer: scan, discovery and catalog-sync jobs use RabbitMQ ports.
- Decorator/chain: Oracle, MSSQL, compatibility and Nmap scanner runtimes are
  currently composed as a hard-coded delegation chain.

## Coupling register for stages 1-3

The following names and formats currently couple the workflow to NSE/Nmap:

- `NseScript`, `INseScriptRepository`, `INseScriptCatalogService`
- `SelectedNseScriptIds` in API and RabbitMQ messages
- `RuleNseScript` and `NseScriptId` relationships
- `ScanScriptExecutionResult.NseScriptId`
- `ScanExecutionResult.RawXml`
- `ScanScriptResult.RawXml`
- XPath matching against one scan-level raw XML document
- UI labels and DTO fields containing `NseScript`

Stages 1-3 must introduce generic contracts beside these legacy contracts. The
legacy names remain operational until compatibility tests prove that all callers
have migrated.

## Characterized scan invariants

- Explicit script identifiers are trimmed and deduplicated case-insensitively
  before a job is published.
- A cancelled scan never invokes the scanner runtime.
- A timeout maps the scan to `Timeout` and preserves the safe error message.
- An unexpected runtime failure maps the scan to `Failed`.
- Requested cancellation is propagated and is not rewritten as a scan failure.
- Successful processing persists script results and findings together and marks
  the scan `Scanned`.
- Credential-bearing MSSQL and Oracle flows must not expose passwords in finding
  evidence.

## Migration constraints

1. No destructive database rename is allowed before dual-read compatibility is
   proven.
2. Existing API and RabbitMQ field names remain accepted during migration.
3. New executor implementations cannot receive arbitrary executable paths or raw
   shell fragments from users.
4. Cancellation, timeout, scope validation and credential protection semantics
   must remain unchanged.
5. Each migration stage must finish with backend tests, frontend lint and a
   production frontend build passing.
