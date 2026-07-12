# Tasks: Phase 9 Activity & Weekly Enforcement

**Input**: Design documents from `/specs/011-activity-weekly-enforcement/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/activity-weekly-enforcement-api.yaml](./contracts/activity-weekly-enforcement-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required. The Phase 9 specification, plan, data model, contract, and quickstart require unit, contract, SQL Server integration, migration, concurrency/idempotency, authorization, audit-safety, no-wallet/no-queue mutation, and performance-oriented validation. In every user-story phase, write the tests first, run the focused filter, and confirm the new tests fail for the expected missing behavior before implementing production tasks.

**Constitution Note**: Keep entities, enums, read models, repository contracts, and domain decisions in `MediBridge.Core`; SQL Server, EF Core configuration, row locking, repositories, and migrations in `MediBridge.Repository`; score/enforcement/admin orchestration in `MediBridge.Services`; and HTTP/Hangfire wiring in `MediBridge.APIs`. Controllers must stay HTTP-only. Every secured response uses `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; errors flow through global middleware without raw stack traces, sensitive account metadata, storage details, wallet internals, or internal job infrastructure details.

**Execution Rule for a Smaller Model**: Execute tasks strictly in numeric order unless the task is explicitly marked `[P]` and every listed dependency is already complete. Do not combine tasks. Do not rename planned types casually. Do not move EF Core into Services or APIs. Do not add queue, wallet, settlement, reporting, withdrawal, notification, external payment-gateway, campaign moderation, or public Hangfire dashboard behavior. If a task appears to require one of those changes, stop and update the design artifacts first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to execute concurrently after its phase prerequisites because it targets different files and does not consume an unfinished symbol.
- **[Story]**: User-story traceability label. Setup, foundational, and polish tasks intentionally have no story label.
- Every task names exact target file(s) or directory and states the observable completion condition.

---

## Phase 1: Setup (Shared Preparation)

**Purpose**: Prepare explicit Phase 9 fixtures, contract references, and implementation targets before runtime behavior changes.

- [X] T001 Verify existing Phase 7/8 surfaces and record any missing prerequisites in `specs/011-activity-weekly-enforcement/tasks.md`: inspect `MediBridge.Core/Entities/Profiles/DoctorProfile.cs`, `MediBridge.Core/Entities/Messaging/DoctorAdDelivery.cs`, `MediBridge.Core/Enums/Phase3DomainEnums.cs`, `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`, `MediBridge.Core/Interfaces/Identity/IProfileRepository.cs`, `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs`, `MediBridge.Repository/Data/MediBridgeDbContext.cs`, `MediBridge.Services/Services/EgyptBusinessClock.cs`, `MediBridge.Services/Services/AdminDeliveryJobService.cs`, `MediBridge.APIs/Controllers/AdminPricingController.cs`, and `MediBridge.APIs/Controllers/AdminDeliveryJobsController.cs`.
  - Evidence 2026-07-12: all listed files exist. Existing prerequisites include `DoctorProfile` with `DailyMessageLimit`, `MinimumWeeklyRequirement`, `ActivityScore`, `Status`, soft-delete fields; `DoctorAdDelivery` with Cairo delivery date, delivered/interacted timestamps, status, feedback, settlement fields; shared enums for marketplace, delivery, reservation, job run statuses; UoW and repository abstractions; EF DbContext; Cairo business clock; Admin pricing/job controllers and services. Missing Phase 9-specific prerequisites to implement in Phase 2: doctor suspension timestamp fields/methods, Phase 9 enums/entities/repositories/job-run types, Phase 9 delivery/profile aggregate methods, and safe Phase 9 workflow exception mappings. Existing `MediBridge.Core.Entities.Policies.ActivityScoreHistory` name collides with planned `MediBridge.Core.Entities.Profiles.ActivityScoreHistory`; Phase 2 EF/context code must use explicit namespaces.
- [X] T002 [P] Create `tests/unit/MediBridge.UnitTests/Phase9ActivityScoreTestData.cs` with deterministic helper records for score dates, Cairo date windows, delivered/interacted/feedback counts, response-time samples, expected sub-scores, final score rounding, and approved/suspended/deleted doctor states; helpers must not call production services.
- [X] T003 [P] Create `tests/unit/MediBridge.UnitTests/Phase9WeeklyEnforcementTestData.cs` with deterministic helper records for Monday week boundaries, Accept/Reject counts, suspension overlap ranges, rolling 8-week violation sets, and expected warning/action eligibility; helpers must not call production services.
- [X] T004 [P] Create `tests/integration/MediBridge.IntegrationTests/Phase9TestHelpers.cs` with narrowly named SQL Server fixture helpers for approved/non-deleted doctors, unapproved doctors, deleted doctors, suspended doctors with explicit `SuspendedAtUtc`/`SuspendedUntilUtc`, deliveries with explicit `DeliveryDateEgypt`/`DeliveredAtUtc`/`InteractedAtUtc`/`Status`/feedback, activity score snapshots, weekly decisions, weekly violations, enforcement actions, and row-count snapshots for wallet/queue/delivery no-mutation assertions.
- [X] T005 [P] Create `tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementContractTests.cs` with OpenAPI fixture constants for `specs/011-activity-weekly-enforcement/contracts/activity-weekly-enforcement-api.yaml`; include route constants for `GET /api/admin/violations`, `PUT /api/admin/doctors/{doctorId}/status`, `GET /api/admin/activity-jobs/status`, `POST /api/admin/activity-jobs/run-score`, and `POST /api/admin/activity-jobs/run-weekly-enforcement`.
- [X] T006 Run `dotnet restore .\MediBridge.slnx` and `dotnet build .\MediBridge.slnx`; record any pre-existing failures in `specs/011-activity-weekly-enforcement/tasks.md` before changing production code.
  - Evidence 2026-07-12: `dotnet restore .\MediBridge.slnx` succeeded; `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors before Phase 9 production code changes.
  - Phase 1 validation evidence 2026-07-12: after fixture/helper creation and Senior review adjustment, `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors; `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --no-restore` passed 148/148; `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --no-restore --no-build` passed when rerun with a 300s timeout.

