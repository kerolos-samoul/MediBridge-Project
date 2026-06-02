# Tasks: Database & Core Models (Phase 3)

**Input**: Design documents from `/specs/003-database-core-models/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/persistence-contracts.md](./contracts/persistence-contracts.md), [quickstart.md](./quickstart.md)

**Tests**: Required. The Phase 3 spec and quickstart require automated validation for schema setup, Phase 2 identity/refresh preservation, repository/UoW boundaries, FIFO queue ordering, delivery uniqueness, wallet idempotency, money precision rejection, refund and withdrawal ledger semantics, atomic wallet transaction plus ledger commits, soft-delete filtering, and append-only audit/history correction behavior.

**Constitution Note**: All implementation must preserve Onion Architecture. `MediBridge.Core` must contain pure domain entities, enums, and contracts only. `MediBridge.Repository` is the only layer allowed to use EF Core or SQL Server infrastructure. `MediBridge.Services` may consume Core abstractions only. `MediBridge.APIs` controllers must remain HTTP-only and must not use repositories, EF Core, `MediBridgeDbContext`, or persistence implementations directly. Phase 3 must not add public validation endpoints or diagnostic controllers.

**Organization**: Tasks are grouped by user story to enable independent implementation and validation. Phase 1 and Phase 2 are shared prerequisites. User Story phases are ordered by priority from the specification.

## Format: `[ID] [P?] [Story] Description`

- **[P]** means the task can run in parallel with other `[P]` tasks in the same phase because it edits different files and does not depend on incomplete tasks.
- **[US1]**, **[US2]**, **[US3]**, and **[US4]** map to the user stories in [spec.md](./spec.md).
- Every task includes at least one exact file path.

## Path Conventions

- Core domain code: `MediBridge.Core/`
- EF Core SQL Server persistence: `MediBridge.Repository/`
- Service orchestration and validators, if needed: `MediBridge.Services/`
- Existing API project only for architecture-boundary checks: `MediBridge.APIs/`
- Integration tests: `tests/integration/MediBridge.IntegrationTests/`
- Unit tests: `tests/unit/MediBridge.UnitTests/`
- Feature docs: `specs/003-database-core-models/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare files, folders, and test scaffolding that all Phase 3 work will use.

