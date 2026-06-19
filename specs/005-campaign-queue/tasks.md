# Tasks: Campaign & Queue (Phase 5)

**Input**: Design documents from `/specs/005-campaign-queue/`
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/campaign-queue-api.yaml`, `contracts/service-contracts.md`, `quickstart.md`

**Tests**: Required. The Phase 5 plan and quickstart require focused contract, integration, and unit coverage for company doctor search, campaign submission, queue creation, company wallet top-up/query, response envelopes, authorization, ownership, idempotency, money precision, audit safety, and layering boundaries.

**Constitution Note**: Tasks MUST enforce Onion layering, service-owned business logic, SQL Server persistence through EF Core in `MediBridge.Repository` behind Repository + Unit of Work abstractions, JWT/role security on secured routes, the standard API response envelope, and global exception middleware handling. Do not reference EF Core from `MediBridge.Core`, `MediBridge.Services`, or controllers.

**Executor Note**: Be literal. Implement only Phase 5. Do not add admin moderation screens, daily injector jobs, expiry jobs, doctor inbox/read/interact endpoints, settlement, reporting analytics, withdrawals, weekly enforcement, activity score jobs, or production payment gateway integration.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the empty Phase 5 files and folders so later tasks have stable targets.

- [X] T001 Create Phase 5 service DTO folders `MediBridge.Services\DTOs\Doctors`, `MediBridge.Services\DTOs\Campaigns`, and `MediBridge.Services\DTOs\Wallets`.
- [X] T002 Create Phase 5 service validator folders `MediBridge.Services\Validators\Doctors`, `MediBridge.Services\Validators\Campaigns`, and `MediBridge.Services\Validators\Wallets`.
- [X] T003 Create placeholder service interface files `MediBridge.Services\Interfaces\ICompanyDoctorSearchService.cs`, `MediBridge.Services\Interfaces\ICampaignWorkflowService.cs`, and `MediBridge.Services\Interfaces\ICompanyWalletService.cs`.
- [X] T004 Create placeholder service implementation files `MediBridge.Services\Services\CompanyDoctorSearchService.cs`, `MediBridge.Services\Services\CampaignWorkflowService.cs`, and `MediBridge.Services\Services\CompanyWalletService.cs`.
- [X] T005 Create placeholder API controller files `MediBridge.APIs\Controllers\CompanyDoctorsController.cs`, `MediBridge.APIs\Controllers\CompanyCampaignsController.cs`, and `MediBridge.APIs\Controllers\CompanyWalletController.cs`.
- [X] T006 Create Phase 5 test files `tests\contract\MediBridge.ContractTests\CompanyDoctorSearchContractTests.cs`, `tests\contract\MediBridge.ContractTests\CompanyCampaignContractTests.cs`, and `tests\contract\MediBridge.ContractTests\CompanyWalletContractTests.cs`.
- [X] T007 Create Phase 5 integration test files `tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs`, `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`, `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`, and `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T008 Create Phase 5 unit test files `tests\unit\MediBridge.UnitTests\Phase5CampaignValidationTests.cs`, `tests\unit\MediBridge.UnitTests\Phase5DoctorSearchOrderingTests.cs`, and `tests\unit\MediBridge.UnitTests\Phase5CompanyWalletValidationTests.cs`.
- [X] T009 Create Phase 5 shared integration helper file `tests\integration\MediBridge.IntegrationTests\Phase5CampaignQueueTestHelpers.cs` for seeding approved companies, approved doctors, campaign assets, wallets, and queue scenarios.
- [X] T010 Create Phase 5 shared contract helper file `tests\contract\MediBridge.ContractTests\Phase5ContractTestHelpers.cs` for JSON envelope assertions, token creation, request builders, and database seeding.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts, persistence support, validation primitives, DI, and authorization/rate-limit wiring required by all user stories.

**Critical**: No user story implementation should begin until this phase is complete.

