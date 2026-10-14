# Stage 9 Cleanup Proposal — Review Only

Status: **not approved and not executable yet**.

No legacy field, route, queue property, database column or adapter is removed
by Stage 9. Cleanup may be scheduled only after the observation log proves all
of the following in every target environment for seven continuous days:

1. every API and worker instance is at Stage 9 or newer;
2. readiness is continuously `1`;
3. legacy fallback and contract-conflict counters do not increase;
4. old-producer/new-consumer and new-producer/old-consumer contract tests remain
   green in the release pipeline;
5. the rollback owner signs off on the observation record.

## Proposed cleanup order

Each step is an independently deployable change and must preserve rollback to
the preceding step.

1. Stop dual-writing legacy RabbitMQ and HTTP response fields while continuing
   to read them.
2. After one additional deployment window, stop reading legacy RabbitMQ and
   HTTP request fields.
3. Remove legacy application names and NSE-only aliases that are not part of
   installed NSE discovery.
4. Add a database migration that drops legacy duplicate columns only after a
   fresh backup and row-count audit.
5. Remove the compatibility metrics and operational alerts in a later release,
   not in the schema-cleanup deployment.

## Rollback rule

Any fallback, conflict, missing deployment instance or readiness value other
than `1` cancels cleanup and restarts the seven-day clock. Stage 10 must not
interpret this proposal as approval to delete compatibility boundaries.