**Checkpoint**: Phase 9 fixture files and baseline evidence exist; runtime behavior is unchanged.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared enums, entities, repository contracts, EF Core mappings, migrations, DTOs, validators, service interfaces, DI registrations, and exception mappings required by all user stories.

**CRITICAL**: Complete every task in this phase before starting any user-story implementation.

- [X] T007 [P] Add Phase 9 enum values to `MediBridge.Core/Enums/Phase3DomainEnums.cs`: `ActivityScoreCalculationMode` with `DefaultNoDeliveries = 1`, `ZeroInteractions = 2`, `Calculated = 3`; `WeeklyEnforcementDecisionType` with `Compliant = 1`, `SuspensionSkipped = 2`, `Violation = 3`; `DoctorEnforcementActionType` with `Warn = 1`, `ReduceDailyLimit = 2`, `Suspend = 3`, `Reactivate = 4`, `AutomaticReactivate = 5`; `ActivityEnforcementJobType` with `DailyActivityScore = 1`, `WeeklyEnforcement = 2`, `SuspensionExpiry = 3`; and `ActivityEnforcementJobRunStatus` matching existing job-run statuses `Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Deferred`, `Interrupted` with stable explicit integers.
- [X] T008 Extend `MediBridge.Core/Entities/Profiles/DoctorProfile.cs` with nullable UTC fields `SuspendedAtUtc`, `SuspendedUntilUtc`, and `LastStatusChangedAtUtc`; add domain methods `ApplyWarning`, `ReduceDailyLimit`, `SuspendUntil`, `Reactivate`, and `ApplyActivityScore` that validate UTC timestamps, future suspension expiry, non-negative limits, score range 0.0-100.0, and never mutate delivery/wallet/queue state.
- [X] T009 [P] Create `MediBridge.Core/Entities/Profiles/ActivityScoreHistory.cs` with fields from `data-model.md`, constants for score bounds, validation for non-negative counts, score range, one-decimal final score expectation, `WindowEndDateEgypt = ScoreDateEgypt.AddDays(-1)`, `WindowStartDateEgypt = ScoreDateEgypt.AddDays(-30)`, and calculation-mode invariants for no deliveries and zero interactions.
- [X] T010 [P] Create `MediBridge.Core/Entities/Profiles/WeeklyEnforcementDecision.cs` with fields from `data-model.md`, validation that `WeekStartDateEgypt` is Monday, `WeekEndDateEgypt = WeekStartDateEgypt.AddDays(7)`, counts are non-negative, `SuspensionSkipped` requires `SuspensionOverlapped = true`, `Violation` requires no suspension overlap and `InteractionCount < MinimumWeeklyRequirement`, and `Compliant` requires no suspension overlap and `InteractionCount >= MinimumWeeklyRequirement`.
- [X] T011 [P] Create `MediBridge.Core/Entities/Profiles/DoctorWeeklyViolation.cs` with fields from `data-model.md`, validation that `InteractionCount < MinimumWeeklyRequirement`, immutable week fields, rolling count evidence, optional audit id, and no methods that edit historical violation facts.
- [X] T012 [P] Create `MediBridge.Core/Entities/Profiles/DoctorEnforcementAction.cs` with fields from `data-model.md`, validation for Admin-required reason, `Suspend` requiring future `SuspendedUntilUtc`, `ReduceDailyLimit` requiring `NewDailyMessageLimit`, `Reactivate` requiring previous suspended status, and `AutomaticReactivate` requiring `EffectiveAtUtc >= SuspendedUntilUtc`.
- [X] T013 [P] Create `MediBridge.Core/Entities/Messaging/ActivityEnforcementJobRun.cs` with fields from `data-model.md`, status enum conversion support, non-negative counters, target-date requirement for `DailyActivityScore`, target-week requirement for `WeeklyEnforcement`, bounded safe failure summary, and no sensitive metadata fields.
- [X] T014 [P] Create `MediBridge.Core/Interfaces/Messaging/ActivityEnforcementReadModels.cs` containing immutable records for `ActivityScoreAggregateReadModel`, `WeeklyInteractionCountReadModel`, `ViolationSummaryReadModel`, `ActivityJobRunCounters`, `ActivityJobStatusReadModel`, and `DoctorSuspensionOverlapReadModel`; include no EF Core, SQL, ASP.NET, or Hangfire types.
- [X] T015 Extend `MediBridge.Core/Interfaces/Identity/IProfileRepository.cs` with methods to page approved/non-deleted doctor ids for activity scoring, page approved/non-deleted doctor ids for weekly enforcement, find a doctor profile for enforcement update by id with lock, find expired suspended doctors for automatic reactivation, apply current activity score, apply enforcement state changes, and check whether suspension overlaps an Egypt week.
- [X] T016 Extend `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs` with score-window aggregate methods for delivered count, interacted count, feedback-qualified count, response-speed contribution sum/count by doctor and Cairo date range, plus weekly Accept/Reject interaction count by doctor and Cairo week boundaries; methods must return projections and never mutate `DoctorAdDelivery`.
- [X] T017 [P] Create `MediBridge.Core/Interfaces/Profiles/IActivityScoreHistoryRepository.cs` with `FindByDoctorAndDateAsync`, `AddAsync`, `ListForDoctorAsync`, and `ExistsAsync` methods using `DoctorId` and `ScoreDateEgypt` only; expose no `IQueryable` or EF types.
- [X] T018 [P] Create `MediBridge.Core/Interfaces/Profiles/IWeeklyEnforcementRepository.cs` with methods for finding/adding weekly decisions, finding/adding weekly violations, querying rolling 8-week violation counts, and paged Admin violation summaries; expose no `IQueryable` or EF types.
- [X] T019 [P] Create `MediBridge.Core/Interfaces/Profiles/IDoctorEnforcementActionRepository.cs` with methods to add enforcement actions, list recent actions for a doctor, find latest action per doctor for violation summaries, and query automatic reactivation evidence; expose no EF types.
- [X] T020 [P] Create `MediBridge.Core/Interfaces/Messaging/IActivityEnforcementJobRunRepository.cs` with methods to interrupt stale running rows, add running row, complete a terminal outcome with counters, list recent runs, and find existing target-date/target-week runs.
- [X] T021 Add `IActivityScoreHistoryRepository`, `IWeeklyEnforcementRepository`, `IDoctorEnforcementActionRepository`, and `IActivityEnforcementJobRunRepository` properties to `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`; keep transaction method signatures unchanged and do not expose `DbContext`.
- [X] T022 [P] Create `MediBridge.Services/DTOs/Admin/ActivityEnforcementDtos.cs` containing `ViolationSummaryDto`, `ViolationSummaryPageDto`, `LastEnforcementActionDto`, `DoctorEnforcementActionRequestDto`, `DoctorEnforcementActionResultDto`, `ActivityJobRunDto`, `RunDailyActivityScoreRequestDto`, and `RunWeeklyEnforcementRequestDto` with Pascal-case properties matching `contracts/activity-weekly-enforcement-api.yaml`.
- [X] T023 [P] Create `MediBridge.Services/Validators/Admin/DoctorEnforcementActionRequestValidator.cs` to require `ActionType` and a trimmed reason between 10 and 1000 characters, require `NewDailyMessageLimit` only for `ReduceDailyLimit`, require future UTC `SuspendedUntilUtc` only for `Suspend`, reject extra action-specific values when not applicable, and reject negative limits.
- [X] T024 [P] Create `MediBridge.Services/Validators/Admin/ActivityJobRequestValidators.cs` validating `RunDailyActivityScoreRequestDto.ScoreDateEgypt` is a completed Cairo score date and `RunWeeklyEnforcementRequestDto.WeekStartDateEgypt` is a Monday completed week start.
- [X] T025 [P] Create `MediBridge.Services/Interfaces/IActivityScoreService.cs` with methods `RunDailyScoreAsync(DateOnly? scoreDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken)` and `ExpireSuspensionsAsync(string? requestedByAdminUserId, CancellationToken cancellationToken)`.
- [X] T026 [P] Create `MediBridge.Services/Interfaces/IWeeklyEnforcementService.cs` with method `RunWeeklyEnforcementAsync(DateOnly? weekStartDateEgypt, string? requestedByAdminUserId, CancellationToken cancellationToken)`.
- [X] T027 [P] Create `MediBridge.Services/Interfaces/IAdminActivityEnforcementService.cs` with methods for `ListViolationsAsync`, `ApplyDoctorEnforcementActionAsync`, `ListJobStatusAsync`, `RunDailyScoreAsync`, and `RunWeeklyEnforcementAsync`.
- [X] T028 [P] Create `MediBridge.Services/Interfaces/Phase9WorkflowExceptions.cs` with safe exception types for validation, forbidden, not found, conflict, and service-unavailable outcomes; each exception must carry a safe message only and no stack/raw persistence details in response payloads.
- [X] T029 Update `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs` to map `Phase9WorkflowExceptions.cs` exception types to the standard envelope with HTTP 400/403/404/409/503 as appropriate; do not expose raw stack traces, SQL text, Hangfire ids, wallet internals, or protected doctor details.
- [X] T030 Create EF Core configuration classes in `MediBridge.Repository/Configurations/Identity/ActivityEnforcementConfigurations.cs` for `ActivityScoreHistory`, `WeeklyEnforcementDecision`, `DoctorWeeklyViolation`, and `DoctorEnforcementAction` with enum conversions, decimal precision, max lengths, unique constraints, indexes, check constraints, restricted FKs, and no cascade deletion of history.
- [X] T031 Create EF Core configuration class in `MediBridge.Repository/Configurations/Messaging/ActivityEnforcementJobRunConfiguration.cs` for `ActivityEnforcementJobRun` with enum conversions, counter checks, safe summary max length 2000, target date/week indexes, and no cascade into business entities.
- [X] T032 Add `DbSet<ActivityScoreHistory>`, `DbSet<WeeklyEnforcementDecision>`, `DbSet<DoctorWeeklyViolation>`, `DbSet<DoctorEnforcementAction>`, and `DbSet<ActivityEnforcementJobRun>` to `MediBridge.Repository/Data/MediBridgeDbContext.cs`.
- [X] T033 Implement `ActivityScoreHistoryRepository` in `MediBridge.Repository/Repositories/Identity/ActivityScoreHistoryRepository.cs` using unique `(DoctorId, ScoreDateEgypt)` lookups and inserts; never update existing snapshot facts except through explicit replay/no-op behavior defined by service.
- [X] T034 Implement `WeeklyEnforcementRepository` in `MediBridge.Repository/Repositories/Identity/WeeklyEnforcementRepository.cs` with weekly decision insert-or-replay, violation insert-or-replay, rolling 8-week count query, and paged violation summary query using bounded projections.
- [X] T035 Implement `DoctorEnforcementActionRepository` in `MediBridge.Repository/Repositories/Identity/DoctorEnforcementActionRepository.cs` with append-only inserts and latest-action projections; never overwrite prior action rows.
- [X] T036 Implement `ActivityEnforcementJobRunRepository` in `MediBridge.Repository/Repositories/Messaging/ActivityEnforcementJobRunRepository.cs` with running/complete/list/replay operations and safe summary truncation.
- [X] T037 Implement the T015 profile repository methods in `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs` using joins to identity user approval/account status, `UPDLOCK, ROWLOCK` only for update paths, and no approval inference from `DoctorMarketplaceStatus` alone.
- [X] T038 Implement the T016 delivery aggregate methods in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs` using Cairo `DateOnly` ranges, Accepted/Rejected status filters, feedback non-whitespace length >= 15, response time from `InteractedAtUtc - DeliveredAtUtc`, and no mutation/tracking for aggregate reads.
- [X] T039 Wire the four new repositories through `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs` and register them in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T040 Generate EF Core migration `AddPhase9ActivityWeeklyEnforcement` in `MediBridge.Repository/Migrations/` and update `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`; migration must add doctor suspension fields, Phase 9 tables, indexes, unique constraints, checks, and FKs, and must not alter wallet balances, wallet transactions, wallet ledgers, queue rows, delivery settlement fields, campaign statuses, or payment records.
  - Evidence 2026-07-12: migration generated as `AddPhase9ActivityWeeklyEnforcement`; the Phase 9 score snapshot table is named `DoctorActivityScoreHistories` to avoid collision with the existing Phase 3 policy `ActivityScoreHistories` table.
- [X] T041 [P] Add SQL Server migration tests in `tests/integration/MediBridge.IntegrationTests/Phase9MigrationTests.cs` proving Phase 9 tables/columns/indexes/unique constraints/checks/FKs exist, duplicate score snapshots fail, duplicate weekly decisions fail, duplicate weekly violations fail, and pre-existing Phase 1-8 schema constraints still exist.
- [X] T042 Register only foundational Phase 9 validators and repository implementations in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` and `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`; leave story-specific service registrations to T052, T061, T070, and T083; ensure Services receives only Core abstractions and never Repository implementation types except via DI registration extension boundaries.
- [X] T043 Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9Migration"` and `dotnet build .\MediBridge.slnx`; fix only foundational failures before starting US1.
  - Evidence 2026-07-12: initial migration tests failed because Phase 9 FK columns were configured as `nvarchar(64)` while existing `DoctorProfiles.Id`/`AuditEvents.Id` principals are `nvarchar(450)`. Root cause fixed in EF configuration, migration regenerated, and optional job-run FKs/timezone/UTC validation were corrected during Senior review. `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9Migration"` passed 3/3; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors.

