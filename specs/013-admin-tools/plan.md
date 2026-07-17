# Implementation Plan: Phase 11 Admin Tools

**Branch**: `[013-admin-tools]` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/013-admin-tools/spec.md`

## Summary

Deliver Phase 11 admin tooling by consolidating existing account, file, campaign, pricing, platform-fee, and enforcement controls into an auditable admin operations surface, then adding the missing withdrawal-request/payout-stub workflow and read-only admin statistics. The implementation extends the current four-project backend: domain records and repository contracts in `MediBridge.Core`, SQL Server/EF Core persistence in `MediBridge.Repository`, orchestration and DTO shaping in `MediBridge.Services`, and HTTP-only controllers in `MediBridge.APIs`. Phase 11 preserves existing delivery activation, settlement, company reporting, and enforcement formulas; the only new wallet mutations are deterministic withdrawal holds, releases, and payout finalization.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, existing standard API envelope/exception/correlation middleware, Entity Framework Core 8 SQL Server, existing identity/profile, file review, campaign review, pricing policy, activity enforcement, wallet transaction, ledger, and audit abstractions  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; all admin reads and withdrawal/payout writes remain behind Repository + Unit of Work abstractions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, SQL Server-backed integration tests through existing unit, contract, integration, and performance test projects  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service using existing Onion Architecture  
**Performance Goals**: At least 95% of warmed admin work queue and statistics requests for a 90-day period complete within 2 seconds; 100% of paginated admin queue and withdrawal-list traversal returns every matching row exactly once; 100% of withdrawal transitions are idempotent under retry and concurrency tests  
**Constraints**: Admin JWT required for all admin tools; doctor withdrawal submission requires the authenticated approved, active, non-suspended Doctor owner; no payout destination collection or saved payout methods; payout stub records status and payout reference only; admin statistics date ranges are capped at 90 days; inconsistent financial evidence withholds affected financial totals while returning unrelated non-financial totals; all responses use the standard envelope; no EF Core access outside Repository; no real external bank transfer/payment-provider integration; no changes to delivery activation, interaction settlement formulas, company reporting paths, or automated enforcement penalties  
**Scale/Scope**: Extend existing admin controllers/services where present; add admin work queue, doctor withdrawal request submission/listing, admin withdrawal decision/payout endpoints, admin statistics endpoints, repository read models, withdrawal hold/finalization ledger operations, contracts, quickstart, unit/contract/integration/performance validation, and a migration only where existing schema lacks required fields/indexes

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Domain records, enums, read models, and repository contracts remain in `MediBridge.Core`; SQL persistence and migrations remain in `MediBridge.Repository`; workflow orchestration, validation, authorization-sensitive decisions, and DTO mapping remain in `MediBridge.Services`; controllers remain HTTP-only in `MediBridge.APIs`.
- **Controller gate: PASS** - Admin and doctor controllers bind route/query/body inputs, resolve actor ids, call services, and return envelopes only. They do not calculate queue urgency, wallet balances, payout transitions, policy history, statistics totals, or audit evidence.
- **Data gate: PASS** - Withdrawal requests, wallet transactions, ledger entries, policy histories, account/file/campaign review history, enforcement summaries, and statistics read models use Repository + Unit of Work abstractions. Services/controllers do not depend on EF Core infrastructure.
- **Security gate: PASS** - Admin tools require Admin JWT authorization. Doctor withdrawal submission uses Doctor JWT plus owner, approval, active-state, and non-suspended checks. Cross-role attempts are denied through safe envelope errors.
- **API contract gate: PASS** - Success and failure paths use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`, with global exception handling for validation, forbidden, stale-state, concurrency, payout, and evidence inconsistency failures.
- **Scope gate: PASS** - The plan excludes real bank transfers, payout destination storage, payment-provider settlement, new delivery activation rules, new interaction settlement formulas, company-owned reporting changes, automated enforcement penalties beyond existing rules, and hard deletion of audit-preserved records.
- **Queue gate: PASS** - Phase 11 does not alter delivery queue ordering, daily limits, expiry, retry, or carry-over. Admin work queue ordering is a separate operational read model and campaign approvals remain idempotent.
- **Wallet gate: PASS** - Wallet mutations are limited to withdrawal hold creation, hold release on rejection/eligible failure, and payout finalization on paid status. Company top-ups, campaign reservations, expiry releases, interaction charges, doctor earnings settlement, and platform fee formulas are unchanged.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/admin-tools-api.yaml](./contracts/admin-tools-api.yaml), and [quickstart.md](./quickstart.md) preserve Core contracts/entities, Repository persistence, Services orchestration, and APIs transport boundaries.
- **Controller gate: PASS** - Contracts require controllers to handle only routing, auth policy selection, request binding, cancellation propagation, and envelope responses.
- **Data gate: PASS** - Design uses repository read models for admin queue/statistics and transactional repository/unit-of-work methods for withdrawal hold, release, and finalization.
- **Security gate: PASS** - Admin-only and doctor-owner-only paths are explicit, with non-disclosing forbidden/not-found behavior where protected resource existence should not leak.
- **API contract gate: PASS** - OpenAPI contract covers standard envelopes, pagination, admin role gates, doctor withdrawal eligibility, validation errors, stale-state conflicts, and safe financial inconsistency responses.
- **Scope gate: PASS** - Research and design preserve payout-stub-only scope and existing delivery/settlement/reporting/enforcement boundaries.
- **Queue gate: PASS** - Admin work queue is an operational read model only; no artifact changes delivery queue mechanics.
- **Wallet gate: PASS** - Wallet transition table and contracts define all withdrawal debit/credit/hold/finalization behavior and reject duplicate or incompatible transitions.