- [X] T001 Create Phase 3 Core entity folders `MediBridge.Core/Entities/Campaigns`, `MediBridge.Core/Entities/Files`, `MediBridge.Core/Entities/Messaging`, `MediBridge.Core/Entities/Policies`, and `MediBridge.Core/Entities/Wallets`.
- [X] T002 Create Phase 3 Core interface folders `MediBridge.Core/Interfaces/Campaigns`, `MediBridge.Core/Interfaces/Files`, `MediBridge.Core/Interfaces/Messaging`, `MediBridge.Core/Interfaces/Policies`, and `MediBridge.Core/Interfaces/Wallets`.
- [X] T003 Create Phase 3 Repository configuration folders `MediBridge.Repository/Configurations/Campaigns`, `MediBridge.Repository/Configurations/Files`, `MediBridge.Repository/Configurations/Messaging`, `MediBridge.Repository/Configurations/Policies`, and `MediBridge.Repository/Configurations/Wallets`.
- [X] T004 Create Phase 3 Repository implementation folders `MediBridge.Repository/Repositories/Campaigns`, `MediBridge.Repository/Repositories/Files`, `MediBridge.Repository/Repositories/Messaging`, `MediBridge.Repository/Repositories/Policies`, and `MediBridge.Repository/Repositories/Wallets`.
- [X] T005 [P] Create a Phase 3 test fixture helper skeleton for SQL Server-backed integration tests in `tests/integration/MediBridge.IntegrationTests/Phase3DatabaseTestHelpers.cs`.
- [X] T006 [P] Create a Phase 3 money assertion helper skeleton in `tests/unit/MediBridge.UnitTests/Phase3MoneyPrecisionTests.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared enums, validation primitives, and repository/UoW contracts that must exist before implementing any user story.

**CRITICAL**: No user story implementation should begin until this phase is complete.

- [X] T007 Define marketplace status enums `DoctorMarketplaceStatus`, `CampaignStatus`, `CampaignReviewDecision`, `QueueItemStatus`, `DeliveryStatus`, `ReservationStatus`, `FeedbackQualityStatus`, `WalletOwnerType`, `WalletTransactionType`, `WalletLedgerEntryDirection`, `WalletBalanceType`, `WithdrawalRequestStatus`, `StoredFileOwnerType`, `StoredFilePurpose`, `StoredFileVisibility`, `StoredFileReviewStatus`, `AuditOutcome`, and `AuditTargetType` in `MediBridge.Core/Enums/Phase3DomainEnums.cs`. `WalletOwnerType` must include `Doctor`, `Company`, and `Platform`. `WalletTransactionType` must use exactly `TopUp = 1`, `Reserve = 10`, `Release = 11`, `Charge = 20`, `Earn = 21`, `Refund = 22`, `WithdrawRequest = 30`, `WithdrawApproved = 31`, `WithdrawRejected = 32`, and `WithdrawPayout = 33`; do not add a vague standalone `Withdraw` value.
- [X] T008 Define an EF-free money validation helper that rejects values with more than two decimal places and negative values when disallowed in `MediBridge.Core/Entities/Wallets/MoneyRules.cs`.
- [X] T009 Define an EF-free soft-delete marker contract with `IsDeleted` and `DeletedAtUtc` in `MediBridge.Core/Entities/ISoftDeleteRecord.cs`.
- [X] T010 Define an EF-free optimistic concurrency marker with `ConcurrencyToken` in `MediBridge.Core/Entities/IConcurrencyTrackedRecord.cs`.
- [X] T011 Define the aggregate `IDomainUnitOfWork` contract with repository properties, `SaveChangesAsync`, and transaction helper overloads in `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`.
- [X] T012 [P] Define `ICampaignRepository` contract methods for campaigns, targets, and campaign review history in `MediBridge.Core/Interfaces/Campaigns/ICampaignRepository.cs`.
- [X] T013 [P] Define `IMessageQueueRepository` contract methods for adding queue items, querying active queued items by doctor/status in FIFO order using `QueuedAtUtc ASC` then `Id ASC`, and updating queue item status in `MediBridge.Core/Interfaces/Messaging/IMessageQueueRepository.cs`. Document that `QueuedAtUtc` is derived from campaign submission time or queue insertion time and that Phase 3 has no priority queue behavior.
- [X] T014 [P] Define `IDeliveryRepository` contract methods for adding deliveries, finding deliveries by doctor/date/campaign, and querying delivery history in `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs`.
- [X] T015 [P] Define `IWalletRepository` contract methods for wallet lookup by owner, adding wallets, staging balance changes, and active-record query behavior in `MediBridge.Core/Interfaces/Wallets/IWalletRepository.cs`.
- [X] T016 [P] Define `IWalletTransactionRepository` contract methods for append-only transaction creation, idempotency lookup by operation type plus key, and wallet transaction browsing in `MediBridge.Core/Interfaces/Wallets/IWalletTransactionRepository.cs`.
- [X] T017 [P] Define `IStoredFileRepository` contract methods for file metadata creation, active metadata lookup, and review metadata lookup in `MediBridge.Core/Interfaces/Files/IStoredFileRepository.cs`.
- [X] T018 [P] Define `IPolicyHistoryRepository` contract methods for doctor price, platform fee, and activity score history records in `MediBridge.Core/Interfaces/Policies/IPolicyHistoryRepository.cs`.
- [X] T019 [P] Define `IAuditEventRepository` contract methods for append-only audit event creation and safe audit lookup in `MediBridge.Core/Interfaces/Policies/IAuditEventRepository.cs`.
- [X] T020 [P] Define `IWalletLedgerEntryRepository` contract methods for immutable ledger entry creation and lookup by wallet, wallet transaction, related campaign/delivery/withdrawal references, and created time in `MediBridge.Core/Interfaces/Wallets/IWalletLedgerEntryRepository.cs`.
- [X] T021 Add `IWalletLedgerEntryRepository` to the `IDomainUnitOfWork` contract so wallet balance changes, wallet transactions, and wallet ledger entries can be committed together in `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`.

**Checkpoint**: Core contracts and shared primitives exist. The executor can compile after each foundational task if all referenced types are included in the same task.

---

## Phase 3: User Story 1 - Persist Core Marketplace Records (Priority: P1) MVP

**Goal**: Persist all authoritative Phase 3 records needed by later campaign, delivery, wallet, file, policy, and audit workflows.

**Independent Test**: Apply the Phase 3 model to a clean SQL Server database, create one valid sample for each core entity, and read those samples back only through repository and unit-of-work boundaries.

### Tests for User Story 1

- [X] T022 [P] [US1] Add failing integration test proving Phase 3 entity DbSets and EF mappings can create/read a complete sample graph in `tests/integration/MediBridge.IntegrationTests/Phase3CoreEntityPersistenceTests.cs`.
- [X] T023 [P] [US1] Add failing integration test proving the Phase 3 migration applies cleanly to SQL Server and preserves Phase 2 Identity tables, Doctor/Company/Admin roles, account approval state, the `RefreshCredential` table/entity, and refresh-token replacement tracing in `tests/integration/MediBridge.IntegrationTests/Phase3MigrationSchemaTests.cs`.
- [X] T024 [P] [US1] Add failing integration test proving stored file metadata persists without implementing upload or retrieval behavior in `tests/integration/MediBridge.IntegrationTests/Phase3StoredFileMetadataTests.cs`.

### Implementation for User Story 1

- [X] T025 [P] [US1] Extend `DoctorProfile` with marketplace fields `DailyMessageLimit`, `MinimumWeeklyRequirement`, `RequestedDailyMessageLimit`, `RequestedMinimumWeeklyRequirement`, `ActivityScore`, `Status`, `PricePerMessage`, `IsDeleted`, and `DeletedAtUtc` in `MediBridge.Core/Entities/Profiles/DoctorProfile.cs`.
- [X] T026 [P] [US1] Extend `CompanyProfile` with `IsDeleted` and `DeletedAtUtc` fields while preserving existing registration fields in `MediBridge.Core/Entities/Profiles/CompanyProfile.cs`.
- [X] T027 [P] [US1] Create `Campaign` entity with company ownership, content metadata, status, timestamps, and soft-delete fields in `MediBridge.Core/Entities/Campaigns/Campaign.cs`.
- [X] T028 [P] [US1] Create `CampaignTarget` entity with doctor targeting snapshots for specialization, experience, location, activity score, and price in `MediBridge.Core/Entities/Campaigns/CampaignTarget.cs`.
- [X] T029 [P] [US1] Create `CampaignReviewHistory` append-only entity with reviewer, decision, reason/notes, timestamp, and correction link in `MediBridge.Core/Entities/Campaigns/CampaignReviewHistory.cs`.
- [X] T030 [P] [US1] Create `DoctorMessageQueue` entity with doctor, campaign, `QueuedAtUtc` FIFO ordering key, optional `CampaignSubmittedAtUtc` trace field, status, and timestamps in `MediBridge.Core/Entities/Messaging/DoctorMessageQueue.cs`.
- [X] T031 [P] [US1] Create `DoctorAdDelivery` entity with doctor, campaign, company, Egypt delivery date, read/interact/feedback fields, reservation fields, money snapshot fields, and concurrency token in `MediBridge.Core/Entities/Messaging/DoctorAdDelivery.cs`.
- [X] T032 [P] [US1] Create `Wallet` entity with owner type (`Doctor`, `Company`, `Platform`), `OwnerUserId`, owner id, `AvailableBalance`, `ReservedBalance`, `Currency = EGP`, timestamps, soft-delete fields, and concurrency token in `MediBridge.Core/Entities/Wallets/Wallet.cs`.
- [X] T033 [P] [US1] Create `WalletTransaction` append-only entity and `WalletLedgerEntry` immutable entity in `MediBridge.Core/Entities/Wallets/WalletTransaction.cs` and `MediBridge.Core/Entities/Wallets/WalletLedgerEntry.cs`. `WalletTransaction` must include operation type, idempotency key, amount, related delivery id, safe metadata, timestamp, and correction link. `WalletLedgerEntry` must include transaction id, wallet id, debit/credit direction, amount, available/reserved balance type, currency, created time, relevant campaign/delivery/doctor/company/withdrawal references, and idempotency key where needed.
- [X] T034 [P] [US1] Create `WithdrawalRequest` entity with doctor id, amount, status, admin decision metadata, payout reference, request/review timestamps, and concurrency token in `MediBridge.Core/Entities/Wallets/WithdrawalRequest.cs`.
- [X] T035 [P] [US1] Create `StoredFile` entity with owner, purpose, original filename, content type, size, storage key, visibility, review status, and review metadata in `MediBridge.Core/Entities/Files/StoredFile.cs`.
- [X] T036 [P] [US1] Create `DoctorPriceHistory`, `PlatformFeePolicyHistory`, and `ActivityScoreHistory` entities with append-only correction links in `MediBridge.Core/Entities/Policies/PolicyHistoryRecords.cs`.
- [X] T037 [P] [US1] Create `AuditEvent` append-only entity with actor, target, outcome, reason, correlation id, safe metadata, timestamp, and correction link in `MediBridge.Core/Entities/Policies/AuditEvent.cs`.
- [X] T038 [US1] Add DbSet properties for all Phase 3 entities in `MediBridge.Repository/Data/MediBridgeDbContext.cs`.
- [X] T039 [P] [US1] Configure EF mapping for extended doctor/company profile fields, active license uniqueness, money precision, and soft-delete fields in `MediBridge.Repository/Configurations/Identity/ProfileConfigurations.cs`.
- [X] T040 [P] [US1] Configure EF mappings for `Campaign`, `CampaignTarget`, and `CampaignReviewHistory` in `MediBridge.Repository/Configurations/Campaigns/CampaignConfigurations.cs`.
- [X] T041 [P] [US1] Configure EF mappings for `DoctorMessageQueue` and `DoctorAdDelivery` in `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`.
- [X] T042 [P] [US1] Configure EF mappings for `Wallet`, `WalletTransaction`, `WalletLedgerEntry`, and `WithdrawalRequest` in `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T043 [P] [US1] Configure EF mapping for `StoredFile` in `MediBridge.Repository/Configurations/Files/StoredFileConfiguration.cs`.
- [X] T044 [P] [US1] Configure EF mappings for policy history and audit event records in `MediBridge.Repository/Configurations/Policies/PolicyAndAuditConfigurations.cs`.
- [X] T045 [US1] Implement campaign repository create/read methods for campaigns, targets, and review history in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`.
- [X] T046 [US1] Implement stored file metadata repository create/read methods in `MediBridge.Repository/Repositories/Files/StoredFileRepository.cs`.
- [X] T047 [US1] Implement policy history repository create/read methods in `MediBridge.Repository/Repositories/Policies/PolicyHistoryRepository.cs`.
- [X] T048 [US1] Implement audit event repository create/read methods in `MediBridge.Repository/Repositories/Policies/AuditEventRepository.cs`.
- [X] T049 [US1] Implement `DomainUnitOfWork` exposing all Phase 3 repositories and transaction helpers in `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`.
- [X] T050 [US1] Register Phase 3 repositories and `IDomainUnitOfWork` in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T051 [US1] Generate EF migration `Phase3DatabaseCoreModels` in `MediBridge.Repository/Migrations` using startup project `MediBridge.APIs/MediBridge.APIs.csproj`.
- [X] T052 [US1] Verify US1 by running only Phase 3 persistence tests from `tests/integration/MediBridge.IntegrationTests/Phase3CoreEntityPersistenceTests.cs` and `tests/integration/MediBridge.IntegrationTests/Phase3MigrationSchemaTests.cs`.

**Checkpoint**: User Story 1 is complete when all authoritative Phase 3 entities can be created/read through repositories/UoW and the migration applies cleanly.

---

## Phase 4: User Story 2 - Protect Queue, Delivery, and Wallet Integrity (Priority: P1)

**Goal**: Enforce queue ordering, delivery uniqueness, wallet idempotency, money precision rejection, and atomic wallet ledger behavior.

**Independent Test**: Create representative queue, delivery, and wallet records; verify FIFO order, duplicate delivery rejection, duplicate wallet transaction rejection, >2 decimal rejection, and wallet balance plus transaction atomicity.

### Tests for User Story 2

- [X] T053 [P] [US2] Add failing integration test for per-doctor queue ordering by `QueuedAtUtc ASC, Id ASC`, including same-timestamp tie-breaker and next-day carry-over ordering for items that exceed a later daily limit, in `tests/integration/MediBridge.IntegrationTests/Phase3QueueOrderingTests.cs`.
- [X] T054 [P] [US2] Add failing integration test for unique `(DoctorId, DeliveryDateEgypt, CampaignId)` delivery constraint in `tests/integration/MediBridge.IntegrationTests/Phase3DeliveryConstraintTests.cs`.
- [X] T055 [P] [US2] Add failing integration test for unique `(OperationType, IdempotencyKey)` wallet transaction constraint proving duplicate top-up, charge, earn, refund, and withdrawal payout retries do not double-apply financial effects in `tests/integration/MediBridge.IntegrationTests/Phase3WalletIdempotencyTests.cs`.
- [X] T056 [P] [US2] Add failing unit tests for `MoneyRules` rejecting more than two decimal places and negative values when disallowed in `tests/unit/MediBridge.UnitTests/Phase3MoneyPrecisionTests.cs`.
- [X] T057 [P] [US2] Add failing integration tests proving wallet balance changes, wallet transactions, and wallet ledger entries commit or roll back together in `tests/integration/MediBridge.IntegrationTests/Phase3WalletAtomicityTests.cs`, and proving wallet flows in `tests/integration/MediBridge.IntegrationTests/Phase3WalletLedgerFlowTests.cs`: `TopUp`, `Reserve`, billable delivery `Charge` + `Earn` + Platform fee, `Refund`, withdrawal approval plus `WithdrawPayout`, withdrawal rejection returning reserved funds to available, positive amounts, no negative balances, and immutable ledger entries.

### Implementation for User Story 2

- [X] T058 [US2] Implement ordered queue query method in `MediBridge.Repository/Repositories/Messaging/MessageQueueRepository.cs` using active records filtered by doctor/status and ordered by `QueuedAtUtc ASC` then `Id ASC`; do not implement priority ordering.
- [X] T059 [US2] Implement delivery repository add/find methods and duplicate detection support in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T060 [US2] Implement wallet repository lookup and balance staging methods that reject negative resulting balances in `MediBridge.Repository/Repositories/Wallets/WalletRepository.cs`.
- [X] T061 [US2] Implement wallet transaction repository append-only creation and idempotency lookup by operation type plus key in `MediBridge.Repository/Repositories/Wallets/WalletTransactionRepository.cs`, and implement wallet ledger entry repository immutable creation and lookup methods in `MediBridge.Repository/Repositories/Wallets/WalletLedgerEntryRepository.cs`.
- [X] T062 [US2] Update `DomainUnitOfWork` to ensure wallet balance updates, wallet transaction inserts, and wallet ledger entry inserts use one EF Core transaction in `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`.
- [X] T063 [US2] Add or verify queue ordering index `(DoctorId, Status, QueuedAtUtc, Id)` and make repository queries order by `QueuedAtUtc ASC, Id ASC` in `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`.
- [X] T064 [US2] Add or verify delivery unique index `(DoctorId, DeliveryDateEgypt, CampaignId)` in `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`.
- [X] T065 [US2] Add or verify wallet owner active uniqueness for Doctor/Company/Platform wallets, wallet transaction unique index `(OperationType, IdempotencyKey)`, wallet ledger browsing indexes, and positive amount constraints where supported in `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T066 [US2] Apply `MoneyRules` checks in wallet, wallet transaction, delivery money snapshots, withdrawal request, and policy history entity factory/update methods in `MediBridge.Core/Entities/Wallets/MoneyRules.cs`.
- [X] T067 [US2] Register message queue, delivery, wallet, wallet transaction, and wallet ledger entry repositories in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T068 [US2] Update migration or add a follow-up migration for US2 indexes and constraints in `MediBridge.Repository/Migrations`.
- [X] T069 [US2] Verify US2 by running `dotnet test` for `tests/integration/MediBridge.IntegrationTests/Phase3QueueOrderingTests.cs`, `Phase3DeliveryConstraintTests.cs`, `Phase3WalletIdempotencyTests.cs`, `Phase3WalletAtomicityTests.cs`, `Phase3WalletLedgerFlowTests.cs`, and `tests/unit/MediBridge.UnitTests/Phase3MoneyPrecisionTests.cs`.

