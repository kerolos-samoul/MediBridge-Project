# Implementation Plan: Phase 6 Campaign Review & Moderation

**Branch**: `[007-campaign-review-moderation]` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/007-campaign-review-moderation/spec.md`

## Summary

Deliver the backend moderation gate that lets admins list and inspect submitted pharmaceutical campaigns, approve compliant campaigns, reject final campaigns, return fixable campaigns for revision, and preserve an auditable review history. Entering moderation requires campaign text, a submitted target snapshot, and at least one active Pending/Approved campaign media asset; approval additionally requires at least one separately Approved media asset. Submission and revision resubmission validate affordability and snapshot targets without mutating wallets. Approval creates deterministic pending queue rows exactly once and never performs wallet reservation, delivery activation, expiry, or settlement. The implementation approach extends the existing campaign, stored-file, campaign-review-history, and doctor-message-queue slices across the established Onion architecture: domain contracts in `MediBridge.Core`, SQL Server EF Core repositories in `MediBridge.Repository`, review orchestration in `MediBridge.Services`, and HTTP-only controllers in `MediBridge.APIs`.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer auth, authorization policies, Swagger/OpenAPI, Entity Framework Core SQL Server, existing API envelope/exception/correlation middleware, existing identity approval, campaign, file, audit, and queue abstractions  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; reuse and extend campaign, campaign target, campaign review history, stored file, doctor message queue, and audit event persistence behind Repository + Unit of Work abstractions  
**Testing**: `dotnet test` across contract, integration, and unit projects; add contract tests for moderation endpoint envelopes, integration tests for review lifecycle/queue creation/authorization, and unit tests for transition policy and idempotency behavior  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: A warmed pending-moderation query returns a normal page of 20 submitted campaigns within 1 second in local/integration fixtures without N+1 campaign/file/target queries; admins can complete a normal campaign review decision in under 2 minutes when the submitted review package is complete  
**Constraints**: Controllers remain HTTP-only; services own campaign editing, submission, moderation decisions, transition validation, audit recording, protected content lookup, and queue creation; secured routes require Admin or owning-company authorization; every response uses the standard envelope; rejected campaigns are final; revision-required campaigns are editable and resubmittable; campaign submission accepts active Pending/Approved media while campaign approval depends on separately Approved media and does not approve media itself; submission/resubmission validate wallet affordability but no wallet mutation, delivery activation, delivery expiry, daily-limit enforcement, company charge, doctor credit, payout, or settlement occurs in this feature  
**Scale/Scope**: Focused backend/API feature over existing campaign workflow foundations; includes company campaign update for Draft/RevisionRequired, non-financial submission/resubmission, admin pending list, admin review detail with short-lived signed file access, admin review decision, company review outcome, review history, deterministic queue creation on approval, stale/concurrent decision handling, and tests. Excludes asset-review implementation except as a separately callable prerequisite gate, delivery jobs, doctor interaction, wallet reservation/settlement, reporting dashboards, global authorization-denial auditing, and real compliance scanning.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Domain entities/enums/interfaces remain in `MediBridge.Core`, SQL Server EF Core persistence remains in `MediBridge.Repository`, moderation orchestration remains in `MediBridge.Services`, and HTTP endpoints remain in `MediBridge.APIs`.
- Controller gate: PASS - Controllers expose request/response surfaces only and delegate pending-list, detail, decision, queue, audit, and ownership behavior to services.
- Data gate: PASS - Campaign, target, review history, file, queue, and audit persistence is reached through Repository + Unit of Work contracts; no controller uses EF Core directly.
- Security gate: PASS - Admin moderation routes require Admin authorization; company review-outcome routes require owning-company authorization; doctors have no moderation access.
- API contract gate: PASS - All planned responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` and errors remain safely mapped through existing middleware/exception paths.
- Scope gate: PASS - Spec and plan explicitly make submission/resubmission affordability-only and exclude wallet mutation, delivery activation, daily limits, expiry, interactions, company charge, doctor earnings, payout, and asset approval as part of campaign approval.
- Queue gate: PASS - Approval creates at most one pending queue row per submitted target doctor, ordered by submitted campaign time, queue creation time, and stable identifier. Daily limits and expiry are out of scope.
- Wallet gate: PASS - Submission, revision resubmission, and moderation perform no debit, credit, reservation, release, charge, earn, fee, payout, wallet transaction, or wallet ledger mutation.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - [data-model.md](./data-model.md), [contracts/campaign-review-moderation-api.yaml](./contracts/campaign-review-moderation-api.yaml), and [quickstart.md](./quickstart.md) keep domain contracts, persistence, services, and HTTP concerns separated.
- Controller gate: PASS - Contracts describe endpoint behavior and envelopes only; business decisions stay in service use cases.
- Data gate: PASS - Data model identifies reused records, needed status expansion, query methods, idempotency keys, review history fields, and transaction boundaries.
- Security gate: PASS - Contracts and quickstart define JWT role gates, admin-only detail/decision surfaces, and owning-company review outcome visibility.
- API contract gate: PASS - OpenAPI contract uses the standard envelope for success, validation, authorization, not-found, and conflict responses.
- Scope gate: PASS - Design artifacts preserve the no-wallet/no-delivery/no-settlement boundary, define active reviewable media at submission, and identify Approved media as an external prerequisite for campaign approval.
- Queue gate: PASS - Design artifacts define approval-triggered queue creation, duplicate prevention, FIFO ordering, and retry behavior without adding delivery activation semantics.
- Wallet gate: PASS - Design artifacts explicitly state submission, resubmission, and moderation have no wallet transaction or ledger effects.

