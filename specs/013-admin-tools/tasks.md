# Tasks: Phase 11 Admin Tools

**Input**: Design documents from `/specs/013-admin-tools/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/admin-tools-api.yaml](./contracts/admin-tools-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Tests are required. The spec defines authorization, privacy, wallet atomicity, audit, pagination, concurrency, and performance outcomes that must be proven with unit, contract, integration, and performance-profile tests.

**Constitution Note**: Every implementation task must preserve Onion Architecture. Put domain records, enums, read models, and repository interfaces in `MediBridge.Core`; SQL Server/EF Core persistence and migrations in `MediBridge.Repository`; orchestration, validation, DTO mapping, and use-case logic in `MediBridge.Services`; and HTTP-only controllers/envelope behavior in `MediBridge.APIs`. Do not reference EF Core from `MediBridge.Core`, `MediBridge.Services`, or API controllers.

**Execution Rule for Smaller/Cheaper Model**: Execute tasks strictly in numeric order unless a task is explicitly marked `[P]` and every dependency listed in its phase is already complete. Do not merge tasks, skip tests, rename planned files without updating this task list, or introduce functionality outside Phase 11. If a task says "create if absent," first inspect the path and extend the existing file if it already exists.

**Critical Non-Negotiables For Executors**:

- Admin tools require Admin JWT authorization except doctor-owned withdrawal submission/listing.
- Doctor withdrawal submission requires the authenticated approved, active, non-suspended Doctor owner.
- Phase 11 must not collect, store, display, validate, log, audit, or return payout destination data such as bank account, card, mobile wallet number, payout-destination text, or saved payout method.
- Payout stub stores payout status, payout reference, reason/note, actor, and timestamps only.
- Withdrawal request creates a deterministic hold; approval preserves the hold; rejection releases it; paid finalizes it; eligible failure releases it.
- Withdrawal transitions must be idempotent under retry and safe under concurrency. Each final wallet effect happens at most once.
- Doctor pricing deactivation is a separate action/state. Do not encode inactive pricing as null, zero, or another numeric price.
- Price and platform fee changes affect future activation snapshots only. Do not recalculate existing delivery snapshots, settlements, reports, or ledgers.
- Admin statistics are read-only. If financial evidence is inconsistent, withhold affected financial totals and return safe flags plus unrelated non-financial totals.
- Admin work queue is an operational read model only. Do not alter delivery queue ordering, daily limits, expiry, retry, carry-over, or campaign delivery activation rules.
- All success and error responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- Never expose raw stack traces, raw storage keys, provider credentials, raw idempotency material, wallet internals beyond authorized summaries, payout destination data, or private doctor contact details beyond authorized review need.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently after the foundational phase.

## Phase 1: Setup (Shared Preparatory Inspection)

**Purpose**: Confirm contracts, existing code shape, and test locations before implementation starts. Do not implement production behavior in this phase.

- [X] T001 Verify the Phase 11 OpenAPI contract parses as OpenAPI 3.0.3 and keep it as the route/schema checklist in `specs/013-admin-tools/contracts/admin-tools-api.yaml`.
- [X] T002 [P] Record any route-name deviations found while comparing existing admin controllers to the Phase 11 contract in `specs/013-admin-tools/quickstart.md`.
- [X] T003 [P] Inspect existing admin account/file/campaign/pricing/platform-fee/enforcement controller methods and record reuse decisions in `specs/013-admin-tools/quickstart.md`.
- [X] T004 [P] Inspect existing wallet transaction, wallet ledger, and withdrawal request entities and record required persistence gaps in `specs/013-admin-tools/quickstart.md`.
- [X] T005 [P] Inspect existing audit metadata safety rules and record admin decision/payout audit conventions in `specs/013-admin-tools/quickstart.md`.
- [X] T006 [P] Inspect existing contract test route helper patterns and record Phase 11 test class naming conventions in `specs/013-admin-tools/quickstart.md`.
- [X] T007 [P] Inspect existing performance test structure and record Phase 11 performance profile assumptions in `specs/013-admin-tools/quickstart.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts, DTOs, validators, repositories, persistence fields, and DI registration that all user stories depend on.

**Critical**: No user story implementation should begin until every task in this phase is complete.

### Foundational Tests

- [X] T008 [P] Add unit tests for admin pagination validation with `PageNumber >= 1`, `1 <= PageSize <= 100`, default page number 1, and default page size 20 in `tests/unit/MediBridge.UnitTests/Admin/AdminToolsPaginationValidatorTests.cs`.
- [X] T009 [P] Add unit tests for admin date range validation with malformed dates, `from > to`, valid 90-day inclusive range, rejected 91-day range, and omitted-date defaulting to latest 90 Egypt business days in `tests/unit/MediBridge.UnitTests/Admin/AdminStatisticsDateRangeValidatorTests.cs`.
- [X] T010 [P] Add unit tests proving Phase 11 DTO privacy mappers exclude payout destination fields, raw idempotency material, raw storage keys, provider credentials, private contact details beyond review need, and stack traces in `tests/unit/MediBridge.UnitTests/Admin/AdminToolsPrivacyMapperTests.cs`.
- [X] T011 [P] Add unit tests for withdrawal state transition rules `Requested -> Approved -> Paid`, `Requested -> Approved -> Failed`, `Requested -> Rejected`, and rejection of every incompatible transition in `tests/unit/MediBridge.UnitTests/Wallets/WithdrawalStateTransitionTests.cs`.
- [X] T012 [P] Add unit tests for withdrawal money rules: positive amount, two-or-fewer decimals, EGP only, insufficient withdrawable earnings, pending hold exclusion, and paid withdrawal exclusion in `tests/unit/MediBridge.UnitTests/Wallets/WithdrawalMoneyRulesTests.cs`.
- [X] T013 [P] Add unit tests for doctor pricing deactivation rules proving inactive pricing is not represented by null, zero, or any numeric price marker in `tests/unit/MediBridge.UnitTests/Admin/AdminPricingDeactivationTests.cs`.

