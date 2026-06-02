# Implementation Plan: Database & Core Models (Phase 3)

**Branch**: `[003-database-core-models]` | **Date**: 2026-06-01 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/003-database-core-models/spec.md`

## Summary

Deliver Phase 3 backend data foundations by extending the existing Core/Repository persistence model with the minimum domain records for doctors, companies, campaigns, targets, queues, deliveries, Doctor/Company/Platform wallets, wallet transactions, immutable wallet ledger entries, withdrawals, stored files, policy history, activity history, and audit history. The implementation approach keeps domain entities and repository contracts in `MediBridge.Core`, SQL Server/EF Core mappings, migrations, repository implementations, and unit-of-work behavior in `MediBridge.Repository`, with `MediBridge.Services` consuming only abstractions and `MediBridge.APIs` remaining HTTP-only. Phase 3 deliberately excludes public or diagnostic controllers, campaign workflows, delivery jobs, interaction settlement, file upload, wallet endpoints, withdrawals, reporting workflows, and persistent reporting read models.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API foundation, ASP.NET Core Identity baseline from Phase 2, Entity Framework Core SQL Server, existing repository/unit-of-work infrastructure, JWT Bearer authorization primitives, existing envelope/exception/correlation middleware  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; Phase 3 adds core domain tables, constraints, indexes, soft-delete query behavior, append-only transaction/history records, immutable wallet ledger entries, concurrency tokens, and migrations while preserving Phase 2 Identity and `RefreshCredential` schema/state  
**Testing**: `dotnet test` across unit, integration, and contract projects; Phase 3 should add repository/model integration coverage for migrations, Phase 2 identity/refresh preservation, constraints, FIFO queue ordering, soft-delete filtering, money precision rejection, wallet idempotency uniqueness, wallet ledger immutability, refund/withdrawal flows, and atomic wallet transaction plus ledger commits  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: Initial migration applies successfully in 100% of validation runs; core entity create/read validation completes in under 10 seconds against a local SQL Server test database; queue ordering and wallet idempotency validation pass in 100% of automated cases  
**Constraints**: Keep `MediBridge.Core` free of EF Core and HTTP types; keep controllers free of direct persistence access; do not add Phase 3 public or diagnostic controllers; preserve Phase 1 API envelope and global exception behavior without adding smoke paths; all SQL persistence goes through EF Core in `MediBridge.Repository`; reject monetary inputs with more than 2 decimals; soft-deleted records remain historically linked and are excluded from active-record queries by default; audit/history records and wallet ledger entries are append-only/immutable with linked correction or compensating records; wallet transaction idempotency is unique by `OperationType + IdempotencyKey`  
**Scale/Scope**: Phase 3 data model and persistence only; includes schema, migrations, repository contracts, repository implementations, unit-of-work boundaries, domain validation primitives, and persistence tests; excludes Phase 4+ file content handling, Phase 5+ campaign workflows, Phase 7+ jobs, Phase 8+ settlement, Phase 10 reporting read models, and Phase 11 admin workflow endpoints

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Changes map to `MediBridge.Core` domain entities/contracts, `MediBridge.Repository` EF Core SQL Server persistence, `MediBridge.Services` abstraction consumption, and no required API controller logic.
- Controller gate: PASS - Phase 3 is persistence-focused and does not add public workflow controllers, public validation endpoints, or diagnostic controllers. Validation is repository, integration, migration, and service-boundary tests only.
- Data gate: PASS - SQL persistence targets SQL Server through EF Core in `MediBridge.Repository`, behind repository and unit-of-work abstractions defined in Core.
- Security gate: PASS - Secured future data access remains JWT/role-aware; Phase 3 supplies ownership-capable data relationships without broadening endpoint access.
- API contract gate: PASS - No new public API surface is required. Existing envelope and global exception middleware remain unchanged; Phase 3 does not add smoke paths.
- Scope gate: PASS - Spec explicitly excludes campaign workflows, jobs, settlement, wallet endpoints, file upload, reporting workflows, and persistent reporting read models.
- Queue gate: PASS - Phase 3 queue scope is persistent model determinism only: per-doctor FIFO by `QueuedAtUtc ASC`, `Id ASC` tie-break, no priority behavior, and queued/activated/cancelled status support.
- Wallet gate: PASS - Wallet scope is persistent model determinism only: Doctor/Company/Platform wallets, available/reserved balances, append-only transactions, immutable ledger entries, explicit `Refund`, canonical withdrawal workflow types, idempotency uniqueness by operation type plus key, 2-decimal EGP precision, rejection of >2 decimal inputs, and atomic balance plus transaction plus ledger commits.
- Phase 2 preservation gate: PASS - Phase 3 migrations must preserve Identity tables, Doctor/Company/Admin roles, account approval state, and `RefreshCredential` rotation tracing.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/persistence-contracts.md`, and `quickstart.md` keep entities/contracts in Core, EF Core details in Repository, and validation responsibilities out of controllers.
- Controller gate: PASS - Design artifacts define no new public workflow endpoints, no validation endpoints, and no diagnostic controllers.
- Data gate: PASS - Data model identifies SQL Server tables, EF Core mappings, indexes, uniqueness rules, concurrency tokens, and unit-of-work transaction requirements behind Repository abstractions.
- Security gate: PASS - Ownership relationships, user-role foundations, soft-delete state, and audit attribution are modeled for future JWT/role-aware workflows without opening new access paths.
- API contract gate: PASS - No public API contract is added; repository/service contracts are internal and API-visible behavior remains limited to existing endpoints and middleware.
- Scope gate: PASS - Reporting read models remain deferred to Phase 10; file content storage/review, queue jobs, settlement, and workflow endpoints remain out of scope.
- Queue gate: PASS - `QueuedAtUtc` is the persisted FIFO key derived from campaign submission or queue insertion time; reads order by `QueuedAtUtc ASC, Id ASC`, including same-timestamp tie-breaks and later carry-over support.
- Wallet gate: PASS - Debit/credit ledger primitives, explicit transaction types including `Refund` and `WithdrawPayout`, idempotency uniqueness, atomic commits, append-only history, and precision validation are explicit and testable.
- Phase 2 preservation gate: PASS - Migration/schema validation must prove Phase 2 roles, approval state, and `RefreshCredential` records remain intact.

## Project Structure

### Documentation (this feature)

```text
specs/003-database-core-models/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── persistence-contracts.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks, not created by /speckit.plan
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Campaigns/
│   ├── Files/
│   ├── Identity/
│   ├── Messaging/
│   ├── Policies/
│   ├── Profiles/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Campaigns/
    ├── Files/
    ├── Messaging/
    ├── Policies/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   ├── Campaigns/
│   ├── Files/
│   ├── Messaging/
│   ├── Policies/
│   └── Wallets/
├── Data/
├── Migrations/
├── Repositories/
└── UnitOfWork/

MediBridge.Services/
├── Interfaces/
├── Services/
└── Validators/

MediBridge.APIs/
├── Controllers/
├── Config/
├── Middleware/
└── Security/

tests/
├── contract/
│   └── MediBridge.ContractTests/
├── integration/
│   └── MediBridge.IntegrationTests/
└── unit/
    └── MediBridge.UnitTests/
```

**Structure Decision**: Use the existing four-project Onion structure. Extend Core with pure domain entities/enums/repository contracts, Repository with EF Core configurations, DbSet registrations, migrations, repositories, and domain unit-of-work boundaries, Services only where service-boundary validation is needed, and existing test projects for persistence/model verification. No additional application project, public API surface, or diagnostic controller is required for Phase 3.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.
