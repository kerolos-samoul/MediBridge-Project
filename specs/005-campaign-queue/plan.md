# Implementation Plan: Campaign & Queue (Phase 5)

**Branch**: `[005-campaign-queue]` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-campaign-queue/spec.md`

## Summary

Deliver Phase 5 company-side campaign and queue readiness by adding eligible doctor search, idempotent campaign submission with target snapshots, non-mutating wallet sufficiency validation before campaign acceptance, approved-campaign queue creation behavior, and company wallet top-up/query. The implementation approach extends Phase 3 campaign, queue, profile, wallet, and audit domain records; reuses Phase 4 approved campaign asset readiness checks; keeps SQL Server persistence in `MediBridge.Repository`; places filtering, ownership, validation, idempotency, wallet sufficiency checks, queue creation, wallet top-up, and audit orchestration in `MediBridge.Services`; and exposes HTTP-only company campaign, doctor search, and wallet endpoints in `MediBridge.APIs`. Phase 5 deliberately excludes admin campaign moderation UI, daily delivery activation, expiry, doctor inbox, settlement, reporting analytics, withdrawals, weekly enforcement, activity score jobs, and production payment gateway processing.

## Technical Context

**Language/Version**: C# / .NET 8
**Primary Dependencies**: ASP.NET Core Web API foundation, ASP.NET Core Identity/JWT baseline, existing role authorization policies, existing response envelope and global exception middleware, existing fixed-window rate limiting, Entity Framework Core SQL Server, Repository + Unit of Work infrastructure, existing Phase 3 campaign/queue/wallet/profile entities, existing Phase 4 campaign file readiness checks
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository` for campaigns, campaign targets, campaign submission idempotency, doctor profile search, queue items, company wallets, wallet transactions, wallet ledger entries, and audit events; only non-secret wallet gateway-stub metadata is persisted
**Testing**: `dotnet test .\MediBridge.slnx`; add focused contract, integration, and unit coverage for company doctor filtering/order/pagination, campaign submission validation, target snapshot persistence, 100-target cap, all-or-nothing target validation, required approved asset, campaign submission wallet sufficiency rejection, campaign submission idempotency, company campaign ownership, approved-campaign queue creation idempotency, queue FIFO order, company wallet top-up/query, top-up idempotency, two-decimal EGP validation, response envelope behavior, role/ownership denial, audit safety, and layering boundaries
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime
**Project Type**: Layered web service (Onion Architecture)
**Operational Performance Targets**: Non-gating targets for implementation validation are: doctor search returns a page of up to 100 doctors in under 1 second; campaign submission validates wallet sufficiency and snapshots up to 100 target doctors atomically in under 2 seconds; approved-campaign queue creation creates or skips all target queue rows idempotently in under 2 seconds for 100 targets; wallet query returns a page of up to 100 transactions in under 1 second. These targets guide integration observation and tuning, but Phase 5 acceptance is governed by the functional success criteria in `spec.md`.
**Constraints**: Keep `MediBridge.Core` free of EF Core and HTTP types; keep controllers HTTP-only; use Repository + Unit of Work for all SQL persistence; company-only workflows require JWT and Pharmaceutical Company role plus approved active account state; eligible doctor search excludes unapproved, suspended, soft-deleted, and zero-price doctors; doctor search orders by activity score descending, price ascending, stable identifier ascending; campaign submission requires title, description, clinical research information, at least one approved campaign asset, 1-100 unique eligible target doctors, company wallet available balance greater than or equal to the sum of selected target price snapshots, and a company-scoped idempotency key; campaign submission is all-or-nothing; top-up minimum is 100 EGP with two-decimal precision; wallet top-up credits available balance only; campaign submission wallet sufficiency validation is read-only; no reserve, charge, earn, release, refund, withdrawal, settlement, delivery activation, or production gateway behavior in Phase 5
**Scale/Scope**: Phase 5 adds company doctor search, campaign create/list/detail, approved-campaign queue creation service behavior, company wallet top-up/query, API/service/repository contracts, SQL persistence changes where needed, and automated validation. It builds on Phase 1-4 foundations and keeps campaign moderation endpoints, daily jobs, doctor message workflows, settlement, reporting, and admin tooling in later phases.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Core owns campaign/profile/queue/wallet entities and contracts, Repository owns SQL Server/EF Core persistence, Services owns filtering/submission/queue/wallet orchestration, and APIs owns HTTP routing/envelope behavior.
- Controller gate: PASS - Planned controllers delegate doctor filtering, campaign submission/list/detail, queue-trigger behavior, wallet top-up/query, ownership, validation, and audit work to services.
- Data gate: PASS - Campaigns, targets, queue items, wallet balances, transactions, ledger entries, idempotency records, and audit events persist through SQL Server/EF Core in `MediBridge.Repository`, behind Repository + Unit of Work abstractions.
- Security gate: PASS - Company workflows require JWT, Pharmaceutical Company role, approved active account state, and ownership checks; trusted queue creation paths do not expose public queue mutation.
- API contract gate: PASS - All Phase 5 endpoints use the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope and global exception handling.
- Scope gate: PASS - Spec explicitly excludes admin campaign moderation screens, daily injector jobs, expiry jobs, doctor inbox, doctor interactions, analytics, withdrawals, weekly enforcement, activity score jobs, and production payment gateway integration.
- Queue gate: PASS - Queue creation is after campaign approval only; pending queue order is `QueuedAtUtc ASC, Id ASC`; daily limits, expiry, activation, carry-over, and reservation skip behavior are deferred to Phase 7.
- Wallet gate: PASS - Wallet behavior is limited to company `TopUp`, wallet query, and read-only campaign submission sufficiency validation; top-up credits available balance with append-only transaction plus ledger in one unit of work; campaign submission rejects when available balance is below selected target price snapshot total without reserving, charging, creating wallet transactions, creating wallet ledger entries, or mutating balances; fee, reservation, charge, earn, refund, withdrawal, and settlement behavior are out of scope.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/campaign-queue-api.yaml`, `contracts/service-contracts.md`, and `quickstart.md` keep domain contracts in Core, EF Core in Repository, orchestration in Services, and HTTP concerns in APIs.
- Controller gate: PASS - API contracts define request/response surfaces only; target validation, idempotency, queue creation, wallet balance mutation, and ownership decisions are service responsibilities.
- Data gate: PASS - Data model extends existing Campaign, CampaignTarget, DoctorMessageQueue, Wallet, WalletTransaction, WalletLedgerEntry, AuditEvent, and repository methods while isolating SQL details in Repository.
- Security gate: PASS - Design requires JWT, Company role policies, approved account checks, ownership filters, idempotency scoping, non-secret audit metadata, and denial for cross-company wallet/campaign access.
- API contract gate: PASS - Contracts retain the standard response envelope, explicit validation failures, and no raw stack traces.
- Scope gate: PASS - Design artifacts retain Phase 5 exclusions and defer admin moderation UI, daily jobs, delivery, settlement, reporting analytics, withdrawals, weekly enforcement, activity scoring, and production gateway work.
- Queue gate: PASS - Queue design documents approval-only creation, per-campaign/doctor uniqueness, retry safety, skip auditing for ineligible targets at approval time, and FIFO ordering.
- Wallet gate: PASS - Wallet design documents top-up-only available balance credit, 100 EGP minimum, two-decimal EGP validation, operation/idempotency duplicate prevention, atomic balance plus transaction plus ledger commit, read-only campaign submission sufficiency validation against selected target price snapshots, and no settlement behavior.

## Project Structure

### Documentation (this feature)

```text
specs/005-campaign-queue/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── campaign-queue-api.yaml
│   └── service-contracts.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks, not created by /speckit.plan
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Campaigns/
│   │   ├── Campaign.cs
│   │   ├── CampaignTarget.cs
│   │   └── CampaignSubmissionRequest.cs
│   ├── Messaging/
│   │   └── DoctorMessageQueue.cs
│   ├── Policies/
│   │   └── AuditEvent.cs
│   ├── Profiles/
│   │   ├── DoctorProfile.cs
│   │   └── CompanyProfile.cs
│   └── Wallets/
│       ├── Wallet.cs
│       ├── WalletTransaction.cs
│       └── WalletLedgerEntry.cs
├── Enums/
│   └── Phase3DomainEnums.cs
└── Interfaces/
    ├── Campaigns/
    ├── Files/
    ├── Identity/
    ├── Messaging/
    ├── Policies/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   ├── Campaigns/
