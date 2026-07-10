# Tasks: Phase 8 Interaction & Payments

**Input**: Design documents from `/specs/009-interaction-payments/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required. The Phase 8 specification, success criteria, quickstart, and plan require unit, contract, SQL Server integration, concurrency, migration, security, redaction, and performance-oriented validation. Within every user-story phase, create the tests first, run the focused test filter, and record that the new tests fail for the expected missing behavior before implementing production code.

**Constitution Note**: Keep entities, enums, repository contracts, and pure domain helpers in `MediBridge.Core`; SQL Server, EF Core, migrations, row locks, and repository implementations in `MediBridge.Repository`; use-case orchestration and validation in `MediBridge.Services`; HTTP binding, authorization, rate limiting, OpenAPI attributes, and response envelopes in `MediBridge.APIs`. Controllers must remain HTTP-only. Services and controllers must not use EF Core infrastructure types directly. Every secured endpoint must use JWT Doctor authorization, the standard response envelope, and global exception middleware with safe redaction.

**Execution Rule for a Smaller Model**: Execute tasks strictly in numeric order unless a task is marked `[P]` and every listed dependency is already complete. Do not combine tasks. Do not rename planned types unless a compile error proves an exact existing name differs. Do not change the financial formulas, idempotency keys, route paths, response envelope, feedback limit, or current-day eligibility rules. Do not implement company reporting, feedback listing, weekly enforcement, activity scoring, notifications, withdrawals, payouts, admin tooling, queue activation, or expiry release in this feature.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to run in parallel after phase prerequisites because it targets different files and does not depend on an unfinished symbol.
- **[Story]**: User-story traceability label. Setup, foundational, and polish tasks intentionally have no story label.
- Every task names exact target file paths and states the observable completion condition.
- When a task says "prove red first", run the named focused test after writing it and verify it fails because the planned behavior is missing, not because of syntax, setup, or unrelated infrastructure.

---

## Phase 1: Setup (Shared Test and Planning Infrastructure)

**Purpose**: Add Phase 8-specific test scaffolding and freeze the implementation decisions before changing runtime behavior.

- [X] T001 Create `tests/integration/MediBridge.IntegrationTests/Phase8InteractionTestHelpers.cs` with explicit helper methods for seeding approved active Doctor users/profiles, approved Company users/profiles, current/prior/future Egypt-date `DoctorAdDelivery` rows, company and doctor wallets, wallet transactions, wallet ledger entries, and optional existing `ReadAtUtc`/Accepted/Rejected/Expired states; helpers must accept explicit ids, timestamps, delivery dates, monetary values, and balances and must not perform read or interaction behavior.
- [X] T002 [P] Create `tests/unit/MediBridge.UnitTests/InteractionPayments/Phase8DeliveryDomainTests.cs` with placeholder test class and factory helpers for `DoctorAdDelivery` state transition tests; do not add production behavior in this task.
- [X] T003 [P] Create `tests/unit/MediBridge.UnitTests/InteractionPayments/Phase8InteractionValidationTests.cs` with placeholder test class and local cases for `Idempotency-Key`, decision, and feedback normalization validation; do not add production behavior in this task.
- [X] T004 [P] Create `tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs` with shared request helpers for `PUT /api/doctor/messages/{deliveryId}/read` and `POST /api/doctor/messages/{deliveryId}/interact`; do not assert behavior yet.
- [X] T005 [P] Create `tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs` with empty fixture class wired to the existing integration-test host/database conventions; do not seed or assert settlement yet.
- [X] T006 [P] Create `tests/integration/MediBridge.IntegrationTests/Phase8LayeringAndScopeGuardTests.cs` with an empty test class and comments naming the forbidden Phase 8 leaks: company reporting, feedback listing, weekly enforcement, activity scoring, notifications, withdrawals, payouts, queue activation, expiry release, and EF Core outside Repository.
- [X] T007 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8"` and `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPayments"`; verify the new placeholder tests compile and record the setup checkpoint in `specs/009-interaction-payments/tasks.md` notes only after the commands succeed.

**Checkpoint**: Phase 8 test files and helpers exist, compile cleanly, and production behavior is unchanged.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared domain types, replay evidence, repository contracts, DTOs, validation types, exception mapping, and persistence required by all user stories.

**CRITICAL**: Complete all tasks in this phase before beginning any user-story implementation.

