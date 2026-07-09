# Tasks: Phase 7 Delivery & Expiry Jobs

**Input**: Design documents from `/specs/008-delivery-expiry-jobs/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/delivery-expiry-api.yaml](./contracts/delivery-expiry-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required. The Phase 7 specification and plan require unit, contract, SQL Server integration, concurrency, migration, security, DST-boundary, and performance-oriented validation. Within every user-story phase, create the tests first, run the focused test filter, and record that the new tests fail for the expected missing behavior before implementing the production tasks.

**Constitution Note**: Keep all entities/contracts/domain decisions in `MediBridge.Core`, SQL Server and EF Core code in `MediBridge.Repository`, orchestration in `MediBridge.Services`, and HTTP/Hangfire wiring in `MediBridge.APIs`. Controllers and recurring-job registration must contain no queue, pricing, wallet, or persistence logic. All secured HTTP responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; errors flow through global middleware without raw stack traces.

**Execution Rule for a Smaller Model**: Execute tasks strictly in numeric order unless the task is explicitly marked `[P]` and every listed dependency is already complete. Do not combine tasks, rename planned types, change financial formulas, add endpoints, expose Hangfire Dashboard, or implement Phase 8+ behavior without updating the design artifacts first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to execute concurrently after its phase prerequisites because it targets different files and does not consume an unfinished symbol.
- **[Story]**: User-story traceability label. Setup, foundational, and polish tasks intentionally have no story label.
- Every task names the exact target file or directory and states the observable completion condition.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish explicit Phase 7 configuration and reusable test fixtures without changing runtime behavior yet.

- [X] T001 Verify `Hangfire.AspNetCore` and `Hangfire.SqlServer` remain pinned at `1.8.17` in `MediBridge.APIs/MediBridge.APIs.csproj`; add a missing reference only if absent, do not upgrade unrelated packages, then run `dotnet restore .\MediBridge.slnx` and record a successful restore.
- [X] T002 Create `MediBridge.APIs/Config/DeliveryJobOptions.cs` with section name `DeliveryJobs`, defaults `Enabled=true`, `BatchSize=100`, `AutomaticRetryAttempts=5`, `WorkerCount=4`, `QueueName="delivery"`, and `TimeZoneId="Africa/Cairo"`; validate positive bounded numeric values, the exact queue name, and the exact clarified time-zone id, while keeping the fixed 00:00/00:05 cron schedules out of mutable configuration.
- [X] T003 [P] Add non-secret `DeliveryJobs` settings and `Logging:LogLevel:Hangfire` configuration to `MediBridge.APIs/appsettings.json` and test-safe overrides to `MediBridge.APIs/appsettings.Development.json`; do not add connection strings, credentials, dashboard flags, cron overrides, or fixed UTC offsets.
- [X] T004 [P] Create `tests/unit/MediBridge.UnitTests/TestDoubles/FakeTimeProvider.cs` as a thread-safe controllable `TimeProvider` whose UTC instant can be set/advanced explicitly; do not use `DateTime.Now`, host local time, or sleeps in Phase 7 time tests.
- [X] T005 [P] Create `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryTestHelpers.cs` with narrowly named helpers for seeding approved/suspended/deleted doctors, companies, effective fee policies, campaigns/assets, FIFO queue rows, wallets, and deliveries; every queue helper must accept distinct explicit `CampaignSubmittedAtUtc` and `QueuedAtUtc` values so tests cannot conflate submission priority with enqueue timing, and every helper must accept explicit ids/timestamps/balances without performing the job behavior being tested.

**Checkpoint**: Configuration types and test fixtures exist; runtime behavior is unchanged; restore succeeds.

**Phase 1 verification (2026-07-02)**: `dotnet restore .\MediBridge.slnx` completed successfully with both Hangfire packages still pinned at 1.8.17. `dotnet test .\MediBridge.slnx --no-restore` passed all 455 tests (68 unit, 115 contract, 272 integration) with zero failures or skips.

**Phase 1 manual Senior review (2026-07-02): PASS** — T001-T005 are complete. The changes preserve Onion Architecture boundaries, introduce no runtime job behavior, use thread-safe controllable time, propagate cancellation through asynchronous EF Core fixture operations, and contain no blocking or ambient-time calls. A fresh full-solution run passed all 455 tests with zero failures or skips. Existing committed database, SMTP, and Cloudinary credentials in the settings files predate Phase 1 and require separate credential rotation/removal; Phase 1 added no secrets.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the shared clock, entities, read models, persistence contracts, SQL locking queries, indexes, migration, and dependency registration required by every user story.

**⚠️ CRITICAL**: Complete all tasks in this phase before beginning any user-story implementation.

- [X] T006 Add `DeliveryJobType` (`ExpiryCleaner`, `DailyInjector`), `DeliveryJobRunStatus` (`Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Deferred`, `Interrupted`), and `RecoveryDispatchStatus` (`Pending`, `Enqueued`, `Completed`, `Failed`) to `MediBridge.Core/Enums/Phase3DomainEnums.cs` with stable explicit integer values; do not alter existing enum values.
- [X] T007 [P] Create `MediBridge.Core/Entities/Messaging/DeliveryJobRun.cs` with every job-run field and validation boundary from `data-model.md`, and create `MediBridge.Core/Entities/Messaging/DeliveryRecoveryDispatch.cs` with unique date/job identity, status, nullable scheduler/dependency identifiers, safe timing/error metadata, and `ConcurrencyToken`; both implement `IConcurrencyTrackedRecord`, and the dispatch entity contains no delivery or financial mutation data.
- [X] T008 Add `ConcurrencyToken` to `MediBridge.Core/Entities/Messaging/DoctorMessageQueue.cs` and nullable `ExpiredAtUtc` to `MediBridge.Core/Entities/Messaging/DoctorAdDelivery.cs`; add small domain transition methods that permit only `Queued -> Activated`, `Queued -> Cancelled`, and `Active/Reserved -> Expired/Released`, set UTC timestamps supplied by the caller, and reject every invalid/repeated transition without mutating state.
- [X] T009 [P] Create `MediBridge.Core/Interfaces/Time/IEgyptBusinessClock.cs` containing `IEgyptBusinessClock`, immutable `EgyptBusinessTimeSnapshot`, and one method that returns a single captured UTC instant, Cairo-local instant, and `DateOnly BusinessDateEgypt`; expose the resolved `TimeZoneInfo` read-only for Hangfire registration.
- [X] T010 [P] Create `MediBridge.Core/Entities/Wallets/DeliveryFinancialOperationKeys.cs` with pure methods `ForReserve(string deliveryId)` and `ForRelease(string deliveryId)` returning exactly `delivery:reserve:{deliveryId}` and `delivery:release:{deliveryId}` after rejecting blank ids; never log or accept caller-selected financial idempotency keys.
- [X] T011 [P] Create `MediBridge.Core/Interfaces/Messaging/DeliveryProcessingReadModels.cs` with immutable cursor/projection records for queued doctor paging, queued candidate paging keyed by `CampaignSubmittedAtUtc, Id`, locked doctor eligibility, locked campaign/company eligibility, overdue-delivery paging, effective fee policy, today-delivery rows, approved asset rows, and delivery-asset authorization; include only fields required by `data-model.md` and no EF Core types.
- [X] T012 Extend `MediBridge.Core/Interfaces/Messaging/IMessageQueueRepository.cs` with keyset methods for distinct queued doctor ids and per-doctor candidates ordered by immutable `CampaignSubmittedAtUtc ASC, Id ASC`, a `FindQueuedItemForUpdateAsync` row-lock method, and expected-state transition methods; require queue creation callers to supply the campaign's authentic submission timestamp, prohibit later mutation, classify null legacy timestamps as malformed, and document cursor semantics so equal timestamps never skip or duplicate ids. Update existing callers and focused queue tests in `MediBridge.Services/Services/AdminCampaignReviewService.cs`, `MediBridge.Services/Services/CampaignWorkflowService.cs`, and `tests/integration/MediBridge.IntegrationTests/Phase3QueueOrderingTests.cs` without deriving submission time from `QueuedAtUtc`.
- [X] T013 Extend `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs` with keyset overdue-id paging, locked Active/Reserved retrieval, current-day count, unique replay lookup, company overdue-existence gate, expiry transition, `PageSize + 1` current-day delivery projection ordered by persisted activation instant `DeliveredAtUtc, Id`, approved asset projection for the bounded returned campaign-id set, and current-day delivery/file authorization lookup; every date argument must be an explicit captured Cairo `DateOnly`, `PageSize` must be 1-100 with default 50 at the service boundary, and `CreatedAtUtc` must not drive inbox order.
- [X] T014 [P] Create `MediBridge.Core/Interfaces/Messaging/IDeliveryJobRunRepository.cs` with methods to interrupt stale Running rows, add a Running row, complete exactly one run with a terminal status/counters/safe summary, and query current-date coverage; create `MediBridge.Core/Interfaces/Messaging/IDeliveryRecoveryDispatchRepository.cs` to atomically find/create unique date/job claims, record scheduler acknowledgement/dependency, reconcile Pending claims, and complete dispatches; create infrastructure-neutral `MediBridge.Core/Interfaces/Messaging/IDeliveryJobEnqueuer.cs` for expiry enqueue, injector continuation, and direct injector enqueue results without exposing Hangfire types.
- [X] T015 Extend `MediBridge.Core/Interfaces/Identity/IProfileRepository.cs` with a locked Doctor delivery-eligibility read returning Doctor profile plus ApplicationUser role/account/deletion state and current price/limit/status; the contract must distinguish terminal deletion from temporary suspension/unapproval/unpricing.
- [X] T016 Extend `MediBridge.Core/Interfaces/Campaigns/ICampaignRepository.cs` with a locked delivery-eligibility read returning campaign status/deletion, owning Company profile deletion, and Company ApplicationUser role/account/deletion state; define `Approved` and `Active` as deliverable, `Paused` as temporary, and Rejected/Completed/Cancelled/deleted owner records as terminal.
- [X] T017 Extend `MediBridge.Core/Interfaces/Policies/IPolicyHistoryRepository.cs` with `FindSingleEffectivePlatformFeePolicyAsync(DateTime effectiveAtUtc, ...)` that returns one effective policy projection, returns null when none exists, and throws a domain-safe conflict when overlapping policies exist; never silently select the first overlap or inject a default fee.
- [X] T018 Add `IDeliveryJobRunRepository DeliveryJobRuns` and `IDeliveryRecoveryDispatchRepository DeliveryRecoveryDispatches` to `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`; keep all transaction methods unchanged and do not expose `DbContext`, `IQueryable`, EF execution strategies, Hangfire types, or SQL primitives.
- [X] T019 Update `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs` to map queue `ConcurrencyToken` as rowversion, nullable immutable `CampaignSubmittedAtUtc` with conditional check `Status <> Queued OR CampaignSubmittedAtUtc IS NOT NULL`, nullable delivery `ExpiredAtUtc`, exact queue scan index `(Status, DoctorId, CampaignSubmittedAtUtc, Id)`, expiry/gate index `(Status, ReservationStatus, DeliveryDateEgypt, CompanyId, Id)`, and inbox/count index `(DoctorId, DeliveryDateEgypt, DeliveredAtUtc, Id)` while preserving existing unique constraints and money checks; terminal historical Activated/Cancelled nulls remain allowed and never re-enter Queued, and `QueuedAtUtc` remains operational metadata only.
- [X] T020 [P] Create `MediBridge.Repository/Configurations/Messaging/DeliveryJobRunConfiguration.cs` mapping the job-run table, enum conversions, rowversion, 2000-character summary, non-negative-counter check constraint, `(JobType, BusinessDateEgypt, StartedAtUtc)` index, and `(Status, StartedAtUtc)` stale-run index; create `MediBridge.Repository/Configurations/Messaging/DeliveryRecoveryDispatchConfiguration.cs` with unique `(BusinessDateEgypt, JobType)`, status conversion, scheduler/dependency lengths, safe summary bound, timing, rowversion, and no cascade into domain business entities.
- [X] T021 Add `DbSet<DeliveryJobRun> DeliveryJobRuns` and `DbSet<DeliveryRecoveryDispatch> DeliveryRecoveryDispatches` to `MediBridge.Repository/Data/MediBridgeDbContext.cs`; do not add Hangfire entities or manage the `HangFire` schema through EF Core.
- [X] T022 Implement the T012 keyset and locking contracts in `MediBridge.Repository/Repositories/Messaging/MessageQueueRepository.cs` using bounded `AsNoTracking` discovery queries and SQL Server `UPDLOCK, ROWLOCK` for the expected Queued row; preserve immutable `CampaignSubmittedAtUtc, Id` ordering, reject queue creation without an authentic campaign submission timestamp, never fall back to `QueuedAtUtc`, and return no row when status changed.
- [X] T023 Implement the T013 overdue, gate, count, paginated inbox, asset, authorization, and locked-delivery contracts in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`; use bounded no-tracking projections, parameterized SQL/EF, `UPDLOCK, ROWLOCK` only inside Unit of Work transactions, `(DeliveredAtUtc, Id)` keyset predicates for `PageSize + 1`, and deterministic date/time/id ordering with no N+1 query loop.
- [X] T024 [P] Create `MediBridge.Repository/Repositories/Messaging/DeliveryJobRunRepository.cs` implementing T014 job-run coverage/tracking and `MediBridge.Repository/Repositories/Messaging/DeliveryRecoveryDispatchRepository.cs` implementing atomic unique claims, Pending reconciliation, scheduler acknowledgement/dependency, and completion; use SQL Server uniqueness/rowversion for concurrent hosts, keep Hangfire types out of Repository, and trim/redact safe summaries before persistence.
- [X] T025 Implement T015 in `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs` by joining DoctorProfile to the mapped identity user and using a locked profile row for activation serialization; do not infer approval from DoctorMarketplaceStatus alone.
- [X] T026 Implement T016 in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs` with locked campaign revalidation plus owning Company profile/user state; keep existing moderation methods behavior unchanged.
- [X] T027 Implement T017 in `MediBridge.Repository/Repositories/Policies/PolicyHistoryRepository.cs` with an effective interval predicate `EffectiveFromUtc <= instant` and `(EffectiveToUtc == null || instant < EffectiveToUtc)`; explicitly detect zero, one, and multiple matches.
- [X] T028 Wire `IDeliveryJobRunRepository` and `IDeliveryRecoveryDispatchRepository` through `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs` and register both implementations in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`; preserve constructor/property order clarity and candidate-level `ExecuteInTransactionAsync` behavior.
- [X] T029 Create `MediBridge.Services/Services/EgyptBusinessClock.cs` using injected `TimeProvider`, first resolving `Africa/Cairo`, then using `TimeZoneInfo.TryConvertIanaIdToWindowsId` only as a Windows fallback; require DST-capable Cairo rules and throw a safe startup configuration exception rather than falling back to host time or fixed UTC+2.
- [X] T030 Register `TimeProvider.System` and singleton `IEgyptBusinessClock` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`; tests must be able to replace `TimeProvider` through DI without static clock state.
- [X] T031 Generate one EF Core migration named `AddPhase7DeliveryExpiryJobs` in `MediBridge.Repository/Migrations/` and update `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`; backfill only Queued rows and only from related `Campaign.SubmittedAtUtc`, abort if any Queued row remains null, never use `QueuedAtUtc`, retain nullable timestamps for terminal historical Activated/Cancelled rows, add conditional check `Status <> Queued OR CampaignSubmittedAtUtc IS NOT NULL`, revised FIFO index, queue rowversion, delivery expiry time, `DeliveryJobRuns`, `DeliveryRecoveryDispatches` with unique date/job claim, indexes/checks/FKs, and no historical activation, expiry, reserve, or release mutation.
- [X] T032 [P] Add `tests/integration/MediBridge.IntegrationTests/Phase7MigrationTests.cs` proving a clean SQL Server database applies the complete migration chain, only Queued timestamps are backfilled from authentic `Campaign.SubmittedAtUtc`, unresolvable Queued rows fail instead of using `QueuedAtUtc`, terminal historical nulls survive and cannot return to Queued, the conditional constraint/recovery-dispatch unique claim/new tables/columns/indexes/checks exist, existing delivery uniqueness and wallet-transaction idempotency remain enforced, and rollback/reapply does not alter business rows.
- [X] T033 [P] Add `tests/unit/MediBridge.UnitTests/EgyptBusinessClockTests.cs` covering Cairo midnight, host-zone independence, Windows-id fallback, invalid-zone startup failure, and real DST transitions before/after the current Egyptian daylight-saving changes using `FakeTimeProvider`.
- [X] T034 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~EgyptBusinessClock"`, `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7Migration"`, and `dotnet build .\MediBridge.slnx`; fix only foundational failures and record a clean foundation checkpoint in `specs/008-delivery-expiry-jobs/tasks.md` notes.