**Checkpoint**: Shared Phase 9 schema, contracts, DTOs, validators, repository abstractions/implementations, DI wiring, and safe exception mappings exist; no story behavior is exposed through controllers yet.

---

## Phase 3: User Story 1 - Recalculate Doctor Activity Scores Daily (Priority: P1) MVP

**Goal**: Calculate and persist a current Activity Score plus one immutable history snapshot for each approved, non-deleted doctor using the last 30 completed Africa/Cairo calendar days before the score date.

**Independent Test**: Seed doctors with no deliveries, zero interactions, partial interactions, feedback-qualified interactions, and suspended status in the score window; run daily scoring for a score date; verify exact sub-scores/final score/current doctor score/history snapshot and company search visibility without wallet/queue/delivery mutation.

### Tests for User Story 1 - write and observe failure first

- [X] T044 [P] [US1] Add unit tests for activity score date-window calculation in `tests/unit/MediBridge.UnitTests/Phase9ActivityScoreTests.cs`: verify score date excludes itself, window start/end cover exactly 30 completed Cairo calendar days, host-local time is ignored, and DST boundaries use `IEgyptBusinessClock`.
- [X] T045 [P] [US1] Add unit tests for score formula in `tests/unit/MediBridge.UnitTests/Phase9ActivityScoreTests.cs`: verify no deliveries -> 95.0, deliveries with zero interactions -> 0.0, response speed contribution clamps 0-100, engagement formula, feedback >= 15 non-whitespace rule, weighted final score, one-decimal rounding, and final clamp 0.0-100.0.
- [X] T046 [P] [US1] Add SQL Server integration tests in `tests/integration/MediBridge.IntegrationTests/Phase9ActivityScoreIntegrationTests.cs` covering approved/non-deleted candidate selection, suspended doctors included, deleted/unapproved doctors excluded, one snapshot per doctor/date, replay/concurrent run no duplicates, current `DoctorProfile.ActivityScore` update, and no mutation to `DoctorAdDeliveries`, `DoctorMessageQueues`, `Wallets`, `WalletTransactions`, or `WalletLedgerEntries`.
- [X] T047 [P] [US1] Add integration test in `tests/integration/MediBridge.IntegrationTests/Phase9CompanyDoctorSearchScoreIntegrationTests.cs` proving `GET /api/company/doctors` uses the latest committed `DoctorProfile.ActivityScore` after scoring and never exposes partial sub-scores during concurrent score updates.