### Foundational Implementation

- [X] T014 Add admin query value objects for pagination, work queue filters, withdrawal filters, and statistics date range in `MediBridge.Core/Interfaces/Admin/AdminToolQueries.cs`.
- [X] T015 Add admin work queue, withdrawal, payout, doctor pricing, and statistics read models in `MediBridge.Core/Interfaces/Admin/AdminToolReadModels.cs`.
- [X] T016 Add or extend withdrawal operation/read models for hold, release, finalization, and withdrawable balance calculation in `MediBridge.Core/Interfaces/Wallets/WithdrawalReadModels.cs`.
- [X] T017 Add `IAdminWorkQueueRepository` with methods for category counts and paged queue projection in `MediBridge.Core/Interfaces/Admin/IAdminWorkQueueRepository.cs`.
- [X] T018 Add `IAdminStatisticsRepository` with methods for account/review/campaign/delivery/interaction/withdrawal/policy/enforcement source counts in `MediBridge.Core/Interfaces/Admin/IAdminStatisticsRepository.cs`.
- [X] T019 Add `IWithdrawalRequestRepository` with add, find-for-update, list doctor-owned, list admin, count, status-filter, and concurrency-aware lookup methods in `MediBridge.Core/Interfaces/Wallets/IWithdrawalRequestRepository.cs`.
- [X] T020 Extend `IWalletRepository` with service-needed methods for withdrawable balance locking and available/hold balance staging if existing methods are insufficient in `MediBridge.Core/Interfaces/Wallets/IWalletRepository.cs`.
- [X] T021 Extend `IWalletTransactionRepository` with withdrawal hold/release/finalize idempotency and list-by-withdrawal methods in `MediBridge.Core/Interfaces/Wallets/IWalletTransactionRepository.cs`.
- [X] T022 Extend `IWalletLedgerEntryRepository` with typed withdrawal ledger evidence query methods if existing `ListLedgerEntryIdsByReferencesAsync` is insufficient in `MediBridge.Core/Interfaces/Wallets/IWalletLedgerEntryRepository.cs`.
- [X] T023 Extend `IPolicyHistoryRepository` with pricing deactivation history query/write methods if current nullable price history cannot represent active/inactive state safely in `MediBridge.Core/Interfaces/Policies/IPolicyHistoryRepository.cs`.
- [X] T024 Add or extend withdrawal statuses, wallet transaction operation types, and audit target types required for `Hold`, `Release`, and `FinalizePayout` evidence in `MediBridge.Core/Enums/Phase3DomainEnums.cs`.
- [X] T025 Extend `WithdrawalRequest` with payout status actor/time, failure reason, and any missing concurrency-safe fields without adding payout destination fields in `MediBridge.Core/Entities/Wallets/WithdrawalRequest.cs`.
- [X] T026 Extend policy history records with pricing active/inactive state if needed for separate deactivation evidence in `MediBridge.Core/Entities/Policies/PolicyHistoryRecords.cs`.
- [X] T027 Add Phase 11 service DTOs for admin work queue, withdrawal, admin withdrawal decision requests, payout requests, pricing deactivation, and statistics in `MediBridge.Services/DTOs/Admin/AdminToolsDtos.cs`.
- [X] T028 Add doctor withdrawal request/list DTOs in `MediBridge.Services/DTOs/Wallets/DoctorWithdrawalDtos.cs`.
- [X] T029 Add DTO mappers from Core admin/withdrawal read models to service DTOs in `MediBridge.Services/DTOs/Admin/AdminToolsDtoMapper.cs`.
- [X] T030 Add `AdminToolsPaginationValidator` for shared page validation in `MediBridge.Services/Validators/Admin/AdminToolsPaginationValidator.cs`.
- [X] T031 Add `AdminStatisticsDateRangeValidator` using the Egypt business clock and 90-day inclusive maximum in `MediBridge.Services/Validators/Admin/AdminStatisticsDateRangeValidator.cs`.
- [X] T032 Add `CreateWithdrawalRequestValidator` for positive amount, two decimal places, and no destination fields in `MediBridge.Services/Validators/Wallets/CreateWithdrawalRequestValidator.cs`.
- [X] T033 Add `AdminWithdrawalDecisionRequestValidator`, `MarkWithdrawalPaidRequestValidator`, and `MarkWithdrawalFailedRequestValidator` in `MediBridge.Services/Validators/Admin/AdminWithdrawalRequestValidators.cs`.
- [X] T034 Add `DeactivateDoctorPricingRequestValidator` requiring non-empty reason and max length 1000 in `MediBridge.Services/Validators/Pricing/DeactivateDoctorPricingRequestValidator.cs`.
- [X] T035 Add service interfaces `IAdminWorkQueueService`, `IAdminStatisticsService`, and `IWithdrawalService` in `MediBridge.Services/Interfaces/IAdminToolServices.cs`.
- [X] T036 Add repository implementation `AdminWorkQueueRepository` for the aggregate operational read model in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T037 Add repository implementation `AdminStatisticsRepository` for 90-day source evidence counts and financial reconciliation inputs in `MediBridge.Repository/Repositories/Admin/AdminStatisticsRepository.cs`.
- [X] T038 Add repository implementation `WithdrawalRequestRepository` for withdrawal request persistence and concurrency-aware lookup in `MediBridge.Repository/Repositories/Wallets/WithdrawalRequestRepository.cs`.
- [X] T039 Extend `WalletRepository` for withdrawal hold balance operations only if T020 added methods in `MediBridge.Repository/Repositories/Wallets/WalletRepository.cs`.
- [X] T040 Extend `WalletTransactionRepository` for withdrawal hold/release/finalize transaction evidence in `MediBridge.Repository/Repositories/Wallets/WalletTransactionRepository.cs`.
- [X] T041 Extend `WalletLedgerEntryRepository` for withdrawal hold/release/finalize ledger evidence in `MediBridge.Repository/Repositories/Wallets/WalletLedgerEntryRepository.cs`.
- [X] T042 Extend `PolicyHistoryRepository` for separate pricing deactivation history if T023 added methods in `MediBridge.Repository/Repositories/Policies/PolicyHistoryRepository.cs`.
- [X] T043 Add `IWithdrawalRequestRepository` to `IDomainUnitOfWork` in `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`.
- [X] T044 Inject and expose `IWithdrawalRequestRepository` in `DomainUnitOfWork` in `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`.
- [X] T045 Register admin work queue, admin statistics, and withdrawal repositories in `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`.
- [X] T046 Register admin work queue, admin statistics, withdrawal services, and Phase 11 validators in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T047 Add EF Core configuration for withdrawal payout actor/time, failure reason, indexes, and no payout destination columns in `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T048 Add EF Core configuration for pricing active/inactive history fields and indexes if T026 changed policy history in `MediBridge.Repository/Configurations/Policies/PolicyAndAuditConfigurations.cs`.
- [X] T049 Add EF Core migration for Phase 11 withdrawal/payout fields, pricing deactivation fields, indexes, and repository support only in `MediBridge.Repository/Migrations/`.
- [X] T050 Verify the Phase 11 migration does not rewrite existing delivery snapshots, interaction settlement evidence, company reporting rows, or enforcement history in `MediBridge.Repository/Migrations/`.
- [X] T051 Add controller route constants or helpers for Phase 11 contract tests in `tests/contract/MediBridge.ContractTests/AdminToolsRoutes.cs`.
- [X] T052 Run foundational unit tests for validators, privacy mapping, withdrawal transitions, withdrawal money rules, and pricing deactivation in `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj`.

**Checkpoint**: Foundation ready. Shared contracts, DTOs, validators, repositories, DI, migration, and foundational tests are in place.

---

## Phase 3: User Story 1 - Triage Admin Work Queue (Priority: P1) MVP

**Goal**: Admin can view one safe, paginated operational work queue combining pending accounts, protected files, campaigns, enforcement items, and withdrawals.

**Independent Test**: Sign in as Admin with pending accounts, files, campaigns, violations, and withdrawals; call `GET /api/admin/work-queue`; confirm category counts, safe summaries, next actions, stable ordering by urgency/submitted time/id, and no sensitive payloads.

### Tests for User Story 1

- [X] T053 [P] [US1] Add contract tests for `GET /api/admin/work-queue` success envelope, `Page` metadata, `CategoryCounts`, `Items`, `NextActions`, `SensitiveFlags`, and schema from `contracts/admin-tools-api.yaml` in `tests/contract/MediBridge.ContractTests/AdminToolsWorkQueueContractTests.cs`.
- [X] T054 [P] [US1] Add contract tests for work queue `400`, `401`, and `403` envelopes and page bounds in `tests/contract/MediBridge.ContractTests/AdminToolsWorkQueueContractTests.cs`.
- [X] T055 [P] [US1] Add integration tests seeding pending accounts, file reviews, campaign reviews, enforcement items, and withdrawal requests and asserting all categories appear in `tests/integration/MediBridge.IntegrationTests/AdminToolsWorkQueueIntegrationTests.cs`.
- [X] T056 [P] [US1] Add integration tests proving work queue ordering by `UrgencyRank ASC`, `SubmittedAtUtc ASC`, then `ItemId ASC` and full pagination traversal without duplicates or gaps in `tests/integration/MediBridge.IntegrationTests/AdminToolsWorkQueueIntegrationTests.cs`.
- [X] T057 [P] [US1] Add integration tests proving work queue responses exclude raw storage keys, provider credentials, raw idempotency material, payout destination data, private contact details beyond review need, wallet internals, and stack traces in `tests/integration/MediBridge.IntegrationTests/AdminToolsPrivacyIntegrationTests.cs`.
- [X] T058 [P] [US1] Add integration tests proving work queue reads are non-mutating for accounts, files, campaigns, deliveries, wallets, transactions, ledgers, pricing policy, payout status, enforcement state, and audit evidence in `tests/integration/MediBridge.IntegrationTests/AdminToolsReadOnlyIntegrationTests.cs`.

### Implementation for User Story 1

- [X] T059 [US1] Implement `AdminWorkQueueService` orchestration, category filtering, pagination validation, and DTO mapping in `MediBridge.Services/Services/AdminWorkQueueService.cs`.
- [X] T060 [US1] Implement account pending work item projection in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T061 [US1] Implement protected file pending review work item projection in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T062 [US1] Implement campaign pending review work item projection in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T063 [US1] Implement enforcement review work item projection in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T064 [US1] Implement withdrawal review/payout work item projection in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T065 [US1] Implement category counts using the same source predicates as the paged query in `AdminWorkQueueRepository` in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T066 [US1] Add `AdminWorkQueueController` with `GET /api/admin/work-queue`, Admin-only authorization, HTTP-only query binding, and standard envelope response in `MediBridge.APIs/Controllers/AdminWorkQueueController.cs`.
- [X] T067 [US1] Ensure `AdminWorkQueueController` delegates all filtering, ordering, count, and privacy shaping to `IAdminWorkQueueService` in `MediBridge.APIs/Controllers/AdminWorkQueueController.cs`.
- [X] T068 [US1] Ensure work queue failures use global safe exception handling and never return raw stack traces or source-specific internals in `MediBridge.Services/Services/AdminWorkQueueService.cs`.
- [X] T069 [US1] Add XML comments or Swagger metadata for `GET /api/admin/work-queue` in `MediBridge.APIs/Controllers/AdminWorkQueueController.cs`.
- [X] T070 [US1] Run and pass US1 contract and integration tests for work queue in `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj` and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`.