- [X] T011 [P] Define `EligibleDoctorSearchRequestDto`, `EligibleDoctorDto`, and `EligibleDoctorPageDto` in `MediBridge.Services\DTOs\Doctors\DoctorSearchDtos.cs` with `PageNumber`, `PageSize`, specialization, experience, location, activity score, and price filter fields.
- [X] T012 [P] Define `CreateCampaignRequestDto`, `CampaignAssetDto`, `CampaignTargetSnapshotDto`, `CampaignSummaryDto`, `CampaignDetailDto`, `CampaignPageDto`, and `CampaignQueueCreationResultDto` in `MediBridge.Services\DTOs\Campaigns\CampaignDtos.cs`.
- [X] T013 [P] Define `CompanyWalletDto`, `CompanyWalletTransactionDto`, `CompanyWalletTransactionPageDto`, `TopUpCompanyWalletRequestDto`, and `TopUpCompanyWalletResultDto` in `MediBridge.Services\DTOs\Wallets\CompanyWalletDtos.cs`.
- [X] T014 [P] Define `PagedResultDto<T>` in `MediBridge.Services\DTOs\PagedResultDto.cs` with `Items`, `PageNumber`, `PageSize`, `TotalCount`, `TotalPages`, `HasPreviousPage`, and `HasNextPage`.
- [X] T015 [P] Define Phase 5 service exception types for validation, conflict, not found, and forbidden workflow outcomes in `MediBridge.Services\Interfaces\Phase5WorkflowExceptions.cs`.
- [X] T016 [P] Add `CampaignSubmissionRequest` entity in `MediBridge.Core\Entities\Campaigns\CampaignSubmissionRequest.cs` with `Id`, `CompanyId`, `IdempotencyKey`, `CampaignId`, `RequestHash`, `Status`, `CreatedAtUtc`, and `CompletedAtUtc`.
- [X] T017 [P] Add `CampaignSubmissionRequestStatus` enum values `Succeeded` and `FailedValidation` in `MediBridge.Core\Enums\Phase3DomainEnums.cs`.
- [X] T018 Add `DbSet<CampaignSubmissionRequest>` to `MediBridge.Repository\Data\MediBridgeDbContext.cs`.
- [X] T019 Create EF Core configuration for `CampaignSubmissionRequest` in `MediBridge.Repository\Configurations\Campaigns\CampaignSubmissionRequestConfiguration.cs`, including unique `(CompanyId, IdempotencyKey)` and required length limits for safe string fields.
- [X] T020 Update campaign EF Core configuration in `MediBridge.Repository\Configurations\Campaigns\CampaignConfigurations.cs` to enforce Phase 5 indexes for `(CompanyId, CreatedAtUtc)`, target uniqueness `(CampaignId, DoctorId)`, and any required campaign fields.
- [X] T021 Update messaging EF Core configuration in `MediBridge.Repository\Configurations\Messaging\MessagingConfigurations.cs` to enforce queue duplicate prevention for `(CampaignId, DoctorId)` and preserve `(DoctorId, Status, QueuedAtUtc, Id)` ordering.
- [X] T022 Update wallet EF Core configuration in `MediBridge.Repository\Configurations\Wallets\WalletConfigurations.cs` to confirm top-up transaction idempotency and ledger browsing indexes needed by Phase 5.
- [X] T023 Extend `IProfileRepository` in `MediBridge.Core\Interfaces\Identity\IProfileRepository.cs` with eligible doctor search and count methods that accept filters, skip/take pagination, and deterministic ordering.
- [X] T024 Extend `ICampaignRepository` in `MediBridge.Core\Interfaces\Campaigns\ICampaignRepository.cs` with methods for full campaign creation, target snapshot batch creation, company campaign list/detail, target count, ownership checks, idempotency lookup/write, approved status lookup, and target retrieval for queue creation.
- [X] T025 Extend `IMessageQueueRepository` in `MediBridge.Core\Interfaces\Messaging\IMessageQueueRepository.cs` with methods for queue duplicate detection, batch queue creation, and campaign/doctor queue lookup.
- [X] T026 Extend `IWalletRepository` in `MediBridge.Core\Interfaces\Wallets\IWalletRepository.cs` with methods for company wallet detail lookup, read-only available/reserved balance read for campaign sufficiency validation, and concurrency-safe top-up staging.
- [X] T027 Extend `IWalletTransactionRepository` in `MediBridge.Core\Interfaces\Wallets\IWalletTransactionRepository.cs` with methods for top-up transaction detail lookup and paginated wallet transaction query.
- [X] T028 Extend `IWalletLedgerEntryRepository` in `MediBridge.Core\Interfaces\Wallets\IWalletLedgerEntryRepository.cs` with methods for creating immutable top-up ledger entries and querying by wallet transaction.
- [X] T029 Extend `IAuditEventRepository` in `MediBridge.Core\Interfaces\Policies\IAuditEventRepository.cs` to accept Phase 5 event category, actor, target, outcome, reason, correlation id, and safe metadata.
- [X] T030 Implement eligible doctor search/count repository methods in `MediBridge.Repository\Repositories\Identity\IdentityRepositories.cs` using SQL-side filters and ordering by `ActivityScore DESC`, `PricePerMessage ASC`, `Id ASC`.
- [X] T031 Implement Phase 5 campaign and submission idempotency repository methods in `MediBridge.Repository\Repositories\Campaigns\CampaignRepository.cs`.
- [X] T032 Implement Phase 5 queue duplicate detection and batch queue creation repository methods in `MediBridge.Repository\Repositories\Messaging\MessageQueueRepository.cs`.
- [X] T033 Implement Phase 5 wallet detail, read-only available/reserved balance, and top-up staging repository methods in `MediBridge.Repository\Repositories\Wallets\WalletRepository.cs`.
- [X] T034 Implement Phase 5 top-up transaction lookup and paginated query methods in `MediBridge.Repository\Repositories\Wallets\WalletTransactionRepository.cs`.
- [X] T035 Implement Phase 5 ledger entry creation/query methods in `MediBridge.Repository\Repositories\Wallets\WalletLedgerEntryRepository.cs`.
- [X] T036 Implement Phase 5 audit event repository behavior in `MediBridge.Repository\Repositories\Policies\AuditEventRepository.cs`.
- [X] T037 Update `MediBridge.Repository\UnitOfWork\DomainUnitOfWork.cs` to expose any newly extended repositories and transaction helpers required by campaign submission, queue creation, and wallet top-up.
- [X] T038 Add EF Core migration for Phase 5 schema changes in `MediBridge.Repository\Migrations` and ensure it includes `CampaignSubmissionRequests`, required indexes, and no destructive changes to Phase 1-4 tables.
- [X] T039 [P] Implement `EligibleDoctorSearchRequestValidator` in `MediBridge.Services\Validators\Doctors\EligibleDoctorSearchRequestValidator.cs` for page bounds, non-negative numeric filters, activity score range 0-100, and min/max consistency.
- [X] T040 [P] Implement `CreateCampaignRequestValidator` in `MediBridge.Services\Validators\Campaigns\CreateCampaignRequestValidator.cs` for required title, description, clinical research information, at least one asset id, 1-100 unique target doctor ids, and required idempotency key handled by service/controller boundary.
- [X] T041 [P] Implement `TopUpCompanyWalletRequestValidator` in `MediBridge.Services\Validators\Wallets\TopUpCompanyWalletRequestValidator.cs` for amount >= 100 EGP and no more than two decimal places.
- [X] T042 [P] Add explicit Phase 5 authorization policy names `Phase5CompanyDoctorSearch`, `Phase5CompanyCampaignAccess`, and `Phase5CompanyWalletAccess` in `MediBridge.APIs\Security\AuthorizationPolicies.cs`.
- [X] T043 Update policy registration in `MediBridge.APIs\Extensions\ServiceCollectionExtensions.cs` so Phase 5 company policies require the Pharmaceutical Company role.
- [X] T044 Add explicit rate-limit policy names `Phase5CampaignSubmission` and `Phase5WalletTopUp` in `MediBridge.APIs\Config\RateLimitOptions.cs`.
- [X] T045 Register Phase 5 services and validators in `MediBridge.Services\Extensions\IdentityServiceCollectionExtensions.cs`.
- [X] T046 Update `MediBridge.APIs\Extensions\ServiceCollectionExtensions.cs` to ensure `ICompanyDoctorSearchService`, `ICampaignWorkflowService`, and `ICompanyWalletService` resolve in API and tests.
- [X] T047 Add Phase 5 database seeding helpers to `tests\integration\MediBridge.IntegrationTests\Phase5CampaignQueueTestHelpers.cs` for approved company, approved doctor, suspended doctor, soft-deleted doctor, zero-price doctor, approved campaign asset, company wallet, and queue item setup.
- [X] T048 Add Phase 5 contract seeding and JSON assertion helpers to `tests\contract\MediBridge.ContractTests\Phase5ContractTestHelpers.cs`.
- [X] T049 Run `dotnet build .\MediBridge.slnx` and fix compile errors caused by foundational declarations before starting any user story.