### Implementation for User Story 1

- [X] T048 [US1] Create `MediBridge.Services/Services/ActivityScoreCalculator.cs` as a pure calculation helper for response speed, engagement, feedback, final score, calculation mode, date-window derivation, and rounding; do not inject repositories or use ambient time in this helper.
- [X] T049 [US1] Create `MediBridge.Services/Services/ActivityScoreService.cs` implementing `IActivityScoreService.RunDailyScoreAsync`: capture Cairo score date, call automatic suspension expiry at job start, page approved/non-deleted doctors, read delivery aggregates, create/replay one `ActivityScoreHistory`, update `DoctorProfile.ActivityScore`, and commit each doctor candidate atomically through `IDomainUnitOfWork.ExecuteIsolatedInTransactionAsync`.
- [X] T050 [US1] Implement `IActivityScoreService.ExpireSuspensionsAsync` in `MediBridge.Services/Services/ActivityScoreService.cs`: find suspended approved/non-deleted doctors whose `SuspendedUntilUtc <= nowUtc`, lock each profile, set status Active, add `DoctorEnforcementAction` of `AutomaticReactivate`, add safe audit evidence, and commit each candidate atomically.
- [X] T051 [US1] Add logging to `MediBridge.Services/Services/ActivityScoreService.cs` with job run id, score date, processed/skipped/created/updated/failed counts only; do not log doctor private data, raw exception `ToString`, wallet values, delivery content, or SQL details.
- [X] T052 [US1] Register `ActivityScoreCalculator` and `IActivityScoreService` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T053 [US1] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase9ActivityScore"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9ActivityScore|FullyQualifiedName~Phase9CompanyDoctorSearchScore"`; record the US1 checkpoint in `specs/011-activity-weekly-enforcement/tasks.md`.
  - Evidence 2026-07-12: TDD pre-run failed on missing calculator/service behavior and an invalid delivery fixture FK; fixture root cause fixed by seeding real company/campaign rows. Senior review found a formula bug where final score used rounded sub-scores, producing 62.0 instead of 61.9; fixed by combining raw sub-score values and rounding final score only. Cancellation propagation was also fixed so interrupted runs are not reported as ordinary candidate failures. `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase9ActivityScore"` passed 6/6; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9ActivityScore|FullyQualifiedName~Phase9CompanyDoctorSearchScore"` passed 2/2; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors.

**Checkpoint**: User Story 1 is independently functional and is the MVP slice.

---

## Phase 4: User Story 2 - Enforce Weekly Interaction Minimums (Priority: P1)

**Goal**: Evaluate the last completed Monday-to-Monday Cairo week for each eligible approved, non-deleted doctor; create exactly one compliant, suspension-skip, or violation decision per doctor/week; expose rolling 8-week violation evidence for later Admin review.

**Independent Test**: Seed approved/non-deleted doctors with different minimum weekly requirements, Accept/Reject counts, reads-only activity, zero requirement, and suspension overlap; run weekly enforcement; verify decisions, violations, rolling count, and no duplicates under retry/concurrency.

### Tests for User Story 2 - write and observe failure first

- [X] T054 [P] [US2] Add unit tests for weekly date-window and eligibility logic in `tests/unit/MediBridge.UnitTests/Phase9WeeklyEnforcementTests.cs`: Monday boundary, interaction exactly at Monday 00:00 belongs to new week, minimum requirement 0 is compliant, reads/views/expired/active/queued messages do not count, and any suspension overlap skips violation.
- [X] T055 [P] [US2] Add unit tests for rolling 8-week classification in `tests/unit/MediBridge.UnitTests/Phase9WeeklyEnforcementTests.cs`: counts 1-5 produce warning-stage, greater than 5 produces action-eligible, older than 8 completed weeks excluded, historical rows preserved.
- [X] T056 [P] [US2] Add SQL Server integration tests in `tests/integration/MediBridge.IntegrationTests/Phase9WeeklyEnforcementIntegrationTests.cs` covering compliant, violation, suspension-skipped, unapproved/deleted excluded, suspended overlap skipped, weekly decision uniqueness, violation uniqueness, retry replay, concurrent enforcement, and safe no-wallet/no-queue/no-delivery mutation.

### Implementation for User Story 2

- [X] T057 [US2] Create `MediBridge.Services/Services/WeeklyEnforcementPolicy.cs` as a pure helper deriving completed week start/end, classifying compliant/suspension-skip/violation, validating Monday week starts, computing rolling 8-week window starts, and determining warning/action eligibility; do not inject repositories or use ambient time.
- [X] T058 [US2] Create `MediBridge.Services/Services/WeeklyEnforcementService.cs` implementing `IWeeklyEnforcementService.RunWeeklyEnforcementAsync`: capture target week, call automatic suspension expiry at job start, page approved/non-deleted doctors, skip any doctor with suspension overlap, count Accept/Reject interactions only, create/replay `WeeklyEnforcementDecision`, create/replay `DoctorWeeklyViolation` only for violations, compute rolling count, and commit each doctor candidate atomically.
- [X] T059 [US2] Add safe audit evidence for new weekly violation decisions in `MediBridge.Services/Services/WeeklyEnforcementService.cs`; metadata may include doctor id, week start/end, requirement, interaction count, rolling count, and correlation id, but no delivery content, wallet values, stack traces, or private account metadata.
- [X] T060 [US2] Add logging to `MediBridge.Services/Services/WeeklyEnforcementService.cs` with job run id, week start, processed/skipped/created/failed counts only; do not log protected doctor details or raw exception text.
- [X] T061 [US2] Register `WeeklyEnforcementPolicy` and `IWeeklyEnforcementService` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T062 [US2] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase9WeeklyEnforcement"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9WeeklyEnforcement"`; record the US2 checkpoint in `specs/011-activity-weekly-enforcement/tasks.md`.
  - Evidence 2026-07-12: TDD pre-run failed on missing `WeeklyEnforcementPolicy` and missing `IWeeklyEnforcementService` registration. Implemented pure weekly policy, weekly enforcement service, safe violation audit evidence, idempotent decision/violation replay, and safe logging. `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase9WeeklyEnforcement"` passed 11/11; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9WeeklyEnforcement"` passed 1/1; `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors.