**Checkpoint**: US1 is independently demoable as the MVP admin triage surface.

---

## Phase 4: User Story 2 - Manage Approval and Moderation Decisions (Priority: P1)

**Goal**: Existing Admin account, protected file, and campaign moderation decisions remain compatible, are audit-safe, are concurrency-safe, and appear consistently in the Phase 11 work queue.

**Independent Test**: Prepare pending accounts, files, and campaigns; apply approve/reject/correction decisions as Admin; verify state transitions, public/internal reason separation, concurrency behavior, audit records, standard envelopes, and updated work queue visibility.

### Tests for User Story 2

- [X] T071 [P] [US2] Add or update contract tests for account approval/rejection/correction envelopes, required reasons, and public/internal reason separation in `tests/contract/MediBridge.ContractTests/AdminAccountDecisionContractTests.cs`.
- [X] T072 [P] [US2] Add or update contract tests for protected file review decision envelopes, required reasons, and no raw storage fields in `tests/contract/MediBridge.ContractTests/FileWorkflowContractTests.cs`.
- [X] T073 [P] [US2] Add or update contract tests for campaign moderation approval/rejection/revision envelopes, required reasons, and no internal notes in company-visible responses in `tests/contract/MediBridge.ContractTests/CampaignReviewModerationContractTests.cs`.
- [X] T074 [P] [US2] Add integration tests for account decision concurrency where two admins attempt conflicting decisions and only one succeeds in `tests/integration/MediBridge.IntegrationTests/AdminDecisionTransitionIntegrationTests.cs`.
- [X] T075 [P] [US2] Add integration tests for file review concurrency and append-only history in `tests/integration/MediBridge.IntegrationTests/Phase4FileWorkflowIntegrationTests.cs`.
- [X] T076 [P] [US2] Add integration tests for campaign review retry/conflict behavior proving no duplicate queue rows, wallet charges, delivery records, or settlement evidence in `tests/integration/MediBridge.IntegrationTests/AdminCampaignReviewWorkflowTests.cs`.
- [X] T077 [P] [US2] Add integration tests proving account/file/campaign decisions update or remove corresponding work queue items in `tests/integration/MediBridge.IntegrationTests/AdminToolsWorkQueueIntegrationTests.cs`.
- [X] T078 [P] [US2] Add integration tests proving audit evidence for account/file/campaign decisions includes actor, target, prior state, resulting state, reason, time, and safe correlation evidence in `tests/integration/MediBridge.IntegrationTests/AdminToolsAuditIntegrationTests.cs`.

