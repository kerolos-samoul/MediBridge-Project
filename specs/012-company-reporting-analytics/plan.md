# Implementation Plan: Phase 10 Company Reporting & Analytics

**Branch**: `[012-company-reporting-analytics]` | **Date**: 2026-07-13 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/012-company-reporting-analytics/spec.md`

## Summary

Deliver company-owned campaign reporting over existing campaign, delivery, interaction, wallet transaction, and ledger evidence. Extend the company campaign surface so approved Pharmaceutical Company users can view reporting-enhanced campaign summaries, delivery pages, feedback pages, and one-campaign analytics. Reports are computed live from source delivery records and append-only financial evidence for an inclusive `DeliveryDateEgypt` range of at most 90 business days. The only Phase 10 write is safe discrepancy evidence when delivery states and financial evidence do not reconcile; reporting must otherwise remain read-only and must not mutate campaigns, deliveries, queues, wallets, transactions, ledgers, jobs, activity scores, settlement behavior, or stored reporting aggregates.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, existing standard API envelope/exception/correlation middleware, Entity Framework Core 8 SQL Server, existing Egypt business clock/date handling, existing campaign, delivery, wallet transaction, ledger, audit, and identity/profile abstractions  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; all reporting reads and discrepancy writes remain behind Repository + Unit of Work abstractions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, SQL Server-backed integration tests through existing unit, contract, integration, and performance test projects  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service using existing Onion Architecture  
**Performance Goals**: At least 95% of warmed campaign summary, delivery list, feedback list, and analytics requests for a campaign with 10,000 deliveries inside a 90-day delivery Egypt business-date window complete within 2 seconds; paginated delivery and feedback traversal returns every matching row exactly once; source-evidence reconciliation is correct in 100% of tested samples  
**Constraints**: Approved Company JWT only; strict campaign ownership for every report; date filters use `DeliveryDateEgypt` only and reject ranges longer than 90 inclusive business days; omitted date filters default to a bounded latest-90-day scope; standard `PageNumber`/`PageSize` pagination with maximum page size 100; no doctor names, contact details, wallet internals, raw idempotency material, storage credentials, or admin-only notes in company-visible responses; reporting totals are computed live from deliveries plus append-only financial evidence; stored reporting aggregates are non-authoritative; reconciliation discrepancies block only the affected report scope and create safe operational evidence; no EF Core access outside Repository; no admin statistics, settlement correction, campaign review, withdrawal, activity scoring, weekly enforcement, notification dispatch, or stored aggregate maintenance  
**Scale/Scope**: Extend `CompanyCampaignsController`, add company reporting service/DTOs/validators, add reporting read-model queries to campaign/delivery/wallet repositories, add safe discrepancy evidence through existing audit infrastructure or a narrowly scoped reporting discrepancy entity, add OpenAPI/contract tests, integration tests, and performance-profile tests for a 90-day/10,000-delivery campaign

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Report request/value objects, repository contracts, and optional discrepancy evidence contracts live in `MediBridge.Core`; SQL projections and indexes live in `MediBridge.Repository`; ownership, validation, reconciliation, and DTO shaping live in `MediBridge.Services`; controllers remain HTTP-only in `MediBridge.APIs`.
- **Controller gate: PASS** - Company controllers will map route/query parameters, actor identity, rate-limit/envelope behavior, and service calls only. They will not calculate counts, rates, money, reconciliation, privacy shaping, or persistence.
- **Data gate: PASS** - Campaign, delivery, wallet transaction, ledger, and discrepancy persistence use Repository + Unit of Work abstractions. Services do not depend on EF Core infrastructure types.
- **Security gate: PASS** - All Phase 10 routes require approved Pharmaceutical Company JWT authorization and ownership scoping. Cross-company requests fail without revealing whether protected campaign, delivery, feedback, or financial evidence exists.
- **API contract gate: PASS** - All success and failure paths use the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope and global exception middleware.
- **Scope gate: PASS** - The plan excludes admin platform-wide statistics, settlement correction, campaign moderation decisions, withdrawals, activity scoring, weekly enforcement, notifications, stored aggregate maintenance, and new settlement behavior.
- **Queue gate: PASS** - Phase 10 reads resulting delivery states only. It does not requeue, reprioritize, activate, expire, retry, or change daily limits/carry-over behavior.
- **Wallet gate: PASS** - Reporting reads stored delivery and append-only wallet/ledger evidence only. It does not debit, credit, reserve, release, charge, earn, refund, withdraw, correct, or recalculate historical fees.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/company-reporting-api.yaml](./contracts/company-reporting-api.yaml), and [quickstart.md](./quickstart.md) keep source projections in repositories, orchestration in services, and HTTP mapping in APIs.
- **Controller gate: PASS** - Contracts require only transport, company authorization, query binding, and envelope shaping at controller level.
- **Data gate: PASS** - Design uses repository read models for live source-data reporting and safe discrepancy persistence without exposing `DbContext` to services/controllers.
- **Security gate: PASS** - Public doctor identifiers, company ownership filters, approved-company role gates, and non-disclosing errors are explicit and testable.
- **API contract gate: PASS** - Contract covers success, validation, unauthorized/forbidden/not-found, discrepancy conflict/unavailable, and standard envelope schemas.
- **Scope gate: PASS** - Research and design preserve all Phase 10 exclusions, including no stored reporting aggregate maintenance.
- **Queue gate: PASS** - No artifact changes queue state, injection, expiry, ordering, or retry rules.
- **Wallet gate: PASS** - Monetary totals are live source-data reads; discrepancy handling records safe evidence but never repairs or mutates financial source records.

## Project Structure

### Documentation (this feature)

```text
specs/012-company-reporting-analytics/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── company-reporting-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks; not created here
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Campaigns/
│   ├── Messaging/
│   ├── Policies/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Campaigns/
    ├── Messaging/
    ├── Policies/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   ├── Messaging/