**Checkpoint**: User Story 2 is independently functional using seeded doctors and deliveries even before Admin endpoints are added.

---

## Phase 5: User Story 3 - Review Violations and Apply Admin Actions (Priority: P2)

**Goal**: Provide Admin-only violation review and manual enforcement actions for warning, daily-limit reduction, temporary suspension with `SuspendedUntilUtc`, and manual reactivation with audit evidence.

**Independent Test**: Authenticate as Admin, list violation summaries, apply warning/reduce/suspend/reactivate actions with required reasons, and verify doctor profile changes, action records, audit events, envelopes, authorization, and history preservation.

### Tests for User Story 3 - write and observe failure first

- [X] T063 [P] [US3] Add contract tests in `tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementContractTests.cs` for `GET /api/admin/violations`: Admin success envelope, pagination fields, filters `doctorId/status/eligibility/weekFrom/weekTo/minRollingViolations`, safe 401/403 envelopes, max `PageSize = 100`, and no protected wallet/delivery internals in response JSON.
- [X] T064 [P] [US3] Add contract tests in `tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementContractTests.cs` for `PUT /api/admin/doctors/{doctorId}/status`: required Admin JWT, required reason, `Warn`, `ReduceDailyLimit`, `Suspend`, `Reactivate`, future `SuspendedUntilUtc`, invalid action-specific payloads, 400/401/403/404/409 envelopes, and response fields matching the OpenAPI contract.
- [X] T065 [P] [US3] Add SQL Server integration tests in `tests/integration/MediBridge.IntegrationTests/Phase9AdminViolationReviewIntegrationTests.cs` covering rolling summaries, warning/action eligibility, pagination/filter correctness, latest enforcement action projection, automatic suspension expiry before read, and no cross-role access.
- [X] T066 [P] [US3] Add SQL Server integration tests in `tests/integration/MediBridge.IntegrationTests/Phase9AdminEnforcementActionIntegrationTests.cs` covering warning, reduce daily limit, suspend until future UTC, manual reactivation before expiry, invalid transition conflicts, required reason, audit/action persistence, prior history preservation, and no wallet/queue/delivery mutation.

