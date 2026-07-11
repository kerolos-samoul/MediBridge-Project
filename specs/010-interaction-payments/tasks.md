# Tasks: Phase 8 Interaction & Payments

**Input**: Design documents from `/specs/010-interaction-payments/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/interaction-payments-api.yaml](./contracts/interaction-payments-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required. The Phase 8 specification, plan, and quickstart define unit, contract, SQL Server integration, migration, concurrency, idempotency, security/rate-limit, audit-safety, feedback-validation, current-day, and performance-oriented validation. Within every user-story phase, create the tests first, run the focused test filter, and record that the new tests fail for the expected missing behavior before implementing the production tasks.

**Constitution Note**: Keep entities, enums, operation keys, and repository contracts in `MediBridge.Core`; SQL Server, EF Core configuration, row locking, and migrations in `MediBridge.Repository`; settlement orchestration in `MediBridge.Services`; and HTTP/rate-limit wiring in `MediBridge.APIs`. Controllers must stay HTTP-only. Every secured response uses `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; errors flow through global middleware without raw stack traces, wallet internals, or raw idempotency material.

**Execution Rule for a Smaller Model**: Execute tasks strictly in numeric order unless the task is explicitly marked `[P]` and every listed dependency is already complete. Do not combine tasks, rename planned types, change financial formulas, alter Phase 7 injector/expiry behavior, add company/admin analytics, add payout/payment-gateway behavior, or implement Phase 9+ enforcement/scoring without updating the design artifacts first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Safe to execute concurrently after its phase prerequisites because it targets different files and does not consume an unfinished symbol.
- **[Story]**: User-story traceability label. Setup, foundational, and polish tasks intentionally have no story label.
- Every task names the exact target file or directory and states the observable completion condition.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare Phase 8 test fixtures and confirm existing dependency/policy surfaces before changing runtime behavior.

- [X] T001 Verify existing Phase 7 Doctor message surface, wallet repositories, audit repositories, `RateLimitPolicyNames.DoctorInteraction`, and `WalletTransactionType.Charge/Earn` in `MediBridge.APIs/Controllers/DoctorMessagesController.cs`, `MediBridge.Services/Services/DoctorMessageService.cs`, `MediBridge.Core/Enums/Phase3DomainEnums.cs`, and `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`; record any missing planned surface in this task before starting implementation.
- [X] T002 [P] Create `tests/unit/MediBridge.UnitTests/Phase8InteractionTestData.cs` with deterministic helpers for delivery ids, idempotency keys, feedback text boundaries, Cairo timestamps, and expected Charge/Earn amounts; helpers must not perform production behavior.
- [X] T003 [P] Extend `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryTestHelpers.cs` with Phase 8 helpers for Active/Reserved current-day deliveries, doctor wallets, company reserved balances, and settled delivery assertions; helpers must accept explicit ids/timestamps/balances and must not call read/interact services.
- [X] T004 [P] Add Phase 8 OpenAPI contract fixture loading or snapshots in `tests/contract/MediBridge.ContractTests/DoctorMessageInteractionContractTests.cs`; include the `PUT /read` and `POST /interact` paths from `specs/010-interaction-payments/contracts/interaction-payments-api.yaml`.

**Checkpoint**: Phase 8 fixtures and contract-test scaffolding exist; runtime behavior is unchanged.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared domain, persistence, DTO, validation, repository, migration, and DI primitives required by every user story.

**⚠️ CRITICAL**: Complete all tasks in this phase before beginning any user-story implementation.