│   └── Policies/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Campaigns/
│   ├── Messaging/
│   ├── Policies/
│   └── Wallets/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   └── Campaigns/
├── Interfaces/
├── Services/
└── Validators/
    └── Campaigns/

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

**Structure Decision**: Extend the existing four-project backend and the existing company campaign route group. Add reporting DTOs/services/repository read models under current Campaigns/Messaging/Wallets boundaries. Add no new project and no background worker.

## Resolved Implementation Decisions

- Extend `GET /api/company/campaigns` to return reporting-enhanced `CompanyCampaignReportSummary` items when Phase 10 fields are available; preserve existing status/page query behavior and add `fromDateEgypt`/`toDateEgypt` filters capped at 90 inclusive delivery business days.
- Add `GET /api/company/campaigns/{campaignId}/deliveries` for delivery-level reporting with filters for `status`, `fromDateEgypt`, `toDateEgypt`, `doctorSpecialization`, `doctorLocation`, and `state` (`Read`, `Unread`, `Interacted`, `Uninteracted`), plus standard pagination.
- Add `GET /api/company/campaigns/{campaignId}/feedback` for feedback-bearing interacted deliveries, with filters for `outcome`, date range, `feedbackEligibility`, doctor specialization, and doctor location.
- Add `GET /api/company/campaigns/{campaignId}/analytics` for one-campaign counts, rates, reserved amount, charged spend, doctor earnings, and platform fee.
- Reuse `AuthorizationPolicies.Phase5CompanyCampaignAccess` or introduce an alias such as `Phase10CompanyReportingAccess` with the same approved Company role/profile requirements if the codebase benefits from a clearer policy name. Either way, access must require an approved Company account.
- Resolve the actor user id to the active company profile inside service flow, then scope every campaign, delivery, feedback, wallet transaction, and ledger query by that company id.
- Use `DeliveryDateEgypt` as the only reporting date filter. Reject malformed dates, `fromDateEgypt > toDateEgypt`, and inclusive ranges longer than 90 business days before performing source-data projections.
- Resolve omitted date filters before repository queries: when both bounds are omitted, use the latest 90 inclusive delivery Egypt business days ending on the current Egypt business date; when only `toDateEgypt` is provided, set `fromDateEgypt = toDateEgypt - 89 days`; when only `fromDateEgypt` is provided, set `toDateEgypt` to the earlier of `fromDateEgypt + 89 days` or the current Egypt business date.
- Use standard `PageNumber`/`PageSize`, default page size 20, maximum page size 100. Delivery pages order by `DeliveryDateEgypt DESC`, `DeliveredAtUtc DESC`, then stable delivery id. Feedback pages order by `FeedbackCreatedAtUtc DESC`, then stable delivery id. Campaign summaries order by calculated campaign activity timestamp descending, then campaign id.
- Calculate campaign activity timestamp as the greatest non-null timestamp among latest delivery `DeliveredAtUtc`, latest `ReadAtUtc`, latest `InteractedAtUtc`, latest `FeedbackCreatedAtUtc`, latest campaign review time, `SubmittedAtUtc`, and campaign creation time for the campaign. Ignore absent values.
- Compute all report totals live from `DoctorAdDelivery`, `DeliveryInteraction`, `WalletTransaction`, and ledger/source evidence. Stored reporting aggregate tables, cached counters, or derived read models are not authoritative and are not introduced in Phase 10.
- Reconciliation compares delivery state/snapshot totals against append-only financial evidence for the selected campaign and date scope. Accepted and rejected deliveries must reconcile charged spend, doctor earnings, and platform fee. Active unanswered deliveries expose reserved amount separately. Expired deliveries contribute no spend/earn/fee.
- If source evidence disagrees, block only the affected campaign/report scope, return a safe reporting-discrepancy failure envelope, and create safe discrepancy evidence through existing audit event infrastructure or a new minimal `ReportingReconciliationDiscrepancy` entity if existing audit metadata cannot capture the required scope safely.
- Discrepancy evidence must include company id, campaign id, optional date scope, discrepancy category, detected time, safe counts/amount fingerprints where useful, and outcome. It must exclude stack traces, raw idempotency material, wallet internals, doctor private data, and response/request bodies.
- Company-visible doctor context is limited to stable public doctor identifier, specialization, experience band, and location. Doctor names, contacts, verification data, wallet data, and private account data are never included.
- Feedback reports include only non-empty stored feedback. Short feedback remains visible and marked as not score-eligible when applicable.
- Rates use decimal percentages with zero-denominator behavior: return `0` and a `HasEligibleRecords`/denominator flag rather than null or divide-by-zero errors.
- Keep reporting read-only except discrepancy evidence creation. Do not mutate campaigns, deliveries, interactions, wallets, wallet transactions, ledger entries, queues, job records, activity scores, settlement state, or stored aggregates.
- Add repository projections/index guidance for `(CompanyId, CampaignId, DeliveryDateEgypt, Status, DeliveredAtUtc, Id)`, feedback filtering by `FeedbackCreatedAtUtc`, and wallet transaction/evidence joins by related delivery/campaign where existing indexes are insufficient.
- Unit tests cover date-window validation, public-doctor summary shaping, rate calculations including zero denominator, live total aggregation rules, discrepancy classification, and privacy exclusions.
- Contract tests cover routes, query validation, envelopes, approved-company role gates, non-disclosing cross-company access, pagination metadata, and no doctor private fields.
- SQL Server integration tests cover source-data reconciliation, active/expired/accepted/rejected monetary totals, feedback filtering, 90-day rejection, soft-delete ownership behavior, discrepancy evidence creation, and read-only mutation audits.
- Performance-profile tests seed 10,000 deliveries inside a 90-day window and measure summary, deliveries, feedback, and analytics requests against the 2-second p95 target.

