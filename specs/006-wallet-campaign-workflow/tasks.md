# Tasks: Wallet and Campaign Workflow

**Input**: Design documents from `D:\My Project\MediBridge Project\MediBridge\specs\006-wallet-campaign-workflow\`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/wallet-campaign-workflow-api.yaml](./contracts/wallet-campaign-workflow-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Required. The feature spec and quickstart require real HTTP smoke validation, contract tests, integration tests, and service/unit tests for wallet creation, mock payment top-up, idempotency replay/conflict, campaign draft/asset/submission, admin pricing, moderation, queue creation, role visibility, standard envelope behavior, and no real payment-provider integration.

**Constitution Note**: Every implementation task must preserve Onion Architecture. `MediBridge.Core` contains pure domain entities and contracts only. `MediBridge.Repository` contains SQL Server EF Core persistence and repository implementations only. `MediBridge.Services` owns business orchestration and validation. `MediBridge.APIs` controllers stay HTTP-only and return the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope. Do not reference EF Core infrastructure types from services or controllers. Do not add real payment gateways, redirects, webhooks, callback endpoints, provider credentials, provider configuration, delivery activation jobs, doctor daily-limit processing, delivered-message expiry jobs, doctor interaction settlement, payouts, reporting read models, or malware scanning.

**Small-Model Execution Rule**: Complete tasks in order unless a task is explicitly marked `[P]`. For every task that changes behavior, run the named test for that task or the nearest affected test project before marking it done. If a task mentions "verify", the executor must inspect the referenced file and confirm the stated condition, not assume it exists.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Task can run in parallel with other `[P]` tasks in the same phase because it touches different files and does not depend on incomplete work.
- **[Story]**: User-story task label. Setup, foundational, and polish tasks intentionally have no story label.
- **File paths**: Every task includes exact target paths. If a path does not exist, create it with the expected namespace and project conventions.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare shared folders, test fixtures, and compile-time scaffolding without implementing story behavior.

- [X] T001 Verify current branch is `006-wallet-campaign-workflow` by running `git status --short --branch` from repository root `D:\My Project\MediBridge Project\MediBridge` and record the output in the executor's implementation report.
- [X] T002 Create missing Core payment folders `MediBridge.Core/Entities/Payments` and `MediBridge.Core/Interfaces/Payments` for the mock payment domain.
- [X] T003 Create missing Repository payment folders `MediBridge.Repository/Configurations/Payments` and `MediBridge.Repository/Repositories/Payments` for EF Core mapping and repository implementation.
- [X] T004 Create missing Services feature folders `MediBridge.Services/DTOs/Wallets`, `MediBridge.Services/DTOs/Payments`, `MediBridge.Services/DTOs/Campaigns`, `MediBridge.Services/DTOs/Pricing`, `MediBridge.Services/Validators/Wallets`, `MediBridge.Services/Validators/Campaigns`, and `MediBridge.Services/Validators/Pricing`.
- [X] T005 Create missing contract test folders `tests/contract/MediBridge.ContractTests/Wallets`, `tests/contract/MediBridge.ContractTests/Campaigns`, and `tests/contract/MediBridge.ContractTests/Admin`.
- [X] T006 Create missing integration test folders `tests/integration/MediBridge.IntegrationTests/Wallets`, `tests/integration/MediBridge.IntegrationTests/Campaigns`, and `tests/integration/MediBridge.IntegrationTests/Workflow`.
- [X] T007 Create missing unit test folders `tests/unit/MediBridge.UnitTests/Wallets`, `tests/unit/MediBridge.UnitTests/Campaigns`, and `tests/unit/MediBridge.UnitTests/Pricing`.
- [X] T008 [P] Add `MediBridge.Core/Enums/PaymentStatus.cs` with `Succeeded = 1` only, and do not add Pending, Failed, ProviderCallback, or ProviderWebhook statuses.
- [X] T009 [P] Add empty test helper shell `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignWorkflowTestHelpers.cs` to centralize approved admin/company/doctor fixture creation for this feature.
- [X] T010 [P] Add endpoint route constants shell `tests/contract/MediBridge.ContractTests/WalletCampaignWorkflowRoutes.cs` with constants for every route in `specs/006-wallet-campaign-workflow/contracts/wallet-campaign-workflow-api.yaml`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared domain, repository, DTO, validator, service, authorization, and test infrastructure that all user stories depend on.

**CRITICAL**: Do not start user-story implementation until this phase compiles and its boundary tests pass.

- [X] T011 Add `MockPaymentTransaction` entity in `MediBridge.Core/Entities/Payments/MockPaymentTransaction.cs` with properties exactly matching `data-model.md`: `PaymentId`, `CompanyId`, `WalletId`, `Amount`, `Currency`, `Status`, `CreatedAtUtc`, `TransactionReference`, `IdempotencyKey`, `WalletBalanceBefore`, `WalletBalanceAfter`, `WalletTransactionId`, and nullable `AuditEventId`.
- [X] T012 Add `IPaymentRepository` contract in `MediBridge.Core/Interfaces/Payments/IPaymentRepository.cs` with methods to add a mock payment, find by payment id, find by company id plus idempotency key, find by transaction reference, and list payment ids by company ordered by `CreatedAtUtc DESC`.
- [X] T013 Update `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs` to expose `IPaymentRepository Payments { get; }` while keeping all existing repository properties unchanged.
- [X] T014 Add EF Core configuration `MediBridge.Repository/Configurations/Payments/MockPaymentTransactionConfiguration.cs` mapping table `MockPaymentTransactions`, decimal precision `(18,2)`, required `Currency = EGP`, required `Status = Succeeded`, unique `TransactionReference`, index `(CompanyId, CreatedAtUtc)`, and unique index `(CompanyId, IdempotencyKey)`.
- [X] T015 Update `MediBridge.Repository/Data/MediBridgeDbContext.cs` to add `DbSet<MockPaymentTransaction> MockPaymentTransactions` and apply the payment configuration.
- [X] T016 Add `PaymentRepository` implementation in `MediBridge.Repository/Repositories/Payments/PaymentRepository.cs` using `MediBridgeDbContext` and no service-layer dependencies.
- [X] T017 Update `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs` constructor and property list to inject and expose `IPaymentRepository Payments`.
- [X] T018 Update `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs` to register `IPaymentRepository` with `PaymentRepository`.
- [X] T019 Generate an EF Core migration named `AddMockPaymentTransactions` using the repository's existing migration command pattern. The generated files will have EF's timestamp prefix, for example `YYYYMMDDHHMMSS_AddMockPaymentTransactions.cs` and `YYYYMMDDHHMMSS_AddMockPaymentTransactions.Designer.cs`; do not create files with the literal name `<timestamp>_AddMockPaymentTransactions.cs`. Verify the migration does not alter Phase 2 identity or Phase 3 wallet/campaign/queue tables except for model snapshot additions.
- [X] T020 [P] Add DTOs `CompanyWalletDto`, `MockTopUpRequestDto`, and `MockPaymentResultDto` in `MediBridge.Services/DTOs/Wallets/CompanyWalletDto.cs`, `MediBridge.Services/DTOs/Payments/MockTopUpRequestDto.cs`, and `MediBridge.Services/DTOs/Payments/MockPaymentResultDto.cs`.
- [X] T021 [P] Add campaign DTO files `MediBridge.Services/DTOs/Campaigns/CampaignDto.cs`, `MediBridge.Services/DTOs/Campaigns/CreateCampaignDraftRequestDto.cs`, `MediBridge.Services/DTOs/Campaigns/CampaignAssetDto.cs`, `MediBridge.Services/DTOs/Campaigns/TargetPreviewDto.cs`, `MediBridge.Services/DTOs/Campaigns/CampaignSubmissionDto.cs`, `MediBridge.Services/DTOs/Campaigns/CampaignReviewResultDto.cs`, `MediBridge.Services/DTOs/Campaigns/QueueSummaryDto.cs`, and `MediBridge.Services/DTOs/Campaigns/QueueRowDto.cs`.
- [X] T022 [P] Add pricing DTOs `SetDoctorPriceRequestDto` and `DoctorPriceDto` in `MediBridge.Services/DTOs/Pricing`.
- [X] T023 [P] Add shared `ReviewDecisionRequestDto` in `MediBridge.Services/DTOs/Campaigns/ReviewDecisionRequestDto.cs` with `Decision` and nullable `Reason`.
- [X] T024 [P] Add `MockTopUpRequestDtoValidator` in `MediBridge.Services/Validators/Wallets/MockTopUpRequestDtoValidator.cs` requiring positive amount, EGP currency, and no more than two decimals.
- [X] T025 [P] Add `CreateCampaignDraftRequestDtoValidator`, `CampaignAssetUploadRequestValidator`, and `ReviewDecisionRequestDtoValidator` in `MediBridge.Services/Validators/Campaigns` enforcing title/description lengths, asset metadata requirements, and rejection/changes-requested reason requirements.
- [X] T026 [P] Add `SetDoctorPriceRequestDtoValidator` in `MediBridge.Services/Validators/Pricing/SetDoctorPriceRequestDtoValidator.cs` requiring positive EGP amount with at most two decimals and rejecting null, zero, negative, and over-precise values.
- [X] T027 Add service contract files `MediBridge.Services/Interfaces/ICompanyWalletService.cs`, `MediBridge.Services/Interfaces/ICampaignWorkflowService.cs`, `MediBridge.Services/Interfaces/IAdminPricingService.cs`, and `MediBridge.Services/Interfaces/IAdminCampaignReviewService.cs` with method names matching the contract operations: wallet query, mock top-up, draft create, asset upload, target preview, submit, queue summary, doctor price update, asset review, campaign review, and admin queue rows.
- [X] T028 Add service exception classes in `MediBridge.Services/Interfaces/WorkflowExceptions.cs`: `WorkflowValidationException`, `WorkflowConflictException`, `WorkflowNotFoundException`, `WorkflowForbiddenException`, and `WorkflowUnauthorizedException`; include safe `Message` values only, with no raw request or stack data.
- [X] T029 Update `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` to register all new services and validators.
- [X] T030 Update `MediBridge.APIs/Security/AuthorizationPolicies.cs` to verify existing Admin and Pharmaceutical Company policies are available; add `CompanyOnly` policy in the same file only when the policy is missing, without weakening existing Admin policy.
- [X] T031 Add API exception mapping helper `MediBridge.APIs/Contracts/WorkflowActionResultMapper.cs` that converts workflow exceptions to standard envelopes with HTTP 400, 401, 403, 404, and 409.
- [X] T032 Add contract test base helper `tests/contract/MediBridge.ContractTests/WalletCampaignContractTestBase.cs` for authenticated admin/company/doctor clients and envelope assertions.
- [X] T033 Add integration test fixture helpers in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignWorkflowTestHelpers.cs` to create approved admin, approved company, approved doctor, JWTs, and direct database lookups for wallet, payment, campaign, asset, target, and queue evidence.
- [X] T034 Add unit test `tests/unit/MediBridge.UnitTests/Wallets/MockPaymentEntityTests.cs` proving `MockPaymentTransaction` requires positive two-decimal EGP amounts, `Succeeded` status, generated transaction reference, and before/after balances.
- [X] T035 Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj --filter MockPaymentEntityTests`; expect failure before implementation and success after T011-T019 and T034 are complete.
- [X] T036 Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter Phase3`; verify existing Phase 3 wallet, queue, file, campaign, policy, and audit tests still pass after adding payment persistence.