- [X] T005 [P] Add `DeliveryInteractionOutcome` enum with stable explicit values `Accept = 1` and `Reject = 2` to `MediBridge.Core/Enums/Phase3DomainEnums.cs`; do not alter existing enum values.
- [X] T006 [P] Extend `MediBridge.Core/Entities/Wallets/DeliveryFinancialOperationKeys.cs` with pure `ForCharge(string deliveryId)` and `ForEarn(string deliveryId)` methods returning exactly `delivery:charge:{deliveryId}` and `delivery:earn:{deliveryId}` after rejecting blank ids.
- [X] T007 [P] Create `MediBridge.Core/Entities/Messaging/DeliveryInteraction.cs` with fields from `data-model.md`, rowversion support, max feedback length constant 2000, safe idempotency-key hash storage, request fingerprint, Charge/Earn transaction links, optional audit link, and no raw idempotency-key persistence.
- [X] T008 Add `MarkRead(DateTime readAtUtc)` and `MarkInteractedAndCharged(DeliveryInteractionOutcome outcome, DateTime interactedAtUtc, string? feedbackText, FeedbackQualityStatus? feedbackQualityStatus)` domain methods to `MediBridge.Core/Entities/Messaging/DoctorAdDelivery.cs`; require UTC timestamps, preserve first read time, allow only Active/Reserved -> Accepted/Charged or Rejected/Charged, and reject invalid/repeated transitions without mutating state.
- [X] T009 [P] Create `MediBridge.Core/Interfaces/Messaging/DeliveryInteractionReadModels.cs` with immutable records for read tracking replay, interaction replay, idempotency conflict classification, locked settlement delivery, and Doctor-safe interaction result projection; include no EF Core types.
- [X] T010 Extend `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs` with owned current-day read lookup/update, owned Active/Reserved interaction lock, settled interaction replay lookup, and expected-state read/interact transition methods from `data-model.md`.
- [X] T011 Create `MediBridge.Core/Interfaces/Messaging/IDeliveryInteractionRepository.cs` with methods `FindByIdempotencyHashAsync`, `FindByDeliveryIdAsync`, and `AddInteractionAsync`; expose it from `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs` without exposing SQL, EF Core, `IQueryable`, or `DbContext`.
- [X] T012 [P] Create `MediBridge.Services/DTOs/Messaging/DoctorInteractionDtos.cs` containing `ReadTrackingResultDto`, `DoctorInteractionRequestDto`, and `DoctorInteractionResultDto` exactly matching the API contract and excluding wallet balances, raw idempotency material, and audit internals.
- [X] T013 [P] Create `MediBridge.Services/Validators/Messaging/DoctorInteractionRequestValidator.cs` to require outcome Accept/Reject, trim optional feedback, allow omitted/empty/whitespace-only/plain-text feedback, reject literal `<` or `>`, encoded `&lt;`/`&gt;` in any casing, Markdown links/images, `javascript:`/`data:` URI schemes, and feedback longer than 2,000 characters after trimming.
- [X] T014 [P] Create `MediBridge.Services/Services/DoctorInteractionIdempotency.cs` with deterministic normalization and hashing for Doctor id, client `Idempotency-Key`, delivery id, outcome, and normalized feedback; add no persistence dependency and never return raw key material for persistence, audit, logging, diagnostics, responses, or task evidence.
- [X] T015 Extend `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs` to configure `DeliveryInteraction` including unique `DeliveryId`, unique Doctor/idempotency hash scope, enum conversion, feedback length check, Charge/Earn transaction FKs, optional audit FK, rowversion, and no cascade path that can delete financial history.
- [X] T016 Add `DbSet<DeliveryInteraction> DeliveryInteractions` to `MediBridge.Repository/Data/MediBridgeDbContext.cs`.
- [X] T017 Implement the T010 delivery read/interact locking and transition contracts in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs` using bounded no-tracking reads for projections and SQL Server `UPDLOCK, ROWLOCK` only inside Unit of Work transactions.
- [X] T018 Create `MediBridge.Repository/Repositories/Messaging/DeliveryInteractionRepository.cs` implementing T011 with parameterized queries, normalized hashes only, rowversion-safe inserts, and no raw idempotency-key logging.
- [X] T019 Wire `IDeliveryInteractionRepository` through `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs` and register it in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T020 Generate EF Core migration `AddPhase8InteractionPayments` in `MediBridge.Repository/Migrations/` and update `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`; migration must create interaction evidence/indexes/checks/FKs, preserve Phase 7 job tables and existing wallet constraints, and perform no historical reads/interactions/charges/earns/releases/activations/expiry.
- [X] T021 [P] Add unit tests for foundational domain helpers in `tests/unit/MediBridge.UnitTests/Phase8DeliveryDomainTests.cs`, covering Charge/Earn keys, `MarkRead`, `MarkInteractedAndCharged`, invalid states, UTC timestamp validation, feedback quality status assignment, and no mutation on invalid transitions.
- [X] T022 [P] Add unit tests for `DoctorInteractionIdempotency` and `DoctorInteractionRequestValidator` in `tests/unit/MediBridge.UnitTests/Phase8InteractionValidationTests.cs`, covering same-key fingerprint equality, different content conflicts, no raw-key output, omitted/empty/whitespace/plain-text feedback, 14/15-character score boundary, 2,000/2,001-character boundary, and rejection of literal angle brackets, encoded angle brackets, Markdown links/images, `javascript:`, and `data:` patterns.
- [ ] T023 [P] Add SQL Server migration tests in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionMigrationTests.cs`, proving `DeliveryInteractions` constraints/indexes/FKs/checks exist, duplicate delivery/idempotency hashes fail, feedback length constraint holds, existing Phase 7 tables survive, and rollback/reapply does not mutate business rows.
- [X] T024 Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase8DeliveryDomain|FullyQualifiedName~Phase8InteractionValidation"`, `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase8InteractionMigration"`, and `dotnet build .\MediBridge.slnx`; fix only foundational failures before starting US1.

**Checkpoint**: Shared domain, DTOs, validation, repository contracts, EF mappings, migration, and DI primitives exist; no endpoint behavior is exposed yet.

---

## Phase 3: User Story 1 - Track Message Reads Without Billing (Priority: P1) 🎯 MVP

**Goal**: Record first-read time for the authenticated Doctor's current-day delivery without creating financial effects.

**Independent Test**: Authenticate as a Doctor with an Active current-day delivery, mark it read twice, and verify first-read time is preserved while delivery status, reservation status, company wallet, doctor wallet, transactions, ledgers, and financial audit records remain unchanged.

### Tests for User Story 1 — write and observe failure first

- [X] T025 [P] [US1] Add contract tests for `PUT /api/doctor/messages/{deliveryId}/read` success for Active and already Accepted/Rejected current-day deliveries, safe 401/403/404/429 envelopes, and response shape in `tests/contract/MediBridge.ContractTests/DoctorMessageInteractionContractTests.cs`.
- [X] T026 [P] [US1] Add SQL Server integration tests for read first-write-wins, repeated read replay, read-after-Accepted/Rejected preserving outcome/reservation/balances/transactions/ledger/platform-fee/audit state, current Cairo date enforcement, cross-doctor denial, and no wallet/transaction/ledger/audit mutation in `tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs`.
- [X] T027 [P] [US1] Add service unit tests for read tracking empty actor, invalid delivery id, approved Doctor resolution, first read, already-read replay, and cancellation propagation in `tests/unit/MediBridge.UnitTests/DoctorMessageReadTrackingServiceTests.cs`.

### Implementation for User Story 1

- [X] T028 [US1] Extend `MediBridge.Services/Interfaces/IDoctorMessageService.cs` with `MarkReadAsync(string actorUserId, string deliveryId, CancellationToken cancellationToken = default)` returning `ReadTrackingResultDto`.
- [X] T029 [US1] Implement read tracking in `MediBridge.Services/Services/DoctorMessageService.cs`: resolve approved active Doctor, capture one Cairo snapshot, lock owned current-day Active/Accepted/Rejected delivery, set `ReadAtUtc` only when null, return `AlreadyRead`, and create no wallet/ledger/transaction/financial-audit mutation or outcome/reservation change.
- [X] T030 [US1] Add `PUT api/doctor/messages/{deliveryId}/read` action to `MediBridge.APIs/Controllers/DoctorMessagesController.cs`; keep the controller HTTP-only, apply Doctor JWT authorization, apply `RateLimitPolicyNames.DoctorInteraction`, and return the standard envelope message `Message read recorded.`
- [X] T031 [US1] Update rate-limit tests in `tests/integration/MediBridge.IntegrationTests/RateLimitPolicyRegistrationTests.cs` and `tests/integration/MediBridge.IntegrationTests/RateLimitEnvelopeTests.cs` to prove the read endpoint uses `RateLimitPolicyNames.DoctorInteraction` and throttled reads do not call service mutation.
- [X] T032 [US1] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "ReadTracking"`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteraction"`, and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8ReadTracking|RateLimit"`; record the US1 checkpoint after all pass.

**Checkpoint**: User Story 1 is independently functional and is the MVP slice.

---

## Phase 4: User Story 2 - Settle Accept or Reject Exactly Once (Priority: P1)

**Goal**: Settle one eligible Accept or Reject interaction by charging company Reserved funds, crediting Doctor earnings, preserving platform-fee evidence, and storing optional feedback.

**Independent Test**: Prepare one Active current-day Reserved delivery with valid snapshots and wallets, submit Accept or Reject with optional feedback, and verify one final outcome, one company Charge, one platform-fee evidence record, and one Doctor Earn using stored snapshots.

### Tests for User Story 2 — write and observe failure first

- [X] T033 [P] [US2] Add contract tests for `POST /api/doctor/messages/{deliveryId}/interact` success, required `Idempotency-Key`, Accept/Reject body validation, plain-text feedback validation allowed/blocked patterns, raw-key redaction from responses, and response shape in `tests/contract/MediBridge.ContractTests/DoctorMessageInteractionContractTests.cs`.
- [X] T034 [P] [US2] Add SQL Server integration tests for Accept and Reject settlement, stored 100.00/12.345% -> 12.35/87.65 values, Charge/Earn transaction keys, company Reserved debit, Doctor Available credit, platform-fee evidence, stored feedback, and no company Available change in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs`.
- [X] T035 [P] [US2] Add unit tests for settlement calculation validation, feedback score eligibility, Doctor wallet availability/repair classification, and no current-policy recalculation in `tests/unit/MediBridge.UnitTests/DoctorMessageInteractionSettlementTests.cs`.