### Implementation for User Story 3

- [X] T067 [US3] Implement `IAdminActivityEnforcementService.ListViolationsAsync` in `MediBridge.Services/Services/AdminActivityEnforcementService.cs`: validate pagination, run automatic suspension expiry first, query paged summaries, compute eligibility from rolling counts, map DTOs, and return no raw persistence objects or protected wallet/delivery fields.
- [X] T068 [US3] Implement `IAdminActivityEnforcementService.ApplyDoctorEnforcementActionAsync` in `MediBridge.Services/Services/AdminActivityEnforcementService.cs`: validate request with `DoctorEnforcementActionRequestValidator`, lock doctor profile, enforce valid current-state transitions, apply warning/reduce/suspend/reactivate domain method, add `DoctorEnforcementAction`, add safe `AuditEvent`, commit atomically, and return `DoctorEnforcementActionResultDto`.
- [X] T069 [US3] Create `MediBridge.APIs/Controllers/AdminActivityEnforcementController.cs` with `GET api/admin/violations` and `PUT api/admin/doctors/{doctorId}/status`; apply `AuthorizationPolicies.AdminOnly`, `RateLimitPolicyNames.Envelope`, standard `ApiEnvelopeFactory`, current Admin actor extraction, and no business logic beyond null-body/authentication checks.
- [X] T070 [US3] Register `IAdminActivityEnforcementService` and `DoctorEnforcementActionRequestValidator` in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T071 [US3] Update Swagger/security tests in `tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs` to ensure the new Admin routes require bearer security and document standard 200/400/401/403/404/409 envelope responses.
- [X] T072 [US3] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~ActivityWeeklyEnforcement"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9AdminViolationReview|FullyQualifiedName~Phase9AdminEnforcementAction|FullyQualifiedName~SwaggerEnvironmentPolicy"`; record the US3 checkpoint in `specs/011-activity-weekly-enforcement/tasks.md`.
  - Evidence 2026-07-12: US3 build and focused verification passed after Senior review fixes. `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors; `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~ActivityWeeklyEnforcement"` passed 4/4; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9AdminViolationReview|FullyQualifiedName~Phase9AdminEnforcementAction|FullyQualifiedName~SwaggerEnvironmentPolicy"` passed 12/12. Senior review root causes fixed: SQL Server materialized persisted UTC suspension timestamps as `Unspecified` and automatic reactivation idempotency rejected them; Admin violation filters silently accepted invalid eligibility/week inputs; default violation windows were not Cairo completed-week based; failed suspension expiry could otherwise return stale Admin summaries; warning a suspended doctor could leave stale suspension timestamps.

**Checkpoint**: User Story 3 is independently functional for Admin review/actions after US1/US2 have produced score and violation data.

---

## Phase 6: User Story 4 - Protect Scheduled Job Safety and Observability (Priority: P2)

**Goal**: Schedule and operate Phase 9 daily/weekly jobs safely under retry, overlap, missed schedules, and Admin catch-up, with job-run evidence and no public job-control surface.

**Independent Test**: Run daily score, weekly enforcement, and suspension-expiry operations repeatedly/concurrently for the same score date/week/expired doctor set, including manual catch-up; confirm one score snapshot per doctor/date, one weekly decision per doctor/week, one automatic reactivation action per expired suspension, safe job-run counters, and no raw failure details.

### Tests for User Story 4 - write and observe failure first

- [X] T073 [P] [US4] Add SQL Server integration tests in `tests/integration/MediBridge.IntegrationTests/Phase9JobRunIntegrationTests.cs` covering `ActivityEnforcementJobRun` Running -> terminal transitions for `DailyActivityScore`, `WeeklyEnforcement`, and `SuspensionExpiry`, stale Running -> Interrupted, safe summary truncation/redaction, processed/skipped/created/updated/failed counts, cancellation behavior, and no stack traces in persisted summaries.
- [X] T074 [P] [US4] Add SQL Server concurrency tests in `tests/integration/MediBridge.IntegrationTests/Phase9JobConcurrencyIntegrationTests.cs` covering concurrent daily score runs for same score date, concurrent weekly enforcement runs for same week, concurrent suspension-expiry runs for the same expired suspended doctors, crash/retry simulation, unique snapshot/decision convergence, one `AutomaticReactivate` action per expired suspension, and no duplicate violations.
- [X] T075 [P] [US4] Add Hangfire registration tests in `tests/integration/MediBridge.IntegrationTests/Phase9HangfireRegistrationTests.cs` proving daily score recurring job is registered at `30 0 * * *` in Africa/Cairo, weekly enforcement recurring job is registered at `0 0 * * 1` in Africa/Cairo, suspension-expiry recurring job is registered at `*/5 * * * *` in Africa/Cairo, recurring registration is idempotent, no `/hangfire` dashboard or unauthenticated manual job routes are mapped, and jobs call service interfaces rather than controllers.
- [X] T076 [P] [US4] Add contract tests in `tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementJobContractTests.cs` for `GET /api/admin/activity-jobs/status`, `POST /api/admin/activity-jobs/run-score`, `POST /api/admin/activity-jobs/run-weekly-enforcement`, and `POST /api/admin/activity-jobs/run-suspension-expiry`: Admin-only security, request validation, Monday week-start validation, completed score-date validation, standard envelopes, `SuspensionExpiry` response shape with null score/week targets, and safe job-run DTOs.

### Implementation for User Story 4