- [X] T008 Add `delivery:charge:{deliveryId}` and `delivery:earn:{deliveryId}` helpers to `MediBridge.Core/Entities/Wallets/DeliveryFinancialOperationKeys.cs`; reject blank delivery ids exactly like existing Reserve/Release helpers and do not log or accept caller-selected financial keys.
- [X] T009 Add domain methods `MarkRead(DateTime readAtUtc)` and `MarkInteracted(DeliveryStatus finalStatus, DateTime interactedAtUtc, string? normalizedFeedbackText)` to `MediBridge.Core/Entities/Messaging/DoctorAdDelivery.cs`; `MarkRead` sets `ReadAtUtc` only when null and never changes wallet/reservation/interaction fields; `MarkInteracted` allows only Accepted or Rejected from Active/Reserved, sets `ReservationStatus = Charged`, sets `InteractedAtUtc`, stores optional feedback and `FeedbackCreatedAtUtc`, leaves `ReadAtUtc` unchanged, validates UTC timestamps, and rejects invalid/repeated transitions without partial mutation.
- [X] T010 [P] Add `DeliveryInteractionDecision`, `DeliveryInteractionOperationStatus`, and any needed explicit enum values to `MediBridge.Core/Enums/Phase3DomainEnums.cs`; preserve all existing enum numeric values and use stable new integer values.
- [X] T011 Create `MediBridge.Core/Entities/Messaging/DeliveryInteractionOperation.cs` with fields from `data-model.md`: `Id`, `DoctorId`, `DeliveryId`, `IdempotencyKey`, `Decision`, `FeedbackText`, `Status`, `ChargeTransactionId`, `EarnTransactionId`, `CreatedAtUtc`, `CompletedAtUtc`, `SafeFailureSummary`, and `ConcurrencyToken`; enforce normalized key length 8-128, feedback max 1,000, UTC timestamps, no raw stack traces, and implement `IConcurrencyTrackedRecord`.
- [X] T012 [P] Create `MediBridge.Core/Interfaces/Messaging/DeliveryInteractionReadModels.cs` with immutable records for read result, interaction replay evidence, locked interaction delivery snapshot, and settlement result; include only fields required by services and no EF Core types.
- [X] T013 Extend `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs` with methods for Doctor-owned current-date read lock, Active/Reserved current-date interaction lock, already-settled Doctor-owned delivery lookup, and current-date read visibility lookup; every method must accept explicit `doctorId`, `deliveryId`, and captured `DateOnly businessDateEgypt`.
- [X] T014 Create `MediBridge.Core/Interfaces/Messaging/IDeliveryInteractionOperationRepository.cs` with methods `FindForUpdateAsync(doctorId, deliveryId, idempotencyKey)`, `AddAsync(operation)`, `MarkSucceededAsync(...)`, and any read-only lookup needed for already-settled replay classification; do not expose EF Core, SQL, or IQueryable.
- [X] T015 Add `IDeliveryInteractionOperationRepository DeliveryInteractionOperations` to `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`; preserve existing transaction APIs and do not expose persistence infrastructure.
- [X] T016 Verify `MediBridge.Core/Interfaces/Wallets/IWalletRepository.cs` exposes the exact Phase 8 wallet capabilities before service work: `FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType.Company, companyId, ...)`, `FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType.Doctor, doctorId, ...)`, `StageReservedBalanceChangeAsync(companyWalletId, -reservedAmount, ...)`, and `StageAvailableBalanceChangeAsync(doctorWalletId, doctorEarnings, ...)`. If the existing signatures match, make no interface change and document that T016 is satisfied in the implementation notes; if any capability is absent, add the smallest owner-explicit method needed and update `WalletRepository` and tests in the same task.
- [X] T017 [P] Create `MediBridge.Services/DTOs/Messaging/DoctorInteractionPaymentDtos.cs` containing `MarkReadResultDto`, `InteractDeliveryRequestDto`, `InteractDeliveryResultDto`, and a small enum/string convention for `Created`/`Replayed`; response DTOs must not contain wallet balances, platform fee internals, raw idempotency keys, storage keys, or audit metadata.
- [X] T018 [P] Create `MediBridge.Services/Validators/Messaging/InteractionPaymentValidation.cs` with pure methods to normalize `Idempotency-Key`, parse decision `Accept`/`Reject`, normalize feedback, validate max 1,000 characters after trimming, and treat null/empty/whitespace feedback as absent; use the same 8-128 key length convention as existing wallet top-up validation.
- [X] T019 Create `MediBridge.Services/Interfaces/Phase8InteractionExceptions.cs` with BadRequest, Forbidden, NotFound, Conflict, and Consistency exceptions that map to safe envelopes; messages must not expose delivery existence across owners, raw idempotency keys, wallet balances, or stack traces.
- [X] T020 Update `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs` to map Phase 8 exceptions to 400/403/404/409/500 or existing safe status conventions; preserve existing Phase 5/Phase 7 mappings and standard `{ Code, Message, Data }` envelope behavior.
- [X] T021 [P] Update `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs` so `DoctorAdDelivery.FeedbackText` has max length 1000 and any existing value over 1000 is handled by the migration plan without silent truncation; preserve all Phase 7 delivery indexes and constraints.
- [X] T022 Create `MediBridge.Repository/Configurations/Messaging/DeliveryInteractionOperationConfiguration.cs` mapping `DeliveryInteractionOperations`, enum conversions, `IdempotencyKey` max 128, `FeedbackText` max 1000, `SafeFailureSummary` max 2000, rowversion, unique `(DoctorId, DeliveryId, IdempotencyKey)`, index `(DeliveryId, Decision)`, and restrict delete behavior to delivery.
- [X] T023 Add `DbSet<DeliveryInteractionOperation> DeliveryInteractionOperations` to `MediBridge.Repository/Data/MediBridgeDbContext.cs`.
- [X] T024 Implement `IDeliveryInteractionOperationRepository` in `MediBridge.Repository/Repositories/Messaging/DeliveryInteractionOperationRepository.cs` using SQL Server `UPDLOCK, ROWLOCK` for replay-evidence lookup inside transactions, bounded projections for read-only lookup, and no business decisions outside persistence.
- [X] T025 Implement the new `IDeliveryRepository` read/interaction methods in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs` using parameterized EF/SQL, explicit Doctor id, delivery id, current Egypt date, expected state predicates, `UPDLOCK, ROWLOCK` for mutation paths, and safe no-tracking projections for already-settled classification.
- [X] T026 Implement or extend locked wallet methods in `MediBridge.Repository/Repositories/Wallets/WalletRepository.cs`; use owner type plus owner id, SQL Server update locks, EGP wallet validation data, and do not perform Phase 8 business decisions in Repository.
- [X] T027 Wire `IDeliveryInteractionOperationRepository` through `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs` and `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T028 Generate EF Core migration `AddPhase8InteractionPayments` in `MediBridge.Repository/Migrations/` and update `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`; migration must create the interaction operation table, indexes, rowversion, constraints, feedback length update, and no queue activation, expiry, Charge/Earn data backfill, reporting, scoring, notification, withdrawal, or admin tables.
- [X] T029 [P] Add `tests/integration/MediBridge.IntegrationTests/Phase8MigrationTests.cs` proving the migration applies from an empty database, creates `DeliveryInteractionOperations` with unique `(DoctorId, DeliveryId, IdempotencyKey)`, keeps wallet transaction unique `(OperationType, IdempotencyKey)`, preserves Phase 7 delivery indexes, enforces or supports 1,000-character feedback, and rollback/reapply does not mutate existing delivery or wallet business rows.
- [X] T030 [P] Add unit tests to `tests/unit/MediBridge.UnitTests/InteractionPayments/Phase8DeliveryDomainTests.cs` proving `MarkRead` first-write-wins, `MarkRead` requires UTC, `MarkInteracted` accepts only Accepted/Rejected, changes Reserved to Charged, stores feedback timestamp only when feedback exists, leaves `ReadAtUtc` unchanged, and rejects invalid/repeated transitions.
- [X] T031 [P] Add unit tests to `tests/unit/MediBridge.UnitTests/InteractionPayments/Phase8InteractionValidationTests.cs` proving idempotency key trim/required/8-128 validation, decision parsing, feedback trim, whitespace-as-null, 1000-character acceptance, and 1001-character rejection.
- [X] T032 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8DeliveryDomain|FullyQualifiedName~Phase8InteractionValidation"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8Migration"`; require green after foundational implementation before starting user stories.

**Checkpoint**: Shared domain, validation, persistence, migration, exception, and repository foundations exist and focused foundation tests pass.

---

## Phase 3: User Story 1 - Record Message Reads Without Payment Settlement (Priority: P1) - MVP

**Goal**: A Doctor can mark an owned current-day delivery as read exactly once, retries return the first read timestamp, and no financial state changes.

**Independent Test**: Seed an approved Doctor with an Active current-day delivery and company/doctor wallets, call the read endpoint twice, and verify `ReadAtUtc` is set once while status, reservation status, wallet balances, wallet transactions, and ledger entries remain unchanged.

### Tests for User Story 1 - write and observe failure first

- [X] T033 [P] [US1] Add contract tests in `tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs` for `PUT /api/doctor/messages/{deliveryId}/read`: 200 envelope with `DeliveryId`, `ReadAtUtc`, `ReadStatus`, 401/403/404/429 documented envelopes, and no `Idempotency-Key` requirement.
- [X] T034 [P] [US1] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs` proving first read sets `ReadAtUtc`, second read returns `Replayed` with unchanged timestamp, and delivery status/reservation status/company wallet/doctor wallet/WalletTransactions/WalletLedgerEntries are unchanged.
- [X] T035 [P] [US1] Add authorization integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs` proving unauthenticated, Company, Admin, and another Doctor cannot read the delivery and receive safe envelopes without existence leakage.

### Implementation for User Story 1

- [X] T036 [US1] Extend `MediBridge.Services/Interfaces/IDoctorMessageService.cs` with `Task<MarkReadResultDto> MarkDeliveryReadAsync(string actorUserId, string deliveryId, CancellationToken cancellationToken = default)`.
- [X] T037 [US1] Implement `MarkDeliveryReadAsync` in `MediBridge.Services/Services/DoctorMessageService.cs`: validate nonblank delivery id, resolve approved active Doctor, capture one `IEgyptBusinessClock` snapshot, execute a Unit of Work transaction, use the new delivery repository read lock, call `DoctorAdDelivery.MarkRead`, save changes, audit Created/Replayed safely, and return `MarkReadResultDto`.
- [X] T038 [US1] Add `PUT "{deliveryId}/read"` action to `MediBridge.APIs/Controllers/DoctorMessagesController.cs`; bind only `deliveryId` and cancellation token, get actor id from `ICurrentUserContext`, delegate to service, return message `"Message read recorded."` in the standard envelope, and put no date, ownership, wallet, or persistence logic in the controller.
- [X] T039 [US1] Apply the read rate-limit policy to the read action in `MediBridge.APIs/Controllers/DoctorMessagesController.cs`; reuse `RateLimitPolicyNames.Phase7DoctorMessagesRead` unless a compile-time policy change is required, and do not apply `DoctorInteraction` to read.
- [X] T040 [US1] Add safe audit event writing for read Created/Replayed in `MediBridge.Services/Services/DoctorMessageService.cs` using existing `AuditEvents` abstractions; metadata may include actor id, doctor id, delivery id, campaign id, outcome, and timestamp, but must not include wallet balances, storage keys, or raw exception details.
- [X] T041 [US1] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8ReadTracking"`; verify US1 is independently green before starting US2.