### Implementation for User Story 2

- [X] T036 [US2] Extend `MediBridge.Services/Interfaces/IDoctorMessageService.cs` with `InteractAsync(string actorUserId, string deliveryId, string? idempotencyKey, DoctorInteractionRequestDto request, CancellationToken cancellationToken = default)` returning `DoctorInteractionResultDto`.
- [X] T037 [US2] Implement the single-settlement transaction in `MediBridge.Services/Services/DoctorMessageService.cs`: validate idempotency key and feedback, resolve Doctor, capture Cairo snapshot, lock delivery, validate Active/Reserved current-day state and stored snapshots, lock company and Doctor wallets, stage delivery Accepted/Charged or Rejected/Charged, stage Charge/Earn transactions, ledger entries, platform-fee evidence, interaction evidence, and commit once.
- [X] T038 [US2] Add Charge/Earn ledger creation helpers to `MediBridge.Services/Services/DoctorMessageService.cs` or a scoped helper under `MediBridge.Services/Services/DoctorInteractionSettlementLedgerWriter.cs`; write company Reserved Debit and Doctor Available Credit entries with delivery/campaign/company/doctor references and no raw idempotency material.
- [X] T039 [US2] Add `POST api/doctor/messages/{deliveryId}/interact` action to `MediBridge.APIs/Controllers/DoctorMessagesController.cs`; require `Idempotency-Key` header, keep the controller HTTP-only, apply Doctor JWT authorization, apply `RateLimitPolicyNames.DoctorInteraction`, and return the standard envelope message `Message interaction recorded.`
- [X] T040 [US2] Register any new scoped settlement helper in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`; do not add EF Core or Repository implementation types to `MediBridge.Services`.
- [X] T041 [US2] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "InteractionSettlement"`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteraction"`, and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionSettlement"`; record the US2 checkpoint after all pass.

**Checkpoint**: User Story 2 settles a single eligible interaction and remains independently demonstrable through the Doctor HTTP contract.

---

## Phase 5: User Story 3 - Retry or Race Interactions Safely (Priority: P1)

**Goal**: Ensure repeated, conflicting, throttled, interrupted, and concurrent interaction requests converge on one financial outcome with safe audit evidence.

**Independent Test**: Submit same-key/same-content replays, same-key/different-content conflicts, different-key settled delivery requests, forced interruptions, and concurrent requests; prove at most one final outcome and one complete Charge/Fee/Earn effect exist.

### Tests for User Story 3 — write and observe failure first

- [X] T042 [P] [US3] Add SQL Server idempotency tests for same-key replay, same-key different outcome, same-key different feedback, different-key matching settled replay, and different-key conflicting settled request in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionIdempotencyTests.cs`.
- [X] T043 [P] [US3] Add SQL Server concurrency tests for two Accept requests, Accept versus Reject race, read versus interact race, forced failure before commit, forced failure after staged wallet changes, and rollback isolation in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionConcurrencyTests.cs`.
- [X] T044 [P] [US3] Add audit safety tests for successful settlement, idempotency conflict, reservation/snapshot anomaly, ordinary read no financial audit, and redaction of raw idempotency keys from persisted interaction evidence/logs/audits/responses plus wallet internals in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionAuditSafetyTests.cs`.
- [X] T045 [P] [US3] Add rate-limit integration tests proving read and interact endpoints use Doctor interaction throttling and throttled requests create no domain or financial mutation in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionRateLimitTests.cs`.

### Implementation for User Story 3

- [X] T046 [US3] Harden idempotency replay/conflict classification in `MediBridge.Services/Services/DoctorMessageService.cs` using `IDeliveryInteractionRepository`, normalized request fingerprints, existing delivery interaction lookup, and deterministic no-mutation conflict exits.
- [X] T047 [US3] Add replay verification helpers for Charge/Earn transactions and ledger entries in `MediBridge.Services/Services/DoctorMessageService.cs` so a completed settlement can be recognized without rebuilding partial financial evidence.
- [X] T048 [US3] Add isolated transaction/retry classification around interaction settlement in `MediBridge.Services/Services/DoctorMessageService.cs`; stale-state, unique-key, and concurrency conflicts must reload fresh state and return replay/conflict/anomaly without partial mutation.
- [X] T049 [US3] Implement safe audit creation for successful settlement, idempotency conflicts, and reservation/snapshot anomalies through existing audit repository abstractions in `MediBridge.Services/Services/DoctorMessageService.cs`; ordinary successful read tracking must not create financial audit records.
- [X] T050 [US3] Update `MediBridge.APIs/Controllers/DoctorMessagesController.cs` and rate-limit registration tests so both read and interact actions explicitly use `RateLimitPolicyNames.DoctorInteraction` and return safe 429 envelopes without invoking business mutation after throttling.
- [X] T051 [US3] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionIdempotency|Phase8InteractionConcurrency|Phase8InteractionAuditSafety|Phase8InteractionRateLimit"` and `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "Idempotency|InteractionSettlement"`; record the US3 checkpoint after all pass.