**Checkpoint**: User Story 2 is complete when queue, delivery, and wallet integrity rules fail before implementation and pass afterward.

---

## Phase 5: User Story 3 - Enforce Application Data Boundaries (Priority: P2)

**Goal**: Prove services and controllers cannot bypass Core contracts and cannot depend directly on EF Core, SQL Server infrastructure, or repository implementations.

**Independent Test**: Inspect project references and compiled types to confirm Core is persistence-free, Services consume Core abstractions only, Repository owns EF Core implementations, and API controllers remain HTTP-only.

### Tests for User Story 3

- [X] T070 [P] [US3] Add failing architecture test that `MediBridge.Core` references no EF Core, ASP.NET Core HTTP, Repository, or API assemblies in `tests/integration/MediBridge.IntegrationTests/Phase3LayeringBoundaryTests.cs`.
- [X] T071 [P] [US3] Add failing architecture test that `MediBridge.Services` has no direct dependency on `MediBridgeDbContext`, EF Core infrastructure types, or `MediBridge.Repository.Repositories` concrete classes in `tests/integration/MediBridge.IntegrationTests/Phase3ServicePersistenceBoundaryTests.cs`.
- [X] T072 [P] [US3] Add failing architecture test that `MediBridge.APIs` controllers do not inject Phase 3 repositories, `IDomainUnitOfWork`, or `MediBridgeDbContext` in `tests/integration/MediBridge.IntegrationTests/Phase3ControllerBoundaryTests.cs`.