**Checkpoint**: User Story 1 is a complete MVP. Read tracking works independently and has zero payment effects.

---

## Phase 4: User Story 2 - Settle Accept or Reject Exactly Once (Priority: P1)

**Goal**: A Doctor can Accept or Reject an Active/Reserved current-day delivery and settle one company Charge plus one doctor Earn exactly once under retry and concurrency.

**Independent Test**: Seed Active/Reserved current-day deliveries with company Reserved balance and doctor wallet, submit Accept and Reject with valid `Idempotency-Key`, retry same requests, run concurrent requests, and verify final delivery state, wallet balances, Charge/Earn transactions, ledger entries, replay/conflict behavior, and unchanged `ReadAtUtc`.

### Tests for User Story 2 - write and observe failure first

- [X] T042 [P] [US2] Add contract tests in `tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs` for `POST /api/doctor/messages/{deliveryId}/interact`: required `Idempotency-Key` header in OpenAPI/Swagger, 200 envelope with `DeliveryId`, `Status`, `InteractedAtUtc`, `IdempotencyStatus`, 400 for missing/invalid key/body/decision, 409 for conflict, and 401/403/404/429 safe envelopes.
- [X] T043 [P] [US2] Add unit tests in `tests/unit/MediBridge.UnitTests/InteractionPayments/DeliveryFinancialOperationKeysPhase8Tests.cs` proving `ForCharge(deliveryId)` returns exactly `delivery:charge:{deliveryId}`, `ForEarn(deliveryId)` returns exactly `delivery:earn:{deliveryId}`, and both reject blank ids.
- [X] T044 [P] [US2] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs` proving Accept settles Active/Reserved delivery once: Accepted/Charged, InteractedAtUtc set, ReadAtUtc unchanged, company Reserved debited by ReservedAmount, doctor Available credited by DoctorEarnings, Charge/Earn transactions created with deterministic keys, and ledger entries are company Reserved Debit plus doctor Available Credit.
- [X] T045 [P] [US2] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs` proving Reject uses the same financial settlement as Accept and stores final status Rejected.
- [X] T046 [P] [US2] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionIdempotencyIntegrationTests.cs` proving same Doctor + delivery + key + same decision + same normalized feedback returns `Replayed` with no duplicate delivery transition, wallet mutation, Charge transaction, Earn transaction, ledger entry, or audit settlement effect.
- [X] T047 [P] [US2] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionIdempotencyIntegrationTests.cs` proving replay conflicts and already-settled different-key behavior: same Doctor + delivery + key with different decision or different normalized feedback returns 409 conflict and preserves the original final state, feedback, and financial records; different `Idempotency-Key` after an already Accepted or Rejected delivery returns the existing settled result when the requested decision matches the final state, returns 409 conflict when the requested decision differs, and never mutates delivery, feedback, wallet, transaction, ledger, or audit settlement state.
- [X] T048 [P] [US2] Add concurrency integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionConcurrencyTests.cs` proving concurrent same-key same-payload requests produce exactly one Created and the rest Replayed, while concurrent Accept and Reject attempts produce one final winner and no duplicate Charge/Earn effects.
- [X] T049 [P] [US2] Add forced-rollback integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8SettlementAtomicityTests.cs` proving an injected failure after delivery update, after company wallet debit, or after Charge transaction creation rolls back delivery, reservation, both wallets, interaction operation evidence, transactions, and ledgers.

### Implementation for User Story 2