**Checkpoint**: User Story 3 proves exactly-once behavior under replay, conflict, concurrency, throttling, and failure injection.

---

## Phase 6: User Story 4 - Reject Ineligible Interaction Attempts (Priority: P2)

**Goal**: Refuse expired, stale-date, unauthorized, inconsistent-reservation, invalid-snapshot, or broken-wallet attempts without leaking protected data or mutating balances.

**Independent Test**: Attempt read tracking and interaction against expired, already settled, prior-day Active, missing-reservation, invalid-snapshot, missing-wallet, cross-doctor, non-doctor, and unauthenticated cases; prove safe errors and zero unauthorized/invalid financial effects.

### Tests for User Story 4 — write and observe failure first

- [X] T052 [P] [US4] Add SQL Server negative-flow tests for expired, Released, non-current Active, deleted delivery, already-settled conflicting, missing reservation evidence, insufficient company Reserved, invalid stored fee/earning formula, and missing/deleted/non-EGP wallets in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionIneligibleTests.cs`.
- [X] T053 [P] [US4] Add authorization contract and integration tests for unauthenticated, Company, Admin, another Doctor, suspended Doctor, deleted Doctor, and unapproved Doctor attempts against read and interact endpoints in `tests/contract/MediBridge.ContractTests/DoctorMessageInteractionAuthorizationTests.cs` and `tests/integration/MediBridge.IntegrationTests/Phase8InteractionAuthorizationTests.cs`.
- [X] T054 [P] [US4] Add current-day boundary tests using fake Cairo time for prior-day Active deliveries, tomorrow deliveries, DST boundary dates, and delayed expiry cases in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionBusinessDateTests.cs`.