**Checkpoint**: Foundation ready. Repository contracts compile, EF Core migration exists, services can be registered, and all Phase 5 user stories can now be implemented.

---

## Phase 3: User Story 1 - Find Eligible Doctors (Priority: P1) - MVP

**Goal**: Approved company users can filter eligible doctors and receive deterministic paginated results.

**Independent Test**: Sign in as an approved company user, request `/api/company/doctors` with filters, and confirm only approved active positive-price doctors are returned in `ActivityScore DESC`, `PricePerMessage ASC`, `Id ASC` order with envelope pagination.

### Tests for User Story 1

- [X] T050 [P] [US1] Add contract tests for `GET /api/company/doctors` success envelope, `PageNumber`/`PageSize` defaults, and response fields in `tests\contract\MediBridge.ContractTests\CompanyDoctorSearchContractTests.cs`.
- [X] T051 [P] [US1] Add contract tests for `GET /api/company/doctors` 401, 403, invalid pagination 400, invalid filter 400, and 429 envelope behavior in `tests\contract\MediBridge.ContractTests\CompanyDoctorSearchContractTests.cs`.
- [X] T052 [P] [US1] Add integration tests proving unapproved, suspended, soft-deleted, and zero-price doctors are excluded from company search in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs`.
- [X] T053 [P] [US1] Add integration tests proving specialization, min/max experience, location, min activity score, and min/max price filters combine with AND semantics in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs`.
- [X] T054 [P] [US1] Add integration tests proving default ordering by activity score descending, price ascending, and stable id ascending across same-score/same-price doctors in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs`.
- [X] T055 [P] [US1] Add integration tests proving pagination default page size 20, maximum page size 100, empty result page, and invalid page bounds in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs`.
- [X] T056 [P] [US1] Add unit tests for doctor search ordering and pagination validation in `tests\unit\MediBridge.UnitTests\Phase5DoctorSearchOrderingTests.cs`.

### Implementation for User Story 1