**Checkpoint**: Foundation is ready when the solution compiles, payment persistence exists, service/controller skeletons can be registered, and no Phase 3 persistence tests regress.

---

## Phase 3: User Story 1 - Approved Companies Can Fund Campaigns (Priority: P1) MVP

**Goal**: Approved company accounts can query an active wallet, self-repair missing wallet rows, run mock checkout top-ups, replay idempotent top-ups safely, reject conflicting replays, and receive standard envelope responses.

**Independent Test**: Approve a company account, then use only secured company HTTP requests to query wallet, top up, replay identical top-up, and replay conflicting top-up; verify wallet balance, mock payment record, wallet transaction, ledger entry, and audit evidence.

### Tests for User Story 1

- [X] T037 [P] [US1] Add contract tests for `GET /api/company/wallet` in `tests/contract/MediBridge.ContractTests/Wallets/CompanyWalletContractTests.cs` verifying 200 envelope for company token, 401 without token, 403 for doctor token, and no 404 solely because wallet row was missing.
- [X] T038 [P] [US1] Add contract tests for `POST /api/company/wallet/mock-checkout` in `tests/contract/MediBridge.ContractTests/Wallets/MockCheckoutContractTests.cs` verifying 200 envelope shape, `Succeeded` status, required `Idempotency-Key`, 400 invalid amount/currency, 401 missing token, 403 non-company token, and 409 conflicting replay.
- [X] T039 [P] [US1] Add integration test `CompanyWalletQuery_CreatesMissingWalletForApprovedCompany` in `tests/integration/MediBridge.IntegrationTests/Wallets/CompanyWalletWorkflowTests.cs` that deletes or omits the company wallet fixture, calls `GET /api/company/wallet`, and verifies one active company wallet exists.
- [X] T040 [P] [US1] Add integration test `MockCheckout_CreditsWalletAndRecordsPaymentTransactionLedgerAudit` in `tests/integration/MediBridge.IntegrationTests/Wallets/MockCheckoutWorkflowTests.cs` verifying payment row, wallet balance before/after, `TopUp` wallet transaction, wallet ledger credit, and audit event.
- [X] T041 [P] [US1] Add integration test `MockCheckout_ReplayReturnsOriginalResultAndConflictDoesNotMutateBalance` in `tests/integration/MediBridge.IntegrationTests/Wallets/MockCheckoutIdempotencyTests.cs` verifying replay by the same company with the same `Idempotency-Key`, amount, and currency returns the original `PaymentId` and `TransactionReference`, while a different amount or currency returns 409 with unchanged wallet balance.
- [X] T042 [P] [US1] Add unit tests for company wallet service validation in `tests/unit/MediBridge.UnitTests/Wallets/CompanyWalletServiceTests.cs` covering EGP-only, positive amount, two-decimal precision, no provider redirect/callback fields, and safe conflict messages.