### Implementation for User Story 4

- [X] T055 [US4] Add explicit anomaly classifiers and safe exception mapping for ineligible interaction attempts in `MediBridge.Services/Services/DoctorMessageService.cs`; map invalid reservation/snapshot/wallet state to safe 503 or configured domain error with no stack traces or wallet internals.
- [X] T056 [US4] Ensure repository methods in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs` never disclose cross-owner delivery existence and return null for non-current, deleted, expired, released, or non-owned deliveries at the service boundary.
- [X] T057 [US4] Ensure feedback validation failures in `MediBridge.Services/Validators/Messaging/DoctorInteractionRequestValidator.cs` occur before any delivery, wallet, interaction, ledger, or audit mutation and return safe 400 envelopes.
- [X] T058 [US4] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteractionAuthorization|DoctorMessageInteraction"`, `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionIneligible|Phase8InteractionAuthorization|Phase8InteractionBusinessDate"`, and `dotnet build .\MediBridge.slnx`; record the US4 checkpoint after all pass.

**Checkpoint**: User Story 4 prevents stale, unauthorized, and inconsistent settlement with safe no-mutation behavior.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation alignment, performance evidence, and constitution compliance.

- [X] T059 [P] Update `specs/010-interaction-payments/quickstart.md` with any final endpoint messages, command filters, migration name, or known opt-in performance prerequisites discovered during implementation.
- [X] T060 [P] Add or update focused performance tests for the Phase 8 profile in `tests/integration/MediBridge.IntegrationTests/Phase8InteractionPerformanceTests.cs`; tests must be opt-in, report unmet prerequisites as skips, and never count a skip as performance evidence.
- [X] T061 Run the full verification set from `specs/010-interaction-payments/quickstart.md`: unit, contract, integration, and `dotnet build .\MediBridge.slnx`; record pass/fail counts and any opt-in performance skips in `specs/010-interaction-payments/tasks.md`.
- [X] T062 Run `git diff --check` and inspect changed files for raw idempotency-key logging, wallet-balance disclosure, raw stack traces, EF Core leakage outside Repository, controller business logic, and unintended Phase 7/Phase 9+ behavior.
- [X] T063 Perform a final constitution compliance pass across `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`, and `tests/`; verify layering, thin controllers, Repository + Unit of Work persistence, JWT/role security, standard envelopes, safe errors, deterministic wallet rules, and no out-of-scope features.