- [X] T057 [P] [US1] Finalize doctor search DTOs in `MediBridge.Services\DTOs\Doctors\DoctorSearchDtos.cs` to match `contracts\campaign-queue-api.yaml`.
- [X] T058 [US1] Define `ICompanyDoctorSearchService.SearchEligibleDoctorsAsync` in `MediBridge.Services\Interfaces\ICompanyDoctorSearchService.cs` with actor user id, filters, pagination, and cancellation token.
- [X] T059 [US1] Implement `CompanyDoctorSearchService.SearchEligibleDoctorsAsync` in `MediBridge.Services\Services\CompanyDoctorSearchService.cs` with approved-company actor validation, repository filter call, pagination metadata, deterministic ordering, and safe audit on denial.
- [X] T060 [US1] Implement `CompanyDoctorsController.GetEligibleDoctors` in `MediBridge.APIs\Controllers\CompanyDoctorsController.cs` with `[Route("api/company/doctors")]`, JWT/company authorization, `[FromQuery]` filters, service delegation, `ApiEnvelopeFactory`, and no business logic.
- [X] T061 [US1] Wire doctor-search validator usage in `MediBridge.Services\Services\CompanyDoctorSearchService.cs` so invalid filters raise Phase 5 validation exceptions that global error handling maps to the standard 400 envelope.
- [X] T062 [US1] Add company doctor search sample requests and expected envelope examples to `MediBridge.APIs\MediBridge.APIs.http`.
- [X] T063 [US1] Run focused US1 tests with `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyDoctorSearch"` and fix failures in Phase 5 doctor search files.
- [X] T064 [US1] Run focused US1 integration tests with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5CompanyDoctorSearch"` and fix failures in Phase 5 doctor search files.
- [X] T065 [US1] Run focused US1 unit tests with `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5DoctorSearch"` and fix failures in Phase 5 doctor search files.

**Checkpoint**: US1 is independently complete when company doctor search works without campaign submission, queue creation, or wallet top-up.

---

## Phase 4: User Story 2 - Submit Campaign for Review (Priority: P1)

**Goal**: Approved company users can submit campaigns with required content, at least one approved asset, 1-100 eligible targets, all-or-nothing validation, target snapshots, company ownership, and mandatory idempotency.

**Independent Test**: Submit a valid campaign as an approved company and confirm `PendingReview` status, target snapshots, approved asset references, zero queue rows, and idempotent retry behavior.

### Tests for User Story 2

- [X] T066 [P] [US2] Add contract tests for `POST /api/company/campaigns` 201 success envelope, required `Idempotency-Key`, and `PendingReview` response fields in `tests\contract\MediBridge.ContractTests\CompanyCampaignContractTests.cs`.
- [X] T067 [P] [US2] Add contract tests for `POST /api/company/campaigns` missing idempotency key 400, missing title 400, missing description 400, missing clinical research information 400, missing asset 400, no targets 400, over-100 targets 400, duplicate targets 400, insufficient wallet balance 409, idempotency conflict 409, 401, 403, and 429 envelopes in `tests\contract\MediBridge.ContractTests\CompanyCampaignContractTests.cs`.
- [X] T068 [P] [US2] Add contract tests for `GET /api/company/campaigns` and `GET /api/company/campaigns/{campaignId}` company-owned success, 401, 403, and 404 envelopes in `tests\contract\MediBridge.ContractTests\CompanyCampaignContractTests.cs`.
- [X] T069 [P] [US2] Add integration tests proving valid campaign submission creates one `PendingReview` campaign, one target snapshot per selected doctor, no queue rows, and an audit event in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T070 [P] [US2] Add integration tests proving target snapshots preserve specialization, experience, location, activity score, and price at submission time even if the doctor profile changes afterward in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T071 [P] [US2] Add integration tests proving invalid target lists are all-or-nothing for empty, duplicate, over-100, unapproved, suspended, soft-deleted, and zero-price doctors in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T072 [P] [US2] Add integration tests proving missing, pending, rejected, quarantined, deleted, replaced, unrelated, or cross-company asset ids reject campaign submission with no campaign or targets in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T073 [P] [US2] Add integration tests proving campaign submission idempotency returns the existing result for the same company/key and rejects same company/key with different request content in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T074 [P] [US2] Add integration tests proving one company cannot list, view, create, or attach files to another company's campaign data through Phase 5 endpoints in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T075 [P] [US2] Add integration tests proving campaign submission rejects insufficient company wallet available balance before acceptance and creates no campaign, target, queue, wallet transaction, wallet ledger, reservation, charge, or balance-change records in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs`.
- [X] T076 [P] [US2] Add unit tests for campaign content, target count, duplicate target, idempotency key, approved asset validation, and wallet sufficiency total calculation in `tests\unit\MediBridge.UnitTests\Phase5CampaignValidationTests.cs`.

### Implementation for User Story 2