**Phase 2 verification (2026-07-02)**: `EgyptBusinessClock` focused tests passed 8/8, `Phase7Migration` SQL Server integration tests passed 3/3, the updated immutable campaign-submission FIFO regression passed 1/1, and `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors. The final full-solution regression passed all 466 tests (76 unit, 115 contract, 275 integration) with zero failures or skips.

**Phase 2 manual Senior review (2026-07-03): PASS WITH REQUIRED US4 FOLLOW-UPS** — T006-T034 remain complete. Onion Architecture boundaries are preserved: Core has no EF Core/Hangfire dependency, SQL Server and row locking remain in Repository, and the DST-aware Cairo clock remains in Services. Reviewed async paths contain no blocking waits or sleeps and propagate cancellation to EF/SQL operations. Migration backfill, conditional queue integrity, indexes, rollback/reapply behavior, Unit of Work wiring, and Phase 3+ scope exclusion match the design artifacts. Before US4 coordination is approved, T069-T076 must prove and, where necessary, strengthen single-winner `rowversion` behavior for concurrent Pending recovery reconciliation, enforce UTC validation on every bulk operational timestamp transition, and expand safe-summary/log redaction coverage. Fresh review verification: `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors, and unit tests passed 76/76. A fresh SQL-backed full-suite rerun was infrastructure-blocked because drive `C:` had 0 free bytes from accumulated `MediBridge.IntegrationTests_*.mdf`/`*_log.ldf` LocalDB files; no files were deleted without owner approval. The last completed full-suite evidence remains the 466/466 pass recorded above.