### Verification Evidence - 2026-07-11

- US1 checkpoint: `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "ReadTracking"` passed 6/6; `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteraction"` passed 8/8; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8ReadTracking|RateLimit"` passed 15/15.
- US2 checkpoint: `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "InteractionSettlement"` passed 12/12; `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteraction"` passed 13/13; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionSettlement"` passed 2/2.
- US3 checkpoint: `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionIdempotency|Phase8InteractionConcurrency|Phase8InteractionAuditSafety|Phase8InteractionRateLimit"` passed 17/17; `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "Idempotency|InteractionSettlement"` passed 18/18.
- US4 checkpoint: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteractionAuthorization|DoctorMessageInteraction"` passed 16/16; `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionIneligible|Phase8InteractionAuthorization|Phase8InteractionBusinessDate"` passed 30/30; `dotnet build .\MediBridge.slnx` succeeded with 0 warnings and 0 errors.
- Polish checkpoint: `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionPerformance"` passed 1/1 with 1 opt-in Release reference-profile skip.
- `dotnet test .\MediBridge.slnx`: Unit 148/148 passed; Contract 139/139 passed; Integration 422/426 passed with 4 opt-in skips.
- `dotnet build .\MediBridge.slnx`: succeeded with 0 warnings and 0 errors.
- `dotnet ef database update --context MediBridge.Repository.Data.MediBridgeDbContext --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj` with `ConnectionStrings__DefaultConnection` set from API configuration: applied `20260711130805_AddPhase8InteractionPayments` to the configured SQL Server.
- `__EFMigrationsHistory` verification: `20260711130805_AddPhase8InteractionPayments` is present; an older pre-existing `20260710095601_AddPhase8InteractionPayments` row is also present and was preserved.
- Real SQL Server schema verification: `DeliveryInteractions` table exists with unique `IX_DeliveryInteractions_DeliveryId`, unique `IX_DeliveryInteractions_DoctorId_IdempotencyKeyHash`, and `CK_DeliveryInteractions_Feedback_Length` / `CK_DeliveryInteractions_Outcome`.
- `git diff --check`: no whitespace errors; CRLF normalization warnings only.
- Safe evidence JSON: `specs/010-interaction-payments/evidence/phase8-evidence-20260711-171021.json`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies; can start immediately.
- **Phase 2 Foundational**: Depends on Phase 1; blocks every user story.
- **Phase 3 US1**: Depends on Phase 2; MVP slice.
- **Phase 4 US2**: Depends on Phase 2 and can start after service/controller signatures are stable; validating after US1 is recommended because both extend the same files.
- **Phase 5 US3**: Depends on US2 settlement behavior.
- **Phase 6 US4**: Depends on US2/US3 settlement and replay/anomaly paths.
- **Phase 7 Polish**: Depends on desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational; no dependency on other stories.
- **US2 (P1)**: Can start after Foundational; shares `DoctorMessageService`/controller files with US1, so sequential execution reduces merge conflict risk.
- **US3 (P1)**: Depends on US2 because retry/race/audit behavior hardens settlement.
- **US4 (P2)**: Depends on US2 and US3 because it verifies negative and anomaly behavior across the completed interaction path.