## Project Structure

### Documentation (this feature)

```text
specs/013-admin-tools/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── admin-tools-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks; not created here
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Identity/
│   ├── Policies/
│   ├── Profiles/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Admin/
    ├── Identity/
    ├── Policies/
    ├── Profiles/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   ├── Identity/
│   ├── Policies/
│   └── Wallets/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Admin/
│   ├── Identity/
│   ├── Policies/
│   └── Wallets/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   ├── Admin/
│   ├── Pricing/
│   └── Wallets/
├── Interfaces/
├── Services/
└── Validators/
    ├── Admin/
    ├── Pricing/
    └── Wallets/

MediBridge.APIs/
├── Controllers/
├── Security/
└── Program.cs

tests/
├── contract/MediBridge.ContractTests/
├── integration/MediBridge.IntegrationTests/
├── performance/
└── unit/MediBridge.UnitTests/
```

**Structure Decision**: Extend the existing four-project backend. Reuse current admin controllers and services for account, file, campaign, pricing, fee policy, and enforcement behavior where they already exist; add narrowly scoped services/repositories for admin work queue, withdrawals/payouts, and admin statistics. Add no new project and no background worker.

## Resolved Implementation Decisions

- Add `GET /api/admin/work-queue` as an Admin-only operational read model combining pending accounts, protected file reviews, campaign reviews, enforcement review items, and withdrawal requests. It supports category/status filters, `PageNumber`, `PageSize`, and stable ordering by urgency rank, requested/submitted time, then item id.
- Keep existing account, file, campaign, pricing, platform-fee, and enforcement routes where already implemented. Phase 11 hardens their audit, concurrency, DTO privacy, and work-queue inclusion rather than duplicating workflows.
- Add `PUT /api/admin/doctors/{doctorId}/price/deactivate` for separate pricing deactivation. Do not encode inactive pricing as null, zero, or any numeric value. A later valid positive price reactivates paid campaign eligibility.
- Keep doctor price and platform fee changes future-only. Existing delivery snapshots, settlements, company reports, and ledgers are never recalculated after price, deactivation, or fee changes.
- Add doctor-owned `POST /api/doctor/withdrawals` and `GET /api/doctor/withdrawals` so approved, active, non-suspended doctors can request and inspect their own withdrawal requests.
- Add Admin-only `GET /api/admin/withdrawals`, `PUT /api/admin/withdrawals/{withdrawalId}/approve`, `PUT /api/admin/withdrawals/{withdrawalId}/reject`, `PUT /api/admin/withdrawals/{withdrawalId}/mark-paid`, and `PUT /api/admin/withdrawals/{withdrawalId}/mark-failed`.
- Do not collect payout destinations. Withdrawal and payout DTOs may expose payout reference, status, reason/note, timestamps, and safe doctor summary only. They must not include bank account, card, mobile wallet number, payout-destination text, saved payout method, provider payload, or provider diagnostic fields.
- Model withdrawal funds as a deterministic wallet hold: valid request moves withdrawable earnings from available to pending hold; approval preserves the hold; rejection releases the hold; paid finalizes the hold as payout; eligible failure releases the hold. All transitions are idempotent under retries and guarded by concurrency tokens.
- Define withdrawable settled earnings as settled doctor earnings minus pending withdrawal holds, paid withdrawals, disputed/inconsistent evidence, and any already-open request amount. Active campaign reservations and company wallet balances do not participate.
- Use existing wallet transaction and ledger concepts where possible. If current transaction types are insufficient, add explicit withdrawal hold/release/finalize evidence while preserving immutable transaction/ledger history and `WithdrawalRequestId` references.
- Add `GET /api/admin/statistics` for Admin-only read-only statistics over a bounded inclusive date range of at most 90 days. Omitted date filters default to the latest 90 days ending on the current Egypt business date.
- Admin statistics include account counts by role/status, review queue counts, campaign status counts, delivery outcomes, interaction outcomes, withdrawal/payout status counts, wallet movement summaries, current pricing/fee policy summaries, and enforcement action counts.
- If financial statistics do not reconcile to wallet transaction and ledger evidence, withhold affected financial totals and return safe inconsistency flags while preserving unrelated non-financial totals.
- Use standard pagination defaults of `PageNumber = 1`, `PageSize = 20`, maximum `PageSize = 100` for work queue and withdrawal lists.
- Public reason fields are owner-visible where applicable; internal notes remain Admin-only. Admin statistics and queue summaries never expose raw storage keys, storage provider credentials, raw idempotency material, payout destination data, private doctor contact details beyond authorized review need, or raw stack traces.
- Add repository indexes for withdrawal filtering/status transitions and admin statistics read paths where existing schema is insufficient: status/date/doctor, reviewed date/admin, payout reference, wallet evidence by `WithdrawalRequestId`, and work-queue source status/date combinations.
- Unit tests cover withdrawal eligibility, amount validation, hold/release/finalize math, idempotency keys or replay behavior, pricing deactivation rules, statistics financial inconsistency behavior, and DTO privacy mapping.
- Contract tests cover Admin-only role gates, Doctor-owner withdrawal submission/listing, standard envelopes, pagination bounds, validation errors, stale/incompatible transitions, payout-reference-only behavior, and absence of payout destination fields.
- SQL Server integration tests cover concurrent withdrawal submission/decision/payout attempts, ledger atomicity, rollback on failure, work-queue pagination, statistics reconciliation/withholding, existing admin workflow compatibility, and read-only mutation audits for statistics/work-queue reads.
- Performance-profile tests seed realistic 90-day admin data and measure work queue and statistics p95 against the 2-second target.