- [X] T050 [US2] Extend `MediBridge.Services/Interfaces/IDoctorMessageService.cs` with `Task<InteractDeliveryResultDto> InteractWithDeliveryAsync(string actorUserId, string deliveryId, string? idempotencyKey, InteractDeliveryRequestDto? request, CancellationToken cancellationToken = default)`.
- [X] T051 [US2] Implement `InteractWithDeliveryAsync` validation path in `MediBridge.Services/Services/DoctorMessageService.cs`: validate delivery id, required request body, required normalized `Idempotency-Key`, decision Accept/Reject, and feedback normalization before opening the settlement transaction.
- [X] T052 [US2] Implement settlement replay evidence in `MediBridge.Services/Services/DoctorMessageService.cs`: inside an isolated transaction, lock or create `DeliveryInteractionOperation`, classify same-key same-payload replay as `Replayed`, same-key different decision/feedback as Conflict, classify different-key already-settled same-decision requests as the existing settled result, classify different-key already-settled different-decision requests as Conflict, and never use audit rows or wallet transactions alone as the client replay authority.
- [X] T053 [US2] Implement Active/Reserved delivery eligibility in `MediBridge.Services/Services/DoctorMessageService.cs`: resolve approved active Doctor, capture one Egypt business-clock snapshot, lock delivery by Doctor id/delivery id/current date/status/reservation, reject prior-day/future-day/expired/released/cross-owner/invisible deliveries safely, and do not require or mutate `ReadAtUtc`.
- [X] T054 [US2] Implement monetary snapshot validation in `MediBridge.Services/Services/DoctorMessageService.cs`: require positive `ReservedAmount`, `PricePerMessageSnapshot`, `PlatformFeeAmount`, `DoctorEarnings`, `ReservedAmount == PricePerMessageSnapshot`, `PlatformFeeAmount + DoctorEarnings == PricePerMessageSnapshot`, and two-decimal EGP values before mutating wallets.
- [X] T055 [US2] Implement wallet locking and mutation in `MediBridge.Services/Services/DoctorMessageService.cs`: lock company wallet first by `CompanyId`, then doctor wallet by `DoctorId`; verify company Reserved covers `ReservedAmount`; debit company Reserved only; credit doctor Available only; reject missing/deleted/wrong-owner wallets without partial mutation.
- [X] T056 [US2] Implement Charge and Earn transaction creation in `MediBridge.Services/Services/DoctorMessageService.cs`: use `DeliveryFinancialOperationKeys.ForCharge(deliveryId)` and `.ForEarn(deliveryId)`, create one company Charge transaction and one doctor Earn transaction linked to `RelatedDeliveryId`, create the matching ledger entries, and classify exact existing completed pairs as replay while inconsistent partial evidence is a consistency failure.
- [X] T057 [US2] Call `DoctorAdDelivery.MarkInteracted` from `MediBridge.Services/Services/DoctorMessageService.cs`, passing Accepted or Rejected final status, captured UTC timestamp, and normalized feedback; verify `ReadAtUtc` is not changed by service code.
- [X] T058 [US2] Link successful `DeliveryInteractionOperation` evidence to Charge/Earn transaction ids and mark it succeeded in `MediBridge.Services/Services/DoctorMessageService.cs`; store enough safe normalized payload data to classify future replays and conflicts.
- [X] T059 [US2] Add `POST "{deliveryId}/interact"` action to `MediBridge.APIs/Controllers/DoctorMessagesController.cs` with `[RequireIdempotencyKey]` and `[EnableRateLimiting(RateLimitPolicyNames.DoctorInteraction)]`; read `Idempotency-Key` from headers, bind `InteractDeliveryRequestDto`, delegate to service, return `"Message interaction settled."` envelope, and keep controller free of business logic.
- [X] T060 [US2] Add safe audit event writing in `MediBridge.Services/Services/DoctorMessageService.cs` for interaction Created, Replayed, Conflict, and consistency failure; never audit raw `Idempotency-Key`, wallet balances, full feedback text, stack traces, or private campaign/storage data.
- [X] T061 [US2] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliveryFinancialOperationKeysPhase8|FullyQualifiedName~Phase8DeliveryDomain"` plus the Phase 8 contract/integration filters for settlement, idempotency, concurrency, and atomicity; require green before starting US3.

**Checkpoint**: User Story 2 is independently functional. Accept and Reject settle exactly once with deterministic idempotency and atomic wallet effects.

---

## Phase 5: User Story 3 - Capture Optional Doctor Feedback With the Interaction (Priority: P2)

**Goal**: Feedback supplied with Accept or Reject is optional, normalized, capped at 1,000 characters, stored with the interaction, and never changes settlement math.

**Independent Test**: Settle deliveries with no feedback, whitespace feedback, short feedback, 1,000-character feedback, and 1,001-character feedback; verify normalization, storage, conflicts on changed replay feedback, and zero partial settlement on invalid feedback.

### Tests for User Story 3 - write and observe failure first

- [X] T062 [P] [US3] Add contract tests in `tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs` proving `FeedbackText` is optional, nullable, maxLength 1000 in Swagger/OpenAPI, and a 1,001-character value returns a 400 envelope.
- [X] T063 [P] [US3] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionFeedbackIntegrationTests.cs` proving omitted feedback and whitespace-only feedback settle successfully with null stored feedback and no `FeedbackCreatedAtUtc`.
- [X] T064 [P] [US3] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionFeedbackIntegrationTests.cs` proving non-empty feedback is trimmed, stored, linked to the final interaction, and leaves Charge/Earn amounts identical to no-feedback settlement.
- [X] T065 [P] [US3] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionFeedbackIntegrationTests.cs` proving exactly 1,000 characters after trimming is accepted and 1,001 characters after trimming is rejected before delivery, wallet, transaction, ledger, or interaction evidence mutation.
- [X] T066 [P] [US3] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionFeedbackIntegrationTests.cs` proving same key + same decision + different normalized feedback after a successful settlement returns 409 conflict and preserves original stored feedback.

### Implementation for User Story 3

- [X] T067 [US3] Ensure `MediBridge.Services/Validators/Messaging/InteractionPaymentValidation.cs` is the only place that trims feedback and enforces the 1,000-character limit; remove any duplicate trim/length logic from controllers or repositories.
- [X] T068 [US3] Update `MediBridge.Services/Services/DoctorMessageService.cs` to pass normalized feedback consistently to replay evidence, `DoctorAdDelivery.MarkInteracted`, result DTOs, and audit metadata redaction; use null for omitted or whitespace-only feedback.
- [X] T069 [US3] Update `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`, `MediBridge.Repository/Migrations/`, and `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs` if needed so persisted `FeedbackText` cannot exceed 1,000 characters after Phase 8 migration; never silently truncate existing data.
- [X] T070 [US3] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionFeedback"`; require green before starting US4.

**Checkpoint**: User Story 3 is independently functional. Feedback behavior is clear, bounded, replay-safe, and financially neutral.

---

## Phase 6: User Story 4 - Protect Stale, Expired, and Invalid Deliveries (Priority: P2)

**Goal**: Stale, expired, cross-owner, already-settled, missing-wallet, inconsistent-balance, and invalid-snapshot deliveries are rejected safely with no partial financial mutation.

**Independent Test**: Attempt read and interaction against invalid states and relationships, then verify safe envelopes, audit evidence where appropriate, unchanged delivery state, unchanged wallet balances, and no new Charge/Earn records.

### Tests for User Story 4 - write and observe failure first