- [X] T077 [US4] Create `MediBridge.Services/Services/ActivityEnforcementJobRunTracker.cs` to interrupt stale runs, add running rows, complete terminal rows, classify success/partial/failure/deferred outcomes, sanitize safe summaries, and expose reusable tracking for activity score, weekly enforcement, and suspension expiry operations.
- [X] T078 [US4] Integrate `ActivityEnforcementJobRunTracker` into `MediBridge.Services/Services/ActivityScoreService.cs` and `MediBridge.Services/Services/WeeklyEnforcementService.cs` so scheduled/manual daily score, weekly enforcement, and suspension-expiry runs persist run records, candidate counts, safe failures, and terminal status without making job-run state the correctness boundary.
- [X] T079 [US4] Create `MediBridge.Services/Services/AdminActivityJobService.cs` or extend `AdminActivityEnforcementService.cs` to implement `ListJobStatusAsync`, `RunDailyScoreAsync`, `RunWeeklyEnforcementAsync`, and `RunSuspensionExpiryAsync`; validate Admin actor, call the service-interface job methods, and return `ActivityJobRunDto` without exposing scheduler internals.
- [X] T080 [US4] Create `MediBridge.APIs/Controllers/AdminActivityJobsController.cs` with `GET api/admin/activity-jobs/status`, `POST api/admin/activity-jobs/run-score`, `POST api/admin/activity-jobs/run-weekly-enforcement`, and `POST api/admin/activity-jobs/run-suspension-expiry`; apply Admin-only authorization, envelope rate limiting, standard envelopes, and no direct repository/Hangfire mutation.
- [X] T081 [US4] Create `MediBridge.APIs/Extensions/RecurringActivityEnforcementJobRegistrar.cs` registering recurring jobs with stable ids `medibridge-daily-activity-score`, `medibridge-weekly-enforcement`, and `medibridge-suspension-expiry`; use Cairo timezone from `IEgyptBusinessClock`, cron `30 0 * * *`, `0 0 * * 1`, and `*/5 * * * *`, and service-interface methods only.
- [X] T082 [US4] Update `MediBridge.APIs/Program.cs` and `MediBridge.APIs/Extensions/DeliveryJobServiceCollectionExtensions.cs` or a new `MediBridge.APIs/Extensions/ActivityEnforcementJobServiceCollectionExtensions.cs` to register and invoke the recurring Phase 9 registrar without mapping Hangfire Dashboard, without startup business mutation, and without changing Phase 7 delivery job registration.
- [X] T083 [US4] Register `ActivityEnforcementJobRunTracker` and Admin job service in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T084 [US4] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~ActivityWeeklyEnforcementJob"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9JobRun|FullyQualifiedName~Phase9JobConcurrency|FullyQualifiedName~Phase9HangfireRegistration"`; verify the filters include `SuspensionExpiry` cases and record the US4 checkpoint in `specs/011-activity-weekly-enforcement/tasks.md`.
  - Evidence 2026-07-12: US4 build and focused verification passed. `dotnet build .\MediBridge.slnx --no-restore` succeeded with 0 warnings and 0 errors; `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~ActivityWeeklyEnforcementJob"` passed 3/3; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9JobRun|FullyQualifiedName~Phase9JobConcurrency|FullyQualifiedName~Phase9HangfireRegistration"` passed 5/5. Senior review root cause fixed: initial recurring Phase 9 jobs used Hangfire's default queue while the configured server listens to the application queue; registrar now uses `DeliveryJobOptions.QueueName` and tests assert the queue.

**Checkpoint**: Scheduled/manual Phase 9 operations are observable, retry-safe, and secured; all user stories are implemented.

---

## Phase 7: Polish & Cross-Cutting Verification

**Purpose**: Remove drift, prove scope boundaries, run full verification, and prepare implementation evidence.

- [X] T085 [P] Update `docs/backend-plan.md` Phase 9 section only if implementation names, recurring ids, or clarified suspension/score-window rules differ from the current text; do not alter later Phase 10/11 scope.
- [X] T086 [P] Update `specs/011-activity-weekly-enforcement/quickstart.md` with any final command filters, route names, migration name, or expected envelope messages discovered during implementation.
- [X] T087 [P] Add or update scope-guard tests in `tests/integration/MediBridge.IntegrationTests/Phase9ScopeGuardTests.cs` proving Phase 9 creates no mutations to `DoctorMessageQueues`, delivery status/reservation/read/interact fields, `Wallets`, `WalletTransactions`, `WalletLedgerEntries`, campaign status/review tables, payment transactions, or withdrawal state.
- [X] T088 [P] Add or update layering tests in `tests/integration/MediBridge.IntegrationTests/Phase9LayeringBoundaryTests.cs` proving `MediBridge.Core` has no EF/HTTP/Hangfire references, `MediBridge.Services` has no EF Core or Hangfire dependencies, controllers contain no score/enforcement calculations, and Repository is the only EF Core domain boundary.
- [X] T089 [P] Add opt-in performance test `tests/integration/MediBridge.IntegrationTests/Phase9PerformanceTests.cs` following `quickstart.md`: seed at least 1,000 doctors with 8 weeks of mixed decisions, warm 20 requests, measure 200 Admin violation-list requests at concurrency 10 for page size 100, require 95% under 1 second, and skip with unmet prerequisites rather than reporting passing evidence.
- [X] T090 Run quickstart verification commands from `specs/011-activity-weekly-enforcement/quickstart.md`: migration apply, focused unit tests, contract tests, integration tests, scheduled registration checks, manual daily/weekly/suspension-expiry job catch-up checks, Admin action smoke checks, automatic suspension expiry check, company doctor search score check, scope discipline checks, and full `dotnet test .\MediBridge.slnx`; record pass/fail/skip evidence in `specs/011-activity-weekly-enforcement/tasks.md`.
- [X] T091 Run `dotnet build .\MediBridge.slnx`, `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj`, and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj`; require zero failed tests/build errors and document intentional opt-in skips only.
- [X] T092 Run `git diff --check` and inspect changed files for raw stack traces in responses, raw SQL/provider exception leaks, protected doctor detail disclosure, wallet internals in Admin responses, EF Core leakage outside Repository, controller business logic, public Hangfire dashboard/manual job route, and unintended queue/wallet/settlement/reporting/withdrawal/payment changes.
- [X] T093 Perform final constitution compliance review across `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`, and `tests`; verify layering, thin controllers, Repository + Unit of Work persistence, JWT Admin security, standard envelopes, safe error handling, deterministic queue/wallet non-mutation, and no out-of-scope features.
  - Evidence 2026-07-12: Phase 7 polish and final verification passed. `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors; unit tests passed 165/165; contract tests passed 146/146; integration tests passed 462/462 with 5 intentional opt-in skips; `dotnet test .\MediBridge.slnx` passed unit 165/165, contract 146/146, and integration 462/462 with 5 intentional opt-in skips. `git diff --check` reported no whitespace errors, only line-ending warnings. Senior review root causes fixed: legacy Phase 5/7 scope guards still rejected now-valid Phase 9 routes, Phase 7 migration tests seeded a historical schema with the current EF model after Phase 9 columns were added, and a weekly-enforcement fixture allowed automatic expiry to clear the current suspension before asserting suspension-overlap behavior.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies; can start immediately.
- **Phase 2 Foundational**: Depends on Phase 1; blocks every user story.
- **Phase 3 US1**: Depends on Phase 2; MVP slice.
- **Phase 4 US2**: Depends on Phase 2 and can run after US1 or in parallel with careful coordination; it does not require Admin endpoints.
- **Phase 5 US3**: Depends on US1 and US2 because Admin review needs score and weekly violation data.
- **Phase 6 US4**: Depends on US1 and US2 services; can overlap with US3 after service contracts stabilize.
- **Phase 7 Polish**: Depends on all desired user stories.

### User Story Dependencies

- **US1 (P1)**: Daily score calculation. Can start after Foundational. No dependency on US2/US3/US4.
- **US2 (P1)**: Weekly enforcement. Can start after Foundational. It shares some policies/entities with US1 but should remain independently testable.
- **US3 (P2)**: Admin review/actions. Depends on score history and weekly violation data from US1/US2.
- **US4 (P2)**: Scheduled/manual job safety. Depends on US1/US2 service methods and Phase 9 job-run persistence.

### Parallel Opportunities

- T002-T005 can run in parallel after T001.
- T007, T009-T014, T017-T020, T022-T028 can run in parallel by different implementers because they target separate files.
- T030 and T031 can run in parallel after entities exist.
- T033-T038 can run in parallel after repository contracts exist, but T039 must wait for them.
- Test tasks inside each user story marked `[P]` can be written in parallel before production implementation.
- US1 and US2 can be staffed in parallel after Phase 2 if implementers coordinate shared repository methods and enum usage.
- US3 and US4 can be staffed in parallel after US1/US2 service interfaces are stable.

---

## Parallel Example: User Story 1

```text
Task: "T044 [US1] Add unit tests for activity score date-window calculation in tests/unit/MediBridge.UnitTests/Phase9ActivityScoreTests.cs"
Task: "T045 [US1] Add unit tests for score formula in tests/unit/MediBridge.UnitTests/Phase9ActivityScoreTests.cs"
Task: "T046 [US1] Add SQL Server integration tests in tests/integration/MediBridge.IntegrationTests/Phase9ActivityScoreIntegrationTests.cs"
Task: "T047 [US1] Add integration test in tests/integration/MediBridge.IntegrationTests/Phase9CompanyDoctorSearchScoreIntegrationTests.cs"
```

After tests are failing for missing behavior, implement sequentially:

```text
Task: "T048 [US1] Create MediBridge.Services/Services/ActivityScoreCalculator.cs"
Task: "T049 [US1] Create MediBridge.Services/Services/ActivityScoreService.cs"
Task: "T050 [US1] Implement suspension expiry in ActivityScoreService.cs"
Task: "T051 [US1] Add safe logging in ActivityScoreService.cs"
Task: "T052 [US1] Register services in MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs"
Task: "T053 [US1] Run focused US1 tests"
```

## Parallel Example: User Story 2

```text
Task: "T054 [US2] Add weekly date-window and eligibility unit tests in tests/unit/MediBridge.UnitTests/Phase9WeeklyEnforcementTests.cs"
Task: "T055 [US2] Add rolling 8-week classification unit tests in tests/unit/MediBridge.UnitTests/Phase9WeeklyEnforcementTests.cs"
Task: "T056 [US2] Add SQL Server integration tests in tests/integration/MediBridge.IntegrationTests/Phase9WeeklyEnforcementIntegrationTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T063 [US3] Add contract tests for GET /api/admin/violations in tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementContractTests.cs"
Task: "T064 [US3] Add contract tests for PUT /api/admin/doctors/{doctorId}/status in tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementContractTests.cs"
Task: "T065 [US3] Add SQL Server integration tests in tests/integration/MediBridge.IntegrationTests/Phase9AdminViolationReviewIntegrationTests.cs"
Task: "T066 [US3] Add SQL Server integration tests in tests/integration/MediBridge.IntegrationTests/Phase9AdminEnforcementActionIntegrationTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T073 [US4] Add job-run integration tests in tests/integration/MediBridge.IntegrationTests/Phase9JobRunIntegrationTests.cs"
Task: "T074 [US4] Add job concurrency tests in tests/integration/MediBridge.IntegrationTests/Phase9JobConcurrencyIntegrationTests.cs"
Task: "T075 [US4] Add Hangfire registration tests in tests/integration/MediBridge.IntegrationTests/Phase9HangfireRegistrationTests.cs"
Task: "T076 [US4] Add job endpoint contract tests in tests/contract/MediBridge.ContractTests/ActivityWeeklyEnforcementJobContractTests.cs"
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 US1 only.
3. Validate daily activity scoring independently with T053.
4. Stop and demo: company doctor search uses latest committed Activity Score, and one immutable score snapshot exists per doctor/date.

### Incremental Delivery

1. Deliver US1 daily scoring.
2. Deliver US2 weekly enforcement decisions and violations.
3. Deliver US3 Admin review/actions.
4. Deliver US4 scheduled/manual job safety and observability.
5. Finish Phase 7 full verification.

### Safety Rules During Implementation

- Run focused tests after each checkpoint before moving to the next story.
- If a task requires editing a file with unrelated existing changes, read the file first and preserve those changes.
- Do not mark a task complete until the described file exists or is modified and the observable completion condition is true.
- Do not implement Admin UI, company analytics, withdrawal payout, notifications, payment gateway behavior, queue reprioritization, settlement correction, or delivery activation/expiry changes in Phase 9.
- Keep all new API responses in the standard envelope and all new secured routes Admin-only unless the task explicitly says otherwise.