### Implementation for User Story 1

- [X] T043 [US1] Implement `CompanyWalletService` in `MediBridge.Services/Services/CompanyWalletService.cs` with `GetCompanyWalletAsync` that verifies approved company context, finds active company profile, creates missing active company wallet inside `IDomainUnitOfWork.ExecuteInTransactionAsync`, and returns `CompanyWalletDto`.
- [X] T044 [US1] Implement `CompanyWalletService.CreateMockTopUpAsync` in `MediBridge.Services/Services/CompanyWalletService.cs` to validate idempotency key, validate `MockTopUpRequestDto`, look up prior mock top-ups by company plus idempotency key, compare only client-supplied replay fields (`Amount`, `Currency`), return the original result on identical replay, reject conflicting replay payloads, create `MockPaymentTransaction`, stage wallet available balance credit, add `WalletTransactionType.TopUp`, add `WalletLedgerEntry` credit to available balance, add audit event, and commit atomically.
- [X] T045 [US1] Ensure `CompanyWalletService.CreateMockTopUpAsync` never reads payment-provider credentials, never returns redirect URLs, never creates webhook/callback data, and always sets mock payment `Status = Succeeded` in `MediBridge.Services/Services/CompanyWalletService.cs`.
- [X] T046 [US1] Update `MediBridge.Services/Services/AdminAccountService.cs` so approving a company also creates one active company wallet if missing, using service/repository abstractions and preserving existing approval behavior for doctors/admins.
- [X] T047 [US1] Add `CompanyWalletController` in `MediBridge.APIs/Controllers/CompanyWalletController.cs` with `[Authorize]` plus company-role policy, routes `GET api/company/wallet` and `POST api/company/wallet/mock-checkout`, `Idempotency-Key` header binding, standard envelope responses, and no business logic.
- [X] T048 [US1] Add controller exception mapping in `MediBridge.APIs/Controllers/CompanyWalletController.cs` using `WorkflowActionResultMapper` for validation, conflict, unauthorized, forbidden, and not-found cases.
- [X] T049 [US1] Update `MediBridge.APIs/Program.cs` to ensure new company wallet services, validators, repositories, and authorization policy are registered for runtime.
- [X] T050 [US1] Update Swagger/OpenAPI annotations or XML comments in `MediBridge.APIs/Controllers/CompanyWalletController.cs` so generated Swagger matches `specs/006-wallet-campaign-workflow/contracts/wallet-campaign-workflow-api.yaml`.
- [X] T051 [US1] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter CompanyWallet`; fix failures until all US1 wallet contract tests pass.
- [X] T052 [US1] Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter Wallets`; fix failures until all US1 wallet integration tests pass.
- [X] T053 [US1] Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj --filter Wallets`; fix failures until all US1 wallet unit tests pass.

**Checkpoint**: MVP complete. Company wallet query and mock checkout top-up work independently through public secured HTTP and never call a real payment provider.

---

## Phase 4: User Story 2 - Companies Can Prepare a Reviewable Campaign (Priority: P1)

**Goal**: Approved companies can create owned draft campaigns, upload campaign media assets to their drafts, view asset status, and receive a clear validation failure when submission is attempted without an approved asset.

**Independent Test**: Using only secured company HTTP requests, create a draft campaign, upload an asset to that draft, verify pending review status, deny access from another company, and verify submission is rejected until at least one asset is approved.

**Manual Senior Review 2026-06-20**: Phase 4 is complete. Manual review confirms Onion Architecture boundaries, thin company HTTP controllers, service-owned approval and ownership decisions, repository-only EF Core access, cancellation propagation, and transactional draft/asset mutations. Identity and approval dependencies were reviewed alongside the campaign workflow. Verification passed for the `CompanyCampaign`, `Campaigns`, and `CampaignDraft` suites, plus the related login, refresh, logout, and admin-decision integration coverage.

### Tests for User Story 2

- [X] T054 [P] [US2] Add contract tests for `POST /api/company/campaigns` in `tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignDraftContractTests.cs` verifying 201 envelope, 400 validation, 401 missing token, and 403 non-company token.
- [X] T055 [P] [US2] Add contract tests for `POST /api/company/campaigns/{campaignId}/assets` in `tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignAssetContractTests.cs` verifying 201 envelope, pending review status, 400 invalid upload metadata, 403 non-owner/non-company, and 404 invisible campaign.
- [X] T056 [P] [US2] Add integration test `Company_CreatesDraftAndUploadsPendingAsset` in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDraftWorkflowTests.cs` verifying campaign `Draft` status and `StoredFile` owner type `Campaign`, purpose `CampaignMedia`, review status `Pending`.
- [X] T057 [P] [US2] Add integration test `Company_CannotUploadAssetToAnotherCompanyCampaign` in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignOwnershipTests.cs` verifying another company gets 403 or 404 and no `StoredFile` is created.
- [X] T058 [P] [US2] Add integration test `SubmitCampaign_WithoutApprovedAsset_ReturnsValidationEnvelope` in `tests/integration/MediBridge.IntegrationTests/Campaigns/CampaignSubmissionValidationTests.cs` verifying no target snapshots and no wallet reservation are created.
- [X] T059 [P] [US2] Add unit tests for campaign draft and asset validation in `tests/unit/MediBridge.UnitTests/Campaigns/CampaignDraftValidationTests.cs` covering required title, required description, allowed draft states, asset purpose, and no malware-scanning requirement.

### Implementation for User Story 2

- [X] T060 [US2] Implement `CampaignWorkflowService.CreateDraftAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to verify approved company ownership, create `Campaign` with `Status = Draft`, persist through `IDomainUnitOfWork.Campaigns`, and return `CampaignDto`.
- [X] T061 [US2] Implement `CampaignWorkflowService.UploadAssetAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to verify company owns the campaign, campaign is `Draft` or returned for revision, create `StoredFile` metadata with `OwnerType = Campaign`, `Purpose = CampaignMedia`, `ReviewStatus = Pending`, and return `CampaignAssetDto`.
- [X] T062 [US2] Implement `CampaignWorkflowService.SubmitCampaignAsync` precondition checks in `MediBridge.Services/Services/CampaignWorkflowService.cs` so campaigns with no approved asset, only pending assets, or only rejected assets return a validation failure before target snapshots or wallet reservations. Do not implement the successful funded submission path in US2; complete that path in T096 after US3/US4 prerequisites exist.
- [X] T063 [US2] Add `CampaignsController` in `MediBridge.APIs/Controllers/CampaignsController.cs` with company-role routes `POST api/company/campaigns`, `POST api/company/campaigns/{campaignId}/assets`, and `POST api/company/campaigns/{campaignId}/submit`; keep controller HTTP-only and delegate to `ICampaignWorkflowService`.
- [X] T064 [US2] Add request binding for multipart campaign asset upload in `MediBridge.APIs/Controllers/CampaignsController.cs`; for this feature store metadata only and do not implement malware scanning or third-party storage.
- [X] T065 [US2] Add controller exception mapping in `MediBridge.APIs/Controllers/CampaignsController.cs` using `WorkflowActionResultMapper` for validation, ownership, not-found, and conflict responses.
- [X] T066 [US2] Update service registration in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` to register `ICampaignWorkflowService` and campaign validators.
- [X] T067 [US2] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter CompanyCampaign`; fix failures until US2 campaign contract tests pass.
- [X] T068 [US2] Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter Campaigns`; fix failures until US2 draft/asset/submission validation integration tests pass.
- [X] T069 [US2] Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj --filter CampaignDraft`; fix failures until US2 unit tests pass.

**Checkpoint**: Companies can prepare draft campaigns and pending campaign assets independently; submission remains safely blocked until asset approval and later prerequisites exist.

---

## Phase 5: User Story 3 - Admins Can Unlock Eligible Doctor Targeting (Priority: P1)

**Goal**: Admins can set a positive doctor price, invalid price inputs are rejected, price history is retained, and company target preview includes only approved eligible doctors with positive prices.

**Independent Test**: Use an approved doctor with null price, verify exclusion from target preview, set a positive price as admin, verify inclusion and cost estimate, then verify later submitted campaigns keep price snapshots.

### Tests for User Story 3

- [X] T070 [P] [US3] Add contract tests for `PUT /api/admin/doctors/{doctorId}/price` in `tests/contract/MediBridge.ContractTests/Admin/AdminDoctorPricingContractTests.cs` verifying 200 envelope, 400 for null/zero/negative/over-precise price, 401 missing token, 403 company/doctor token, and 404 unknown doctor.
- [X] T071 [P] [US3] Add contract tests for `GET /api/company/campaigns/{campaignId}/target-preview` in `tests/contract/MediBridge.ContractTests/Campaigns/TargetPreviewContractTests.cs` verifying envelope shape, eligible count, estimated cost, 401, 403, and 404.
- [X] T072 [P] [US3] Add integration test `AdminDoctorPricing_PositivePriceCreatesHistoryAndEnablesTargeting` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminDoctorPricingWorkflowTests.cs` verifying `DoctorProfile.PricePerMessage`, `DoctorPriceHistory`, and target preview inclusion.
- [X] T073 [P] [US3] Add integration test `AdminDoctorPricing_InvalidValuesAreRejectedAndDoNotChangeExistingSnapshots` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminDoctorPricingValidationTests.cs` covering null, zero, negative, and three-decimal values.
- [X] T074 [P] [US3] Add integration test `CampaignSubmission_SnapshotsDoctorPriceAtSubmission` in `tests/integration/MediBridge.IntegrationTests/Campaigns/CampaignTargetSnapshotTests.cs` verifying later price changes do not alter existing `CampaignTarget.PricePerMessageSnapshot`.
- [X] T075 [P] [US3] Add unit tests for target eligibility filtering in `tests/unit/MediBridge.UnitTests/Pricing/DoctorTargetEligibilityTests.cs` covering approved status, marketplace status, deleted doctors, null price, zero price, positive price, and over-precise price.

### Implementation for User Story 3

- [X] T076 [US3] Implement `AdminPricingService` in `MediBridge.Services/Services/AdminPricingService.cs` to verify admin user, validate positive two-decimal EGP price, update `DoctorProfile.PricePerMessage`, append `DoctorPriceHistory`, and return `DoctorPriceDto`.
- [X] T077 [US3] Implement target eligibility query logic in `MediBridge.Services/Services/CampaignWorkflowService.cs` or a private helper file `MediBridge.Services/Services/CampaignTargetEligibilityService.cs` so only approved, active, non-deleted doctors with positive `PricePerMessage` appear in previews.
- [X] T078 [US3] Implement `CampaignWorkflowService.PreviewTargetsAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to verify owning company campaign access, compute eligible doctor count, use active platform fee policy, calculate estimated total EGP cost, and return `TargetPreviewDto`.
- [X] T079 [US3] Update `CampaignWorkflowService.SubmitCampaignAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to create immutable `CampaignTarget` rows with doctor facts and `PricePerMessageSnapshot` from submission time.
- [X] T080 [US3] Add `AdminPricingController` in `MediBridge.APIs/Controllers/AdminPricingController.cs` with admin-only route `PUT api/admin/doctors/{doctorId}/price`, standard envelopes, and no direct persistence access.
- [X] T081 [US3] Add target preview route `GET api/company/campaigns/{campaignId}/target-preview` to `MediBridge.APIs/Controllers/CampaignsController.cs`, delegating to `ICampaignWorkflowService.PreviewTargetsAsync`.
- [X] T082 [US3] Update service registration in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` to register `IAdminPricingService`.
- [X] T083 [US3] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter DoctorPricing`; fix failures until US3 pricing contract tests pass.
- [X] T084 [US3] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter TargetPreview`; fix failures until target preview contract tests pass.
- [X] T085 [US3] Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter Pricing`; fix failures until US3 pricing integration tests pass.
- [X] T086 [US3] Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj --filter DoctorTargetEligibility`; fix failures until US3 eligibility unit tests pass.