### Implementation for User Story 3

- [X] T073 [US3] Review `MediBridge.Core/MediBridge.Core.csproj` and keep it free of EF Core, ASP.NET Core, Repository, and API package/project references.
- [X] T074 [US3] Review `MediBridge.Services/MediBridge.Services.csproj` and keep it free of EF Core package references and direct Repository implementation dependencies.
- [X] T075 [US3] If a Phase 3 validation service is needed, define only service interfaces in `MediBridge.Services/Interfaces/IPhase3PersistenceValidationService.cs`.
- [X] T076 [US3] If a Phase 3 validation service is needed, implement it using only `IDomainUnitOfWork` and Core contracts in `MediBridge.Services/Services/Phase3PersistenceValidationService.cs`.
- [X] T077 [US3] Add a clear no-controller validation note to `specs/003-database-core-models/quickstart.md` stating that repository, integration, migration, and service-boundary tests are the Phase 3 validation surface.
- [X] T078 [US3] Update architecture tests to fail if `MediBridge.APIs/Controllers/Phase3DiagnosticsController.cs` or any new Phase 3 public/diagnostic controller exists in `tests/integration/MediBridge.IntegrationTests/Phase3ControllerBoundaryTests.cs`.
- [X] T079 [US3] Verify US3 by running architecture tests in `tests/integration/MediBridge.IntegrationTests/Phase3LayeringBoundaryTests.cs`, `Phase3ServicePersistenceBoundaryTests.cs`, and `Phase3ControllerBoundaryTests.cs`.