- [X] T071 [P] [US4] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InvalidDeliveryProtectionTests.cs` proving prior-day, future-day, Expired/Released, cross-owner, malformed id, and missing delivery cases cannot be read or interacted with and do not leak protected existence; also prove current-day visible Accepted and Rejected deliveries can still be marked read but cannot be interacted with again.
- [X] T072 [P] [US4] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InvalidDeliveryProtectionTests.cs` proving missing company wallet, missing doctor wallet, deleted wallet, company Reserved below `ReservedAmount`, and mismatched owner wallets cause safe failure without delivery or wallet mutation.
- [X] T073 [P] [US4] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InvalidDeliveryProtectionTests.cs` proving invalid stored monetary snapshots such as zero/negative values, `ReservedAmount != PricePerMessageSnapshot`, or `PlatformFeeAmount + DoctorEarnings != PricePerMessageSnapshot` fail safely before any Charge/Earn effect.
- [X] T074 [P] [US4] Add security tests in `tests/integration/MediBridge.IntegrationTests/Phase8AuthorizationAndRedactionTests.cs` proving unauthenticated, Company, Admin, unapproved Doctor, suspended Doctor, deleted Doctor, and another Doctor are denied with standard safe envelopes and no protected wallet/campaign/storage/idempotency details.
- [X] T075 [P] [US4] Add redaction tests in `tests/integration/MediBridge.IntegrationTests/Phase8AuthorizationAndRedactionTests.cs` forcing validation, conflict, consistency, and unexpected settlement failures, then asserting logs/audit/envelopes do not contain raw idempotency keys, wallet balances, full feedback text, storage keys, signed URLs, serialized request bodies, or raw stack traces.

### Implementation for User Story 4

- [X] T076 [US4] Harden `MediBridge.Services/Services/DoctorMessageService.cs` invalid-state branches so read returns safe NotFound/Forbidden for invisible, prior-day, expired, or cross-owner deliveries, and interaction distinguishes same-result replay, conflict, not-found, bad-request, and consistency failure without revealing protected existence.
- [X] T077 [US4] Add consistency failure audit paths in `MediBridge.Services/Services/DoctorMessageService.cs` for missing wallets, insufficient company Reserved balance, invalid monetary snapshots, partial deterministic transaction evidence, and stale delivery state; audit only safe ids/outcome categories.
- [X] T078 [US4] Review and update `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs` so every Phase 8 invalid-state exception maps to the intended status code and standard envelope while preserving prior Phase 5/7 behavior.
- [X] T079 [US4] Add or update integration assertions in `tests/integration/MediBridge.IntegrationTests/Phase8LayeringAndScopeGuardTests.cs` proving Core has no EF/HTTP references, Services has no EF Core dependency, Repository is the only EF domain boundary, `DoctorMessagesController` has no business/wallet/date logic, and no Phase 8-forbidden routes/services/entities were added.
- [X] T080 [US4] Run Phase 8 invalid delivery, authorization/redaction, and layering/scope guard filters; require green before final polish.

**Checkpoint**: User Story 4 is independently functional. Invalid and stale states fail safely without partial effects or information leaks.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Prove full traceability, update documentation if needed, run final checks, and leave a clean handoff for implementation review.

- [X] T081 [P] Update `specs/009-interaction-payments/contracts/interaction-payments-api.yaml` if implementation details require a schema correction; keep route paths, required `Idempotency-Key`, feedback limit, standard envelope, and safe failure responses aligned with tests.
- [X] T082 [P] Update `specs/009-interaction-payments/quickstart.md` only if commands, messages, or expected envelopes changed during implementation; do not broaden scope or add later-phase workflows.
- [X] T083 [P] Update `docs/backend-plan.md` Phase 8 only if implementation reveals documentation drift; keep changes limited to read tracking, Accept/Reject settlement, idempotency, feedback cap, and Charge/Earn behavior.
- [X] T084 Add opt-in performance tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionPerformanceTests.cs` for the documented Release/Server-GC/SQL Server profile: 1,000 active deliveries, 20 warm-up reads/interactions, 200 measured reads and 200 measured interactions at concurrency 10, at least 95% under 1 second, zero duplicate financial effects, and skipped-not-passed behavior when prerequisites are unavailable.
- [X] T085 [P] Update `tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs` so Swagger/OpenAPI exposes `PUT /api/doctor/messages/{deliveryId}/read` and `POST /api/doctor/messages/{deliveryId}/interact`, documents required `Idempotency-Key` only for interact, and preserves Doctor authorization, rate limits, and standard envelopes.
- [X] T086 [P] Update `tests/contract/MediBridge.ContractTests/ResponseEnvelopeSuccessContractTests.cs` if needed so the new Phase 8 success envelopes are included without weakening existing envelope checks.
- [X] T087 Run a manual redaction review over `MediBridge.APIs`, `MediBridge.Services`, `MediBridge.Repository`, and Phase 8 tests for raw idempotency keys, wallet balances, full feedback text, storage credentials, signed URLs, connection strings, serialized request bodies, and raw exception stacks; add regression assertions to `tests/integration/MediBridge.IntegrationTests/Phase8AuthorizationAndRedactionTests.cs` for any issue found.
- [X] T088 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj`; require zero failures and record any intentional skip in `specs/009-interaction-payments/tasks.md` notes.
- [X] T089 Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj`; require zero failures and record any intentional skip in `specs/009-interaction-payments/tasks.md` notes.
- [X] T090 Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj`; require zero failures except explicitly opt-in performance profile skips, and record results in `specs/009-interaction-payments/tasks.md` notes.
- [X] T091 Run `dotnet build .\MediBridge.slnx --no-restore`; require 0 errors and document any warnings in `specs/009-interaction-payments/tasks.md` notes.
- [X] T092 Run `git diff --check` from repository root and fix whitespace errors only in files changed for Phase 8.
- [X] T093 Re-check every constitution gate manually against changed files and update the Requirement Traceability table in `specs/009-interaction-payments/tasks.md` if any task/file mapping changed during implementation.

---

## Requirement Traceability

| Specification requirements | Primary implementation tasks | Primary proof tasks |
|---|---|---|
| FR-001-FR-003 read tracking and no payment effects | T009, T013, T025, T036-T040 | T030, T033-T035, T041 |
| FR-004-FR-004A interaction endpoint and required idempotency key | T017-T018, T050-T052, T059 | T031, T042, T046-T048, T061 |
| FR-005-FR-007 eligibility and final delivery state | T009, T013, T025, T053-T057 | T030, T044-T049, T061, T071-T073 |
| FR-008-FR-011 Charge/Earn wallet and ledger atomicity | T008, T016, T026, T054-T058 | T043-T049, T061 |
| FR-012-FR-014A replay and conflict behavior | T011, T014, T024, T052, T058 | T046-T048, T066, T070 |
| FR-015 invalid/stale delivery protection | T019-T020, T053-T056, T076-T078 | T071-T075, T080 |
| FR-016-FR-017 feedback normalization and later-scope boundary | T018, T021, T057, T067-T069 | T031, T062-T066, T070 |
| FR-018 rate limiting | T039, T059 | T033, T042, T085 |
| FR-019 response envelope | T020, T038, T059, T078 | T033, T042, T086, T089 |
| FR-020 safe error handling/redaction | T019-T020, T060, T076-T078, T087 | T074-T075, T080, T087 |
| FR-021 audit evidence | T040, T060, T077 | T034, T044-T049, T075 |
| FR-022 scope exclusions | T079, T083, T087, T093 | T006, T079, T087, T093 |
| SC-001 read outcome | T036-T040 | T034-T035, T041 |
| SC-002-SC-006 exactly-once settlement and rollback | T050-T058 | T044-T049, T061 |
| SC-007 feedback | T067-T069 | T062-T066, T070 |
| SC-008 performance | T084 | T084, T090 |
| SC-009 authorization | T038-T039, T059, T076-T078 | T035, T074, T080 |
| SC-010 safe audit | T040, T060, T077, T087 | T075, T087 |
| CA-001-CA-003 layering/repository/UoW | T011-T027, T036-T060, T076-T079 | T079, T093 |
| CA-004-CA-006 envelope/error/JWT/security | T019-T020, T038-T039, T059, T076-T078 | T033, T042, T074-T075, T085-T086 |
| CA-007-CA-009 deterministic queue/wallet rules | T008-T016, T050-T058, T079 | T043-T049, T079, T093 |

Every FR, SC, and CA row must have at least one green proof task before T093 can be checked. If implementation changes a mapped requirement, update this table and the associated spec/plan artifacts before continuing.

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Phase 1 Setup
    ↓
Phase 2 Foundation (blocks every story)
    ↓
Phase 3 US1 Read Tracking MVP
    ↓
Phase 4 US2 Exact Settlement
    ↓
Phase 5 US3 Feedback
    ↓
Phase 6 US4 Invalid/Stale Protection
    ↓
Phase 7 Polish & Full Validation
```