- [X] T077 [P] [US2] Finalize campaign DTOs in `MediBridge.Services\DTOs\Campaigns\CampaignDtos.cs` to match `contracts\campaign-queue-api.yaml`.
- [X] T078 [US2] Define `ICampaignWorkflowService.SubmitCampaignAsync`, `GetCompanyCampaignsAsync`, and `GetCompanyCampaignDetailAsync` in `MediBridge.Services\Interfaces\ICampaignWorkflowService.cs`.
- [X] T079 [US2] Implement campaign submission request hash creation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using normalized non-secret request data only, excluding raw files, tokens, request bodies, and response bodies.
- [X] T080 [US2] Implement approved company actor/profile resolution in `MediBridge.Services\Services\CampaignWorkflowService.cs` using Core identity/profile repository contracts only.
- [X] T081 [US2] Implement idempotency handling in `MediBridge.Services\Services\CampaignWorkflowService.cs` so missing key rejects, same company/key replays existing success, and same company/key with different request hash returns conflict.
- [X] T082 [US2] Implement campaign content validation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using `CreateCampaignRequestValidator`.
- [X] T083 [US2] Implement approved asset validation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using `IStoredFileRepository.IsAvailableAsApprovedAssetAsync` and owner/purpose checks.
- [X] T084 [US2] Implement target eligibility revalidation and target price snapshot total calculation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using profile repository methods and all-or-nothing rejection.
- [X] T085 [US2] Implement read-only company wallet sufficiency validation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using `IWalletRepository` available balance read; reject when available balance is below the selected target price snapshot total and do not reserve, charge, create wallet transactions, create wallet ledger entries, or mutate wallet balances.
- [X] T086 [US2] Implement atomic campaign creation, target snapshot insertion, idempotency record update, and audit event creation in `MediBridge.Services\Services\CampaignWorkflowService.cs` through `IDomainUnitOfWork`.
- [X] T087 [US2] Implement company campaign list/detail ownership logic in `MediBridge.Services\Services\CampaignWorkflowService.cs` with standard pagination and no analytics, deliveries, feedback, or spend totals.
- [X] T088 [US2] Implement `CompanyCampaignsController` endpoints in `MediBridge.APIs\Controllers\CompanyCampaignsController.cs` for `POST /api/company/campaigns`, `GET /api/company/campaigns`, and `GET /api/company/campaigns/{campaignId}` with HTTP-only logic and standard envelopes.
- [X] T089 [US2] Ensure `Idempotency-Key` header extraction and missing-header validation occurs in `MediBridge.APIs\Controllers\CompanyCampaignsController.cs` without duplicating business rules.
- [X] T090 [US2] Ensure insufficient wallet balance exceptions from `CampaignWorkflowService` are mapped by global error handling to a standard 409 envelope with a non-sensitive message in `MediBridge.APIs\Middleware\ExceptionHandlingMiddleware.cs`.
- [X] T091 [US2] Add safe campaign DTO mapping helpers in `MediBridge.Services\DTOs\Campaigns\CampaignDtoMapper.cs`; do not expose storage keys or private file access tokens.
- [X] T092 [US2] Add Phase 5 campaign audit event writes in `MediBridge.Services\Services\CampaignWorkflowService.cs` for success, validation failure, asset failure, target failure, insufficient wallet balance, idempotent replay, conflict, and ownership denial.
- [X] T093 [US2] Run focused US2 contract tests with `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyCampaign"` and fix failures in Phase 5 campaign files.
- [X] T094 [US2] Run focused US2 integration tests with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5CampaignWorkflow"` and fix failures in Phase 5 campaign files.
- [X] T095 [US2] Run focused US2 unit tests with `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5CampaignValidation"` and fix failures in Phase 5 campaign files.

**Checkpoint**: US2 is independently complete when a company can submit/list/view its campaigns and no queue rows are created before approval.

---

## Phase 5: User Story 3 - Queue Approved Campaigns Deterministically (Priority: P1)

**Goal**: Approved campaigns create deterministic per-doctor queue items exactly once per eligible target, with retry safety and auditability.

**Independent Test**: Transition a pending campaign to approved through the trusted service path and confirm one queued row per still-eligible target, duplicate prevention on retry, skipped ineligible target audit, and FIFO ordering.

### Tests for User Story 3

- [X] T096 [P] [US3] Add integration tests proving approved campaign queue creation creates one `DoctorMessageQueue` row per eligible target with status `Queued` in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.
- [X] T097 [P] [US3] Add integration tests proving queue creation creates no rows for `Draft`, `PendingReview`, `Rejected`, `Paused`, `Completed`, `Cancelled`, or soft-deleted campaigns in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.
- [X] T098 [P] [US3] Add integration tests proving queue creation retry after partial completion creates only missing queue rows and never duplicates `(CampaignId, DoctorId)` in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.
- [X] T099 [P] [US3] Add integration tests proving target doctors that become suspended, soft-deleted, unapproved, or zero-price before approval are skipped and audited in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.
- [X] T100 [P] [US3] Add integration tests proving pending queue reads for one doctor order by `QueuedAtUtc ASC` and then `Id ASC`, including same-time tie-break cases, in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.
- [X] T101 [P] [US3] Add scope tests proving Phase 5 queue creation does not create `DoctorAdDelivery`, does not reserve wallet funds, and does not change wallet balances in `tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs`.

### Implementation for User Story 3