**Checkpoint**: User Story 3 is complete when layering tests pass and no API controller contains Phase 3 business or persistence logic.

---

## Phase 6: User Story 4 - Capture Policy and Audit History (Priority: P2)

**Goal**: Persist append-only policy, review, activity, financial, and audit history with linked correction/reversal records and safe metadata.

**Independent Test**: Record sample history events, create corrections, and confirm originals remain unchanged while linked correction records explain the change.

### Tests for User Story 4

- [X] T080 [P] [US4] Add failing integration test for append-only campaign review corrections in `tests/integration/MediBridge.IntegrationTests/Phase3CampaignReviewHistoryTests.cs`.
- [X] T081 [P] [US4] Add failing integration test for append-only doctor price and platform fee policy corrections in `tests/integration/MediBridge.IntegrationTests/Phase3PolicyHistoryTests.cs`.
- [X] T082 [P] [US4] Add failing integration test for append-only activity score history corrections in `tests/integration/MediBridge.IntegrationTests/Phase3ActivityHistoryTests.cs`.
- [X] T083 [P] [US4] Add failing integration test for audit event safe metadata and correction links in `tests/integration/MediBridge.IntegrationTests/Phase3AuditEventTests.cs`.
- [X] T084 [P] [US4] Add failing integration test proving soft-deleted records remain historically linked but are excluded from active-record repository queries in `tests/integration/MediBridge.IntegrationTests/Phase3SoftDeleteQueryTests.cs`.