### Implementation for User Story 2

- [X] T079 [US2] Harden account decision audit fields and public/internal reason separation in `MediBridge.Services/Services/AdminAccountService.cs`.
- [X] T080 [US2] Harden account decision stale-state and concurrency conflict handling in `MediBridge.Services/Services/AdminAccountService.cs`.
- [X] T081 [US2] Harden file review decision audit fields, required public reason for non-approval, and raw storage privacy in `MediBridge.Services/Services/FileWorkflowService.cs`.
- [X] T082 [US2] Harden file review stale-state and concurrency conflict handling in `MediBridge.Services/Services/FileWorkflowService.cs`.
- [X] T083 [US2] Harden campaign review audit fields and public/internal reason separation in `MediBridge.Services/Services/AdminCampaignReviewService.cs`.
- [X] T084 [US2] Harden campaign review retry/concurrency behavior so repeated decisions do not duplicate queue rows, wallet charges, delivery records, or settlement evidence in `MediBridge.Services/Services/AdminCampaignReviewService.cs`.
- [X] T085 [US2] Ensure account decision controller responses use the standard envelope and do not catch/shape errors inconsistently with global middleware in `MediBridge.APIs/Controllers/AdminAccountsController.cs`.
- [X] T086 [US2] Ensure file review controller responses use the standard envelope and remain HTTP-only in `MediBridge.APIs/Controllers/AdminFilesController.cs`.
- [X] T087 [US2] Ensure campaign review controller responses use the standard envelope and remain HTTP-only in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`.
- [X] T088 [US2] Update work queue source predicates for account/file/campaign final states so completed decisions no longer appear as pending items in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T089 [US2] Run and pass US2 contract and integration tests for account/file/campaign decisions in `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj` and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`.

**Checkpoint**: US2 is independently functional and proves existing admin moderation paths remain safe and queue-visible.

---

## Phase 5: User Story 3 - Manage Doctor Pricing and Platform Fee Policy (Priority: P1)

**Goal**: Admin can set positive doctor prices, separately deactivate doctor pricing, update platform fee policy, and preserve future-only snapshot semantics with immutable history.

**Independent Test**: Change doctor price, deactivate pricing, reactivate with a new positive price, and update platform fee; activate new deliveries after changes; verify new snapshots use current policy while existing snapshots, settlements, reports, and ledgers remain unchanged.

### Tests for User Story 3

- [X] T090 [P] [US3] Add contract tests for `PUT /api/admin/doctors/{doctorId}/price/deactivate` success, validation, unauthorized, forbidden, not-found, and conflict envelopes in `tests/contract/MediBridge.ContractTests/AdminDoctorPricingContractTests.cs`.
- [X] T091 [P] [US3] Add contract tests proving set-price rejects null, zero, negative, unsupported precision, and inactive-marker values in `tests/contract/MediBridge.ContractTests/AdminDoctorPricingContractTests.cs`.
- [X] T092 [P] [US3] Add contract tests proving platform fee policy requires `0 < FeePercent <= 100`, reason, and standard envelopes in `tests/contract/MediBridge.ContractTests/AdminPlatformFeePolicyContractTests.cs`.
- [X] T093 [P] [US3] Add integration tests proving pricing deactivation makes doctor ineligible for future paid campaign activation until a new valid positive price is set in `tests/integration/MediBridge.IntegrationTests/AdminDoctorPricingDeactivationIntegrationTests.cs`.
- [X] T094 [P] [US3] Add integration tests proving pricing deactivation writes immutable history with previous price, no new numeric inactive price, pricing inactive state, admin actor, reason, and time in `tests/integration/MediBridge.IntegrationTests/AdminDoctorPricingDeactivationIntegrationTests.cs`.
- [X] T095 [P] [US3] Add integration tests proving price, pricing deactivation, and platform fee changes do not rewrite existing delivery snapshots, settlements, company reports, or ledger records in `tests/integration/MediBridge.IntegrationTests/AdminPricingSnapshotIsolationTests.cs`.
- [X] T096 [P] [US3] Add integration tests proving non-admin users cannot set price, deactivate pricing, or update platform fee policy in `tests/integration/MediBridge.IntegrationTests/AdminPricingAuthorizationIntegrationTests.cs`.