**Checkpoint**: The shared schema, Cairo clock, repository contracts/locks, projections, Unit of Work wiring, and migration are complete. No job, HTTP endpoint, Charge, or Earn behavior exists yet.

---

## Phase 3: User Story 1 - Expire Unanswered Deliveries and Release Funds (Priority: P1) 🎯 MVP

**Goal**: Expire every overdue Active/Reserved unanswered delivery and return the exact stored reservation to the owning company's wallet once, with balanced immutable financial evidence.

**Independent Test**: Seed current-day, multi-day-overdue, Accepted, Rejected, already Expired, and inconsistent deliveries; run expiry repeatedly and concurrently; prove only overdue Active/Reserved rows transition and each valid release creates exactly one transaction, two ledger entries, and one balanced wallet reversal.

### Tests for User Story 1 — write and observe failure first

- [X] T035 [P] [US1] Create `tests/integration/MediBridge.IntegrationTests/Phase7ExpiryEligibilityTests.cs` covering all-date catch-up with no lookback limit, Cairo date boundary, Active/Reserved requirement, Accepted/Rejected/current-day/already-expired no-ops, deterministic page continuation, and `ExpiredAtUtc`/UpdatedAtUtc values from the captured clock.
- [X] T036 [P] [US1] Create `tests/integration/MediBridge.IntegrationTests/Phase7ExpiryFinancialTests.cs` covering exact Available/Reserved reversal, one Release transaction keyed `delivery:release:{deliveryId}`, Reserved Debit plus Available Credit entries with delivery/campaign/company references, no Doctor credit/Charge/Earn, repeated/concurrent retries, insufficient Reserved inconsistency, forced-save rollback, and unchanged state after failure.
- [X] T037 [P] [US1] Create `tests/unit/MediBridge.UnitTests/DeliveryExpiryServiceTests.cs` with mocked repository boundaries for empty pages, cancellation propagation, candidate isolation, examined/expired/skipped/failed counts, replay no-op classification, and safe failure text that contains no stack, connection string, storage key, signed URL, wallet balance, or idempotency key.

### Implementation for User Story 1

- [X] T038 [P] [US1] Create `MediBridge.Services/DTOs/Messaging/DeliveryJobResultDto.cs` and `MediBridge.Services/Interfaces/IDeliveryExpiryService.cs`; expose `RunAsync(CancellationToken)` returning business date, timing, outcome, and examined/expired/skipped/failed counts without exposing exceptions or financial keys.
- [X] T039 [US1] Implement the bounded overdue cursor loop in `MediBridge.Services/Services/DeliveryExpiryService.cs`; capture one Cairo snapshot per invocation, process every page until exhausted, honor cancellation between candidates, continue independent candidates after isolated failure, and never use `DateTime.Now/UtcNow` directly.
- [X] T040 [US1] Implement the single-candidate expiry transaction in `MediBridge.Services/Services/DeliveryExpiryService.cs` in this exact order: lock/recheck overdue Active/Reserved delivery; check Release replay; lock active EGP Company wallet; verify Reserved balance; set Expired/Released timestamps; stage Reserved `-amount` and Available `+amount`; add one Release transaction and two referenced ledger entries; commit once; on concurrency/unique conflict reload and classify completed replay versus genuine failure.
- [X] T041 [US1] Register `IDeliveryExpiryService` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`; keep Hangfire references out of `MediBridge.Services` and do not schedule the service yet.
- [X] T042 [US1] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliveryExpiry"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7Expiry"`; confirm all US1 tests pass and manually inspect persisted Release/ledger rows before starting US2.