### Parallel Opportunities

- T002-T004 can run in parallel after T001.
- T005-T007, T009, T012-T014 can run in parallel after Phase 1.
- T021-T023 can run in parallel after their target production contracts/entities exist.
- Within each user story, test files marked `[P]` can be written in parallel before implementation.
- US1 and US2 are conceptually independent after Phase 2, but both edit `IDoctorMessageService`, `DoctorMessageService`, and `DoctorMessagesController`; run them sequentially unless separate implementers coordinate patches carefully.

---

## Parallel Example: User Story 1

```text
Task: "T025 [US1] Add contract tests for read endpoint in tests/contract/MediBridge.ContractTests/DoctorMessageInteractionContractTests.cs"
Task: "T026 [US1] Add SQL Server integration tests for read tracking in tests/integration/MediBridge.IntegrationTests/Phase8ReadTrackingIntegrationTests.cs"
Task: "T027 [US1] Add service unit tests for read tracking in tests/unit/MediBridge.UnitTests/DoctorMessageReadTrackingServiceTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T033 [US2] Add contract tests for interact endpoint in tests/contract/MediBridge.ContractTests/DoctorMessageInteractionContractTests.cs"
Task: "T034 [US2] Add settlement integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionSettlementIntegrationTests.cs"
Task: "T035 [US2] Add settlement unit tests in tests/unit/MediBridge.UnitTests/DoctorMessageInteractionSettlementTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T042 [US3] Add idempotency integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionIdempotencyTests.cs"
Task: "T043 [US3] Add concurrency integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionConcurrencyTests.cs"
Task: "T044 [US3] Add audit safety integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionAuditSafetyTests.cs"
Task: "T045 [US3] Add rate-limit integration tests in tests/integration/MediBridge.IntegrationTests/Phase8InteractionRateLimitTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational schema/contracts/DTOs/migration.
3. Complete Phase 3 US1 read tracking.
4. Stop and validate read tracking independently with no financial effects.

### Incremental Delivery

1. US1: read tracking, non-financial first-write-wins.
2. US2: single successful Accept/Reject settlement.
3. US3: idempotency, concurrency, audit, and rate-limit hardening.
4. US4: stale/unauthorized/anomaly protections.
5. Polish: performance, full verification, constitution pass.

### Notes

- Tests in each story must fail before implementation and pass before moving to the next checkpoint.
- `[P]` tasks target different files or test files and are safe only after prerequisites are complete.
- Do not persist, log, audit, return, or write task evidence with raw `Idempotency-Key`, wallet balances, storage details, or stack traces; use only normalized hashes/fingerprints or non-sensitive conflict categories.
- Keep every financial mutation inside a single Unit of Work transaction.
- Do not change Phase 7 daily injection/expiry semantics while implementing Phase 8.