### Implementation for User Story 3

- [X] T097 [US3] Extend `SetDoctorPriceRequestDto` or related DTOs only if needed to keep set-price separate from deactivate-pricing in `MediBridge.Services/DTOs/Pricing/SetDoctorPriceRequestDto.cs`.
- [X] T098 [US3] Add `DeactivateDoctorPricingRequestDto` and response fields for `pricingIsActive` in `MediBridge.Services/DTOs/Pricing/DoctorPriceDto.cs`; if inactive responses expose `pricePerMessage` as null or omit it, document and test that this is response-display absence only and not the persisted inactive state marker.
- [X] T099 [US3] Add `DeactivateDoctorPricingAsync` to `IAdminPricingService` in `MediBridge.Services/Interfaces/IAdminPricingService.cs`.
- [X] T100 [US3] Implement `DeactivateDoctorPricingAsync` with Admin actor validation, required reason, separate inactive state, immutable history, and future-only eligibility in `MediBridge.Services/Services/AdminPricingService.cs`.
- [X] T101 [US3] Ensure `SetDoctorPriceAsync` sets only positive prices and reactivates pricing when current pricing is inactive in `MediBridge.Services/Services/AdminPricingService.cs`.
- [X] T102 [US3] Ensure delivery candidate eligibility excludes pricing-inactive doctors without treating null or zero price as the inactive marker in `MediBridge.Services/Services/DeliveryCandidateEligibilityPolicy.cs`.
- [X] T103 [US3] Ensure daily delivery activation uses the current positive price and platform fee snapshot only for future activations in `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`.
- [X] T104 [US3] Add `PUT /api/admin/doctors/{doctorId}/price/deactivate` action with Admin-only authorization, HTTP-only body binding, and standard envelope in `MediBridge.APIs/Controllers/AdminPricingController.cs`.
- [X] T105 [US3] Ensure platform fee policy update remains future-only and does not recalculate historical snapshots in `MediBridge.Services/Services/AdminPlatformFeePolicyService.cs`.
- [X] T106 [US3] Add or update XML comments/Swagger metadata for price deactivate and platform fee policy routes in `MediBridge.APIs/Controllers/AdminPricingController.cs` and `MediBridge.APIs/Controllers/AdminPlatformFeePolicyController.cs`.
- [X] T107 [US3] Run and pass US3 unit, contract, and integration tests for pricing and platform fee policy in `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj`, `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj`, and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`.

**Checkpoint**: US3 is independently functional and preserves historical financial snapshots.

---

## Phase 6: User Story 4 - Review Withdrawal Requests and Track Payouts (Priority: P1)

**Goal**: Approved, active, non-suspended doctors can create withdrawal requests from withdrawable settled earnings, and Admin can approve, reject, mark paid, or mark failed with deterministic wallet effects and audit evidence.

**Independent Test**: Give a doctor settled earnings; submit withdrawal; approve/reject/mark paid/mark failed as Admin; verify available and held balances, request statuses, ledger evidence, payout reference-only behavior, retries, concurrency, and audit records.

### Tests for User Story 4

- [X] T108 [P] [US4] Add contract tests for `POST /api/doctor/withdrawals` success `201`, validation `400`, unauthorized `401`, forbidden `403`, and conflict `409` envelopes in `tests/contract/MediBridge.ContractTests/DoctorWithdrawalContractTests.cs`.
- [X] T109 [P] [US4] Add contract tests for `GET /api/doctor/withdrawals` doctor-owned pagination and status filtering in `tests/contract/MediBridge.ContractTests/DoctorWithdrawalContractTests.cs`.
- [X] T110 [P] [US4] Add contract tests for `GET /api/admin/withdrawals` Admin-only pagination, filters, schema, and full pagination traversal proving every matching withdrawal appears exactly once with no duplicates or gaps across pages in `tests/contract/MediBridge.ContractTests/AdminWithdrawalContractTests.cs`.
- [X] T111 [P] [US4] Add contract tests for admin approve, reject, mark-paid, and mark-failed withdrawal routes and all standard error envelopes in `tests/contract/MediBridge.ContractTests/AdminWithdrawalContractTests.cs`.
- [X] T112 [P] [US4] Add contract tests proving withdrawal and payout request/response schemas contain no payout destination fields in `tests/contract/MediBridge.ContractTests/WithdrawalPrivacyContractTests.cs`.
- [X] T113 [P] [US4] Add integration tests proving approved, active, non-suspended doctor can submit a valid withdrawal and requested amount moves from available to held exactly once in `tests/integration/MediBridge.IntegrationTests/WithdrawalRequestIntegrationTests.cs`.
- [X] T114 [P] [US4] Add integration tests proving pending, rejected, inactive, suspended, non-owner, non-doctor, and unauthenticated callers cannot create doctor withdrawal requests in `tests/integration/MediBridge.IntegrationTests/WithdrawalAuthorizationIntegrationTests.cs`.
- [X] T115 [P] [US4] Add integration tests proving invalid amount, unsupported precision, and insufficient withdrawable earnings create no request, no hold, no transaction, and no ledger entry in `tests/integration/MediBridge.IntegrationTests/WithdrawalValidationIntegrationTests.cs`.
- [X] T116 [P] [US4] Add integration tests proving admin approval preserves the hold and records actor, note, prior state, resulting state, and time in `tests/integration/MediBridge.IntegrationTests/AdminWithdrawalDecisionIntegrationTests.cs`.
- [X] T117 [P] [US4] Add integration tests proving admin rejection releases the hold exactly once and requires a reason in `tests/integration/MediBridge.IntegrationTests/AdminWithdrawalDecisionIntegrationTests.cs`.
- [X] T118 [P] [US4] Add integration tests proving mark-paid finalizes the held amount exactly once, requires payout reference, and prevents later release/reject/fail/paid duplication in `tests/integration/MediBridge.IntegrationTests/AdminWithdrawalPayoutIntegrationTests.cs`.
- [X] T119 [P] [US4] Add integration tests proving mark-failed releases an approved hold exactly once before funds leave platform and requires reason in `tests/integration/MediBridge.IntegrationTests/AdminWithdrawalPayoutIntegrationTests.cs`.
- [X] T120 [P] [US4] Add integration tests for duplicate/retried/concurrent doctor submission and admin payout decisions proving at-most-once wallet effects in `tests/integration/MediBridge.IntegrationTests/WithdrawalConcurrencyIntegrationTests.cs`.
- [X] T121 [P] [US4] Add integration tests proving withdrawal list responses never expose payout destination data, raw idempotency material, wallet internals beyond authorized summaries, or raw stack traces in `tests/integration/MediBridge.IntegrationTests/WithdrawalPrivacyIntegrationTests.cs`.

### Implementation for User Story 4

- [X] T122 [US4] Implement withdrawal eligibility checks for approved account, active account, non-suspended status, doctor ownership, and sufficient withdrawable earnings in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T123 [US4] Implement withdrawable settled earnings calculation using settled doctor earnings minus pending holds, paid withdrawals, disputed/inconsistent evidence, and open request amounts in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T124 [US4] Implement `CreateWithdrawalAsync` in a single `IDomainUnitOfWork.ExecuteIsolatedInTransactionAsync` transaction with request creation, hold transaction, hold ledger entries, and audit evidence in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T125 [US4] Implement doctor-owned withdrawal list query and DTO mapping in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T126 [US4] Implement admin withdrawal list query, filters, and DTO mapping in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T127 [US4] Implement admin approve transition from Requested to Approved preserving the hold and recording audit evidence in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T128 [US4] Implement admin reject transition from Requested to Rejected releasing the hold exactly once and recording audit evidence in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T129 [US4] Implement mark-paid transition from Approved to Paid requiring payout reference and finalizing the hold exactly once in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T130 [US4] Implement mark-failed transition from Approved to Failed requiring reason and releasing the hold exactly once in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T131 [US4] Implement stale-state, rowversion, duplicate transaction, and incompatible transition conflict handling in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T132 [US4] Ensure raw idempotency keys, if introduced for withdrawal requests or payout actions, are hashed or otherwise safe and never stored/logged/audited/returned raw in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T133 [US4] Add `DoctorWithdrawalsController` with `POST /api/doctor/withdrawals` and `GET /api/doctor/withdrawals`, Doctor authorization, rate-limit policy if required, HTTP-only binding, and standard envelopes in `MediBridge.APIs/Controllers/DoctorWithdrawalsController.cs`.
- [X] T134 [US4] Add `AdminWithdrawalsController` with `GET /api/admin/withdrawals`, approve, reject, mark-paid, and mark-failed actions, Admin authorization, HTTP-only binding, and standard envelopes in `MediBridge.APIs/Controllers/AdminWithdrawalsController.cs`.
- [X] T135 [US4] Ensure `AdminWithdrawalsController` and `DoctorWithdrawalsController` delegate all wallet, status, audit, and validation decisions to `IWithdrawalService` in `MediBridge.APIs/Controllers/AdminWithdrawalsController.cs` and `MediBridge.APIs/Controllers/DoctorWithdrawalsController.cs`.
- [X] T136 [US4] Add XML comments/Swagger metadata for doctor and admin withdrawal routes in `MediBridge.APIs/Controllers/AdminWithdrawalsController.cs` and `MediBridge.APIs/Controllers/DoctorWithdrawalsController.cs`.
- [X] T137 [US4] Update work queue withdrawal predicates and next actions after each withdrawal transition in `MediBridge.Repository/Repositories/Admin/AdminWorkQueueRepository.cs`.
- [X] T138 [US4] Run and pass US4 unit, contract, and integration tests for withdrawals and payout stub in `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj`, `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj`, and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`.