**Phase 3 verification (2026-07-03)**: `DeliveryExpiry` focused unit tests passed 5/5, `Phase7Expiry` SQL Server integration tests passed 6/6, and `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors. Persisted Release transaction and balanced Reserved Debit/Available Credit ledger references were inspected through SQL-backed assertions. No scheduling or Phase 4+ behavior was added.

**Phase 3 manual Senior review (2026-07-03): TASKS COMPLETE — CHANGES REQUESTED BEFORE PHASE 4** — T035-T042 remain complete. Onion Architecture boundaries are preserved, the expiry service contains no EF Core or Hangfire dependency, cancellation is propagated without blocking waits, one Cairo snapshot drives each run, and candidate financial writes remain inside Unit of Work transactions. A fresh full-solution regression passed all 481 tests (81 unit, 117 contract, 283 integration) with zero failures or skips. Required hardening before Phase 4: perform replay revalidation inside an explicit Unit of Work transaction because it currently calls the update-lock repository method outside one; guarantee `ChangeTracker.Clear()` even when rollback itself throws so a failed candidate cannot leak staged state into the next candidate; and bound/clear successfully committed tracked entities during long catch-up runs to avoid unbounded tracking and repeated change-detection cost. Add SQL-backed regressions for failed-first-candidate isolation and equal-date/equal-created-time cursor continuation when addressing these findings.

**Phase 3 remediation (2026-07-03): RESOLVED** — Added the explicit Core `ExecuteIsolatedInTransactionAsync<T>` contract and repository implementation without changing successful tracking semantics for existing Unit of Work callers. Isolated expiry mutation and replay verification now each run inside their own SQL transaction, including the update-lock delivery read and financial evidence checks; EF tracking is cleared in a `finally` path after every isolated attempt and every failed ordinary attempt; caller cancellation is preserved; and an operation failure plus rollback failure is retained safely as a combined failure. Added SQL-backed coverage for failed-first-candidate continuation, success/failure tracking cleanup, rollback-failure aggregation, cancellation with rollback failure, unchanged ordinary-success tracking semantics, and equal-date/equal-created-time cursor continuation. Verification passed 5/5 focused unit tests, 13/13 focused SQL-backed integration tests, and all 488 solution tests (81 unit, 117 contract, 290 integration) with zero failures or skips; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors. Phase 4 remains untouched.

**Checkpoint**: User Story 1 works when invoked directly through `IDeliveryExpiryService`, is independently testable, and is the recommended service-level MVP.

---

## Phase 4: User Story 2 - Activate the Daily Message Allocation (Priority: P1)

**Goal**: Activate eligible FIFO candidates up to each doctor's global current-day limit, reserve current-price funds exactly once, skip blocked candidates, and isolate companies with unresolved expiry.

**Independent Test**: Seed multiple doctors/companies/campaign states/prices/balances and existing current-day deliveries; run injection repeatedly/concurrently; verify FIFO, limit, snapshot, terminal/temporary, insufficient-fund, company-gate, wallet, and idempotency rules without any Charge/Earn effects.

### Tests for User Story 2 — write and observe failure first

- [X] T043 [P] [US2] Create `tests/unit/MediBridge.UnitTests/DeliverySettlementSnapshotCalculatorTests.cs` covering 20% examples, midpoint `AwayFromZero`, two-decimal EGP output, earnings as price minus rounded fee, the exact `0 < Percent <= 100`, `0 < Fee < Price`, and `DoctorEarnings > 0` boundaries, zero/negative/>100/overlapping/missing policy rejection, rounded zero fee/earnings rejection, and no silent default substitution.
- [X] T044 [P] [US2] Create `tests/unit/MediBridge.UnitTests/DeliveryCandidateEligibilityPolicyTests.cs` covering deliverable Approved/Active state, terminal campaign/company/doctor cancellation, temporary Paused/Suspended/unapproved/unpriced preservation, deleted owner handling, zero limit, full current-day capacity, insufficient funds, and unresolved company-expiry skip classification.
- [X] T045 [P] [US2] Create `tests/integration/MediBridge.IntegrationTests/Phase7InjectorFifoAndLimitTests.cs` covering distinct and equal immutable `CampaignSubmittedAtUtc` values with id tie-breaks, deliberately inverted `QueuedAtUtc` values proving enqueue time never changes priority, malformed null submission snapshots, per-doctor global limits across companies, existing Active/Accepted/Rejected current-day deliveries counting toward capacity, zero/reduced limits, bounded cursor continuation without skip/duplicate, and no deletion of pre-existing deliveries.
- [X] T046 [P] [US2] Create `tests/integration/MediBridge.IntegrationTests/Phase7InjectorEligibilityTests.cs` covering terminal rows becoming Cancelled, temporary rows remaining Queued, insufficient-fund skip-and-continue, exact-balance activation, missing/deleted/non-EGP wallet behavior, and blocked rows not consuming capacity.
- [X] T047 [P] [US2] Create `tests/integration/MediBridge.IntegrationTests/Phase7InjectorFinancialTests.cs` covering current Doctor price snapshot rather than target snapshot, effective fee snapshot, one Reserve key `delivery:reserve:{deliveryId}`, Available Debit plus Reserved Credit ledger entries, atomic queue/delivery/wallet/transaction/ledger commit, forced rollback, concurrent same-doctor limit safety, concurrent same-company balance safety, delivery uniqueness, and replay idempotency.
- [X] T048 [P] [US2] Create `tests/integration/MediBridge.IntegrationTests/Phase7InjectorCompanyGateAndPolicyTests.cs` proving Company A with any overdue Active/Reserved delivery remains blocked while Company B proceeds, Company A activates after release, injector waits on an in-flight expiry transaction safely, invocation before 00:05 Egypt time returns Deferred with no mutation, and missing/overlapping/out-of-range/rounded-invalid effective fee policy leaves the row Queued and fails only that candidate without fallback, delivery, or financial mutation.

### Implementation for User Story 2

- [X] T049 [P] [US2] Create `MediBridge.Services/Services/DeliverySettlementSnapshotCalculator.cs` as a pure calculator using `decimal.Round(price * percent / 100m, 2, MidpointRounding.AwayFromZero)` and `earnings = price - roundedFee`; require exactly one policy, `0 < Percent <= 100`, `0 < roundedFee < price`, and `earnings > 0`, then validate existing `MoneyRules` and `DoctorAdDelivery.ApplySettlementSnapshot` invariants before returning an immutable snapshot.
- [X] T050 [P] [US2] Create `MediBridge.Services/Services/DeliveryCandidateEligibilityPolicy.cs` with an explicit result enum/object for `Eligible`, `CancelTerminal`, `KeepQueuedTemporary`, `KeepQueuedInsufficientFunds`, `KeepQueuedExpiryBlocked`, `NoCapacity`, and `Failed`; do not encode repository queries or wallet mutations in the policy.
- [X] T051 [P] [US2] Create `MediBridge.Services/DTOs/Messaging/DailyInjectorResultDto.cs` and `MediBridge.Services/Interfaces/IDailyDeliveryInjectorService.cs`; `RunAsync(CancellationToken)` returns captured business date/timing, `Deferred` or terminal outcome, and examined/activated/cancelled/skipped/failed counts only.
- [X] T052 [US2] Implement doctor and FIFO keyset scanning in `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`: capture one Cairo snapshot; return Deferred without mutation when Cairo local time is before 00:05; enumerate queued doctor ids; for each doctor enumerate immutable `CampaignSubmittedAtUtc, Id`; classify a null submission snapshot as an isolated malformed candidate and never substitute `QueuedAtUtc`; stop activation at capacity but keep deterministic counters; honor batch size/cancellation; never use campaign target price or mutable host time.
- [X] T053 [US2] Implement the single-candidate transaction in `MediBridge.Services/Services/DailyDeliveryInjectorService.cs` in this exact order: lock Doctor profile/user and recount today's deliveries; lock/recheck Queued row; lock/revalidate campaign/company; cancel terminal or preserve temporary state; check company overdue gate; resolve exactly one fee policy and calculate snapshot; derive/recheck unique delivery and Reserve replay; lock EGP company wallet and verify Available amount; create Active/Reserved delivery; mark queue Activated; move Available to Reserved; add one Reserve transaction and two referenced ledger entries; commit once; reclassify concurrency/unique conflicts from fresh state.
- [X] T054 [US2] Register calculator, eligibility policy, and `IDailyDeliveryInjectorService` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`; keep all registrations scoped except pure stateless helpers, and do not add Hangfire attributes/references to service classes.
- [X] T055 [US2] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliverySettlementSnapshot|FullyQualifiedName~DeliveryCandidateEligibility"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7Injector"`; confirm every US2 test passes with no Charge/Earn transaction and no Doctor wallet mutation.

**Checkpoint**: User Story 2 works through direct service invocation and does not require the HTTP inbox or Hangfire scheduler.

---

## Phase 5: User Story 3 - View Only Today's Doctor Inbox (Priority: P1)

**Goal**: Return only the authenticated approved Doctor's current Cairo-date deliveries and approved asset metadata, then issue current-delivery-scoped 10-minute asset grants through a separate endpoint.

**Independent Test**: Call both API paths with anonymous, Doctor, Company, Admin, cross-doctor, prior-day, and valid current-day cases; verify ownership/date/file gates, deterministic order, empty result, envelope casing, safe errors, and absence of storage or wallet internals.

### Tests for User Story 3 — write and observe failure first

- [X] T056 [P] [US3] Create `tests/contract/MediBridge.ContractTests/DoctorTodayMessagesContractTests.cs` asserting `GET /api/doctor/messages/today` supports optional `PageSize` 1-100 (default 50), opaque `Cursor`, nullable `NextCursor`, safe 400 cursor/page validation, and `GET /api/doctor/messages/{deliveryId}/assets/{fileId}/access` exactly as defined in `contracts/delivery-expiry-api.yaml`; require exact Pascal-case envelope/data properties, successful empty Items with null cursor, only documented fields/statuses, and no storage keys, provider ids, wallet values, fee snapshots, raw exceptions, or admin metadata.
- [X] T057 [P] [US3] Create `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxIntegrationTests.cs` covering Cairo yesterday/today/tomorrow boundaries including DST, approved Doctor resolution, cross-doctor isolation, non-Doctor/anonymous denial, deterministic persisted activation ordering by `DeliveredAtUtc, Id` rather than `CreatedAtUtc`, default/1/100 page sizes, `PageSize + 1` continuation, multi-page traversal with no gaps/duplicates, malformed/cross-doctor/stale-date cursor rejection, Accepted/Rejected current-day visibility, and immediate prior-day disappearance after Cairo midnight.
- [X] T058 [P] [US3] Create `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryAssetAccessTests.cs` covering valid 10-minute grant/audit, current-day delivery ownership, campaign/file relationship, Approved+Stored+not-deleted/not-replaced requirement, cross-doctor/prior-day/unrelated/Pending/Rejected/Quarantined denial, inactive owner behavior, and provider failure mapped to a safe 503 envelope.
- [X] T059 [P] [US3] Create opt-in `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxPerformanceTests.cs` for the documented Release/Server-GC/SQL Server 2022 Testcontainers profile on at least 4 dedicated vCPUs, 8 GB RAM, and SSD storage: seed a 100-item page with representative approved assets, run 20 sequential warm-ups then 200 measured requests at concurrency 10, require at least 190 within 1 second and zero failures, instrument bounded delivery+asset queries with zero signed-grant calls, report p50/p95/p99/query/failure counts, and report unmet profile prerequisites as skipped rather than passing evidence.

### Implementation for User Story 3

- [X] T060 [P] [US3] Create `MediBridge.Services/DTOs/Messaging/DoctorTodayMessageDtos.cs` with `TodayInboxDto` containing `BusinessDateEgypt`, Items, and nullable `NextCursor`, plus `TodayMessageDto`, `DeliveryAssetDto`, and `DeliveryAssetAccessGrantDto` matching the OpenAPI Pascal-case fields exactly; add an internal opaque cursor payload for Doctor id, captured business date, and last `DeliveredAtUtc, Id`, build `AccessPath` from ids rather than storage data, and cap each page—not the reachable inbox—at 100.
- [X] T061 [P] [US3] Create `MediBridge.Services/Interfaces/IDoctorMessageService.cs` with `GetTodayInboxAsync(actorUserId, pageSize, cursor, cancellationToken)` and `CreateDeliveryAssetAccessGrantAsync(actorUserId, deliveryId, fileId, cancellationToken)`; default page size is 50, valid range is 1-100, and no role or Doctor id parameter may be spoofed by callers.
- [X] T062 [US3] Implement `GetTodayInboxAsync` in `MediBridge.Services/Services/DoctorMessageService.cs`: resolve an Approved, active, undeleted Doctor from authenticated user id; capture one Cairo snapshot; validate/decode the opaque cursor against that Doctor and business date; load `PageSize + 1` owned current-date projections by persisted activation instant `DeliveredAtUtc, Id`; return at most `PageSize` with nullable `NextCursor`; load approved active assets for distinct campaign ids in the returned page through one bounded query; group/order DTOs; return empty success; never order by `CreatedAtUtc` or create signed grants/audit rows during inbox listing.
- [X] T063 [US3] Extend `MediBridge.Services/Interfaces/IFileWorkflowService.cs` and `MediBridge.Services/Services/FileWorkflowService.cs` with a delivery-scoped grant method that verifies the T013 authorization projection for actor Doctor/current Cairo date/delivery/campaign/file, requires Approved and available asset state, reuses `IFileStorageProvider` for a 10-minute URL, and writes existing `FileAccessGrantAudit` plus safe `AuditEvent` records without exposing raw provider errors.
- [X] T064 [P] [US3] Create `MediBridge.Services/Interfaces/Phase7WorkflowExceptions.cs` for BadRequest, Forbidden, NotFound, Conflict, and StorageUnavailable outcomes and update `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs` to map them to 400/403/404/409/503 standard envelopes; use BadRequest for malformed, cross-doctor, and stale-date inbox cursors without exposing protected delivery existence, conceal whether a cross-doctor delivery/file exists, and never include provider diagnostics.
- [X] T065 [US3] Create thin `MediBridge.APIs/Controllers/DoctorMessagesController.cs` at route `api/doctor/messages`, apply Doctor authorization, obtain actor id only from `ICurrentUserContext`, bind optional `PageSize` and `Cursor`, delegate validation and paging to `IDoctorMessageService`, return 200 envelopes/messages matching the contract, and implement no cursor decoding, date, ownership, file, queue, or wallet logic.
- [X] T066 [US3] Add `Phase7DoctorMessagesRead` to the policy names/defaults in `MediBridge.APIs/Config/RateLimitOptions.cs`, bind its settings in both `MediBridge.APIs/appsettings.json` and `MediBridge.APIs/appsettings.Development.json`, register it in `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`, and apply it to both controller actions so the contract's 429 response is real and user-partitioned.
- [X] T067 [US3] Register `IDoctorMessageService` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` and add any Doctor-only authorization-policy constant needed in `MediBridge.APIs/Security/AuthorizationPolicies.cs`; reuse existing JWT authentication and do not add a second authentication scheme.
- [X] T068 [US3] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorTodayMessages"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7TodayInbox|FullyQualifiedName~Phase7DeliveryAssetAccess"`; inspect JSON payloads against the YAML contract and confirm no protected fields appear.

**Checkpoint**: User Story 3 is independently functional using seeded deliveries even if recurring jobs are disabled.

---

## Phase 6: User Story 4 - Operate and Retry Daily Cycles Safely (Priority: P2)

**Goal**: Host, schedule, recover missed current-date runs, observe, cancel, and retry the two job services safely without making scheduler coordination the financial correctness boundary or mutating business state directly at startup.

**Independent Test**: Start one and multiple configured hosts before/after 00:05 against SQL Server with missing current-date runs, inspect recurring definitions/job-run/recovery-dispatch rows, force claim-to-enqueue interruption, overlaps, failures, restarts, and cancellation, and verify one durable date/job claim, expiry-first persisted enqueue with eligible injector continuation, no direct startup business mutation, at-least-once safe recovery, Cairo schedules, bounded retries, company isolation, safe counts/logs, and no dashboard/control endpoint.

### Tests for User Story 4 — write and observe failure first

- [X] T069 [P] [US4] Create `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryJobRunIntegrationTests.cs` covering Running-to-terminal updates, stale Running-to-Interrupted recovery, Succeeded/PartiallySucceeded/Failed/Deferred classification including pre-00:05 injector deferral, recovery-dispatch Pending/Enqueued/Completed/Failed transitions including `Failed -> Pending`, proof that Failed/Pending without acknowledgement does not count as coverage, unique date/job claims under concurrent hosts, accurate counters, rowversion conflicts, cancellation, and safe 2000-character summary redaction.
- [X] T070 [P] [US4] Create `tests/integration/MediBridge.IntegrationTests/Phase7HangfireRegistrationTests.cs` asserting SQL Server storage, compatibility 1.8, `delivery` worker queue, stable ids `medibridge-expiry-cleaner`/`medibridge-daily-injector`, exact Cairo cron `0 0 * * *`/`5 0 * * *` as schedule/eligibility rather than exact-start guarantees, DST-aware timezone, bounded retry configuration, idempotent recurring registration, startup recovery before/after 00:05, expiry-first enqueue plus injector continuation using the same service-interface methods, no inline service invocation/business mutation, and absence of `/hangfire` or manual job routes in every environment.
- [X] T071 [P] [US4] Create `tests/integration/MediBridge.IntegrationTests/Phase7JobSafetyAndLoggingTests.cs` forcing cycle-level database failure, candidate-level failure, concurrent multi-host startup, interruption before and after scheduler acknowledgement, shutdown cancellation, restart, automatic retry, and explicit requeue of the persisted failed job through Hangfire's storage/client administration API; assert one durable recovery claim per date/job, Pending reconciliation, at-least-once safe job delivery, required expiry-before-injection ordering, the same service-interface methods and idempotent outcomes, zero startup delivery/queue/wallet/transaction/ledger mutation, independent progress, job-run/dispatch outcomes, and logs/records free of sensitive material.

### Implementation for User Story 4

- [X] T072 [US4] Create `MediBridge.APIs/Extensions/DeliveryJobServiceCollectionExtensions.cs` configuring Hangfire compatibility 1.8, SQL Server storage, bounded retry, and `AddHangfireServer`; create `MediBridge.APIs/Extensions/HangfireDeliveryJobEnqueuer.cs` implementing Core `IDeliveryJobEnqueuer` with `IBackgroundJobClient` to persist the same expiry/injector service-interface jobs and continuation without repository access or business mutation; register the adapter and do not map Dashboard or put domain state in Hangfire arguments.
- [X] T073 [US4] Create `MediBridge.APIs/Extensions/RecurringDeliveryJobRegistrar.cs` using `IRecurringJobManager` for exactly two scheduled interface-method jobs, and create Services-owned `MediBridge.Services/Services/DeliveryJobRecoveryCoordinator.cs` plus `MediBridge.Services/Interfaces/IDeliveryJobRecoveryCoordinator.cs`: use `IEgyptBusinessClock`, Repository + Unit of Work contracts, and Core `IDeliveryJobEnqueuer` to atomically claim missing current-date dispatches, retry Failed/Pending-unacknowledged claims without treating them as coverage, request expiry-first/eligible-injector continuation or direct injection after completed expiry, persist scheduler acknowledgement safely, tolerate at-least-once crash recovery, and never call expiry/injector services or reference Hangfire types.
- [X] T074 [US4] Register `IDeliveryJobRecoveryCoordinator` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`; wire `AddMediBridgeDeliveryJobs` before build, invoke recurring registration, then await the coordinator after build in `MediBridge.APIs/Program.cs`; keep middleware order/authentication unchanged, do not call `UseHangfireDashboard`, perform no inline expiry/injection business work, and fail startup safely when database, dispatch claim, scheduler acknowledgement, or Cairo-zone configuration is invalid so Pending work can reconcile on restart.
- [X] T075 [US4] Create `MediBridge.Services/Services/DeliveryJobRunTracker.cs` and integrate it into `MediBridge.Services/Services/DeliveryExpiryService.cs` and `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`: interrupt stale runs, persist Running before scanning, persist exactly one terminal outcome/counters after scanning, mark matching Enqueued recovery dispatch Completed from the observed job type/date outcome, mark cancellation/interruption safely, and let cycle-level exceptions remain visible to Hangfire after recording Failed without making dispatch state the business correctness boundary.
- [X] T076 [US4] Add structured logs to `DeliveryExpiryService`, `DailyDeliveryInjectorService`, `DeliveryJobRunTracker`, `RecurringDeliveryJobRegistrar`, and `DeliveryJobRecoveryCoordinator` using run/dispatch id, job type, business date, dependency, and aggregate counts only; never log raw exception `ToString`, serialized Hangfire arguments, signed URLs, storage keys, connection strings, wallet balances, Doctor/Company private content, or Reserve/Release idempotency keys.
- [X] T077 [US4] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7DeliveryJobRun|FullyQualifiedName~Phase7HangfireRegistration|FullyQualifiedName~Phase7JobSafety"`; then start multiple API hosts against the same test database before and after 00:05, interrupt one claim-to-acknowledgement path, explicitly requeue one persisted failed job through the protected Hangfire administration API used by the test harness, and confirm exactly two recurring rows, one durable claim per required date/job, expiry-first/eligible-injector continuation order, Pending reconciliation, the same service-interface methods, zero inline startup business mutations, and no duplicate delivery or financial effect.