### Implementation for User Story 4

- [X] T085 [US4] Implement append-only correction creation for campaign review history in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`.
- [X] T086 [US4] Implement append-only correction creation for doctor price, platform fee, and activity score history in `MediBridge.Repository/Repositories/Policies/PolicyHistoryRepository.cs`.
- [X] T087 [US4] Implement append-only audit event creation with safe metadata guardrails in `MediBridge.Repository/Repositories/Policies/AuditEventRepository.cs`.
- [X] T088 [US4] Add safe metadata validation helper that rejects passwords, plaintext tokens, request bodies, and response bodies in `MediBridge.Core/Entities/Policies/AuditMetadataRules.cs`.
- [X] T089 [US4] Implement soft-delete active query filters or repository-level active query predicates for Doctor, Company, Campaign, and Wallet records in `MediBridge.Repository/Configurations/Identity/ProfileConfigurations.cs`, `MediBridge.Repository/Configurations/Campaigns/CampaignConfigurations.cs`, and `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T090 [US4] Add explicit historical query methods that can include soft-deleted records where needed in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`, `MediBridge.Repository/Repositories/Wallets/WalletRepository.cs`, and `MediBridge.Repository/Repositories/Policies/AuditEventRepository.cs`.
- [X] T091 [US4] Verify US4 by running `dotnet test` for `tests/integration/MediBridge.IntegrationTests/Phase3CampaignReviewHistoryTests.cs`, `Phase3PolicyHistoryTests.cs`, `Phase3ActivityHistoryTests.cs`, `Phase3AuditEventTests.cs`, and `Phase3SoftDeleteQueryTests.cs`.

**Checkpoint**: User Story 4 is complete when append-only history, linked corrections, safe metadata, and soft-delete query behavior pass automated validation.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation cleanup, and constitution compliance across all Phase 3 work.

- [X] T092 [P] Update `specs/003-database-core-models/quickstart.md` with the final migration name, exact validation commands, and any required SQL Server local setup notes discovered during implementation.
- [X] T093 [P] Update `specs/003-database-core-models/data-model.md` if implementation chooses different property names while preserving the same business meaning.
- [X] T094 [P] Add missing XML or inline code comments only for non-obvious transaction, idempotency, or soft-delete behavior in `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`, `MediBridge.Repository/Repositories/Wallets/WalletTransactionRepository.cs`, and `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T095 Run `dotnet format` on `MediBridge.slnx` and review only Phase 3 files for intended formatting changes.
- [X] T096 Run `dotnet build .\MediBridge.slnx` from repository root and fix any compile errors in Phase 3 files.
- [X] T097 Run `dotnet test .\MediBridge.slnx` from repository root and fix any failing tests caused by Phase 3 changes.
- [X] T098 Run `dotnet ef database update --project .\MediBridge.Repository --startup-project .\MediBridge.APIs` against the configured local SQL Server database and confirm the Phase 3 migration applies cleanly.
- [X] T099 Run a final constitution scan proving Core has no EF/HTTP references, controllers have no persistence dependencies, and Services do not use EF Core infrastructure in `tests/integration/MediBridge.IntegrationTests/Phase3LayeringBoundaryTests.cs`.
- [X] T100 Review scope guard manually and remove any accidental Phase 3 public/diagnostic controller or Phase 4+ or Phase 5+ workflow endpoints from `MediBridge.APIs/Controllers`, `MediBridge.Services/Services`, and `MediBridge.Repository/Repositories`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies. Complete first so folders and test helper files exist.
- **Phase 2 Foundational**: Depends on Phase 1. Blocks every user story because all story work needs shared enums, money rules, soft-delete/concurrency markers, and repository/UoW contracts.
- **Phase 3 US1**: Depends on Phase 2. This is the MVP because it creates authoritative records, EF mappings, repositories, UoW, and the migration.
- **Phase 4 US2**: Depends on Phase 2 and practically depends on US1 entity/mapping files. It can start after US1 entity skeletons exist, but final validation requires US1 migration/model work.
- **Phase 5 US3**: Depends on Phase 2. It can run in parallel with US1/US2 after contracts exist, but final assertions should run after repository/service/API changes settle.
- **Phase 6 US4**: Depends on Phase 2 and the US1 history entities/mappings. It can start once history entity skeletons exist.
- **Phase 7 Polish**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 Persist Core Marketplace Records**: No dependency on other user stories after Foundation. Required for a complete Phase 3 MVP.
- **US2 Protect Queue, Delivery, and Wallet Integrity**: Depends on US1 entity and mapping skeletons, but its tests and repository methods are independently verifiable.
- **US3 Enforce Application Data Boundaries**: Independent after Foundation; validates all changed code.
- **US4 Capture Policy and Audit History**: Depends on US1 history entity skeletons, but append-only behavior is independently testable.

