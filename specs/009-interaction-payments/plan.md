# Implementation Plan: Phase 8 Interaction & Payments

**Branch**: `[009-interaction-payments]` | **Date**: 2026-07-10 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/009-interaction-payments/spec.md`

## Summary

Deliver doctor read tracking and exactly-once pay-on-interaction settlement for Active current-day deliveries. Extend the existing Doctor messages surface with `PUT /api/doctor/messages/{deliveryId}/read` and `POST /api/doctor/messages/{deliveryId}/interact`, keeping controllers HTTP-only while `MediBridge.Services` owns doctor resolution, Egypt-date eligibility, read idempotency, feedback normalization, and financial settlement. Settlement starts only from existing Active/Reserved deliveries created by Phase 7, requires a normalized `Idempotency-Key` header, transitions the delivery to Accepted or Rejected, charges the company reserved balance, credits the doctor available balance, and writes balanced append-only Charge/Earn ledger evidence in one repository-backed Unit of Work transaction. Read tracking remains non-financial and does not gate interaction.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, Entity Framework Core 8 SQL Server, existing API envelope/exception/correlation middleware, existing `IEgyptBusinessClock`, current-user/identity/profile services, delivery repositories, wallet repositories, audit logging, OpenAPI `RequireIdempotencyKey` support, and rate limiting  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository` behind Repository + Unit of Work abstractions; delivery, wallet, wallet transaction, ledger, and audit writes remain inside `IDomainUnitOfWork` transactions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, SQL Server-backed integration tests, and existing unit/contract/integration projects  
**Target Platform**: ASP.NET Core HTTP APIs on the server-hosted MediBridge runtime  
**Project Type**: Layered web service using the existing Onion Architecture  
**Performance Goals**: At least 95% of measured eligible read and interaction requests complete within 1 second under the reproducible backend test profile; settlement uses bounded point lookups and creates no unbounded delivery, wallet, or asset queries  
**Constraints**: DST-aware `Africa/Cairo` current business date; Doctor JWT ownership; read endpoint mutates only `ReadAtUtc`; interaction does not require prior read and never changes `ReadAtUtc`; required `Idempotency-Key` header trimmed and validated to 8-128 characters; Accept and Reject share the same billable settlement; feedback optional, trimmed, empty-after-trim treated as absent, and capped at 1,000 characters; EGP two-decimal stored monetary values; candidate-level atomicity; deterministic Charge/Earn idempotency keys; no raw EF Core outside Repository; no Phase 9+ enforcement/scoring/reporting/notification/admin payout behavior  
**Scale/Scope**: Two doctor endpoints, request/response DTOs, delivery entity methods, repository methods for read and settlement locking/replay, wallet transaction key extensions, service orchestration, OpenAPI contract, focused migration if required for feedback length/index/constraints, and unit/contract/integration/performance-oriented tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Delivery state rules and key helpers stay in `MediBridge.Core`; SQL Server locking and persistence remain in `MediBridge.Repository`; read/settlement orchestration remains in `MediBridge.Services`; HTTP route, authorization, rate limiting, and envelope mapping remain in `MediBridge.APIs`.
- **Controller gate: PASS** - `DoctorMessagesController` accepts route/body/header data, resolves the authenticated user id, and delegates all read, eligibility, idempotency, feedback, wallet, and audit decisions to services.
- **Data gate: PASS** - Delivery, wallet, transaction, ledger, and audit writes use Repository + Unit of Work contracts; no controller or service uses EF Core infrastructure directly.
- **Security gate: PASS** - Endpoints require Doctor JWT authorization, active approved doctor resolution, authenticated-owner scoping, safe not-found/forbidden behavior, and the existing doctor interaction rate-limit category.
- **API contract gate: PASS** - All success and failure responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; global exception handling remains authoritative and redacts sensitive details.
- **Scope gate: PASS** - The plan excludes expiry release, queue activation, weekly enforcement, activity scoring, notifications, company reporting/feedback listings, withdrawal approval, admin payout tooling, and any direct pricing-policy recalculation at interaction time.
- **Queue gate: PASS** - Phase 8 does not create, reorder, cancel, activate, or carry over queue rows. It consumes only existing Active deliveries produced by Phase 7.
- **Wallet gate: PASS** - Accept/Reject settlement debits company Reserved by the delivery reserved amount, credits doctor Available by stored doctor earnings, treats the platform fee as the retained difference, writes one Charge and one Earn transaction with ledger evidence, and commits atomically under deterministic idempotency.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml), and [quickstart.md](./quickstart.md) keep domain contracts inward and persistence/HTTP concerns at their existing boundaries.
- **Controller gate: PASS** - The HTTP contract adds only read and interact transport endpoints; service use cases own state, wallet, feedback, and replay behavior.
- **Data gate: PASS** - The data model defines delivery locking, replay checks, deterministic keys, wallet lock order, ledger shape, feedback validation, and optional migration changes without exposing `DbContext` outside Repository.
- **Security gate: PASS** - Contract and quickstart require Doctor bearer auth, owner scoping, idempotency-key validation, safe denial of cross-role/cross-doctor attempts, and interaction-specific rate limiting.
- **API contract gate: PASS** - Read, interact, replay, validation, conflict, authorization, rate-limit, and consistency failure responses preserve the standard envelope and safe error behavior.
- **Scope gate: PASS** - Research and design preserve Phase 8 boundaries and intentionally omit reporting, feedback listings, scoring, notifications, and payout/admin workflows.
- **Queue gate: PASS** - The model only reads delivery state; queue behavior remains inherited from earlier phases with no Phase 8 mutation.
- **Wallet gate: PASS** - The model defines exact Charge/Earn debit-credit effects, deterministic operation keys, transaction replay classification, lock order, and rollback behavior.