**Checkpoint**: Both cycles are durably scheduled and observable; all four user stories are independently testable and integrated.

**Phase 6 verification (2026-07-04)**: The required red pass failed on the missing tracker, scheduler adapter/registrar, and recovery coordinator types before production implementation. The final focused T077 suite passed 15/15 against SQL Server, including concurrent shared-database recovery, one claim per date/job, exact recurring rows, pre/post-00:05 behavior, expiry-first continuation, scheduler interruption and restart reconciliation, actual Hangfire SQL storage requeue of the persisted service-interface job, safe records/logs, no mapped dashboard/manual route, and zero inline startup business mutation. Full regression passed unit 105/105, contract 122/122, and integration 342/342 with the existing opt-in SQL Server 2022 performance profile skipped; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors, and `git diff --check` reported no whitespace errors.

**Phase 6 manual Senior review (2026-07-04): PASS** — T069-T077 remain complete. Manual review confirmed Onion Architecture boundaries: scheduler/Hangfire types remain in `MediBridge.APIs`, orchestration and run tracking remain in `MediBridge.Services`, SQL Server locking and EF Core bulk transitions remain in `MediBridge.Repository`, and Core exposes infrastructure-neutral contracts only. All reviewed asynchronous paths use `await`, propagate cancellation, avoid blocking waits/sleeps and ambient local time, preserve at-least-once recovery through durable claims plus idempotent services, and keep financial correctness inside candidate transactions rather than scheduler state. Structured logs and persisted summaries contain only safe operational identifiers/counts and bounded redacted text; no dashboard or manual job-control route is mapped. Review found and removed one order-dependent test-only Hangfire logger lifetime check; the Phase 6 focused suite then passed twice consecutively (15/15 each), architecture/layering filters passed 16/16, the solution built with 0 warnings and 0 errors, and `git diff --check` passed. No Phase 7 work was started.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Remove documentation drift, prove performance/security/layering, validate migration/runtime behavior, and create final evidence without expanding scope.