**Checkpoint**: US4 is independently functional and wallet transitions are deterministic.

---

## Phase 7: User Story 5 - Review System Statistics and Enforcement Outcomes (Priority: P2)

**Goal**: Admin can request read-only system statistics and enforcement summaries over a bounded 90-day Egypt date range, with financial totals withheld when source evidence is inconsistent.

**Independent Test**: Seed accounts, files, campaigns, deliveries, interactions, withdrawals, wallet activity, pricing changes, and enforcement actions; call `GET /api/admin/statistics`; verify totals match committed source evidence, financial inconsistencies withhold only affected financial totals, and non-admin users are denied.

### Tests for User Story 5

- [X] T139 [P] [US5] Add contract tests for `GET /api/admin/statistics` success schema, standard envelope, date parameters, and `WithheldFinancialScopes` in `tests/contract/MediBridge.ContractTests/AdminStatisticsContractTests.cs`.
- [X] T140 [P] [US5] Add contract tests for statistics `400`, `401`, and `403` envelopes and date range validation in `tests/contract/MediBridge.ContractTests/AdminStatisticsContractTests.cs`.
- [X] T141 [P] [US5] Add integration tests for account, review, campaign, delivery, interaction, withdrawal, pricing, fee policy, and enforcement counts over a 90-day period in `tests/integration/MediBridge.IntegrationTests/AdminStatisticsIntegrationTests.cs`.
- [X] T142 [P] [US5] Add integration tests proving empty valid date ranges return zero totals and clear period boundaries in `tests/integration/MediBridge.IntegrationTests/AdminStatisticsIntegrationTests.cs`.
- [X] T143 [P] [US5] Add integration tests proving inconsistent financial evidence withholds affected financial totals, sets safe flags, and still returns unrelated non-financial totals in `tests/integration/MediBridge.IntegrationTests/AdminStatisticsFinancialConsistencyTests.cs`.
- [X] T144 [P] [US5] Add integration tests proving statistics reads mutate no accounts, files, campaigns, deliveries, wallets, transactions, ledgers, pricing policy, payout status, enforcement state, or audit evidence in `tests/integration/MediBridge.IntegrationTests/AdminToolsReadOnlyIntegrationTests.cs`.
- [X] T145 [P] [US5] Add integration tests proving non-admin users cannot access statistics and receive no platform metrics in `tests/integration/MediBridge.IntegrationTests/AdminStatisticsAuthorizationTests.cs`.
- [X] T146 [P] [US5] Add integration tests proving statistics responses exclude raw storage keys, provider credentials, raw idempotency material, payout destination data, private contact details beyond review need, wallet internals beyond authorized summaries, and raw stack traces in `tests/integration/MediBridge.IntegrationTests/AdminToolsPrivacyIntegrationTests.cs`.