## Project Structure

### Documentation (this feature)

```text
specs/009-interaction-payments/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── interaction-payments-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks; not created here
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Messaging/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Messaging/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   └── Messaging/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Messaging/
│   └── Wallets/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   └── Messaging/
├── Interfaces/
├── Services/
└── Validators/
    └── Messaging/

MediBridge.APIs/
├── Config/
├── Controllers/
├── OpenApi/
├── Security/
└── Program.cs

tests/
├── contract/MediBridge.ContractTests/
├── integration/MediBridge.IntegrationTests/
└── unit/MediBridge.UnitTests/
```

**Structure Decision**: Extend the existing four projects and the current `DoctorMessagesController`/`IDoctorMessageService` surface. Add no new worker or reporting project. Use the existing doctor message read model for inbox display and add focused write models and DTOs for read tracking and interaction settlement.

## Resolved Implementation Decisions

- Reuse the existing `DoctorMessagesController` route prefix. Add `PUT /api/doctor/messages/{deliveryId}/read` for read tracking and `POST /api/doctor/messages/{deliveryId}/interact` for Accept/Reject settlement.
- Keep the class-level Doctor authorization policy or update it to a Phase 8 doctor-message policy only if necessary; attach `RateLimitPolicyNames.Phase7DoctorMessagesRead` to read retrieval/read tracking and `RateLimitPolicyNames.DoctorInteraction` to interaction settlement.
- Mark the interact action with `[RequireIdempotencyKey]` and read `Idempotency-Key` from the request header. Normalize by trimming, require length 8-128, and reject missing/invalid keys before any settlement transaction begins.
- Keep read tracking naturally idempotent by authenticated Doctor, delivery, current Egypt date, and first non-null `ReadAtUtc`. It does not require an idempotency key and does not create wallet or settlement records.
- Add `DoctorAdDelivery.MarkRead(readAtUtc)` that sets `ReadAtUtc` only when it is null, validates UTC, leaves financial state untouched, and updates `UpdatedAtUtc` only on the first read.
- Add `DoctorAdDelivery.MarkInteracted(decision, interactedAtUtc, feedbackText)` that requires Active/Reserved state, sets Accepted or Rejected, records `InteractedAtUtc`, optional normalized feedback and `FeedbackCreatedAtUtc`, sets `ReservationStatus = Charged`, leaves `ReadAtUtc` unchanged, and updates `UpdatedAtUtc`.
- Add deterministic operation keys `delivery:charge:{deliveryId}` and `delivery:earn:{deliveryId}` beside the existing Reserve/Release helpers. These keys are used for wallet transaction uniqueness, while the client `Idempotency-Key` is stored with or linked by the interaction operation record/evidence so conflicting replays can be classified by Doctor + delivery + key + payload.
- Introduce a required `DeliveryInteractionOperation` evidence model for client replay classification. It records Doctor id, delivery id, normalized `Idempotency-Key`, decision, stored normalized feedback text, outcome, timestamps, and linked Charge/Earn transaction ids. It stores no secrets and is unique by `(DoctorId, DeliveryId, IdempotencyKey)`.
- Settlement replay behavior: same Doctor + delivery + `Idempotency-Key` + same decision + same normalized feedback returns the existing outcome; same scope with different decision or feedback returns a conflict; a different idempotency key after the delivery is already Accepted/Rejected returns the settled result only when the decision matches and otherwise returns conflict without new financial effects.
- Settlement candidate transaction lock order is delivery, company wallet, doctor wallet, interaction operation/evidence, then transaction/ledger writes. Every lock is followed by state rechecks.
- Charge movement: company wallet `ReservedBalance -= ReservedAmount`; one company `WalletTransaction` of type Charge with amount `ReservedAmount`, `RelatedDeliveryId`, and idempotency key `delivery:charge:{deliveryId}`; one company ledger entry debits Reserved.
- Earn movement: doctor wallet `AvailableBalance += DoctorEarnings`; one doctor `WalletTransaction` of type Earn with amount `DoctorEarnings`, `RelatedDeliveryId`, and idempotency key `delivery:earn:{deliveryId}`; one doctor ledger entry credits Available.
- Validate stored activation snapshots before settlement: `ReservedAmount == PricePerMessageSnapshot`, both positive; `PlatformFeeAmount + DoctorEarnings == PricePerMessageSnapshot`; `DoctorEarnings > 0`; company reserved balance covers the reserved amount; doctor wallet exists and is active; all values are two-decimal EGP.
- Feedback normalization is service-level: null or empty after trim becomes absent; trimmed non-empty text must be <= 1,000 characters before any transaction mutation. Feedback quality review remains unset or Pending only if an existing enum convention requires it; review/reporting is later scope.
- Use one captured Egypt business-clock snapshot per request. Read and interact require `DeliveryDateEgypt == snapshot.BusinessDateEgypt`; prior-day still-Active deliveries are not settled by doctor actions and remain expiry-job responsibility.
- Keep response DTOs small and non-financial beyond the doctor-facing settlement result: delivery id, status, interacted/read timestamps, feedback presence/text as appropriate, and idempotency status (`Created` or `Replayed`). Do not return wallet balances, platform fee internals, raw idempotency material, stack traces, or storage keys.
- Add audit events for first read, read replay, successful interaction, same-key replay, conflict replay, authorization denial where supported, and financial consistency failures. Audit metadata contains safe ids and outcome categories but not wallet balances or raw idempotency material.
- Add repository support for read tracking (`FindCurrentOwnedForReadForUpdateAsync` or equivalent), interaction locking/replay, and any needed indexes such as `(DoctorId, DeliveryDateEgypt, Status, Id)` and `(DoctorId, DeliveryDateEgypt, ReadAtUtc)` if current indexes are insufficient for bounded point reads.
- If the persisted `FeedbackText` column currently allows 4,000 characters, add or update configuration/check constraints so Phase 8 enforces a 1,000-character maximum at the validation layer and, where practical, at the database boundary without data loss.