- [X] T078 [P] Update `docs/backend-plan.md` Phase 7 and locked rules to say DST-aware `Africa/Cairo`, immutable campaign-submission FIFO by `CampaignSubmittedAtUtc, Id` rather than queue insertion time, all-overdue catch-up, company-scoped injector gating, terminal-cancel/temporary-queued rules, and separate delivery-asset access; remove contradictory fixed UTC+2/yesterday-only/queue-time statements while leaving later phases unchanged.
- [X] T079 [P] Create `tests/integration/MediBridge.IntegrationTests/Phase7LayeringAndScopeGuardTests.cs` proving Core has no EF/Hangfire/HTTP references, Services-owned recovery coordination depends only on Core repository/UoW/enqueue abstractions with no EF/Hangfire dependency, the APIs Hangfire enqueue adapter has no repository/business mutation access, controllers/registrar contain no business logic, startup calls only the recovery coordinator, no Dashboard/manual job/read/interact/Charge/Earn/weekly/activity/notification/analytics routes or services leaked into Phase 7, and Repository remains the only EF domain boundary.
- [X] T080 Add opt-in `tests/integration/MediBridge.IntegrationTests/Phase7ReferenceWorkloadTests.cs` for the documented Release/Server-GC/SQL Server 2022 Testcontainers profile on at least 4 dedicated vCPUs, 8 GB RAM, and SSD storage with `BatchSize=100`; seed the fixed documented 1,000-doctor/10,000-candidate eligibility/funding distribution, perform one warm-up then three clean-database measured runs, require every measured run within 5 minutes with bounded batches/stable memory-query behavior/exact limits and immutable campaign-submission order/zero duplicate effects, emit measurements, and report unmet profile prerequisites as skipped rather than passing evidence.
- [X] T081 Execute every scenario in `specs/008-delivery-expiry-jobs/quickstart.md` against a clean local/Testcontainers SQL Server; validate multi-page inbox traversal/cursor rejection and the operator runbook for infrastructure-managed read-only SQL review plus protected Hangfire requeue without exposing `/hangfire` or manual API routes; update only inaccurate commands or expected envelopes, capture the tested date/time-zone, and keep secrets/tokens/connection strings out of committed documentation.
- [X] T082 Validate migration both ways using `dotnet ef database update` from empty database to latest, downgrade to the pre-Phase-7 migration, and reapply latest; verify Queued-only authentic submission backfill/conditional null constraint/terminal-null preservation and `DeliveryRecoveryDispatches` unique date/job claim, then record that domain rows survive expected downgrade boundaries and Hangfire's internal schema is not included in EF migration files in `specs/008-delivery-expiry-jobs/tasks.md` notes.
- [X] T083 [P] Update Swagger/contract coverage in `tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs` and `tests/contract/MediBridge.ContractTests/ResponseEnvelopeSuccessContractTests.cs` so both Doctor routes appear only as designed, the inbox documents `PageSize`, `Cursor`, nullable `NextCursor`, and safe 400 responses, routes remain Doctor-secured with standard envelopes, and `/hangfire` plus manual job-control routes remain absent in every environment.
- [X] T084 Run a security/redaction review over `MediBridge.APIs`, `MediBridge.Services`, and new Phase 7 tests for raw storage locations, provider credentials, connection strings, signed URLs, raw exceptions, wallet balances, financial keys, and serialized Hangfire arguments in recovery dispatch/logs; add regression assertions to `tests/integration/MediBridge.IntegrationTests/Phase7JobSafetyAndLoggingTests.cs` for every issue found before marking the task complete.
- [X] T085 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj`, `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj`, and `dotnet build .\MediBridge.slnx`; require zero failed tests/build errors, document warnings or intentionally opt-in performance tests in `specs/008-delivery-expiry-jobs/tasks.md` notes, and re-check every constitution gate before completion.

---

## Requirement Traceability

| Specification requirements | Primary implementation tasks | Primary proof tasks |
|---|---|---|
| FR-001 Cairo clock/date | T002-T004, T009, T029-T030 | T033, T057, T070, T081 |
| FR-002-FR-002A schedule/eligibility, pre-00:05 deferral, durable missed-run recovery, and company-scoped expiry-first gate | T007, T014, T018, T020-T021, T024, T028, T031, T051-T053, T072-T075 | T032, T048, T069-T071, T077 |
| FR-003-FR-008 expiry/release/idempotency/atomicity | T008, T010, T013, T019, T023, T038-T041 | T035-T037, T042 |
| FR-009-FR-010A-FR-013 daily count/conditional campaign-submission FIFO/eligibility/uniqueness | T005, T011-T012, T015-T016, T019, T022-T023, T031-T032, T050, T052-T053 | T032, T044-T048, T055 |
| FR-014-FR-017 valid fee snapshots/reservation/ledger/atomicity | T010, T017, T027, T049, T051-T054 | T043, T047-T048, T055 |
| FR-018-FR-020 insufficient funds, terminal/temporary states, retry | T008, T012, T022, T050, T052-T053 | T044-T048, T055 |
| FR-021 company-scoped overdue gate and retry | T013, T023, T048, T052-T053, T075 | T048, T071, T077 |
| FR-022-FR-023 and FR-023A-FR-026 paginated today inbox, `DeliveredAtUtc` ordering, content, assets, ownership | T011, T013, T023, T060-T067 | T056-T059, T068, T083 |
| FR-027-FR-030 scheduled/recovery/requeue equivalence, infrastructure operator scope, job/dispatch records, isolation, safe failures | T007, T014, T018, T020-T024, T028, T039-T040, T052-T053, T069-T076, T081 | T032, T037, T069-T071, T077, T084 |
| FR-031 standard response envelope | T064-T067 | T056, T058, T068, T083 |
| FR-032 Phase 8+ exclusions | T079, T084-T085 | T079, T085 |
| SC-012 idempotent missed-schedule startup/recovery | T007, T014, T018, T020-T021, T024, T028, T031, T072-T076 | T032, T069-T071, T077, T079 |
| CA-001-CA-003 Onion/Repository/UoW boundaries | T011-T030, T039-T041, T049-T054, T060-T067, T072-T076 | T079, T085 |
| CA-004-CA-006 envelope/error/JWT security | T064-T067 | T056-T058, T068, T083-T085 |
| CA-007-CA-009 deterministic queue/wallet rules | T008-T017, T019-T027, T035-T055 | T035-T048, T055, T080, T085 |

Every FR and CA row must have at least one green proof task before T085 can be checked. If implementation changes a mapped requirement, update this table and the associated spec/plan artifacts before continuing.

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Phase 1 Setup
    ↓
Phase 2 Foundation (blocks every story)
    ├──→ Phase 3 US1 Expiry ─────────────┐
    ├──→ Phase 4 US2 Injection ──────────┼──→ Phase 6 US4 Scheduling & Operations
    └──→ Phase 5 US3 Today Inbox ────────┘                 ↓
                                                Phase 7 Polish & Full Validation
```

