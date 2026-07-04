# Implementation Plan: Phase 7 Delivery & Expiry Jobs

**Branch**: `[008-delivery-expiry-jobs]` | **Date**: 2026-07-02 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/008-delivery-expiry-jobs/spec.md`

## Summary

Deliver the Egypt-day background workflow that expires all overdue unanswered deliveries, releases company reservations exactly once, activates current-day messages per doctor in immutable campaign-submission FIFO order up to the global daily limit, and exposes each authenticated doctor's current-day inbox. Reuse the already referenced Hangfire 1.8.17 SQL Server integration for durable recurring scheduling at 00:00 and 00:05 in DST-aware `Africa/Cairo` time. Add an idempotent startup/recovery coordinator that durably claims missing current-date job dispatches and enqueues the same persisted expiry job first plus an eligible injector continuation without invoking business services or mutating business state at startup. Keep correctness inside service-owned, repository-backed candidate transactions: delivery/queue row locking, doctor and wallet serialization, existing unique constraints, deterministic Reserve/Release idempotency keys, and balanced append-only ledger entries make retries and overlapping workers harmless. Company-scoped injection gating is derived from unresolved overdue Active deliveries, allowing unaffected companies to continue.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, Entity Framework Core 8.0.11 SQL Server, Hangfire.AspNetCore 1.8.17, Hangfire.SqlServer 1.8.17, BCL `TimeProvider`/`TimeZoneInfo`, existing API envelope/exception/correlation middleware, existing campaign/file/queue/delivery/wallet/audit abstractions  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; Hangfire uses its own `HangFire` scheduler schema in the configured SQL Server database, while all MediBridge delivery, wallet, ledger, and job-run records remain behind Repository + Unit of Work abstractions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, and SQL Server Testcontainers through existing unit, contract, and integration projects  
**Target Platform**: Always-running ASP.NET Core server process with one or more Hangfire workers  
**Project Type**: Layered web service using the existing Onion Architecture  
**Performance Goals**: Under the reproducible profile below, at least 190 of 200 measured warmed inbox requests for a 100-item page complete within 1 second; each of three reference cycles covering 1,000 doctors and 10,000 queued candidates completes within 5 minutes; candidate reads use bounded/keyset batches with no N+1 delivery-content queries  
**Constraints**: DST-aware `Africa/Cairo`; expiry scheduled and eligible at 00:00 and injector scheduled and eligible at 00:05 without promising exact worker-start instants; injection never activates before 00:05; startup recovery durably claims missing date/job dispatches and enqueues persisted service-interface jobs without direct business mutation; company-scoped expiry gate; FIFO by immutable `CampaignSubmittedAtUtc`, then queue id, with conditional non-null enforcement for Queued rows and `QueuedAtUtc` retained only as operational enqueue timing; current doctor price and exactly one valid active platform fee snapshot at activation; EGP values at two decimals; candidate-level atomicity; no raw EF Core outside Repository; no public or application-mapped dashboard/manual job-control HTTP surface in Phase 7; operator review/requeue occurs only through infrastructure-managed SQL/Hangfire tooling governed by platform IAM and database permissions; Doctor JWT ownership for inbox, cursor, and asset access; no interaction settlement, activity scoring, weekly enforcement, notifications, or analytics  
**Scale/Scope**: Two recurring jobs, one operational job-run model, one operational recovery-dispatch model, one startup/recovery enqueue coordinator, delivery/queue repository extensions and indexes, Reserve/Release wallet movements, one today-inbox endpoint, one delivery-asset access endpoint, configuration/registration, migration, and unit/contract/integration/performance-oriented tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Business clock, delivery job contracts, read models, and domain state remain in `MediBridge.Core`; SQL Server/EF Core and row locking remain in `MediBridge.Repository`; orchestration remains in `MediBridge.Services`; Hangfire and HTTP wiring remain in `MediBridge.APIs`.
- **Controller gate: PASS** - `DoctorMessagesController` only maps authenticated HTTP requests and envelopes; jobs call service interfaces and contain no queue, pricing, wallet, or persistence logic.
- **Data gate: PASS** - Every delivery, queue, job-run, recovery-dispatch, wallet, transaction, ledger, campaign, profile, and file operation uses Repository + Unit of Work contracts. Hangfire's internal scheduler schema contains no MediBridge domain state; startup coordinates operational dispatch only and performs no direct business mutation.
- **Security gate: PASS** - Inbox, pagination cursor, and delivery-asset access require Doctor JWT authorization and service-level approved-user/doctor ownership checks. No public or application-mapped operational dashboard or manual job endpoint is exposed; operator SQL review and Hangfire requeue are infrastructure-managed and governed by platform IAM/database permissions.
- **API contract gate: PASS** - Both doctor endpoints use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; existing global exception/status handling remains authoritative.
- **Scope gate: PASS** - The plan excludes read/interact state changes, Charge/Earn settlement, weekly enforcement, activity scoring, notifications, reporting, and job administration UI.
- **Queue gate: PASS** - FIFO is immutable `CampaignSubmittedAtUtc ASC, Id ASC`; Queued rows must have an authentic non-null snapshot while terminal historical rows may remain null because they never re-enter Queued; `QueuedAtUtc` is neither a priority nor backfill key; existing current-day deliveries consume the daily limit; terminal rows cancel, temporary and insufficient-fund rows remain queued, skipped rows do not consume capacity, and delivery uniqueness prevents replay.
- **Wallet gate: PASS** - Activation moves Available to Reserved through one Reserve transaction and two balanced ledger entries; expiry reverses it through one Release transaction and two balanced ledger entries; each candidate commits atomically with deterministic idempotency.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/delivery-expiry-api.yaml](./contracts/delivery-expiry-api.yaml), and [quickstart.md](./quickstart.md) preserve inward dependencies and keep Hangfire/EF concerns at their allowed boundaries.
- **Controller gate: PASS** - The HTTP contract exposes retrieval and access-grant transport only; services own doctor resolution, Egypt-date filtering, and delivery/file authorization.
- **Data gate: PASS** - The data model defines repository projections, recovery-dispatch claims, lock order, row versions, conditional queue constraints, indexes, and candidate-sized transaction boundaries without exposing `DbContext` to services or controllers.
- **Security gate: PASS** - Contract and quickstart require Doctor JWT plus current-delivery ownership; opaque inbox cursors are Doctor/date-scoped; signed asset grants are delivery-scoped, current-day only, approved-file only, and short-lived; infrastructure operator access is outside the application HTTP surface.
- **API contract gate: PASS** - Paginated success and all documented cursor, authorization, storage, and rate-limit failures retain the standard envelope and safe error behavior.
- **Scope gate: PASS** - Research and design preserve all Phase 8+ exclusions and intentionally omit Hangfire dashboard/manual-control endpoints.
- **Queue gate: PASS** - The model and research resolve concurrency, daily counts, deterministic scanning, terminal/temporary handling, insufficient funds, retries, and carry-over behavior.
- **Wallet gate: PASS** - The model defines exact Reserve/Release debit-credit pairs, deterministic keys, snapshot rounding, lock order, and rollback behavior.

## Project Structure

### Documentation (this feature)

```text
specs/008-delivery-expiry-jobs/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── delivery-expiry-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks; not created here
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   └── Messaging/
├── Enums/
└── Interfaces/
    ├── Messaging/
    └── Time/