### Within Each User Story

- Write the listed tests first and confirm they fail for the expected reason.
- Add or update Core entities/enums/contracts before Repository mappings.
- Add Repository mappings before repositories.
- Add repository implementations before DI registration.
- Add or update migrations after mappings are complete.
- Run story-specific tests before moving to the next story.

---

## Parallel Opportunities

- T005 and T006 can run in parallel after T001-T004.
- T012 through T019 can run in parallel after T007-T011 because they create separate interface files.
- T022 through T024 can run in parallel because they create separate test files.
- T025 through T037 can run in parallel after foundational enums exist because they create or edit separate entity files, except T025 and T026 both depend on understanding existing Phase 2 profile fields.
- T039 through T044 can run in parallel after entity files exist because they edit separate configuration files.
- T053 through T057 can run in parallel because they create separate test files.
- T070 through T072 can run in parallel because they create separate architecture test files.
- T080 through T084 can run in parallel because they create separate history/soft-delete test files.
- T092 through T094 can run in parallel during polish because they touch different documentation/code files.

## Parallel Example: User Story 1

```text
Task: "T022 Add failing integration test proving Phase 3 entity DbSets and EF mappings can create/read a complete sample graph in tests/integration/MediBridge.IntegrationTests/Phase3CoreEntityPersistenceTests.cs"
Task: "T023 Add failing integration test proving the Phase 3 migration applies cleanly to SQL Server and preserves Phase 2 Identity tables, Doctor/Company/Admin roles, account approval state, and RefreshCredential replacement tracing in tests/integration/MediBridge.IntegrationTests/Phase3MigrationSchemaTests.cs"
Task: "T024 Add failing integration test proving stored file metadata persists without implementing upload or retrieval behavior in tests/integration/MediBridge.IntegrationTests/Phase3StoredFileMetadataTests.cs"
```

