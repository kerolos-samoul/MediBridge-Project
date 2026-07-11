# Implementation Plan: Phase 8 Interaction & Payments

**Branch**: `[010-interaction-payments]` | **Date**: 2026-07-11 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/010-interaction-payments/spec.md`

## Summary

Deliver doctor read tracking plus billable Accept/Reject interaction settlement for already Active current Egypt-day deliveries. Extend the existing Phase 7 doctor message surface and delivery/wallet repositories so reading records first-open time with no financial effect, while Accept or Reject atomically moves a delivery to Accepted/Rejected, converts the company reservation to a final Charge, credits the doctor Earn amount, records platform-fee evidence, and writes safe audit evidence. Correctness is enforced with Doctor JWT ownership, the existing doctor interaction rate-limit policy, client `Idempotency-Key` replay/conflict rules, row locks, SQL Server uniqueness constraints, Repository + Unit of Work transactions, and append-only wallet transaction/ledger records.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, ASP.NET Core rate limiting, Entity Framework Core 8.0.11 SQL Server, existing API envelope/exception/correlation middleware, existing Egypt business clock, existing delivery, wallet, audit, and doctor message abstractions  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; all delivery, interaction, wallet, ledger, and audit persistence remains behind Repository + Unit of Work abstractions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, and SQL Server Testcontainers through existing unit, contract, and integration projects  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service using the existing Onion Architecture  
**Performance Goals**: At least 95% of warmed read-tracking and Accept/Reject interaction requests for valid current-day deliveries complete within 1 second under the Phase 8 profile; idempotent replays and throttled attempts must avoid duplicate wallet mutations in 100% of tested retry/concurrency cases  
**Constraints**: DST-aware `Africa/Cairo` current-day eligibility; Doctor JWT ownership only; read tracking has no financial effect; Accept and Reject are both billable; interaction requests require client `Idempotency-Key`; raw idempotency keys are transient request input only and are never stored, logged, audited, or returned; feedback is optional, trimmed, capped at 2,000 characters, and unsafe/over-limit feedback blocks settlement; same-key/different-content requests conflict; successful settlements, idempotency conflicts, and reservation/snapshot anomalies create safe audit evidence; both read and interact actions must use `RateLimitPolicyNames.DoctorInteraction`; no raw EF Core outside Repository; no changes to Phase 7 injection/expiry, weekly enforcement, activity scoring, withdrawal payout, company analytics, admin correction, or external payment gateway behavior  
**Scale/Scope**: Extend the existing Doctor messages controller/service, delivery repository, wallet transaction key helpers, wallet/ledger repositories, audit repository usage, DTO/validator set, rate-limit attachment, migration/model snapshot, and unit/contract/integration/performance-oriented tests for read tracking and interaction settlement

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Delivery state, operation keys, repository contracts, and domain enums remain in `MediBridge.Core`; SQL Server/EF Core locking and persistence remain in `MediBridge.Repository`; settlement orchestration remains in `MediBridge.Services`; HTTP/rate-limit wiring remains in `MediBridge.APIs`.
- **Controller gate: PASS** - `DoctorMessagesController` will map HTTP requests, current user, idempotency header, and envelopes only. It will not contain delivery eligibility, feedback validation, wallet mutation, or persistence logic.
- **Data gate: PASS** - Read tracking, interaction, wallet, ledger, idempotency, and audit writes use Repository + Unit of Work contracts. Services do not take EF Core dependencies.
- **Security gate: PASS** - Both read tracking and interaction require Doctor JWT authorization, ownership checks, and doctor interaction rate limiting. Cross-doctor and non-doctor access fail without protected delivery disclosure.
- **API contract gate: PASS** - New endpoints use the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope and existing global exception middleware.
- **Scope gate: PASS** - The plan excludes delivery injection, expiry release, weekly enforcement, activity-score jobs, withdrawals, company reporting/analytics, admin correction tooling, notifications, and payment-gateway capture.
- **Queue gate: PASS** - Phase 8 consumes only deliveries already activated by Phase 7. It does not alter queue order, daily limits, expiry, carry-over, or injection behavior.
- **Wallet gate: PASS** - Read tracking has no wallet effect. Interaction debits company Reserved through Charge, credits Doctor Available through Earn, records platform-fee evidence, and commits every delivery/feedback/wallet/ledger/audit mutation atomically with operation-level idempotency.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml), and [quickstart.md](./quickstart.md) keep EF Core in Repository, business orchestration in Services, and HTTP details in APIs.
- **Controller gate: PASS** - The contract requires only transport mapping, role/rate-limit attributes, envelope shaping, and idempotency header forwarding.
- **Data gate: PASS** - The model defines repository methods, row locks, unique interaction idempotency scope, transaction/ledger evidence, and migration/index changes without exposing `DbContext` to controllers or services.
- **Security gate: PASS** - Doctor JWT, owner scoping, current-day checks, idempotency conflict handling, feedback validation, and throttling are all explicit and testable.
- **API contract gate: PASS** - Success, validation, idempotency conflict, authorization, rate-limit, and anomaly failures all retain safe standard envelopes.
- **Scope gate: PASS** - Research and design preserve every Phase 9+ and reporting/admin/payment-gateway exclusion.
- **Queue gate: PASS** - No Phase 8 artifact changes queue state or delivery activation/expiry semantics.
- **Wallet gate: PASS** - Charge/Earn/platform-fee effects, deterministic keys, replay behavior, audit evidence, lock order, and rollback behavior are defined.

## Project Structure

### Documentation (this feature)

```text
specs/010-interaction-payments/
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
│   ├── Policies/
│   └── Wallets/
├── Enums/
└── Interfaces/
    ├── Messaging/
    ├── Policies/
    └── Wallets/