## Performance Test Profile

- Run Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers or the project standard SQL Server test database on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, and no debugger or coverage collector.
- Seed one approved company, one owned campaign, 10,000 deliveries inside an inclusive 90-day `DeliveryDateEgypt` range, representative accepted/rejected/expired/active states, feedback rows, wallet transactions, and ledger evidence.
- Warm with 10 requests per endpoint, then measure 200 requests per endpoint at concurrency 10 for campaign summaries, delivery list, feedback list, and analytics.
- Require p95 under 2 seconds, zero failed valid requests, zero source-evidence reconciliation false positives, and stable bounded query counts. Report p50, p95, p99, query count, row count, discrepancies, validation failures, and authorization failures.
- Performance tests are opt-in outside the designated CI/performance profile. A skipped run must report unmet prerequisites rather than count as passing evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves live source-data reporting, date-window validation, company ownership scoping, doctor privacy, reconciliation discrepancy handling, pagination/order, feedback visibility, zero-denominator rates, and performance profile. No unresolved clarification markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines reporting read models, source entities, validation rules, reconciliation behavior, discrepancy evidence, indexes, and state/money interpretation.
- [contracts/company-reporting-api.yaml](./contracts/company-reporting-api.yaml) defines the company campaign reporting HTTP contracts.
- [quickstart.md](./quickstart.md) provides setup, migration, smoke, validation, reconciliation, privacy, read-only, and performance checks.