- [X] T102 [US3] Define `ICampaignWorkflowService.CreateQueueForApprovedCampaignAsync` in `MediBridge.Services\Interfaces\ICampaignWorkflowService.cs`.
- [X] T103 [US3] Implement approved campaign lookup and status gate in `MediBridge.Services\Services\CampaignWorkflowService.cs` so only approved, non-deleted campaigns can create queue rows.
- [X] T104 [US3] Implement target retrieval and approval-time doctor eligibility recheck in `MediBridge.Services\Services\CampaignWorkflowService.cs`.
- [X] T105 [US3] Implement idempotent queue row creation in `MediBridge.Services\Services\CampaignWorkflowService.cs` using `IMessageQueueRepository` duplicate checks and a single Unit of Work transaction.
- [X] T106 [US3] Ensure queued rows set `QueuedAtUtc` from approval/queue insertion time and preserve `CampaignSubmittedAtUtc` from campaign creation when available in `MediBridge.Services\Services\CampaignWorkflowService.cs`.
- [X] T107 [US3] Implement queue creation result mapping with created count, skipped count, and duplicate existing count in `MediBridge.Services\DTOs\Campaigns\CampaignDtos.cs`.
- [X] T108 [US3] Add Phase 5 queue audit events for creation success, skipped target, duplicate retry, and non-approved no-op in `MediBridge.Services\Services\CampaignWorkflowService.cs`.
- [X] T109 [US3] Add a trusted test helper method named `TriggerApprovedCampaignQueueCreationAsync` in `tests\integration\MediBridge.IntegrationTests\Phase5CampaignQueueTestHelpers.cs` that calls `ICampaignWorkflowService.CreateQueueForApprovedCampaignAsync`; do not expose a public queue mutation endpoint in `MediBridge.APIs\Controllers`.
- [X] T110 [US3] Run focused US3 integration tests with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5QueueCreation"` and fix failures in Phase 5 queue files.

**Checkpoint**: US3 is independently complete when approved campaigns produce deterministic queued items without deliveries, wallet reservations, or public queue mutation endpoints.

---

## Phase 6: User Story 4 - Fund Company Wallet for Future Delivery (Priority: P2)

**Goal**: Approved company users can top up and query their own wallet with idempotent EGP top-up behavior and paginated transactions.

**Independent Test**: Top up a company wallet with at least 100 EGP, retry with the same idempotency key, and confirm available balance plus append-only transaction and ledger history reflect exactly one financial effect.

### Tests for User Story 4

- [X] T111 [P] [US4] Add contract tests for `GET /api/company/wallet` success envelope, transaction page shape, 401, 403, and pagination validation in `tests\contract\MediBridge.ContractTests\CompanyWalletContractTests.cs`.
- [X] T112 [P] [US4] Add contract tests for `POST /api/company/wallet/topup` success envelope, missing idempotency key 400, amount below 100 EGP 400, more-than-two-decimal amount 400, duplicate replay 200, conflict 409, 401, 403, and 429 in `tests\contract\MediBridge.ContractTests\CompanyWalletContractTests.cs`.
- [X] T113 [P] [US4] Add integration tests proving top-up credits company wallet available balance only and leaves reserved balance unchanged in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T114 [P] [US4] Add integration tests proving top-up creates one append-only `TopUp` transaction and one immutable available-balance ledger entry in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T115 [P] [US4] Add integration tests proving duplicate top-up retry with the same operation type and idempotency key produces no duplicate balance, transaction, or ledger effect in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T116 [P] [US4] Add integration tests proving company wallet query and top-up deny Doctor, Admin, anonymous users, pending companies, rejected companies, and other companies in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T117 [P] [US4] Add integration tests proving top-up rejects below 100 EGP, more than two decimal places, zero, negative, and missing idempotency key without balance changes in `tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs`.
- [X] T118 [P] [US4] Add unit tests for company wallet top-up amount precision, minimum amount, and idempotency-key validation in `tests\unit\MediBridge.UnitTests\Phase5CompanyWalletValidationTests.cs`.

### Implementation for User Story 4

- [X] T119 [P] [US4] Finalize company wallet DTOs in `MediBridge.Services\DTOs\Wallets\CompanyWalletDtos.cs` to match `contracts\campaign-queue-api.yaml`.
- [X] T120 [US4] Define `ICompanyWalletService.GetCompanyWalletAsync` and `TopUpCompanyWalletAsync` in `MediBridge.Services\Interfaces\ICompanyWalletService.cs`.
- [X] T121 [US4] Implement approved company actor/profile and wallet ownership resolution in `MediBridge.Services\Services\CompanyWalletService.cs`.
- [X] T122 [US4] Implement company wallet query in `MediBridge.Services\Services\CompanyWalletService.cs` with available balance, reserved balance, currency, paginated transactions, and no cross-company data leakage.
- [X] T123 [US4] Implement top-up validation in `MediBridge.Services\Services\CompanyWalletService.cs` using `TopUpCompanyWalletRequestValidator` and required idempotency key.
- [X] T124 [US4] Implement top-up idempotency in `MediBridge.Services\Services\CompanyWalletService.cs` using `WalletTransactionType.TopUp` plus idempotency key and existing transaction replay behavior.
- [X] T125 [US4] Implement atomic available-balance credit, append-only `TopUp` transaction, immutable ledger entry, and audit event creation in `MediBridge.Services\Services\CompanyWalletService.cs` through `IDomainUnitOfWork`.
- [X] T126 [US4] Ensure top-up metadata in `MediBridge.Services\Services\CompanyWalletService.cs` stores only safe description/reference text and never raw gateway payloads, secrets, request bodies, or response bodies.
- [X] T127 [US4] Implement `CompanyWalletController` endpoints in `MediBridge.APIs\Controllers\CompanyWalletController.cs` for `GET /api/company/wallet` and `POST /api/company/wallet/topup` with HTTP-only logic, `Idempotency-Key` extraction, service delegation, and standard envelopes.
- [X] T128 [US4] Add Phase 5 wallet audit events for top-up success, validation failure, idempotent replay, conflict, and ownership denial in `MediBridge.Services\Services\CompanyWalletService.cs`.
- [X] T129 [US4] Run focused US4 contract tests with `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyWallet"` and fix failures in Phase 5 wallet files.
- [X] T130 [US4] Run focused US4 integration tests with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5CompanyWallet"` and fix failures in Phase 5 wallet files.
- [X] T131 [US4] Run focused US4 unit tests with `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5CompanyWallet"` and fix failures in Phase 5 wallet files.