- **Phase 1** has no code dependency and starts immediately.
- **Phase 2** depends on Phase 1 and blocks all user stories.
- **US1, US2, and US3** may start after Phase 2. They touch some shared registration files, so a single smaller model should execute them sequentially in task-number order even though separate developers could coordinate them.
- **US4** depends on completed US1 and US2 job-service interfaces/implementations. It does not require US3 for scheduling, but complete US3 before the final integrated checkpoint.
- **Phase 7** depends on all selected user stories.

### User Story Dependencies and Independent Delivery

- **US1 (P1)**: Foundation only. Directly invoke `IDeliveryExpiryService`; no Hangfire or HTTP requirement. Recommended service-level MVP.
- **US2 (P1)**: Foundation only. Directly invoke `IDailyDeliveryInjectorService`; no dependency on US1 implementation because company gating uses authoritative overdue delivery state, though US1 is needed to clear a blocked company.
- **US3 (P1)**: Foundation only. Seed deliveries directly and test both HTTP endpoints; no scheduler dependency.
- **US4 (P2)**: Requires US1 and US2 because it schedules and tracks their service methods.

### Critical Within-Story Order

1. Create every story test and confirm expected failure.
2. Add pure DTO/contracts/policies/calculators.
3. Implement service orchestration.
4. Register services/endpoints/scheduler adapters.
5. Run the exact focused checkpoint commands.
6. Do not start the next story while the current checkpoint is red.

---

## Parallel Opportunities

### Foundation

- T007, T009, T010, T011, T014, and T020 target independent new files after T006.
- T032 and T033 target separate test projects after migration/clock implementation.
- Do not parallelize repository interface edits with their implementation edits unless the interface contract has already been committed.

### User Story 1

```text
Parallel test authoring after Foundation:
- T035 Phase7ExpiryEligibilityTests.cs
- T036 Phase7ExpiryFinancialTests.cs
- T037 DeliveryExpiryServiceTests.cs
- T038 DeliveryJobResultDto.cs + IDeliveryExpiryService.cs

Then sequentially: T039 → T040 → T041 → T042
```

### User Story 2

```text
Parallel test/pure-component authoring after Foundation:
- T043 DeliverySettlementSnapshotCalculatorTests.cs
- T044 DeliveryCandidateEligibilityPolicyTests.cs
- T045 Phase7InjectorFifoAndLimitTests.cs
- T046 Phase7InjectorEligibilityTests.cs
- T047 Phase7InjectorFinancialTests.cs
- T048 Phase7InjectorCompanyGateAndPolicyTests.cs
- T049 DeliverySettlementSnapshotCalculator.cs
- T050 DeliveryCandidateEligibilityPolicy.cs
- T051 DailyInjectorResultDto.cs + IDailyDeliveryInjectorService.cs

Then sequentially: T052 → T053 → T054 → T055
```

### User Story 3

```text
Parallel test/contract authoring after Foundation:
- T056 DoctorTodayMessagesContractTests.cs
- T057 Phase7TodayInboxIntegrationTests.cs
- T058 Phase7DeliveryAssetAccessTests.cs
- T059 Phase7TodayInboxPerformanceTests.cs
- T060 DoctorTodayMessageDtos.cs
- T061 IDoctorMessageService.cs
- T064 Phase7WorkflowExceptions.cs (middleware edit must be coordinated)

Then sequentially: T062 → T063 → T065 → T066 → T067 → T068
```

### User Story 4

```text
Parallel test authoring after US1 and US2:
- T069 Phase7DeliveryJobRunIntegrationTests.cs
- T070 Phase7HangfireRegistrationTests.cs
- T071 Phase7JobSafetyAndLoggingTests.cs

Then sequentially: T072 → T073 → T074 → T075 → T076 → T077
```

---

## Implementation Strategy

### MVP First

1. Complete T001-T005 (Setup).
2. Complete T006-T034 (Foundation) and require a green checkpoint.
3. Complete T035-T042 (US1 expiry).
4. Stop and demonstrate exact once-only Release behavior through the US1 integration tests.
5. Do not call the overall Phase 7 feature production-ready yet; scheduling and injection still remain.

### Incremental Delivery

1. **Foundation + US1**: Safe catch-up expiry/release service.
2. **Add US2**: Safe direct-invocation injector with full queue/wallet behavior.
3. **Add US3**: Doctor today inbox and approved asset access.
4. **Add US4**: Durable recurring schedules, retry/cancellation, and job-run observability.
5. **Polish**: Performance, documentation, migration, security, layering, and full-suite evidence.

### Smaller-Model Execution Discipline

- Before each task, open only the referenced design section and target files; do not redesign already resolved choices.
- After each test task, run the narrow filter and verify failure is caused by the missing behavior, not compilation damage or broken fixtures.
- After each production task, rerun the nearest focused tests; never wait until T085 to discover a basic regression.
- Preserve unrelated user changes and current migration history. Never delete/recreate migrations to make tests pass.
- Catch concurrency exceptions only where the task explicitly requires fresh-state replay classification. Never blanket-retry an entire financial mutation without reloading.
- Use exactly one captured Cairo snapshot per request/job invocation and pass timestamps/dates downward; do not read ambient time deep in repositories/entities.
- Use exactly one Unit of Work transaction per expiry/activation candidate. Never commit delivery/queue state separately from wallet transaction and ledger evidence.
- Treat Hangfire as trigger/retry infrastructure. If the database invariants are not sufficient under direct concurrent service invocation, the implementation is incomplete.
- Stop at every checkpoint. If a task reveals a genuine contradiction in spec/plan/data-model, update the design artifacts or run `/speckit.clarify`; do not guess.

---

## Notes

- `[P]` means file-level parallelism only after prerequisites; it does not waive test-first or dependency rules.
- US1-US4 labels map exactly to the four user stories in [spec.md](./spec.md).
- No task authorizes interaction/read mutation, Charge/Earn settlement, Doctor wallet credit, weekly enforcement, activity scoring, notifications, analytics, Hangfire Dashboard, or manual job-control APIs.
- Expected final task count: 85. Any added task must receive the next sequential id and update the summary/dependency notes.
- Append focused validation evidence below this line as tasks/checkpoints are completed; do not overwrite the task definitions.

