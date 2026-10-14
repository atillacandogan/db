# ADR-001: Extensible Scan Executors

- Status: Accepted
- Date: 2026-10-03

## Context

DbScanner currently exposes a useful `IScannerRuntime` port, but its contract,
catalog, persistence model, queue messages and presentation language are coupled
to NSE scripts and Nmap XML. MSSQL and Oracle native probes are composed through
a runtime delegation chain. Adding another scanner would therefore require edits
across existing runtimes and several layers.

## Decision

The scan pipeline will be migrated incrementally to an executor-oriented model:

1. Introduce generic check, execution-context, payload and result contracts next
   to the existing NSE contracts.
2. Introduce `IScanExecutorStrategy`, a resolver/registry and a composite
   orchestrator.
3. Adapt the existing Nmap/NSE and native database probes to strategies without
   changing their observable behavior.
4. Generalize catalog, API, messaging, persistence and UI names using dual-read
   compatibility before removing legacy NSE contracts.

The orchestrator groups checks by an explicit, server-controlled executor type.
It resolves registered strategies, executes the groups and merges normalized
results for the existing rule and scoring pipeline.

## Compatibility rules

- Existing NSE identifiers remain stable.
- Legacy API and queue fields continue to be read during the transition.
- When legacy and generic fields are both supplied, conflicting values are
  rejected rather than guessed.
- Database changes are additive until migrated data and rollback paths are
  verified.
- Nmap remains one executor implementation; it is not the domain abstraction.

## Security rules

- Executor types map only to DI-registered strategies.
- User input cannot select executable paths or inject shell arguments.
- Strategies must honor cancellation and bounded timeouts.
- Authentication material is passed through the existing protected credential
  path and must not be stored in raw or normalized scan output.
- Scope authorization remains outside and before executor invocation.

## Consequences

New scanner integrations require a strategy, registration, catalog definitions
and tests instead of modifications to the central scan workflow. During the
transition, adapters and duplicate contract names add temporary complexity. That
complexity is intentional and will be removed only after stage-3 compatibility
criteria pass.