**Checkpoint**: US4 is independently complete when company wallet top-up/query works without campaign submission, queue creation, or later payment gateway behavior.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validate the whole Phase 5 increment, protect scope, and clean up documentation.

- [X] T132 [P] Add sample requests for company doctor search, campaign submission, campaign list/detail, wallet query, and wallet top-up in `MediBridge.APIs\MediBridge.APIs.http`.
- [X] T133 [P] Update `specs\005-campaign-queue\quickstart.md` with the final Phase 5 command filters, endpoint paths, response shapes, and validation evidence.
- [X] T134 [P] Update the Phase 5 section of `docs\backend-plan.md` with the implemented campaign, queue, and wallet behavior; do not broaden scope beyond Phase 5.
- [X] T135 Add integration scope guard tests proving no Phase 5 public endpoint or service implements daily injector jobs, expiry jobs, doctor inbox, doctor read/interact, settlement, reporting analytics, withdrawals, weekly enforcement, activity score jobs, or production payment gateway behavior in `tests\integration\MediBridge.IntegrationTests\Phase5ScopeGuardTests.cs`.
- [X] T136 Add layering boundary tests proving `MediBridge.APIs` controllers do not reference EF Core types and `MediBridge.Services` does not reference EF Core infrastructure in `tests\integration\MediBridge.IntegrationTests\Phase5LayeringBoundaryTests.cs`.
- [X] T137 Add audit safety tests proving Phase 5 campaign and wallet audit metadata excludes raw gateway payloads, secrets, private file access tokens, storage keys, request bodies, response bodies, and stack traces in `tests\integration\MediBridge.IntegrationTests\Phase5AuditSafetyTests.cs`.
- [X] T138 Run contract test suite with `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyDoctorSearch|FullyQualifiedName~CompanyCampaign|FullyQualifiedName~CompanyWallet"` and fix failures in Phase 5 files.
- [X] T139 Run integration test suite with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5"` and fix failures in Phase 5 files.
- [X] T140 Run unit test suite with `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5"` and fix failures in Phase 5 files.
- [X] T141 Run full build with `dotnet build .\MediBridge.slnx` and fix compile, analyzer, or warning regressions introduced by Phase 5 files.
- [X] T142 Run full regression with `dotnet test .\MediBridge.slnx` and fix regressions in Phase 1-5 behavior.
- [X] T143 Manually inspect `MediBridge.APIs\Controllers\CompanyDoctorsController.cs`, `MediBridge.APIs\Controllers\CompanyCampaignsController.cs`, and `MediBridge.APIs\Controllers\CompanyWalletController.cs` to confirm controllers remain HTTP-only and delegate all business logic to services.
- [X] T144 Manually inspect `MediBridge.Services\Services\CompanyDoctorSearchService.cs`, `MediBridge.Services\Services\CampaignWorkflowService.cs`, and `MediBridge.Services\Services\CompanyWalletService.cs` to confirm orchestration uses Core contracts and Unit of Work abstractions only.
- [X] T145 Manually inspect `MediBridge.Core\MediBridge.Core.csproj`, `MediBridge.Services\MediBridge.Services.csproj`, and source `using` directives to confirm no EF Core, ASP.NET Core HTTP, Cloudinary, or storage-provider infrastructure leaked into Core or service contracts.
- [X] T146 Manually inspect `MediBridge.Repository\Migrations` and `MediBridge.Repository\Data\MediBridgeDbContext.cs` to confirm Phase 5 migration is additive and preserves Phase 1-4 tables/data.
- [X] T147 Manually inspect API DTOs in `MediBridge.Services\DTOs\Doctors`, `MediBridge.Services\DTOs\Campaigns`, and `MediBridge.Services\DTOs\Wallets` to confirm no private storage keys, signed URLs, secrets, raw gateway payloads, stack traces, request bodies, or response bodies are exposed.
- [X] T148 Record final Phase 5 validation evidence in `specs\005-campaign-queue\quickstart.md` under a new Phase 5 validation notes section.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies. Creates files and folders only.
- **Phase 2 Foundational**: Depends on Phase 1. Blocks every user story.
- **Phase 3 US1**: Depends on Phase 2. MVP slice.
- **Phase 4 US2**: Depends on Phase 2. Can be implemented after US1 or in parallel after foundational work, but campaign submission tests may reuse US1 doctor-search seed helpers.
- **Phase 5 US3**: Depends on Phase 2 and benefits from US2 campaign/target service methods. Queue creation can be tested with seeded approved campaigns if US2 is incomplete.
- **Phase 6 US4**: Depends on Phase 2. Independent from US1-US3.
- **Phase 7 Polish**: Depends on all desired user stories being implemented.

### User Story Dependencies