## Project Structure

### Documentation (this feature)

```text
specs/007-campaign-review-moderation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── campaign-review-moderation-api.yaml
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
│   └── Messaging/
├── Enums/
└── Interfaces/
    ├── Campaigns/
    ├── Files/
    └── Messaging/

MediBridge.Repository/
├── Configurations/
│   ├── Campaigns/
│   ├── Files/
│   └── Messaging/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Campaigns/
│   ├── Files/
│   └── Messaging/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   └── Campaigns/
├── Interfaces/
├── Services/
└── Validators/

MediBridge.APIs/
├── Controllers/
│   ├── AdminCampaignsController.cs
│   └── CampaignsController.cs
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

**Structure Decision**: Use the existing four-project Onion structure. Extend existing campaign, stored-file, review-history, queue, and audit abstractions rather than introducing a new project or direct persistence path. Add DTOs/services/controllers only for the moderation surfaces and test them in the existing contract, integration, and unit projects.

## Resolved Implementation Decisions

- Add nullable `Campaign.SubmittedAtUtc` and persist it through an EF Core migration together with campaign-review prior/resulting status fields. Do not use `UpdatedAtUtc` as a submission-order key.
- Add append-only `CampaignSubmissionAttempt` persistence with a unique `(CampaignId, IdempotencyKey)` index. It replaces the current misuse of Reserve wallet transactions as submission replay evidence and stores submitted time, target count, estimated cost, and currency.
- Add `UpdateCampaignRequestDto`, its validator, `ICampaignWorkflowService.UpdateCampaignAsync`, and `PUT /api/company/campaigns/{campaignId}`. The update operation changes title, description, and optional clinical research information only for an owned Draft or RevisionRequired campaign.
- Change asset upload, replacement, and deletion state checks to allow Draft or RevisionRequired and to reject every other campaign state. Rejected remains terminal.
- Change submission/resubmission to require campaign text, at least one active Pending/Approved campaign media asset, and at least one eligible target. Refresh target snapshots, validate company-wallet affordability, set `SubmittedAtUtc`, and move to PendingReview without changing wallet balances or adding wallet transactions/ledger entries.
- Keep asset approval separate. `ReviewCampaignAsync` requires at least one active Approved campaign media asset before an Approved decision, but it never changes asset review status.
- Reuse `IFileAccessService.GetSignedAccessAsync` from admin review detail mapping so each returned `ReviewFileDto` contains `accessUrl` and `accessExpiresAtUtc`; map provider outages to the existing safe 503 envelope.
- Implement pending-list projection as a bounded repository query ordered by `SubmittedAtUtc`, then campaign id, with target count and approved-media readiness projected without per-row repository calls.
- Keep ASP.NET authorization-policy denials outside service audit creation because those requests never reach the service. Audit completed decisions, replays, and service-detected conflicts only.
- Update or replace all inherited Phase 5 tests that assert reserve-on-submission, release-on-rejection/revision, Draft mapping for ChangesRequested, or approval without an approved active media asset.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

Research decisions are captured in [research.md](./research.md). All planning unknowns are resolved: pending-list/detail scope, active reviewable media at submission, separately Approved media at campaign approval, final rejection lifecycle, explicit revision editing, revision-required resubmission, non-financial submission/moderation, append-only submission idempotency, explicit submission timestamps, protected signed access, campaign review idempotency, queue creation ordering, ownership/security, and authorization-audit scope.

## Phase 1 Design Summary

Design artifacts are complete:

- [data-model.md](./data-model.md) defines reused entities, required fields, validation rules, status transitions, indexes, and atomic business actions.
- [contracts/campaign-review-moderation-api.yaml](./contracts/campaign-review-moderation-api.yaml) defines the secured HTTP moderation contract.
- [quickstart.md](./quickstart.md) defines smoke and negative validation steps for the moderation workflow.