**Checkpoint**: Admin pricing and company target preview work independently; newly approved doctors become targetable only after a valid positive price is set.

---

## Phase 6: User Story 4 - Admins Can Review Assets and Campaigns to Create Queues (Priority: P1)

**Goal**: Admins can approve/reject campaign assets, review submitted campaigns, approve campaigns to create deterministic queue rows exactly once, and reject campaigns to release reserved funds and avoid queue creation.

**Independent Test**: With seeded approved company wallet funds, priced doctor, draft campaign, uploaded asset, and submitted campaign, approve the asset, approve the campaign, verify queue rows, retry approval, and verify no duplicates.

**Manual Senior Review 2026-06-21**: Phase 6 is certified complete after root-cause remediation. Campaign and asset mutations use narrow SQL Server update locks; campaign-review idempotency is stored in a dedicated, campaign-scoped unique column; active campaign/doctor queue uniqueness is enforced by a filtered database index; revision resubmission atomically replaces target snapshots and requires a new submission key; tracked application settings contain no deployable credentials. Migration backfill, duplicate-data guards, concurrent moderation, revision replay/conflict behavior, queue invariants, and the full solution regression suite were verified.

### Tests for User Story 4

- [X] T087 [P] [US4] Add contract tests for `POST /api/admin/campaign-assets/{assetId}/review` in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignAssetReviewContractTests.cs` verifying approve/reject envelope, required reason for reject, 401, 403, and 404.
- [X] T088 [P] [US4] Add contract tests for `POST /api/admin/campaigns/{campaignId}/review` in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs` verifying approve/reject/changes-requested envelope, idempotency header, 400 invalid transition, 401, 403, 404, and 409 replay conflict.
- [X] T089 [P] [US4] Add contract tests for `GET /api/admin/campaigns/{campaignId}/queue` in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignQueueContractTests.cs` verifying admin queue row envelope and 403 for company/doctor tokens.
- [X] T090 [P] [US4] Add integration test `AdminAssetApproval_AllowsCampaignSubmission` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminAssetReviewWorkflowTests.cs` verifying admin asset approval makes the company submission endpoint eligible when priced targets and sufficient wallet funds exist.
- [X] T091 [P] [US4] Add integration test `AdminCampaignApproval_CreatesQueueRowsOnceAndIsIdempotent` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalQueueTests.cs` verifying one queue row per target doctor and no duplicates on retry.
- [X] T092 [P] [US4] Add integration test `AdminCampaignRejection_ReleasesReservedFundsAndCreatesNoQueueRows` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRejectionReleaseTests.cs` verifying reserved balance returns to available and queue count is zero.
- [X] T093 [P] [US4] Add integration test `AdminQueueRows_AreOrderedBySubmissionTimeThenQueueId` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminQueueOrderingWorkflowTests.cs` verifying queued rows are returned in `QueuedAtUtc ASC, Id ASC` order without activating rows or evaluating daily delivery limits.
- [X] T094 [P] [US4] Add unit tests for campaign review transition rules in `tests/unit/MediBridge.UnitTests/Campaigns/CampaignReviewTransitionTests.cs` covering valid approve/reject/changes-requested states, required reasons, idempotent approval, and invalid transitions.

### Implementation for User Story 4

- [X] T095 [US4] Implement `AdminCampaignReviewService.ReviewAssetAsync` in `MediBridge.Services/Services/AdminCampaignReviewService.cs` to update `StoredFile.ReviewStatus`, `ReviewedAtUtc`, `ReviewedByAdminId`, `ReviewReason`, and append audit evidence for approve/reject decisions.
- [X] T096 [US4] Complete `CampaignWorkflowService.SubmitCampaignAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to validate approved asset, target count, wallet sufficiency, reserve company funds with `WalletTransactionType.Reserve`, create wallet ledger entries, move campaign to `PendingReview`, and commit atomically.
- [X] T097 [US4] Implement `AdminCampaignReviewService.ReviewCampaignAsync` in `MediBridge.Services/Services/AdminCampaignReviewService.cs` to append `CampaignReviewHistory`, approve/reject/changes-requested submitted campaigns, enforce idempotency key replay rules, and return `CampaignReviewResultDto`.
- [X] T098 [US4] Implement queue creation inside `AdminCampaignReviewService.ReviewCampaignAsync` for approval: create exactly one `DoctorMessageQueue` per `CampaignTarget`, set `QueuedAtUtc` from campaign submission time or review-approved fallback, and prevent duplicate active rows for `CampaignId + DoctorId`.
- [X] T099 [US4] Implement campaign rejection and changes-requested release behavior in `MediBridge.Services/Services/AdminCampaignReviewService.cs`: release uncharged reserved company funds, cancel pending queue rows if any, retain review reason, and keep all effects atomic.
- [X] T100 [US4] Implement `AdminCampaignReviewService.GetQueueRowsAsync` in `MediBridge.Services/Services/AdminCampaignReviewService.cs` returning doctor-level `QueueRowDto` for admin only and ordering by `QueuedAtUtc ASC`, then queue id.
- [X] T101 [US4] Add `AdminCampaignsController` in `MediBridge.APIs/Controllers/AdminCampaignsController.cs` with admin-only routes `POST api/admin/campaign-assets/{assetId}/review`, `POST api/admin/campaigns/{campaignId}/review`, and `GET api/admin/campaigns/{campaignId}/queue`.
- [X] T102 [US4] Update `MediBridge.APIs/Controllers/CampaignsController.cs` submit route to call the completed submission behavior and return validation, conflict, forbidden, and not-found envelopes through `WorkflowActionResultMapper`.
- [X] T103 [US4] Update service registration in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` to register `IAdminCampaignReviewService`.
- [X] T104 [US4] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter AdminCampaign`; fix failures until US4 admin campaign contract tests pass.
- [X] T105 [US4] Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter AdminCampaign`; fix failures until US4 review/queue integration tests pass.
- [X] T106 [US4] Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj --filter CampaignReview`; fix failures until US4 transition unit tests pass.