**Phase 4 manual Senior review (2026-07-03): TASKS COMPLETE — HARDENING REQUESTED BEFORE PHASE 5** — T043-T055 remain complete. The Phase 4 implementation preserves the Core/Repository/Services boundaries, keeps EF Core and Hangfire out of Core/Services, captures one Cairo business-time snapshot per run, propagates cancellation tokens through asynchronous scans and candidate operations, and commits each delivery/queue/wallet/transaction/ledger mutation through one isolated Unit of Work transaction. Fresh verification passed 24/24 focused unit tests and 22/22 SQL-backed injector integration tests; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors. Senior review follow-ups: lock or otherwise concurrency-protect the Doctor/Company identity and profile rows used by eligibility revalidation so suspension/deletion cannot race a financial activation; require complete matching Reserve evidence before classifying an existing delivery/idempotency record as replay; add a same-company/different-doctor concurrency test so wallet locking is tested independently of Doctor serialization; replace the timing-only in-flight-expiry assertion with deterministic synchronization; and make the activation timestamp mandatory at the repository boundary instead of retaining an ambient `DateTime.UtcNow` fallback.

**Phase 5 verification (2026-07-03)**: T056-T068 complete. Test-first red state confirmed both Doctor message routes returned 404 before implementation. Focused contract tests passed 4/4; focused SQL Server inbox/asset integration tests passed 5/5 with the documented reference-profile performance test skipped because the opt-in environment flag/profile was unavailable; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors. JSON envelopes and DTO property sets were checked against `contracts/delivery-expiry-api.yaml`; current-day ownership, persisted activation ordering, paging/cursor scope, approved asset filtering, ten-minute audited grants, and safe provider failures are covered.

**Phase 5 manual Senior review (2026-07-03): IMPLEMENTATION TASKS COMPLETE — TEST HARDENING REQUIRED BEFORE PHASE 6** — T056-T068 remain marked complete. The production implementation preserves Onion Architecture: Core contains only contracts/read models, Repository owns EF Core and bounded no-tracking projections, Services owns Doctor/date/cursor/file-access orchestration, and the controller remains transport-only. The reviewed production path has no blocking waits or sleeps, captures Egypt business time through `IEgyptBusinessClock`, and propagates cancellation through identity, repository, storage-provider, audit, and save operations. Authorization, current-date ownership, safe envelopes, user-partitioned rate limiting, provider-error redaction, and the absence of wallet/storage internals in public DTOs are sound. Fresh review verification passed 4/4 focused contract tests and 5/5 focused SQL-backed integration tests, skipped the opt-in performance test because the reference profile was unavailable, and built the full solution with 0 warnings and 0 errors. Required hardening before Phase 6 approval: isolate or override the Doctor read rate limit in the opt-in performance host because its 20 warm-ups plus 200 measured requests exceed the Production limit of 60 requests/minute; add the required bounded delivery/asset query instrumentation and emit p50/p95/p99/query/failure measurements on successful runs; and expand T057/T058 assertions to directly cover the task-declared DST transition/future-date/default-page matrix plus cross-doctor, inactive-owner, Pending, Rejected, Quarantined, deleted, and replaced asset denials.

**Phase 5 hardening verification (2026-07-03): APPROVED FOR PHASE 6** — Root-cause analysis confirmed the production user-partitioned 60-request/minute policy was correct and the benchmark's single-Doctor 220-request allocation was invalid. Production rate limiting remains unchanged. The reference benchmark now uses four approved Doctors with equivalent 100-item inboxes and round-robin allocation (5 warm-ups + 50 measured requests = 55 requests per Doctor), preserves the full HTTP/auth/rate-limit/service/repository/SQL path, counts exactly one bounded delivery query and one bounded asset query per measured request through a thread-safe EF command interceptor, rejects signed-grant calls, and emits p50/p95/p99/query/failure metrics on successful runs. Coverage now directly proves default-50/maximum-100 paging, multi-page reachability, Cairo DST/midnight date changes, future/prior-day isolation, anonymous/Company/Admin/cross-Doctor denial, stale/malformed cursors, production 429 envelopes, and Pending/Rejected/Quarantined/deleted/replaced/unrelated/prior-day/inactive-Doctor asset denial without storage calls. The first full regression exposed two stale guards; they were narrowed to allow only the specified `ICurrentUserContext` controller dependency and exactly the two contracted Doctor-message routes while retaining all other layering/scope exclusions. Final verification: unit 105/105 passed, contract 122/122 passed, integration 327/327 passed with one opt-in reference benchmark skipped, full build succeeded with 0 warnings and 0 errors, and `git diff --check` reported no whitespace errors. The reference performance threshold remains unmeasured on this host because Docker Desktop's Linux engine is unavailable; the skip is not performance evidence.

**Phase 7 migration validation (2026-07-04)**: A disposable empty LocalDB database was updated through `20260702174103_AddPhase7DeliveryExpiryJobs` with the EF CLI, downgraded to `20260701172632_AddMockPaymentTransactionsClean`, and reapplied to latest. Direct schema checks observed the Phase 7 job table, queued-row conditional constraint, and unique recovery date/job claim after each applicable up migration and confirmed the Phase 7 job table was absent after downgrade. The focused migration suite passed 3/3, proving Queued-only authentic campaign-submission backfill, unresolved-Queued abort behavior, terminal-null preservation, and survival of pre-existing Doctor/company/campaign/queue domain rows across downgrade/reapply. Phase 7 migration source and the model snapshot contain no Hangfire internal-schema references.

**Phase 7 final verification (2026-07-04)**: The exact T085 commands passed on the final Phase 7 state: unit 105/105, contract 123/123, and integration 351/351, with the two intentionally opt-in SQL Server 2022 reference performance tests skipped because the documented Release/Server-GC/dedicated-CPU/8-GB/SSD profile was not enabled. Those skips are not performance evidence. The full solution build succeeded with 0 warnings and 0 errors. Constitution gates were re-checked through the green layering/scope guards, controller/Swagger/authorization/envelope tests, repository and isolated-transaction tests, global exception/redaction tests, and deterministic FIFO/wallet/idempotency suites: Onion dependencies point inward, controllers remain transport-only, EF remains in Repository, service persistence uses Repository + Unit of Work, Doctor routes use JWT role-aware authorization and standard envelopes, exceptions are safely transformed/redacted, and queue/wallet rules remain deterministic. No Phase 8+ route or service was added.

**Phase 7 manual Senior AI review (2026-07-04): IMPLEMENTATION TASKS COMPLETE — HARDENING REQUIRED BEFORE PRODUCTION APPROVAL** — T078-T085 remain complete. Manual review was performed without CodeRabbit across Core, Repository, Services, APIs, migration, scheduling/recovery, financial idempotency, cancellation, authorization, and Phase 7 tests. Architecture is sound: Core remains infrastructure-free, Repository owns EF/SQL locking, candidate financial mutations use isolated Unit of Work transactions, controllers are thin, startup invokes only recovery coordination, and retry/concurrency correctness is enforced by row locks plus database uniqueness. Fresh focused verification passed 37/37 unit, 6/6 contract, and 77/77 SQL-backed integration tests; the two opt-in reference performance tests were skipped and remain non-evidence; the solution built with 0 warnings and 0 errors. Required hardening before production approval: map provider-originated `OperationCanceledException`/timeouts to the contracted safe 503 while propagating only caller cancellation; strengthen expiry replay classification to verify delivery Expired/Released state plus ledger currency and release idempotency keys; preserve partial counters when a job is interrupted after committed candidates instead of recording all zeroes; and use the invocation's single captured Cairo snapshot on exceptional completion rather than calling `Capture()` again. No Phase 8 work is authorized by this review.

**Phase 7 Senior hardening completion (2026-07-04): COMPLETE — APPROVED FOR REVIEW** — The four manual-review findings above are resolved without changing Phase 7 requirements or adding later-phase behavior. Storage-provider timeouts and provider-originated cancellation now use the safe 503 contract while caller cancellation still propagates. Expiry replay requires the persisted Expired/Released delivery state, matching Release transaction, deterministic idempotency key, and exact EGP ledger pair; injector replay likewise requires Active/Reserved state and matching Reserve evidence. Both jobs now retain committed candidate counters across interruption/failure and use one invocation-owned Cairo snapshot and elapsed clock for every terminal path. Test-first evidence captured the previous failures before each fix. Final verification passed unit 108/108, contract 123/123, and integration 353/353; the two documented opt-in SQL Server 2022 reference performance tests were skipped and remain non-performance evidence. `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors, and `git diff --check` reported no whitespace errors. T001-T085 remain checked complete; Phase 7 stops here pending review, with no Phase 8 work added.