MediBridge.Repository/
├── Configurations/
│   └── Messaging/
├── Data/
├── Migrations/
├── Repositories/
│   └── Messaging/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   └── Messaging/
├── Interfaces/
├── Services/
└── Extensions/

MediBridge.APIs/
├── Config/
├── Controllers/
├── Extensions/
├── Security/
└── Program.cs

tests/
├── contract/MediBridge.ContractTests/
├── integration/MediBridge.IntegrationTests/
└── unit/MediBridge.UnitTests/
```

**Structure Decision**: Extend the four existing projects. Add no worker project: Phase 7 runs Hangfire Server in the always-running API host, with stable service-interface job methods resolved through DI. Keep a later extraction to a separate worker possible because Hangfire persists interface method calls and all business behavior lives outside `MediBridge.APIs`.

## Resolved Implementation Decisions

- Keep the existing Hangfire package versions. Configure `AddHangfire` with SQL Server, compatibility level 1.8, recommended serializer settings, and the configured database connection; add `AddHangfireServer` for a dedicated `delivery` queue. Hangfire owns only its internal `HangFire` schema.
- Register recurring identifiers `medibridge-expiry-cleaner` and `medibridge-daily-injector` idempotently at startup. Use Cairo-local cron schedules `0 0 * * *` and `5 0 * * *` with a DST-aware `TimeZoneInfo` resolved from `Africa/Cairo` and Windows-ID fallback. These are schedule/eligibility instants, not guaranteed worker starts. The injector service returns Deferred without mutation before 00:05 Egypt time. Fail startup if a DST-capable Cairo zone cannot be resolved.
- Do not map the Hangfire dashboard or add manual-control HTTP endpoints in Phase 7. Authorized operators inspect `DeliveryJobRun` through infrastructure-managed read-only SQL access and requeue failed persisted jobs through protected Hangfire administration tooling. Platform IAM and database permissions are the authorization boundary. Automatic and operator-initiated requeues invoke the same persisted service-interface methods.
- Use ordinary `CancellationToken` parameters on both job services. Keep bounded automatic retry for cycle-level transient failures, while recording candidate failures and continuing safe independent work.
- Treat scheduler coordination as an efficiency aid only. Database row locks, row versions, current-state rechecks, unique `(DoctorId, DeliveryDateEgypt, CampaignId)`, and unique `(OperationType, IdempotencyKey)` constraints are the correctness boundary.
- Add an `IEgyptBusinessClock` contract and `EgyptBusinessClock` implementation over `TimeProvider`. All date comparisons receive a single captured `(UtcNow, EgyptLocalNow, EgyptDate)` snapshot per job or request.
- Add a `DeliveryJobRun` record for cycle observability. A new run closes stale prior Running records as interrupted, records safe counters/outcome, and never stores exception stacks, credentials, signed URLs, or financial idempotency material.
- Add `DeliveryRecoveryDispatch` with a unique `(BusinessDateEgypt, JobType)` key, Pending/Enqueued/Completed/Failed state, nullable scheduler job id, safe timing/error metadata, and rowversion. Failed or Pending without acknowledgement never counts as current-date coverage and is reset/retried through the same claim; Enqueued or Completed prevents intentional duplicate recovery dispatch. Define an infrastructure-neutral `IDeliveryJobEnqueuer` contract in Core. A Services-owned recovery coordinator captures one Cairo snapshot, uses Repository + Unit of Work to create/reuse dispatch claims, and calls only that enqueue abstraction. The APIs-layer Hangfire adapter implements the abstraction with `IBackgroundJobClient`, enqueueing `IDeliveryExpiryService.RunAsync` first plus `IDailyDeliveryInjectorService.RunAsync` as a continuation when Cairo time is at or after 00:05; if expiry already completed and only injection is missing, it enqueues injection directly. Program resolves the coordinator after startup wiring but never invokes either business service or mutates delivery/queue/wallet state. A crash between durable claim and scheduler acknowledgement may cause at-least-once enqueue on reconciliation; service idempotency remains the business correctness boundary.
- Expiry reads every Active/Reserved delivery before today in deterministic bounded pages. Each candidate transaction locks/rechecks the delivery, locks the company wallet, verifies the stored reservation, creates `delivery:release:{deliveryId}`, writes Reserved Debit plus Available Credit ledger entries, and transitions to Expired/Released. Existing or settled candidates are no-ops; inconsistent candidates are recorded as failures and remain blocking for their company.
- Injection enumerates doctors with Queued rows and processes each doctor's candidates by immutable `CampaignSubmittedAtUtc`, then id. The snapshot is copied from the campaign's actual `SubmittedAtUtc` when the row is created and is never mutated; null snapshots are malformed and are never silently ordered by `QueuedAtUtc`. Each candidate transaction locks the doctor profile before counting current-day deliveries, locks/rechecks the queue row and campaign/company/doctor eligibility, verifies the company has no overdue Active deliveries, locks the company wallet, and applies activation atomically.
- Use current doctor price, not the campaign target snapshot, at activation. Read exactly one effective platform-fee policy at captured UTC time and require `0 < Percent <= 100` at two-decimal precision. Compute `Fee = decimal.Round(Price * Percent / 100, 2, MidpointRounding.AwayFromZero)` and `DoctorEarnings = Price - Fee`; require `0 < Fee < Price` and `DoctorEarnings > 0`. Missing, overlapping, out-of-range, or rounded-invalid policies leave the row Queued, fail only that candidate, and create no delivery or financial effect.
- Use deterministic reservation key `delivery:reserve:{deliveryId}` and release key `delivery:release:{deliveryId}`. A Reserve writes Available Debit and Reserved Credit; a Release writes Reserved Debit and Available Credit. All four ledger references include delivery, campaign, company, and idempotency context already supported by the model.
- Add queue `rowversion` and exact queue/delivery indexes required by FIFO scanning, overdue gating, current-day counts, and inbox order. Keep `CampaignSubmittedAtUtc` nullable at the column level only for terminal historical Activated/Cancelled rows, add a conditional check requiring it whenever `Status = Queued`, and use FIFO scan index `(Status, DoctorId, CampaignSubmittedAtUtc, Id)`. Backfill only Queued rows and only from `Campaign.SubmittedAtUtc`; never infer submission order from `QueuedAtUtc`, and fail migration validation if any Queued row remains null. Terminal historical nulls remain allowed because Phase 7 forbids return to Queued. Add `ExpiredAtUtc`, `DeliveryJobRuns`, and `DeliveryRecoveryDispatches` through one Phase 7 EF Core migration.
- Terminal campaign/company/doctor states cancel the locked queue row without wallet effects. Paused campaigns, suspended or temporarily unapproved doctors, temporary missing prices, insufficient funds, and unresolved company expiry leave rows Queued and continue the scan.
- Implement `GET /api/doctor/messages/today` as a joined, no-tracking keyset read model ordered by the persisted activation instant `DeliveredAtUtc`, then id. Accept `PageSize` from 1 to 100 with default 50 plus an optional opaque cursor scoped to the authenticated doctor and captured Egypt business date; query `PageSize + 1` rows to return a nullable `NextCursor` so no current-day delivery becomes unreachable. Reject malformed, cross-doctor, and stale-date cursors safely. Return approved active asset metadata and a delivery-scoped access path, not signed URLs generated during the inbox query; never use `CreatedAtUtc` as the inbox order.
- Implement `GET /api/doctor/messages/{deliveryId}/assets/{fileId}/access`. It verifies approved active Doctor identity, ownership of a delivery for the current Egypt date, campaign/file relationship, and active Approved file status before reusing the storage provider and existing access-audit persistence to issue a 10-minute grant.
- Use unit tests for business clock/DST, rounding, eligibility and keys; contract tests for envelopes and role gates; SQL Server integration tests for locking, idempotency, atomic ledger movements, company-scoped gating, inbox date boundaries, migration/index behavior, and Hangfire recurring registration.

## Performance Test Profile

- Run a Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, `BatchSize = 100`, and no debugger, coverage collector, or parallel non-test workload.
- Inbox profile: seed a 100-item current-day page with representative approved assets; issue 20 sequential warm-up requests followed by 200 measured requests at concurrency 10; require at least 190 requests within 1 second, zero failed requests, bounded delivery/asset query counts, and report p50, p95, p99, query count, and failures.
- Daily-cycle profile: seed exactly 1,000 doctors and 10,000 queued candidates with a fixed documented eligibility/funding distribution; perform one warm-up run and then three clean-database measured runs; require every measured run within 5 minutes, stable bounded batches/memory/query behavior, and zero duplicate deliveries or financial effects.
- Performance tests are explicitly opt-in outside the designated CI profile. A skipped run must report which profile prerequisite was unavailable; a skip is not passing performance evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves Hangfire hosting/storage, schedule-versus-worker-start semantics, durable startup/recovery dispatch and retries, DST-aware Cairo time, candidate concurrency, company-scoped expiry gating, immutable campaign-submission FIFO with conditional nullability, wallet ledger shape, fee validity/rounding, queue state handling, infrastructure operator review/requeue, reproducible performance measurement, paginated inbox projection, and delivery-scoped file access. No `NEEDS CLARIFICATION` markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines reused and changed entities, immutable campaign-submission ordering, fee validity, new job-run persistence, paginated read models, indexes, transitions, lock order, and atomic actions.
- [contracts/delivery-expiry-api.yaml](./contracts/delivery-expiry-api.yaml) defines the paginated Doctor today-inbox and delivery-asset access contracts.
- [quickstart.md](./quickstart.md) provides configuration, migration, smoke, pagination, operator, performance, negative, idempotency, DST, and verification steps.
