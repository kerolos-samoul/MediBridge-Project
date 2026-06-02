# Research: Database & Core Models (Phase 3)

## Decision: Extend existing EF Core SQL Server persistence in Repository

**Rationale**: The constitution requires SQL Server persistence through EF Core in `MediBridge.Repository`, behind repository and unit-of-work abstractions. The current code already has `MediBridgeDbContext`, Identity integration, EF Core configurations, migrations, repository implementations, and an `IdentityUnitOfWork`, so Phase 3 should extend those patterns rather than create a parallel persistence layer.

**Alternatives considered**:

- Direct SQL from services or controllers: rejected by constitution and would break persistence boundaries.
- A second DbContext for domain records: rejected for this phase because wallet/delivery/identity relationships and transactions benefit from one transaction boundary unless a later performance need justifies separation.
- Non-SQL storage for ledger/history: rejected because SQL Server is the required persistence platform.

## Decision: Keep Core entities persistence-ignorant

**Rationale**: `MediBridge.Core` must remain free of EF Core and HTTP concerns. Domain entities should expose identifiers, relationships by id, state fields, and domain validation helpers where useful, while EF-specific configuration belongs in `MediBridge.Repository`.

**Alternatives considered**:

- Adding EF attributes to Core entities: rejected because it leaks persistence implementation details into Core.
- Using Repository-only data models with no Core entities: rejected because services need stable domain contracts independent of EF.

## Decision: Introduce domain-specific repository contracts plus unit-of-work boundary

**Rationale**: Phase 3 needs query and persistence access for campaigns, queues, deliveries, wallets, files, policy history, and audits without exposing `DbContext` to services. A domain unit of work can coordinate wallet balance changes and append-only wallet transactions atomically, while existing identity unit-of-work remains intact.

**Alternatives considered**:

- One generic repository for all entities: rejected because queue ordering, delivery uniqueness, soft-delete filtering, and wallet idempotency need domain-specific methods.
- Reusing only the Phase 2 identity unit-of-work: rejected because it would mix identity lifecycle concerns with core marketplace persistence.

## Decision: Enforce wallet transaction idempotency by operation type plus idempotency key

**Rationale**: Clarification selected uniqueness by `OperationType + IdempotencyKey`. This allows independent namespaces for top-up, reserve, release, charge, earn, withdrawal, decision, and payout operations while preventing duplicate financial effects inside each operation type.

**Alternatives considered**:

- Globally unique idempotency key: rejected because it is stricter than the backend plan requires and creates unnecessary cross-operation coupling.
- Owner plus operation type plus key: rejected because operation type plus idempotency key is sufficient when keys are generated per operation scope and keeps validation simpler.

## Decision: Reject monetary inputs with more than two decimals

**Rationale**: Clarification selected rejection rather than rounding or truncation. Explicit rejection avoids silent balance changes and supports trustworthy ledger validation. Calculated fee values still follow the backend plan's fee formula and 2-decimal rounding rule when future settlement workflows are implemented.

**Alternatives considered**:

- Round input values: rejected because it can alter user-provided amounts silently.
- Truncate input values: rejected because it loses money precision silently and is harder to explain in audits.

## Decision: Soft-delete records remain historically linked and are excluded from active queries

**Rationale**: The backend plan forbids hard deletion for Doctor, Company, Campaign, and Wallet records with financial or audit history. Clarification selected default exclusion from active-record queries while preserving relationships for historical and audit access.

**Alternatives considered**:

- Keep soft-deleted records visible in all queries: rejected because normal workflows should not accidentally target inactive records.
- Store flags only and defer query behavior: rejected because it would make repository contracts ambiguous.

## Decision: Audit/history is append-only with linked correction records

**Rationale**: Audit, policy, review, activity, and financial history records support compliance and operational traceability. Corrections, reversals, or invalidations should be modeled as new linked records so the original event remains explainable.

**Alternatives considered**:

- Update incorrect history rows in place: rejected because it removes the evidence trail.
- Soft-delete history rows: rejected because history rows should remain immutable evidence.

## Decision: Defer persistent reporting read models to Phase 10

**Rationale**: Clarification selected deferral. Phase 3 should store authoritative records and constraints that later reports can derive from, while persistent campaign analytics/read models belong with Phase 10 reporting and analytics workflows.

**Alternatives considered**:

- Add campaign analytics read model tables now: rejected as scope creep and likely to change after delivery/settlement workflows exist.
- Add placeholder tables only: rejected because placeholder persistence adds migration churn without user value.

## Decision: No new public API contract for Phase 3

**Rationale**: Phase 3 is an internal persistence foundation. The public API already has Phase 1/2 contracts, and future workflow endpoints are explicitly out of scope. The useful contract for this phase is the internal service-facing persistence contract.

**Alternatives considered**:

- Add CRUD endpoints for data validation: rejected because controllers must stay thin and Phase 3 should not expose workflow surfaces prematurely.
- Add OpenAPI placeholders for future endpoints: rejected because future phases own those public contracts.