- **Phase 1** has no production-code dependency and can start immediately.
- **Phase 2** depends on Phase 1 and blocks all user stories.
- **US1** depends on Phase 2 and is the MVP.
- **US2** depends on Phase 2 and can technically start after Foundation, but a single smaller model should complete US1 first because both stories edit `DoctorMessageService`, `DoctorMessagesController`, and shared test helpers.
- **US3** depends on US2 because feedback is stored during interaction settlement.
- **US4** depends on US1 and US2 because it hardens read and interaction failure branches.
- **Phase 7** depends on all selected user stories.

### User Story Dependencies and Independent Delivery

- **US1 (P1)**: Foundation only. Demonstrates read tracking without any payment effect.
- **US2 (P1)**: Foundation only in theory, but execute after US1 for a single model to avoid file conflicts. Demonstrates settlement without relying on feedback.
- **US3 (P2)**: Depends on US2 interaction flow. Demonstrates optional feedback while preserving settlement behavior.
- **US4 (P2)**: Depends on US1/US2 failure paths. Demonstrates safety and redaction for invalid states.

### Within Each User Story

- Write tests first and run the focused filter to prove red.
- Implement domain/entity changes before repository changes.
- Implement repository methods before service orchestration.
- Implement services before controller actions.
- Implement controller actions before contract/OpenAPI final checks.
- Run the story-specific focused tests before moving to the next story.

---

## Parallel Opportunities

- T002-T006 can run in parallel after T001 if test helper names are agreed.
- T010, T012, T017, T018, T021, T029-T031 can run in parallel after their direct dependencies are satisfied.
- T033-T035 can run in parallel for US1 test-first work.
- T042-T049 can run in parallel for US2 test-first work.
- T062-T066 can run in parallel for US3 test-first work.
- T071-T075 can run in parallel for US4 test-first work.
- T081-T086 can run in parallel during polish if all story implementation is complete.

### Parallel Example: User Story 1

```text
Task: "T033 Contract tests for read endpoint in tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs"
Task: "T034 Read idempotency integration tests in tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs"
Task: "T035 Read authorization integration tests in tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs"
```

### Parallel Example: User Story 2