MediBridge.Repository/
├── Configurations/
│   ├── Messaging/
│   └── Wallets/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Messaging/
│   ├── Policies/
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
├── Security/
└── Program.cs

tests/
├── contract/MediBridge.ContractTests/
├── integration/MediBridge.IntegrationTests/
└── unit/MediBridge.UnitTests/
```

**Structure Decision**: Extend the existing four projects and the existing `DoctorMessagesController`/`DoctorMessageService` surface. Add no new application project and no background worker. Phase 8 is request-driven behavior over Phase 7 Active deliveries.

## Resolved Implementation Decisions

- Add `PUT /api/doctor/messages/{deliveryId}/read` to record first read time. The service resolves the approved active Doctor, captures one Cairo business-time snapshot, verifies ownership/current-day visibility, and sets `ReadAtUtc` only when null. Replays preserve the original first-read time.
- Add `POST /api/doctor/messages/{deliveryId}/interact` with required `Idempotency-Key` header and body `{ "Outcome": "Accept" | "Reject", "Feedback": string? }`. The controller forwards actor id, delivery id, idempotency key, and DTO to the service.
- Apply `RateLimitPolicyNames.DoctorInteraction` to both read and interact actions. Throttled requests fail before service mutation and create no delivery, feedback, reservation, wallet, ledger, transaction, or financial audit mutation beyond standard safe request handling.
- Use the existing `IEgyptBusinessClock` capture for current-day checks. A delivery is interactable only when it is Active, Reserved, owned by the authenticated doctor, and `DeliveryDateEgypt` equals the captured Cairo date.
- Add delivery domain methods for `MarkRead(DateTime readAtUtc)` and `MarkInteracted(...)`. `MarkInteracted` changes Active/Reserved to Accepted/Charged or Rejected/Charged, records `InteractedAtUtc`, normalized feedback fields, and `UpdatedAtUtc`; it rejects non-UTC timestamps or invalid current state.
- Feedback normalization trims provided text. Omitted, empty, and whitespace-only feedback are allowed and stored as null/empty per existing model convention. Feedback is plain text only: ordinary letters, numbers, whitespace, line breaks, and punctuation are allowed except markup delimiters. Feedback longer than 2,000 characters after trimming, containing `<` or `>`, containing case-insensitive encoded angle brackets `&lt;` or `&gt;`, containing Markdown links/images such as `[text](url)` or `![alt](url)`, or containing `javascript:` or `data:` URI schemes is rejected before settlement and creates no mutation. Feedback with at least 15 non-whitespace characters is marked eligible for later feedback-score credit; shorter stored feedback remains ineligible.
- Introduce deterministic financial operation keys `delivery:charge:{deliveryId}` and `delivery:earn:{deliveryId}`. Preserve existing Reserve and Release keys.
- Use the client `Idempotency-Key` as transient interaction request input, immediately normalize it with Doctor id, delivery id, outcome, and normalized feedback into non-sensitive hashes/fingerprints, and persist only those hashes/fingerprints. The raw key must never be stored, logged, audited, returned, or used in task evidence. Same key + same delivery/outcome/feedback returns the original result. Same key + different delivery/outcome/feedback is a conflict with no mutation. A different key for an already-settled delivery may return the settled result only when it matches the persisted final outcome and feedback; conflicting settled-state requests fail without mutation.
- Add an `InteractionRecord` or equivalent persisted interaction evidence tied one-to-one to the delivery and unique by client idempotency key scope. This record stores only safe key hashes/fingerprints and never raw idempotency key material.
- Lock order for interaction settlement: delivery for update, company wallet for update, doctor wallet for update, idempotency/interaction evidence check, wallet transaction replay checks, then delivery/wallet/transaction/ledger/audit staging. Keep lock order stable to reduce deadlock risk.
- Settlement debits company Reserved by the stored `ReservedAmount`, sets reservation to Charged, writes one Charge transaction, and writes a Reserved Debit ledger entry. The company Available balance is not changed by interaction.
- Settlement credits the Doctor wallet Available by stored `DoctorEarnings`, writes one Earn transaction, and writes an Available Credit ledger entry. If the Doctor wallet is missing but the Doctor is otherwise eligible, create/repair it inside the same transaction only if existing wallet rules already permit auto-create for approved users; otherwise reject with a safe anomaly and no mutation.
- Platform fee evidence is recorded as part of settlement using the stored `PlatformFeeAmount`. It must be queryable for Phase 10 reporting without changing wallet balances unless a later accounting model introduces a platform wallet.
- Settlement validates the existing activation snapshots rather than recalculating policy from current admin settings. It rejects zero/negative/invalid stored price, fee, earning, reserved amount, or mismatched formula with safe audit evidence and no mutation.
- Successful settlements, idempotency conflicts, and reservation/snapshot anomalies create safe audit evidence. Ordinary successful read tracking does not create financial audit records.
- Add repository methods for current-day read lookup/update, interaction lookup by key/fingerprint, active reserved delivery lock with ownership/date, settled delivery replay lookup, and atomic status/reservation transition. Implement row locking only in `MediBridge.Repository`.
- Add a Phase 8 EF Core migration for any new interaction evidence table, delivery indexes needed for current-day read/interact lookups, feedback length/check constraints where appropriate, and any new audit/transaction references. Do not rewrite historical delivery or wallet balances.
- Unit tests cover feedback normalization/validation, operation-key generation, delivery state methods, snapshot validation, idempotency classification, and wallet movement calculations.
- Contract tests cover envelopes, role gates, required `Idempotency-Key`, `RateLimitPolicyNames.DoctorInteraction` on both endpoints, read idempotence including reads after Accepted/Rejected settlement, Accept/Reject responses, feedback validation including allowed plain text and blocked markup patterns, and safe conflict/anomaly errors.
- SQL Server integration tests cover atomic settlement, Charge/Earn ledger evidence, idempotent replay, same-key conflict, concurrent interaction races, read/interact concurrency, throttled no-mutation behavior, current-day boundary checks, authorization ownership, and migration constraints.

## Performance Test Profile

- Run Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, and no debugger, coverage collector, or parallel non-test workload.
- Seed approved Doctors, current-day Active/Reserved deliveries, valid company and doctor wallets, and representative feedback payloads.
- Warm with 20 sequential read and interaction requests, then measure 200 read requests and 200 interaction/replay requests at concurrency 10.
- Require at least 95% of warmed requests to complete within 1 second, zero duplicate financial effects, zero failed valid requests, and bounded query count. Report p50, p95, p99, query count, conflicts, throttles, and failures.
- Performance tests are opt-in outside the designated CI profile. A skipped run must report unmet prerequisites and is not passing performance evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves the read tracking boundary, interaction idempotency contract, settlement lock order, Charge/Earn ledger shape, platform-fee evidence, feedback validation, audit scope, rate-limit scope, current-day eligibility, and performance profile. No unresolved clarification markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines reused and changed entities, new interaction evidence, read/interact state transitions, financial operation keys, validation rules, indexes, lock order, and atomic settlement actions.
- [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml) defines read tracking and Accept/Reject interaction HTTP contracts.
- [quickstart.md](./quickstart.md) provides configuration, migration, smoke, idempotency, feedback, rate-limit, audit, current-day, and verification steps.