- **US1 Find Eligible Doctors**: Independent after foundational tasks. Recommended MVP.
- **US2 Submit Campaign for Review**: Independent after foundational tasks if tests seed eligible doctors and approved assets directly; naturally benefits from US1 semantics.
- **US3 Queue Approved Campaigns Deterministically**: Depends on campaign/target persistence contracts from foundational tasks; can seed approved campaigns directly or reuse US2 implementation.
- **US4 Fund Company Wallet**: Independent after foundational tasks.

### Within Each User Story

- Write tests before implementation and confirm they fail for the missing behavior.
- Complete DTOs and service interface before service implementation.
- Complete service implementation before controllers.
- Complete controllers before contract tests can pass.
- Complete repository methods before integration tests can pass.
- Run focused tests at each checkpoint before moving to the next story.

---

## Parallel Opportunities

- Setup tasks T001-T010 are mostly independent and can be split by folder/test area.
- Foundational DTO tasks T011-T014 can run in parallel.
- Foundational entity/config/interface tasks T016-T029 can run in parallel if each developer owns one file.
- Repository implementation tasks T030-T036 can run in parallel after interfaces are agreed.
- Validator tasks T039-T041 can run in parallel.
- US1 test tasks T050-T056 can run in parallel before US1 implementation.
- US2 test tasks T066-T076 can run in parallel before US2 implementation.
- US3 test tasks T096-T101 can run in parallel before US3 implementation.
- US4 test tasks T111-T118 can run in parallel before US4 implementation.
- US1, US2, and US4 can proceed in parallel after Phase 2 if developers coordinate shared DTO/interface changes.
- Polish documentation and manual inspection tasks T132-T148 can be split after implementation stabilizes.

---

## Parallel Example: User Story 1

```text
Task: "T050 [US1] Add contract tests for GET /api/company/doctors in tests\contract\MediBridge.ContractTests\CompanyDoctorSearchContractTests.cs"
Task: "T052 [US1] Add integration tests for doctor eligibility exclusion in tests\integration\MediBridge.IntegrationTests\Phase5CompanyDoctorSearchIntegrationTests.cs"
Task: "T056 [US1] Add unit tests for ordering and pagination in tests\unit\MediBridge.UnitTests\Phase5DoctorSearchOrderingTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T066 [US2] Add campaign submission contract tests in tests\contract\MediBridge.ContractTests\CompanyCampaignContractTests.cs"
Task: "T069 [US2] Add valid campaign submission integration tests in tests\integration\MediBridge.IntegrationTests\Phase5CampaignWorkflowIntegrationTests.cs"
Task: "T076 [US2] Add campaign validation unit tests in tests\unit\MediBridge.UnitTests\Phase5CampaignValidationTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T096 [US3] Add approved campaign queue creation tests in tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs"
Task: "T098 [US3] Add queue retry idempotency tests in tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs"
Task: "T101 [US3] Add no-delivery/no-wallet-reservation scope tests in tests\integration\MediBridge.IntegrationTests\Phase5QueueCreationIntegrationTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T111 [US4] Add company wallet query contract tests in tests\contract\MediBridge.ContractTests\CompanyWalletContractTests.cs"
Task: "T113 [US4] Add company wallet top-up balance integration tests in tests\integration\MediBridge.IntegrationTests\Phase5CompanyWalletIntegrationTests.cs"
Task: "T118 [US4] Add company wallet validation unit tests in tests\unit\MediBridge.UnitTests\Phase5CompanyWalletValidationTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 setup.
2. Complete Phase 2 foundational contracts, persistence, validators, policies, and DI.
3. Complete Phase 3 US1 company doctor search.
4. Stop and validate US1 with T063, T064, and T065.
5. Demonstrate eligible doctor search as the first useful slice.

### Incremental Delivery

1. US1: Company can find eligible doctors.
2. US2: Company can submit a campaign for review with target snapshots, read-only wallet sufficiency validation, and no queue rows.
3. US3: Approved campaigns produce deterministic queue rows.
4. US4: Company can fund and query wallet for later activation.
5. Polish: run all focused and full regression checks.

### Safe Scope Boundaries

- Do not create public admin campaign review endpoints in Phase 5 except test/service hooks needed to verify approved-campaign queue creation.
- Do not create `DoctorAdDelivery` records in Phase 5.
- Do not modify company wallet reserved balance in Phase 5.
- Do not implement `Reserve`, `Release`, `Charge`, `Earn`, `Refund`, withdrawal, platform fee, and settlement workflows in Phase 5.
- Do not add reporting analytics, feedback, spend totals, weekly enforcement, activity score jobs, or production payment gateway code.

---

## Final Validation Commands

```powershell
dotnet build .\MediBridge.slnx
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyDoctorSearch|FullyQualifiedName~CompanyCampaign|FullyQualifiedName~CompanyWallet"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5"
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5"
dotnet test .\MediBridge.slnx
```

## Notes

- `[P]` means the task touches different files and can run in parallel after its dependencies are met.
- `[US1]`, `[US2]`, `[US3]`, and `[US4]` map directly to the user stories in `specs\005-campaign-queue\spec.md`.
- Every task has an exact target file or command path.
- Tests must be written before implementation for the behavior they cover.
- Keep commits small if committing during implementation, ideally after each checkpoint.