### Implementation for User Story 5

- [X] T147 [US5] Implement `AdminStatisticsService` date validation, omitted-date defaulting, source query orchestration, financial consistency handling, and DTO mapping in `MediBridge.Services/Services/AdminStatisticsService.cs`.
- [X] T148 [US5] Implement account and review source count queries in `AdminStatisticsRepository` in `MediBridge.Repository/Repositories/Admin/AdminStatisticsRepository.cs`.
- [X] T149 [US5] Implement campaign, delivery, and interaction source count queries in `AdminStatisticsRepository` in `MediBridge.Repository/Repositories/Admin/AdminStatisticsRepository.cs`.
- [X] T150 [US5] Implement withdrawal status, payout status, and wallet movement source queries in `AdminStatisticsRepository` in `MediBridge.Repository/Repositories/Admin/AdminStatisticsRepository.cs`.
- [X] T151 [US5] Implement pricing policy, platform fee policy, and enforcement action count queries in `AdminStatisticsRepository` in `MediBridge.Repository/Repositories/Admin/AdminStatisticsRepository.cs`.
- [X] T152 [US5] Implement financial reconciliation checks for statistics using wallet transaction and ledger evidence in `MediBridge.Services/Services/AdminStatisticsService.cs`.
- [X] T153 [US5] Implement affected-financial-total withholding and `WithheldFinancialScopes` flags without failing unrelated non-financial totals in `MediBridge.Services/Services/AdminStatisticsService.cs`.
- [X] T154 [US5] Add `AdminStatisticsController` with `GET /api/admin/statistics`, Admin-only authorization, HTTP-only query binding, and standard envelope response in `MediBridge.APIs/Controllers/AdminStatisticsController.cs`.
- [X] T155 [US5] Ensure statistics controller delegates all date defaulting, calculations, reconciliation, and privacy shaping to `IAdminStatisticsService` in `MediBridge.APIs/Controllers/AdminStatisticsController.cs`.
- [X] T156 [US5] Add XML comments/Swagger metadata for `GET /api/admin/statistics` in `MediBridge.APIs/Controllers/AdminStatisticsController.cs`.
- [X] T157 [US5] Run and pass US5 contract and integration tests for statistics in `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj` and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`.

**Checkpoint**: US5 is independently functional and read-only operational statistics are safe.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Verify the feature end-to-end, harden privacy/performance, and prepare implementation evidence.

- [X] T158 [P] Add or update OpenAPI generation assertions for all Phase 11 routes in `tests/contract/MediBridge.ContractTests/SwaggerPhase11AdminToolsContractTests.cs`.
- [X] T159 [P] Add performance-profile seed helpers for accounts, files, campaigns, deliveries, interactions, wallet transactions, withdrawals, policy histories, and enforcement actions in `tests/performance/Phase11AdminToolsPerformanceSeed.cs`.
- [X] T160 [P] Add performance test for `GET /api/admin/work-queue` p95 under 2 seconds with stable pagination in `tests/performance/Phase11AdminToolsPerformanceTests.cs`.
- [X] T161 [P] Add performance test for `GET /api/admin/withdrawals` p95 under 2 seconds with filters and stable pagination in `tests/performance/Phase11AdminToolsPerformanceTests.cs`.
- [X] T162 [P] Add performance test for `GET /api/admin/statistics` p95 under 2 seconds with 90-day source data and bounded query count in `tests/performance/Phase11AdminToolsPerformanceTests.cs`.
- [X] T163 Review Phase 11 DTOs for prohibited fields and remove any payout destination, raw idempotency, raw storage, provider credential, private contact, stack trace, or unauthorized wallet-internal fields in `MediBridge.Services/DTOs/Admin/AdminToolsDtos.cs` and `MediBridge.Services/DTOs/Wallets/DoctorWithdrawalDtos.cs`.
- [X] T164 Review Phase 11 services to confirm controllers are HTTP-only and all business logic is in Services in `MediBridge.Services/Services/`.
- [X] T165 Review Phase 11 repository changes to confirm all SQL/EF Core access is confined to `MediBridge.Repository/`.
- [X] T166 Review Phase 11 wallet logic to confirm no company top-up, campaign reservation, expiry release, interaction charge, doctor earnings settlement, platform fee formula, or company reporting behavior changed in `MediBridge.Services/Services/WithdrawalService.cs`.
- [X] T167 Run `dotnet build .\MediBridge.slnx` and record build result in `specs/013-admin-tools/quickstart.md`.
- [X] T168 Run Phase 11 focused unit tests and record command/result in `specs/013-admin-tools/quickstart.md`.
- [X] T169 Run Phase 11 focused contract tests and record command/result in `specs/013-admin-tools/quickstart.md`.
- [X] T170 Run Phase 11 focused integration tests and record command/result in `specs/013-admin-tools/quickstart.md`.
- [X] T171 Run Phase 11 performance tests when prerequisites are available, or record explicit unmet prerequisites in `specs/013-admin-tools/quickstart.md`.
- [X] T172 Perform final constitution compliance review for layering, controller thinness, SQL persistence boundary, JWT/role security, response envelope, global exception handling, queue determinism, and wallet determinism in `specs/013-admin-tools/quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies. Complete before editing production code.
- **Phase 2 Foundational**: Depends on Phase 1. Blocks all user stories because it creates shared contracts, validators, repositories, DTOs, DI, and migration support.
- **Phase 3 US1 Work Queue**: Depends on Phase 2. MVP and first independently demoable admin surface.
- **Phase 4 US2 Approval/Moderation Hardening**: Depends on Phase 2. Can run after US1 starts if the team avoids editing the same work queue repository methods concurrently.
- **Phase 5 US3 Pricing/Fee Policy**: Depends on Phase 2. Can run after Phase 2 and is mostly independent of US1/US2.
- **Phase 6 US4 Withdrawals/Payouts**: Depends on Phase 2. It uses foundational wallet and withdrawal contracts.
- **Phase 7 US5 Statistics**: Depends on Phase 2. It benefits from US4 fields for withdrawal counts but can be built with seeded source data after foundational repositories exist.
- **Phase 8 Polish**: Depends on completed desired user stories.

