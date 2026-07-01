# Implementation Plan: Wallet and Campaign Workflow

**Branch**: `[006-wallet-campaign-workflow]` | **Date**: 2026-06-19 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/006-wallet-campaign-workflow/spec.md`

## Summary

Deliver the missing public HTTP workflow that lets approved pharmaceutical companies fund their wallets with a mock payment checkout, create draft campaigns, upload campaign assets, block submission until asset approval, and then submit campaigns with approved assets and funded target snapshots after the pricing, wallet, and review prerequisites exist. Admins can set doctor pricing, review assets/campaigns, and verify created queue rows. The implementation approach builds on the existing Phase 3 domain records for wallets, campaigns, files, policies, and queues; adds a small internal mock payment record; keeps business orchestration in `MediBridge.Services`; keeps controllers HTTP-only in `MediBridge.APIs`; and persists all authoritative state through SQL Server EF Core repositories and unit-of-work boundaries in `MediBridge.Repository`.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer auth, authorization policies, Swagger/OpenAPI, Entity Framework Core SQL Server, existing API envelope/exception/correlation middleware, existing Phase 2 identity approval, existing Phase 3 wallet/campaign/file/queue/policy repositories  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; reuse Phase 3 tables for wallets, wallet transactions, wallet ledger entries, campaigns, campaign targets, campaign review history, stored files, doctor message queues, policy history, and audit events; add mock payment persistence for company top-ups  
**Testing**: `dotnet test` across contract, integration, and unit projects; add contract tests for the public endpoint surface, integration tests for real HTTP smoke flow, and unit tests for service-level money, idempotency, pricing, and transition rules  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: The full funded campaign smoke workflow completes in under 5 minutes using only public secured requests and normal smoke-test fixtures. This feature does not introduce production SLA, load-test, or per-endpoint performance requirements.  
**Constraints**: No real payment gateway, third-party redirect, webhook, callback endpoint, provider credential, or provider configuration; controllers remain HTTP-only; services own wallet, payment, pricing, campaign, asset review, fund reservation, and queue decisions; all monetary inputs use EGP with at most two decimals; top-up and campaign approval are idempotent; top-up idempotency compares only client-supplied request fields plus company, operation type, and idempotency key; internally generated transaction references are returned and stored but are not replay request inputs; companies see only aggregate queue counts for owned campaigns while admins can inspect doctor-level queue rows; file content scanning and real storage-provider behavior are out of scope  
**Scale/Scope**: Workflow/API feature over existing Phase 2 and Phase 3 foundations; includes company wallet query/top-up mock checkout, campaign draft/assets/submission, admin asset review, admin doctor pricing, admin campaign review, queue creation, admin queue verification, company aggregate queue summary, contracts, and smoke tests; excludes real payment providers, third-party payment pages, webhooks, delivery activation jobs, doctor daily-limit processing, delivered-message expiry, doctor interaction settlement, payouts, reporting read models, and malware scanning

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Domain entities and contracts stay in `MediBridge.Core`, EF Core SQL Server persistence stays in `MediBridge.Repository`, workflow orchestration stays in `MediBridge.Services`, and HTTP endpoints stay in `MediBridge.APIs`.
- Controller gate: PASS - Planned controllers expose authenticated request/response surfaces only and delegate wallet, mock payment, pricing, campaign, review, reservation, and queue decisions to services.
- Data gate: PASS - Existing repository/unit-of-work contracts are reused for Phase 3 records, with a new repository contract only where the mock payment record needs authoritative persistence.
- Security gate: PASS - Company routes require Pharmaceutical Company authorization and ownership checks; admin pricing, review, and doctor-level queue inspection require Admin authorization; doctors cannot manage company wallets or campaigns.
- API contract gate: PASS - All new public responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` and safe errors remain under global exception handling.
- Scope gate: PASS - Spec and plan explicitly exclude real payment integration, redirects, webhooks, callbacks, delivery activation jobs, doctor daily-limit processing, delivered-message expiry, settlement, payouts, reporting read models, and malware scanning.
- Queue gate: PASS - Queue creation, uniqueness, FIFO ordering, campaign expiry/cancellation behavior, and visibility rules are explicit and testable. Delivery activation, doctor daily-limit processing, and delivered-message expiry are explicitly outside this feature.
- Wallet gate: PASS - Wallet creation, top-up credit, mock payment success, idempotency, reservation/release, platform fee use, and atomic payment/transaction/ledger commits are explicit and testable.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/wallet-campaign-workflow-api.yaml`, and `quickstart.md` preserve Onion boundaries and avoid repository or EF Core dependencies from API contracts.
- Controller gate: PASS - Contracts describe endpoint behavior only; controllers remain thin and service-owned use cases handle business decisions.
- Data gate: PASS - Data model identifies reused Phase 3 records, the new mock payment record, indexes/uniqueness needs, state transitions, and unit-of-work boundaries.
- Security gate: PASS - Contract and quickstart define JWT role gates, company ownership limits, admin-only queue row inspection, and company aggregate queue visibility.
- API contract gate: PASS - OpenAPI contract uses the standard envelope for success, validation, authorization, conflict, and not-found responses.
- Scope gate: PASS - Design artifacts retain the no-real-payment and no-delivery-settlement boundaries.
- Queue gate: PASS - Contracts and quickstart validate queue creation exactly once per approved campaign/doctor, FIFO ordering, admin row inspection, and company aggregate counts without adding delivery activation, doctor daily-limit processing, or delivered-message expiry behavior.
- Wallet gate: PASS - Contracts and quickstart validate wallet creation/query, mock payment top-up, idempotency replay/conflict, EGP precision, and atomic payment plus wallet ledger evidence.

## Project Structure

### Documentation (this feature)

```text
specs/006-wallet-campaign-workflow/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── wallet-campaign-workflow-api.yaml
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
│   ├── Messaging/
│   ├── Payments/
│   ├── Policies/
│   ├── Profiles/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Campaigns/
    ├── Files/
    ├── Messaging/
    ├── Payments/
    ├── Policies/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   └── Payments/
├── Data/
├── Migrations/
├── Repositories/
│   └── Payments/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   ├── Campaigns/
│   ├── Payments/
│   ├── Pricing/
│   └── Wallets/
├── Interfaces/
├── Services/
└── Validators/

MediBridge.APIs/
├── Controllers/
│   ├── AdminCampaignsController.cs
│   ├── AdminPricingController.cs
│   ├── CampaignsController.cs
│   └── CompanyWalletController.cs
├── Contracts/
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

**Structure Decision**: Use the existing four-project Onion structure. Add only the new mock payment domain and repository slice needed for top-up demonstration; otherwise extend existing Phase 3 campaign, wallet, file, queue, policy, and audit abstractions. Add services and DTOs for workflow orchestration, API controllers for role-gated HTTP endpoints, and tests in the existing contract/integration/unit projects.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

Research decisions are captured in [research.md](./research.md). All technical unknowns from planning are resolved: mock payment scope, wallet creation strategy, idempotency conflict policy, campaign submission and reservation timing, doctor pricing validation, queue visibility, asset review scope, and atomicity boundaries.

## Phase 1 Design Summary

Design artifacts are complete:

- [data-model.md](./data-model.md) defines reused and new entities, validation rules, relationships, and state transitions.
- [contracts/wallet-campaign-workflow-api.yaml](./contracts/wallet-campaign-workflow-api.yaml) defines the public secured HTTP workflow contract.
- [quickstart.md](./quickstart.md) defines smoke-test and validation steps for the end-to-end workflow.