**Checkpoint**: Admin asset review, campaign review, fund release, and queue creation work independently with deterministic queue behavior and idempotent approval.

---

## Phase 7: User Story 5 - End-to-End HTTP Smoke Workflow Completes (Priority: P2)

**Goal**: A tester can complete the full workflow using only public secured HTTP requests, and role-specific queue visibility is correct: admins see doctor-level rows, companies see aggregate counts only.

**Independent Test**: Starting from approved company, approved doctor, and admin accounts, use HTTP to fund the wallet, set doctor pricing, create draft campaign, upload and approve asset, submit and approve campaign, verify admin queue rows, verify company queue summary, and verify all responses use the standard envelope.

**Manual Senior Review 2026-06-23**: Phase 7 is complete. Manual review confirms Onion Architecture boundaries, thin role-gated controllers, service-owned identity/approval and campaign-ownership checks, repository-only EF Core access, cancellation propagation, standard response envelopes, aggregate-only company queue visibility, and no payment-provider integration surface. Verification passed for the `WalletCampaign` integration suite and `QueueSummary` contract suite, and the full solution builds with zero warnings and zero errors.

### Tests for User Story 5

- [X] T107 [P] [US5] Add full smoke test `FullWalletCampaignWorkflow_CompletesThroughPublicHttp` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignEndToEndSmokeTests.cs` following every step in `specs/006-wallet-campaign-workflow/quickstart.md` and asserting the complete workflow finishes in under 5 minutes for normal smoke-test fixtures.
- [X] T108 [P] [US5] Add security smoke test `WorkflowRoleVisibility_EnforcesCompanyAdminDoctorBoundaries` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignRoleVisibilityTests.cs` verifying company cannot inspect doctor-level queue rows, doctor cannot manage company wallet/campaign, and company cannot access another company's campaign summary.
- [X] T109 [P] [US5] Add envelope smoke test `WalletCampaignWorkflow_AllResponsesUseStandardEnvelope` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignEnvelopeTests.cs` sampling success, validation failure, unauthorized, forbidden, not-found, and conflict cases.
- [X] T110 [P] [US5] Add no-provider smoke test `MockPaymentWorkflow_DoesNotExposeProviderIntegrationSurface` in `tests/integration/MediBridge.IntegrationTests/Workflow/MockPaymentNoProviderTests.cs` verifying no redirect URL, webhook URL, callback URL, provider credential, provider configuration value, or external provider status appears in responses or persisted mock payment metadata.
- [X] T111 [P] [US5] Add contract test `CompanyQueueSummary_HidesDoctorLevelDetails` in `tests/contract/MediBridge.ContractTests/Campaigns/CompanyQueueSummaryContractTests.cs` verifying `GET /api/company/campaigns/{campaignId}/queue-summary` returns aggregate counts and no `doctorId` field.

### Implementation for User Story 5

- [X] T112 [US5] Implement `CampaignWorkflowService.GetQueueSummaryAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs` to verify company ownership and return aggregate queued/activated/cancelled/expired counts without doctor identifiers.
- [X] T113 [US5] Add `GET api/company/campaigns/{campaignId}/queue-summary` route to `MediBridge.APIs/Controllers/CampaignsController.cs` returning `QueueSummaryDto` in the standard envelope and forbidding non-owner companies.
- [X] T114 [US5] Harden every new controller in `MediBridge.APIs/Controllers/CompanyWalletController.cs`, `MediBridge.APIs/Controllers/CampaignsController.cs`, `MediBridge.APIs/Controllers/AdminPricingController.cs`, and `MediBridge.APIs/Controllers/AdminCampaignsController.cs` so no action returns raw DTOs, strings, stack traces, or non-envelope error payloads.
- [X] T115 [US5] Add route and service registration verification tests in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignEndpointRegistrationTests.cs` proving every path in `contracts/wallet-campaign-workflow-api.yaml` is reachable with the expected auth behavior.
- [X] T116 [US5] Update `specs/006-wallet-campaign-workflow/quickstart.md` only if endpoint names changed during implementation; keep the quickstart aligned with actual route paths and response fields.
- [X] T117 [US5] Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj --filter WalletCampaign`; fix failures until full smoke, role visibility, envelope, and no-provider tests pass.
- [X] T118 [US5] Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj --filter QueueSummary`; fix failures until company queue summary contract tests pass.