### User Story Dependencies

- **US1 (P1, MVP)**: No dependency on other stories after Phase 2. It may show zero withdrawal items until US4 is complete, but its projection must support withdrawal records once present.
- **US2 (P1)**: No dependency on US1 behavior, but must keep work queue state consistent with US1.
- **US3 (P1)**: No dependency on US1/US2/US4/US5 after Phase 2.
- **US4 (P1)**: No dependency on US1 except work queue integration task T137.
- **US5 (P2)**: No dependency on UI or new external services. It reads committed source evidence and should not mutate anything.

### Within Each User Story

- Write tests first and confirm they fail for missing behavior before implementing.
- Core interfaces/read models before Repository implementations.
- Repository implementations before Services that call them.
- Service DTOs and validators before Controllers.
- Controllers remain HTTP-only and must delegate all decisions to Services.
- Run the story-specific checkpoint tests before moving to the next story if executing sequentially.

---

## Parallel Opportunities

- Setup tasks T002-T007 can run in parallel because they only record notes in different sections or inspect different code areas.
- Foundational test tasks T008-T013 can run in parallel because they create separate test files.
- Foundational repository/interface tasks can be split by domain only after T014-T024 decisions are complete: Admin read models, Wallet withdrawal contracts, and Policy pricing contracts.
- US1 tests T053-T058 can run in parallel before US1 implementation.
- US2 tests T071-T078 can run in parallel before US2 implementation.
- US3 tests T090-T096 can run in parallel before US3 implementation.
- US4 tests T108-T121 can run in parallel before US4 implementation.
- US5 tests T139-T146 can run in parallel before US5 implementation.
- Performance tasks T159-T162 can run in parallel after all relevant endpoints are implemented.

## Parallel Example: User Story 4

```text
Task: "T108 Add doctor withdrawal creation contract tests in tests/contract/MediBridge.ContractTests/DoctorWithdrawalContractTests.cs"
Task: "T110 Add admin withdrawal list contract tests in tests/contract/MediBridge.ContractTests/AdminWithdrawalContractTests.cs"
Task: "T113 Add valid withdrawal hold integration tests in tests/integration/MediBridge.IntegrationTests/WithdrawalRequestIntegrationTests.cs"
Task: "T120 Add concurrency integration tests in tests/integration/MediBridge.IntegrationTests/WithdrawalConcurrencyIntegrationTests.cs"
```

After those tests exist and fail for missing behavior, implement T122-T138 in order.

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational tasks.
3. Complete Phase 3 US1 work queue.
4. Stop and validate `GET /api/admin/work-queue` independently.
5. Demo the work queue before adding payout or statistics behavior.

### Incremental Delivery

1. US1 gives Admin one triage surface.
2. US2 hardens existing account/file/campaign decisions and keeps them queue-visible.
3. US3 adds commercial policy controls and pricing deactivation.
4. US4 adds the wallet-sensitive withdrawal/payout workflow.
5. US5 adds read-only operational statistics.
6. Phase 8 verifies performance, privacy, and constitutional compliance.

### Guardrails For Implementation

- Do not create a generic admin decision endpoint that replaces domain-specific workflows.
- Do not introduce payout provider integration, payout destination storage, or saved payout methods.
- Do not introduce stored statistics aggregate tables unless a later spec explicitly changes Phase 11 scope.
- Do not use raw SQL or EF Core outside `MediBridge.Repository`.
- Do not let API controllers calculate balances, statistics, queue urgency, or state transitions.
- Do not make work queue or statistics reads mutate audit records or repair source evidence.
- Do not mark tasks complete until tests and code changes for that task are committed or clearly recorded as complete in the working tree.

---

## Task Counts

- Setup: 7 tasks
- Foundational: 45 tasks
- US1 Work Queue: 18 tasks
- US2 Approval and Moderation: 19 tasks
- US3 Pricing and Fee Policy: 18 tasks
- US4 Withdrawals and Payouts: 31 tasks
- US5 Statistics: 19 tasks
- Polish: 15 tasks
- **Total**: 172 tasks