```text
Task: "T027 Create Campaign entity with company ownership, content metadata, status, timestamps, and soft-delete fields in MediBridge.Core/Entities/Campaigns/Campaign.cs"
Task: "T030 Create DoctorMessageQueue entity with doctor, campaign, queued time, status, and timestamps in MediBridge.Core/Entities/Messaging/DoctorMessageQueue.cs"
Task: "T032 Create Wallet entity with Doctor/Company/Platform owner type, OwnerUserId, owner id, available/reserved balances, EGP currency, timestamps, soft-delete fields, and concurrency token in MediBridge.Core/Entities/Wallets/Wallet.cs"
Task: "T035 Create StoredFile entity with owner, purpose, original filename, content type, size, storage key, visibility, review status, and review metadata in MediBridge.Core/Entities/Files/StoredFile.cs"
```

## Parallel Example: User Story 2

```text
Task: "T053 Add failing integration test for per-doctor queue ordering by QueuedAtUtc ASC, Id ASC, including same-timestamp tie-breaker and next-day carry-over ordering, in tests/integration/MediBridge.IntegrationTests/Phase3QueueOrderingTests.cs"
Task: "T054 Add failing integration test for unique (DoctorId, DeliveryDateEgypt, CampaignId) delivery constraint in tests/integration/MediBridge.IntegrationTests/Phase3DeliveryConstraintTests.cs"
Task: "T055 Add failing integration test for unique (OperationType, IdempotencyKey) wallet transaction constraint proving duplicate top-up, charge, earn, refund, and withdrawal payout retries do not double-apply financial effects in tests/integration/MediBridge.IntegrationTests/Phase3WalletIdempotencyTests.cs"
Task: "T056 Add failing unit tests for MoneyRules rejecting more than two decimal places and negative values when disallowed in tests/unit/MediBridge.UnitTests/Phase3MoneyPrecisionTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T080 Add failing integration test for append-only campaign review corrections in tests/integration/MediBridge.IntegrationTests/Phase3CampaignReviewHistoryTests.cs"
Task: "T081 Add failing integration test for append-only doctor price and platform fee policy corrections in tests/integration/MediBridge.IntegrationTests/Phase3PolicyHistoryTests.cs"
Task: "T082 Add failing integration test for append-only activity score history corrections in tests/integration/MediBridge.IntegrationTests/Phase3ActivityHistoryTests.cs"
Task: "T083 Add failing integration test for audit event safe metadata and correction links in tests/integration/MediBridge.IntegrationTests/Phase3AuditEventTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 Setup.
2. Complete Phase 2 Foundational tasks.
3. Complete Phase 3 User Story 1.
4. Stop and validate: run `dotnet build .\MediBridge.slnx`, run US1 tests, generate/apply the migration, and confirm core records persist through repositories and unit of work.
5. Do not expose new public API endpoints for MVP validation.

### Incremental Delivery

1. US1 creates the authoritative data model and persistence baseline.
2. US2 adds integrity rules for queue, delivery, wallet, money, idempotency, and atomicity.
3. US3 proves architecture boundaries and prevents accidental persistence leaks.
4. US4 adds append-only history/correction behavior and soft-delete query behavior.
5. Polish runs full build, full test suite, migration application, and scope guard.

### Guidance For A Smaller Executor Model

1. Do tasks in numeric order unless a task is explicitly marked `[P]` and assigned to another worker.
2. Never move EF Core attributes, `DbContext`, migrations, or SQL Server-specific code into `MediBridge.Core`.
3. Never inject repositories, `IDomainUnitOfWork`, or `MediBridgeDbContext` into API controllers.
4. Do not implement public campaign, file upload, wallet, withdrawal, delivery, interaction, reporting, validation, or diagnostic endpoints in Phase 3.
5. If a task seems to require a future workflow, stop and satisfy only the persistence/model/test portion described in that task.
6. Keep migrations focused on Phase 3 tables, columns, indexes, constraints, and relationships.
7. After every group of related tasks, run `dotnet build .\MediBridge.slnx` before continuing.

## Notes

- `[P]` tasks use different files and can be parallelized after their prerequisites exist.
- Story labels map directly to the user stories in [spec.md](./spec.md).
- Tests are intentionally included because Phase 3 success criteria require measurable automated validation.
- Existing connection string edits in `MediBridge.APIs/appsettings.json` and `MediBridge.APIs/appsettings.Development.json` are not part of this task list but may be needed for local SQL Server validation.
- Commit after each completed phase or logical set of passing tests.