│   ├── Messaging/
│   └── Wallets/
├── Data/
│   └── MediBridgeDbContext.cs
├── Migrations/
├── Repositories/
│   ├── Campaigns/
│   ├── Messaging/
│   ├── Policies/
│   ├── Identity/
│   └── Wallets/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   ├── Campaigns/
│   ├── Doctors/
│   └── Wallets/
├── Interfaces/
│   ├── ICampaignWorkflowService.cs
│   ├── ICompanyDoctorSearchService.cs
│   └── ICompanyWalletService.cs
├── Services/
│   ├── CampaignWorkflowService.cs
│   ├── CompanyDoctorSearchService.cs
│   └── CompanyWalletService.cs
└── Validators/
    ├── Campaigns/
    ├── Doctors/
    └── Wallets/

MediBridge.APIs/
├── Controllers/
│   ├── CompanyCampaignsController.cs
│   ├── CompanyDoctorsController.cs
│   └── CompanyWalletController.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
└── Security/
    └── AuthorizationPolicies.cs

tests/
├── contract/
│   └── MediBridge.ContractTests/
├── integration/
│   └── MediBridge.IntegrationTests/
└── unit/
    └── MediBridge.UnitTests/
```

**Structure Decision**: Use the existing four-project Onion structure. Extend existing Phase 3 campaign, queue, profile, wallet, and audit foundations rather than creating parallel models. Add company-specific API controllers for doctor search, campaign workflows, and wallet workflows. Keep Phase 4 campaign asset readiness as a repository/service dependency for campaign submission validation. Automated validation uses existing unit, integration, and contract test projects.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.
