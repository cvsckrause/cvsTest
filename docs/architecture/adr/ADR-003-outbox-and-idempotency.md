# ADR-003: Transactional outbox and idempotent processing

- Status: Accepted
- Date: 2026-10-13

## Context

Sales-order release changes business state and triggers inventory, delivery, invoicing, and external integration work. Database commits and message publication cannot form one distributed transaction.

## Decision

Persist business changes, audit records, and outbox events in one PostgreSQL transaction. A separate worker publishes stable, versioned integration events. Consumers record message IDs in an inbox before applying effects and tolerate redelivery. Public write APIs support an `Idempotency-Key`.

Delivery is at least once. Retry, visibility timeout, dead-letter queues, and replay tooling are explicit; the product does not claim end-to-end exactly once.

## Consequences

- Consumers must be idempotent and observable.
- Outbox backlog and DLQ age are operational signals.
- Event contracts are versioned independently from internal domain events.
- Duplicate requests and messages require automated tests.