## Performance Test Profile

- Run Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers or a local SQL Server instance on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, no debugger, no coverage collector, and no parallel non-test workload.
- Read/interact profile: seed 1,000 Active current-day deliveries distributed across at least 100 doctors with matching company and doctor wallets. Perform 20 warm-up read requests and 20 warm-up interaction requests, then 200 measured eligible read requests and 200 measured eligible interaction requests at concurrency 10. Require at least 95% within 1 second, zero failed eligible requests, no duplicate financial effects, and bounded query counts.
- Replay/concurrency profile: issue concurrent same-key same-payload interactions and same-key conflicting-payload interactions against seeded deliveries. Require one final settlement, deterministic replay/conflict responses, and no duplicate Charge/Earn ledger effects.
- Performance tests are opt-in outside the designated CI profile. A skipped run must report which prerequisite was unavailable; a skip is not passing performance evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves doctor-message endpoint placement, read idempotency, interaction idempotency-key scope, settlement replay classification, feedback normalization, current-day eligibility, wallet transaction shape, operation keys, transaction lock order, audit redaction, and performance measurement. No `NEEDS CLARIFICATION` markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines reused and changed entities, read tracking, interaction operation evidence, settlement state transitions, deterministic keys, lock order, indexes, validation, and atomic actions.
- [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml) defines Doctor read and interaction contracts, request/response envelopes, idempotency header requirements, and safe failure responses.
- [quickstart.md](./quickstart.md) provides migration/configuration expectations, smoke scenarios, negative tests, concurrency/idempotency checks, performance profile, and done criteria.