```text
Task: "T042 Contract tests for interact endpoint in tests/contract/MediBridge.ContractTests/DoctorInteractionPaymentsContractTests.cs"
Task: "T044 Accept settlement integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs"
Task: "T046 Same-key replay integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionIdempotencyIntegrationTests.cs"
Task: "T048 Concurrent settlement integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionConcurrencyTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundation.
3. Complete Phase 3 US1 read tracking.
4. Stop and validate:
   - read endpoint returns standard envelope;
   - first read sets `ReadAtUtc`;
   - replay preserves `ReadAtUtc`;
   - no Charge, Earn, wallet, reservation, or interaction state changes.

### Incremental Delivery

1. US1 adds non-financial read tracking.
2. US2 adds exact settlement for Accept/Reject.
3. US3 adds feedback normalization/storage on top of settlement.
4. US4 hardens invalid states, authorization, consistency failures, and redaction.
5. Polish validates performance, documentation, scope guards, and full regression.

### Single Smaller-Model Strategy

Execute strictly T001 through T093. Ignore `[P]` unless you intentionally run independent tasks in separate workers. After each checkpoint, run the listed focused tests. If a test fails for any reason other than the expected missing behavior in a red-first task, stop and fix the setup before continuing. Never "make tests pass" by weakening assertions around money, idempotency, authorization, or redaction.

### Stop Conditions

Stop and update design artifacts before coding further if you discover:

- an existing model cannot represent `DeliveryInteractionOperation` without changing replay semantics;
- current wallet ledger design cannot represent company Reserved Debit or doctor Available Credit;
- a requirement would require platform wallet accounting, reporting, scoring, notifications, withdrawals, payouts, or queue/expiry behavior;
- any service or controller appears to need direct EF Core access;
- any test requires exposing raw idempotency keys, wallet balances, full feedback text, or protected delivery existence.

---

## Notes

- Phase 5 US3 feedback checkpoint (2026-07-10): Completed T062-T070 only. Red-first verification after writing Phase 5 tests exposed the missing OpenAPI feedback metadata: `DoctorInteractionPaymentsContractTests.InteractFeedbackContract_DocumentsOptionalNullableMaxLength_AndRejectsOversizedFeedback` failed because the generated interaction request schema did not document the required request shape and `FeedbackText` maxLength. Added `InteractionPaymentSchemaFilter` as documentation-only OpenAPI metadata so runtime trimming and the 1,000-character validation remain centralized in `InteractionPaymentValidation`. Runtime service, persistence configuration, and migration behavior already used normalized feedback consistently and already constrained persisted delivery/operation feedback to 1,000 characters without silent truncation. Added `Phase8InteractionFeedbackIntegrationTests` covering omitted feedback, whitespace-as-null, trimmed stored feedback linked to delivery and operation evidence, financially neutral Charge/Earn settlement, 1,000/1,001 length boundaries, and same-key feedback conflicts. Verification passed: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` (7 passed) and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionFeedback"` (4 passed). Phase 6 was not started.
- Phase 6 US4 invalid/stale protection checkpoint (2026-07-10): Completed T071-T080 only. Red-first verification after adding Phase 6 tests exposed a redaction gap: consistency audit metadata used the category `InsufficientCompanyReservedBalance`, which contained wallet-balance terminology. Renamed the safe category to `CompanyReservedInsufficient` while preserving the existing Phase 8 exception mapping and standard envelopes. Added `Phase8InvalidDeliveryProtectionTests` covering prior-day, future-day, Expired/Released, cross-owner, malformed, missing, already-settled, missing/deleted/mismatched wallet, insufficient company reserved, and invalid stored monetary snapshot cases with no partial Charge/Earn effects. Added `Phase8AuthorizationAndRedactionTests` covering unauthenticated, Company, Admin, unapproved/suspended/deleted/other Doctor denial plus validation/conflict/consistency/unexpected-failure redaction. Added real assertions to `Phase8LayeringAndScopeGuardTests` for Core/Services dependency boundaries, HTTP-only controller behavior, and forbidden later-scope Phase 8 additions. Verification passed: `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InvalidDeliveryProtection|FullyQualifiedName~Phase8AuthorizationAndRedaction|FullyQualifiedName~Phase8LayeringAndScopeGuard"` (17 passed). Phase 7 was not started.
- Phase 6 manual senior review (2026-07-10): Reviewed Phase 6 US4 invalid/stale protection manually without CodeRabbit. Architecture review passed: `DoctorMessagesController` remains an HTTP-only adapter, Phase 8 invalid-state branching and audit categories stay in `DoctorMessageService`, SQL Server locks and projections remain behind Repository/UoW abstractions, and no Core/Services EF Core dependency or later-scope reporting, scoring, notification, withdrawal, payout, admin, queue activation, or expiry-release behavior was introduced. Async and transaction review passed: controller/service/repository paths use async APIs with cancellation propagation, no sync-over-async patterns were found in the Phase 6 path, consistency failures return safe outcomes from the isolated Unit of Work without mutating delivery or wallet state, and unexpected settlement failures roll back through existing transaction handling. Security/redaction review passed: stale, missing, cross-owner, inactive Doctor, role-denied, consistency, conflict, validation, and unexpected failure paths use standard safe envelopes and do not expose raw idempotency keys, wallet balances, full feedback text, storage keys, signed URLs, serialized request bodies, connection strings, or stack traces. Review verification passed: `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InvalidDeliveryProtection|FullyQualifiedName~Phase8AuthorizationAndRedaction|FullyQualifiedName~Phase8LayeringAndScopeGuard"` (17 passed); `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors). No Phase 6 review findings remain open.
- Phase 7 polish and cross-cutting checkpoint (2026-07-10): Completed T081-T093 only and stopped after Phase 7. Reviewed `contracts/interaction-payments-api.yaml`, `quickstart.md`, and Phase 8 in `docs/backend-plan.md`; no documentation change was required because the implemented routes, required interaction `Idempotency-Key`, feedback cap, standard envelopes, read tracking, Accept/Reject settlement, and Charge/Earn behavior already matched. Added opt-in `Phase8InteractionPerformanceTests` for the documented SQL Server 2022 Release/Server-GC profile: 1,000 active deliveries, 20 warm-up reads/interactions, 200 measured reads and 200 measured interactions at concurrency 10, 95% under 1 second, zero duplicate financial effects, and explicit skip behavior outside prerequisites. Updated Swagger/OpenAPI coverage to expose read and interact, require `Idempotency-Key` only for interact, and preserve Doctor authorization/rate-limit/envelope expectations; this required correcting the OpenAPI idempotency filter so Phase 8 read tracking is not documented as idempotency-key protected. Updated success-envelope contract coverage for Phase 8 read and interact DTOs. Full integration validation exposed stale Phase 5/Phase 7 scope guards that still treated Phase 8 doctor read/interact routes as future scope; updated them to allow only the contracted Phase 8 routes while preserving exclusions for job control, analytics, withdrawals, weekly enforcement, activity scoring, notifications, and charge/earn route surfaces. Manual redaction review over APIs, Services, Repository, and Phase 8 tests found no new leak requiring extra assertions; existing Phase 8 redaction tests already cover raw idempotency keys, wallet balance names, full feedback text, storage keys, signed URLs, request-body wording, and stack traces. Requirement Traceability required no task-id mapping changes. Verification passed: `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj` (149 passed); `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj` (130 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj` (401 passed, 3 intentionally skipped opt-in performance tests: two Phase 7 and one Phase 8); `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors); `git diff --check` (0 whitespace errors; Git reported line-ending normalization warnings only). No Phase 7 review findings remain open.
- Phase 7 manual senior review (2026-07-10): Reviewed Phase 7 polish changes manually without CodeRabbit. Architecture review passed: OpenAPI idempotency metadata remains in `MediBridge.APIs/OpenApi`, response-envelope checks remain in contract tests, Swagger/rate-limit/authorization checks remain in integration tests, performance validation is opt-in test infrastructure only, and scope guards continue to block job control, analytics, withdrawals, weekly enforcement, activity scoring, notifications, and direct Charge/Earn route surfaces. Async review passed: no production async flow was altered by Phase 7 polish; the new performance test uses async HTTP/database calls, bounded `SemaphoreSlim` concurrency with release in `finally`, async disposal for the SQL Server-backed factory, and no sync-over-async patterns in the reviewed changes. Security and redaction review passed: read remains documented without `Idempotency-Key`, interact remains documented with required `Idempotency-Key`, response envelope tests assert no wallet balance, raw idempotency header, or platform-fee internals in success DTOs, and the performance test does not log connection strings or sensitive payloads. `tasks.md` already had T081-T093 marked `[X]`; this review confirms Phase 7 remains complete with no open senior-review findings.
- Phase 5 manual senior review (2026-07-10): Reviewed Phase 5 US3 feedback changes manually without CodeRabbit. Architecture review passed: OpenAPI feedback metadata is isolated to `MediBridge.APIs/OpenApi`, controllers remain HTTP-only adapters, feedback normalization stays centralized in `InteractionPaymentValidation`, service orchestration passes the normalized value consistently to replay evidence, delivery state, result DTOs, and redacted audit metadata, and SQL Server feedback length boundaries remain in Repository configuration/migration. Async and transaction review passed: request validation occurs before settlement transactions, all service/repository/test-host calls remain async with cancellation propagation where production paths expose it, and the invalid 1,001-character feedback path returns before delivery, wallet, transaction, ledger, or interaction evidence mutation. Security/scope review passed: raw idempotency keys, wallet balances, full feedback text, platform-fee internals, and persistence details are not exposed in response/audit metadata; feedback remains optional, replay-safe, bounded, and financially neutral; no Phase 6 invalid/stale hardening or later reporting/scoring/notification/payout behavior was added. Review verification passed: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` (7 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionFeedback"` (4 passed); `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors). No Phase 5 review findings remain open.
- Phase 3 US1 read tracking checkpoint (2026-07-10): Completed T033-T041 only. Red-first verification failed as expected before implementation: contract test reported missing `DoctorMessagesController.MarkRead`, and integration read requests returned 404. Implemented `PUT /api/doctor/messages/{deliveryId}/read` with Doctor-only service orchestration, current Egypt-date repository read lock, first-write-wins `ReadAtUtc`, standard envelope, Phase7 doctor-message read rate limit, and safe Created/Replayed audit metadata. Verification passed: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` (3 passed) and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8ReadTracking"` (2 passed). Phase 4 was not started.
- Phase 3 manual senior review (2026-07-10): Reviewed Phase 3 US1 read-tracking changes manually without CodeRabbit. Architecture review passed: controller remains an HTTP-only adapter; service owns Doctor resolution, Egypt business-date capture, transactional orchestration, and safe audit writing; SQL Server update-lock persistence remains behind `IDeliveryRepository`; no EF Core infrastructure was introduced into APIs or Services. Async/cancellation review passed: all repository, identity, audit, save, and transaction calls use async APIs with propagated cancellation tokens, and `ExecuteIsolatedInTransactionAsync` returns only detached DTO data. Security/scope review passed: Doctor authorization/rate limiting use the existing Phase 7 doctor-message policy, cross-role/cross-doctor attempts return safe envelopes, no `Idempotency-Key` is required for read, wallet balances and settlement state are not exposed, and no Phase 4 interaction, Charge/Earn settlement, reporting, feedback listing, scoring, notification, withdrawal, payout, admin, queue activation, or expiry behavior was added. Review verification passed: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests"` (3 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8ReadTracking"` (2 passed); `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors). No Phase 3 review findings remain open.
- Phase 4 US2 exact-settlement checkpoint (2026-07-10): Completed T042-T061 only. Red-first verification after writing tests failed for the expected missing interaction behavior: contract reflection reported missing `DoctorMessagesController.Interact`, and settlement/idempotency/concurrency/atomicity API tests received 404 before implementation. Implemented `POST /api/doctor/messages/{deliveryId}/interact` with required `Idempotency-Key`, Doctor interaction rate limit, service-level request/key/decision/feedback validation, approved active Doctor resolution, one captured Egypt business-clock snapshot, current-day Active/Reserved delivery locking, deterministic `DeliveryInteractionOperation` replay/conflict classification, monetary snapshot validation, company Reserved debit, doctor Available credit, deterministic Charge/Earn transactions and ledger entries, `DoctorAdDelivery.MarkInteracted`, successful operation linkage, safe Created/Replayed/Conflict/consistency audit metadata, and safe Phase 8 exception logging. Verification passed: `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors); `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliveryFinancialOperationKeysPhase8|FullyQualifiedName~Phase8DeliveryDomain" --no-build` (21 passed); `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests" --no-build` (6 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionSettlement|FullyQualifiedName~Phase8InteractionIdempotency|FullyQualifiedName~Phase8InteractionConcurrency|FullyQualifiedName~Phase8SettlementAtomicity" --no-build` (10 passed). Phase 5 was not started.
- Phase 4 manual senior review (2026-07-10): Reviewed Phase 4 US2 exact-settlement changes manually without CodeRabbit. Architecture review passed: `DoctorMessagesController` remains an HTTP-only adapter for route/body/header binding, `DoctorMessageService` owns validation, Doctor resolution, Egypt-date capture, replay classification, wallet orchestration, and audit writing, and SQL Server/EF Core locking remains behind Repository/UoW interfaces. Async and transaction review passed: service/controller paths use async APIs with propagated cancellation tokens, no sync-over-async patterns were found, and delivery state, wallet mutations, interaction evidence, Charge/Earn transactions, ledger entries, and audit evidence commit through the existing isolated Unit of Work transaction. Security and scope review passed: interaction uses Doctor authorization plus `RateLimitPolicyNames.DoctorInteraction`, required normalized `Idempotency-Key`, standard safe envelopes, no raw client idempotency key, wallet balance, stack trace, storage credential, signed URL, or full feedback text in service audit metadata/logs, and no Phase 8-forbidden reporting, feedback listing, weekly enforcement, scoring, notifications, withdrawals, payouts, admin tooling, queue activation, expiry release, or direct EF access was introduced. Review verification passed: `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors); `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliveryFinancialOperationKeysPhase8|FullyQualifiedName~Phase8DeliveryDomain" --no-build` (21 passed); `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPaymentsContractTests" --no-build` (6 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionSettlement|FullyQualifiedName~Phase8InteractionIdempotency|FullyQualifiedName~Phase8InteractionConcurrency|FullyQualifiedName~Phase8SettlementAtomicity" --no-build` (10 passed). No Phase 4 review findings remain open.
- Phase 2 manual senior review (2026-07-10): Reviewed the completed foundational changes for architecture boundaries, async/cancellation flow, SQL Server repository locking/projection behavior, EF migration safety, safe exception mapping, DTO redaction, and scope control. Phase 2 remains complete only; no user-story endpoint, settlement orchestration, reporting, scoring, notification, withdrawal, payout, admin, queue activation, or expiry behavior was implemented. Review verification passed: `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors); `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8DeliveryDomain|FullyQualifiedName~Phase8InteractionValidation" --no-build` (33 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8Migration" --no-build` (3 passed).
- Phase 2 foundational checkpoint (2026-07-10): Completed T008-T032 only. T016 required no interface change because `IWalletRepository` already exposes owner-explicit locked lookup via `FindActiveWalletForUpdateByOwnerAsync(WalletOwnerType, ownerId, ...)` plus `StageReservedBalanceChangeAsync(...)` and `StageAvailableBalanceChangeAsync(...)`; `WalletRepository` already uses SQL Server update locks for owner lookup. Generated `AddPhase8InteractionPayments` with `DeliveryInteractionOperations`, rowversion, unique replay boundary, feedback length narrowing, and an explicit guard that throws instead of silently truncating existing feedback over 1,000 characters. Verification passed: `dotnet build .\MediBridge.slnx --no-restore` (0 warnings, 0 errors); `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8DeliveryDomain|FullyQualifiedName~Phase8InteractionValidation" --no-build` (33 passed); `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8Migration" --no-build` (3 passed).
- Phase 1 setup checkpoint (2026-07-10): Added Phase 8 test scaffolding only. Required focused verification passed: `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8"` (2 passed) and `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~DoctorInteractionPayments"` (1 passed). Also compiled the integration project with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8" --no-restore`; no Phase 8 integration facts exist yet, as required by Phase 1.
- Phase 1 manual senior review (2026-07-10): Reviewed Phase 8 setup scaffolding for architecture boundaries, async/cancellation safety, task-scope compliance, and clean placeholder behavior. Phase 1 remains complete only; no Phase 2 or runtime behavior was implemented.
- `[P]` means different files and no dependency on incomplete symbols.
- `[US#]` labels map directly to the user stories in [spec.md](./spec.md).
- Keep all paths exactly under the existing project layout.
- Commit only after a green checkpoint or logical group if committing is requested.
- Leave unrelated dirty files untouched, especially pre-existing changes in `docs/backend-plan.md`.