**Checkpoint**: Full workflow is demonstrable end to end by HTTP and meets the graduation-project mock payment constraint.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Clean up, verify architecture, run the complete suite, and make the implementation easy to review.

**Manual Senior Review 2026-06-23**: Phase 8 is complete. Manual review found no critical or important findings and confirmed Onion Architecture boundaries, thin HTTP-only controllers, service-owned identity/approval and workflow orchestration, repository-only EF Core access, shared transaction atomicity for company approval and wallet creation, cancellation-token propagation, SQL Server update-lock ordering, idempotency and uniqueness enforcement, safe validation envelopes, and absence of payment-provider integration. Verification passed for formatting, the full solution build, all unit/contract/integration suites, and focused identity/concurrency coverage.

- [X] T119 [P] Add architecture boundary test `WalletCampaignControllerBoundaryTests` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignControllerBoundaryTests.cs` proving new controllers do not reference `MediBridgeDbContext`, EF Core namespaces, or Repository implementations.
- [X] T120 [P] Add service boundary test `WalletCampaignServiceBoundaryTests` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignServiceBoundaryTests.cs` proving `MediBridge.Services` does not reference EF Core infrastructure types or API controller types.
- [X] T121 [P] Add scope guard test `WalletCampaignScopeGuardTests` in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignScopeGuardTests.cs` scanning changed source for forbidden payment-provider markers: `Paymob`, `Stripe`, `Webhook`, `CallbackUrl`, `RedirectUrl`, `ApiKey`, `ProviderSecret`, and `PaymentGateway`.
- [X] T122 [P] Update API documentation comments in `MediBridge.APIs/Controllers/CompanyWalletController.cs`, `MediBridge.APIs/Controllers/CampaignsController.cs`, `MediBridge.APIs/Controllers/AdminPricingController.cs`, and `MediBridge.APIs/Controllers/AdminCampaignsController.cs` so Swagger summaries match `specs/006-wallet-campaign-workflow/contracts/wallet-campaign-workflow-api.yaml`.
- [X] T123 [P] Update `AGENTS.md` manual additions only if implementation discovers a durable command or convention for this workflow; do not rewrite auto-generated sections.
- [X] T124 Run `dotnet build MediBridge.slnx` from repository root `D:\My Project\MediBridge Project\MediBridge` and fix compile errors in all projects referenced by `MediBridge.slnx`.
- [X] T125 Run `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj` and fix all unit test failures.
- [X] T126 Run `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj` and fix all contract test failures.
- [X] T127 Run `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj` and fix all integration test failures.
- [X] T128 Run the complete quickstart workflow from `specs/006-wallet-campaign-workflow/quickstart.md` against the local API, record elapsed time, verify it completes in under 5 minutes for normal smoke-test fixtures, and record evidence in the final implementation report.
- [X] T129 Review all changed files for the constitution: Core has no EF/API references, Repository owns EF, Services own business logic, APIs are HTTP-only, wallet/queue determinism is explicit, delivery activation/daily-limit/delivered-message expiry remain outside this feature, all responses use standard envelope, and global exception middleware remains the error boundary.
- [X] T130 Review all new validation messages for safety: they must be actionable but must not expose stack traces, SQL details, tokens, provider secrets, raw request bodies, raw response bodies, or another user's private/financial data.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies. Start here.
- **Phase 2 Foundational**: Depends on Phase 1. Blocks every user story.
- **Phase 3 US1 MVP**: Depends on Phase 2. Recommended first implementation slice.
- **Phase 4 US2**: Depends on Phase 2. Can be developed after US1 or in parallel by another implementer, but full submission funding will use US1 wallet behavior.
- **Phase 5 US3**: Depends on Phase 2. Can be developed after US1 or in parallel by another implementer, but campaign submission later needs priced doctors.
- **Phase 6 US4**: Depends on Phase 2 and is easiest after US1, US2, and US3 because it needs funded wallet, approved asset, and priced target data.
- **Phase 7 US5**: Depends on US1 through US4 because it validates the full public HTTP workflow.
- **Phase 8 Polish**: Depends on all desired user stories.

### User Story Dependencies

- **US1 Approved Companies Can Fund Campaigns**: Independent MVP after foundation.
- **US2 Companies Can Prepare a Reviewable Campaign**: Independently testable for draft, asset upload, ownership, and blocked submission after foundation.
- **US3 Admins Can Unlock Eligible Doctor Targeting**: Independently testable for pricing and target preview after foundation.
- **US4 Admins Can Review Assets and Campaigns to Create Queues**: Functionally uses US1 wallet reservation, US2 campaign/assets, and US3 priced targets; integration tests may seed prerequisites if implemented separately.
- **US5 End-to-End HTTP Smoke Workflow Completes**: Requires US1, US2, US3, and US4.

### Within Each User Story

- Write tests before implementation tasks in that story phase.
- Run story-specific tests before moving to the next story.
- Services must be implemented before controllers.
- Controllers must call services only; do not put wallet, pricing, campaign, queue, or persistence decisions in controllers.
- Atomic business actions must use `IDomainUnitOfWork.ExecuteInTransactionAsync`.

---

## Parallel Execution Examples

### Phase 2 Parallel Work

```text
Task: "T020 Create wallet/payment DTOs"
Task: "T021 Create campaign DTOs"
Task: "T022 Create pricing DTOs"
Task: "T024 Create wallet validator"
Task: "T025 Create campaign validators"
Task: "T026 Create pricing validator"
```

### US1 Parallel Test Writing

```text
Task: "T037 Company wallet contract tests"
Task: "T038 Mock checkout contract tests"
Task: "T039 Wallet self-repair integration test"
Task: "T040 Top-up ledger integration test"
Task: "T041 Idempotency replay/conflict integration test"
Task: "T042 Wallet service unit tests"
```

### US2 Parallel Test Writing

```text
Task: "T054 Draft campaign contract tests"
Task: "T055 Asset upload contract tests"
Task: "T056 Draft and asset integration test"
Task: "T057 Ownership integration test"
Task: "T058 Submission validation integration test"
Task: "T059 Campaign validation unit tests"
```

### US3 Parallel Test Writing

```text
Task: "T070 Doctor pricing contract tests"
Task: "T071 Target preview contract tests"
Task: "T072 Price history integration test"
Task: "T073 Invalid price integration test"
Task: "T074 Price snapshot integration test"
Task: "T075 Eligibility unit tests"
```

### US4 Parallel Test Writing

```text
Task: "T087 Asset review contract tests"
Task: "T088 Campaign review contract tests"
Task: "T089 Admin queue contract tests"
Task: "T090 Asset approval integration test"
Task: "T091 Approval queue idempotency integration test"
Task: "T092 Rejection fund release integration test"
Task: "T093 Queue ordering workflow test"
Task: "T094 Review transition unit tests"
```

### US5 Parallel Test Writing

```text
Task: "T107 Full workflow smoke test"
Task: "T108 Role visibility smoke test"
Task: "T109 Envelope smoke test"
Task: "T110 No-provider smoke test"
Task: "T111 Queue summary contract test"
```

---

## Implementation Strategy

### MVP First: US1 Only

1. Complete Phase 1.
2. Complete Phase 2.
3. Complete Phase 3 US1.
4. Stop and validate company wallet query, mock checkout, idempotency replay, conflict replay, payment persistence, wallet transaction, ledger, audit, and standard envelopes.
5. Demo wallet funding before touching campaign workflows.

### Incremental Delivery

1. US1: company wallet funding.
2. US2: company draft campaign and asset upload.
3. US3: admin doctor pricing and target preview.
4. US4: admin moderation and queue creation.
5. US5: full HTTP smoke workflow and role visibility.

### Quality Gates Before Marking Complete

1. `dotnet build MediBridge.slnx`
2. `dotnet test tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj`
3. `dotnet test tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj`
4. `dotnet test tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`
5. Manual or automated quickstart execution from `specs/006-wallet-campaign-workflow/quickstart.md`

### Non-Negotiable Constraints

- Do not integrate Paymob, Stripe, or any real payment provider.
- Do not add redirects to payment pages.
- Do not add webhook or callback endpoints.
- Do not add payment-provider credentials or configuration.
- Do not expose doctor-level queue rows to companies.
- Do not allow null, zero, negative, or over-precise admin doctor prices.
- Do not round monetary input; reject invalid precision.
- Do not create duplicate wallet credits or queue rows on replay.
- Do not bypass repositories or unit of work from services/controllers.
- Do not return non-envelope responses from new endpoints.