## Performance Test Profile

- Run Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers or the project standard SQL Server test database on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, and no debugger or coverage collector.
- Seed at least 10,000 operational source records across accounts, files, campaigns, deliveries, interactions, wallet transactions, withdrawal requests, policy histories, and enforcement actions inside a 90-day window.
- Warm with 10 requests per endpoint, then measure 200 requests at concurrency 10 for `GET /api/admin/work-queue`, `GET /api/admin/withdrawals`, and `GET /api/admin/statistics`.
- Require p95 under 2 seconds, zero failed valid requests, stable pagination, no financial inconsistency false positives, no sensitive-field leakage, and bounded query counts. Report p50, p95, p99, query count, row count, validation failures, authorization failures, and withheld financial scopes.
- Performance tests are opt-in outside the designated CI/performance profile. A skipped run must report unmet prerequisites rather than count as passing evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves work-queue composition, reuse of existing admin workflows, pricing deactivation, withdrawal hold accounting, payout-stub boundaries, statistics financial inconsistency handling, authorization/privacy, concurrency/idempotency, and performance profile. No unresolved clarification markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines admin work queue read models, withdrawal request/hold/payout state, pricing deactivation history, admin statistics snapshots, validation rules, indexes, and state transitions.
- [contracts/admin-tools-api.yaml](./contracts/admin-tools-api.yaml) defines the Admin and doctor withdrawal HTTP contracts.
- [quickstart.md](./quickstart.md) provides setup, migration, smoke, authorization, wallet atomicity, statistics consistency, privacy, and performance checks.
