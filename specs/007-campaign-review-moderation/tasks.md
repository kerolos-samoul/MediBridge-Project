# Tasks: Phase 6 Campaign Review & Moderation

**Input**: Design documents from `/specs/007-campaign-review-moderation/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/campaign-review-moderation-api.yaml](./contracts/campaign-review-moderation-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Include tests. The implementation plan explicitly requires contract, integration, and unit coverage, and the feature touches security, moderation state transitions, queue creation, and auditability.

**Clean Recovery Status (2026-06-30)**: Reconstructed on `codex/007-campaign-review-moderation-recovery` from the approved 007 artifacts against the PR #8 development baseline. Shared files were hunk-merged; the EF migration was regenerated from the current model; the existing direct campaign submission and draft routes were preserved; settings, E2E runtime data, wallet/payment implementation changes, and unrelated 006 files were excluded. Verification completed with a warning-free solution build, no pending EF model changes, and full unit, contract, and integration suites passing. Changes remain unstaged and uncommitted pending approval.

**Constitution Note**: Keep Onion layering strict. Put domain state/contracts in `MediBridge.Core`, SQL Server EF Core persistence in `MediBridge.Repository`, use-case orchestration in `MediBridge.Services`, and HTTP-only controllers in `MediBridge.APIs`. All secured endpoints must use JWT role/ownership authorization and return the standard envelope `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.

**Important Existing-Code Context For Executor**:

- Existing related files already present: `MediBridge.APIs/Controllers/AdminCampaignsController.cs`, `MediBridge.APIs/Controllers/CampaignsController.cs`, `MediBridge.Services/Services/AdminCampaignReviewService.cs`, `MediBridge.Services/Services/CampaignWorkflowService.cs`, `MediBridge.Services/Services/CampaignReviewTransitionPolicy.cs`, `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`, `MediBridge.Repository/Repositories/Files/StoredFileRepository.cs`, `MediBridge.Repository/Repositories/Messaging/MessageQueueRepository.cs`.
- Current code may already implement parts of the broader `006-wallet-campaign-workflow`; do not duplicate services/controllers. Extend and harden the existing files named in each task.
- The clarified decision names the company-return state `RevisionRequired`. Existing code may still use `ChangesRequested` or return campaigns to `Draft`; this feature must make the API and persisted lifecycle unambiguous.
- Initial submission and revision resubmission require at least one active campaign media asset with `ReviewStatus = Pending` or `Approved`. Campaign approval requires at least one active separately Approved campaign media asset and must not approve pending/rejected media itself.
- `RevisionRequired` must have a real owning-company content update operation. The current application has no campaign update endpoint; implementing only asset replacement does not satisfy this feature.
- The current Phase 5 code reserves funds in `CampaignWorkflowService.SubmitCampaignAsync` and releases funds in `AdminCampaignReviewService`. This feature intentionally replaces that behavior: submission/resubmission validate affordability only, and no submission or moderation path may mutate wallets or create wallet transaction/ledger evidence.
- Add explicit `Campaign.SubmittedAtUtc`; do not continue using mutable `UpdatedAtUtc` as the moderation or queue submission-order timestamp.
- Reuse the existing `IFileAccessService` for protected review-file URLs. Never map `StoredFile.StorageKey` into a service DTO or API response.
- ASP.NET authorization rejects anonymous/non-admin requests before `AdminCampaignReviewService` runs. Verify those denials are non-mutating; do not attempt to create service-layer audit events for requests that never reach the service.

**Inherited Phase 5 Tests That Must Be Updated, Not Preserved**:

- `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRevisionWorkflowTests.cs` currently expects reserve-release-reserve behavior and `ChangesRequested -> Draft`; rewrite it for `RevisionRequired`, explicit content update, refreshed `SubmittedAtUtc`, and zero wallet mutations.
- `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRejectionReleaseTests.cs` currently expects a Release transaction; replace that expectation with unchanged wallet balances and zero moderation-created wallet records.
- `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignConcurrentReviewTests.cs` currently branches on wallet release; keep the single-winner assertion but require the seeded wallet and wallet record counts to remain unchanged for either winning decision.
- `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalQueueTests.cs` currently seeds a pending campaign without an approved active media asset; seed an Approved active campaign media asset before expecting approval success.
- `tests/contract/MediBridge.ContractTests/WalletCampaignContractTestBase.cs` and `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignWorkflowTestHelpers.cs` must create active Pending or Approved media deliberately for each scenario and must never hide required setup inside unrelated helpers.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel after earlier dependencies in the same phase are complete.
- **[Story]**: User story label for traceability.
- Every task below uses the required checklist format and includes a concrete file path.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare the implementation branch and make the feature contract visible to tests and implementers.

**Status**: Complete — manually reviewed on 2026-06-28 for contract alignment, architecture, identity/approval guards, and async safety.

- [X] T001 Verify the active branch is `007-campaign-review-moderation`; run `git status --short`; do not edit, stage, delete, or revert unrelated existing changes; stop only if an unrelated change makes a named task file impossible to edit safely
- [X] T002 [P] Add route constants for company campaign update, non-financial submit/resubmit, admin pending list, admin review detail, admin review decision, admin queue rows, and company review outcome from `specs/007-campaign-review-moderation/contracts/campaign-review-moderation-api.yaml` to `tests/contract/MediBridge.ContractTests/WalletCampaignWorkflowRoutes.cs`
- [X] T003 [P] Add explicit integration fixture helpers for Draft and RevisionRequired campaigns, `SubmittedAtUtc`, target snapshots, and active campaign media in Pending/Approved/Rejected/Deleted/Superseded states in `tests/integration/MediBridge.IntegrationTests/Workflow/WalletCampaignWorkflowTestHelpers.cs`; each helper must state whether it creates wallet rows and must default to no wallet mutation
- [X] T004 [P] Add matching contract-test helpers for Draft, RevisionRequired, PendingReview, active Pending/Approved media, target snapshots, and explicit submission timestamps in `tests/contract/MediBridge.ContractTests/WalletCampaignContractTestBase.cs`
- [X] T005 [P] Review `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` and `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs` to confirm existing campaign review services, validators, repositories, and authorization policies are registered before adding new registrations

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared domain lifecycle, DTO, repository, and validator support used by every user story.

**Critical**: Complete this phase before any user story implementation task. These tasks define names, state transitions, and shared read models that all later work depends on.

**Review Status**: Complete — manually reviewed and remediated on 2026-06-28 for legacy lifecycle migration safety, authoritative timestamps, canonical decisions, idempotent replay, architecture, and async safety.

- [X] T006 Add `RevisionRequired` to `CampaignStatus` without reusing the numeric value of `Draft`, `Rejected`, or any existing status in `MediBridge.Core/Enums/Phase3DomainEnums.cs`
- [X] T007 Keep backward-compatible parsing for existing `"ChangesRequested"` input while making `"RevisionRequired"` the canonical public decision/status name in `MediBridge.Services/Services/CampaignReviewTransitionPolicy.cs`
- [X] T008 Add nullable `SubmittedAtUtc` to `Campaign`, add append-only `CampaignSubmissionAttempt` with campaign id/key/time/target count/estimated cost/currency, and add `PriorStatus`/`ResultingStatus` enum-backed fields to review history in `MediBridge.Core/Entities/Campaigns/Campaign.cs`, `MediBridge.Core/Entities/Campaigns/CampaignSubmissionAttempt.cs`, and `MediBridge.Core/Entities/Campaigns/CampaignReviewHistory.cs`; do not repurpose creation/update timestamps or wallet transactions
- [X] T009 Add one SQL Server EF Core migration in `MediBridge.Repository/Migrations/` that adds nullable `Campaigns.SubmittedAtUtc`, creates `CampaignSubmissionAttempts` with unique `(CampaignId, IdempotencyKey)` plus current-attempt `(CampaignId, SubmittedAtUtc, Id)` index, adds review-history prior/resulting status columns, and adds pending-list `(Status, SubmittedAtUtc, Id)` index; adding enum member RevisionRequired requires no standalone column because status is stored as an integer
- [X] T010 Add `DbSet<CampaignSubmissionAttempt>` to `MediBridge.Repository/Data/MediBridgeDbContext.cs` and update `MediBridge.Repository/Configurations/Campaigns/CampaignConfigurations.cs` to map `SubmittedAtUtc`, configure submission attempts with `EstimatedCost decimal(18,2)`, `Currency` max length 3, required key max length 128, Campaign foreign key, and both indexes, map prior/resulting status as integers, and configure the pending-list index
- [X] T011 Add provider-neutral pending-list/detail projection records in `MediBridge.Core/Interfaces/Campaigns/CampaignReviewReadModels.cs`, then extend `ICampaignRepository` with bounded pending paging, pending detail, latest review history, review idempotency, submission-attempt add/current/key lookup, and queue-count/read methods in `MediBridge.Core/Interfaces/Campaigns/ICampaignRepository.cs`; no contract may reference EF Core or Services DTO types
- [X] T012 Implement the new repository methods in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`: filter active PendingReview; order `SubmittedAtUtc ASC, Id ASC`; apply `Skip/Take`; project campaign/company/target/readiness facts without N+1; use `AsNoTracking` for reads; add submission attempts through DbSet and query unique campaign/key plus latest attempt; storage-key suppression remains a service DTO responsibility
- [X] T013 Extend `IStoredFileRepository` with methods that list active reviewable campaign files (`StorageState = Active`, not deleted/superseded, review status Pending or Approved), test for at least one active Approved campaign media asset, and return optional review-file metadata in `MediBridge.Core/Interfaces/Files/IStoredFileRepository.cs`
- [X] T014 Implement the active reviewable/approved campaign-file queries in `MediBridge.Repository/Repositories/Files/StoredFileRepository.cs`; always filter owner type Campaign, matching campaign id, expected purpose, Active storage state, `DeletedAtUtc = null`, and `SupersededByFileId = null`
- [X] T015 Extend `IMessageQueueRepository` with duplicate-safe active queue checks and campaign-row ordering by `CampaignSubmittedAtUtc`, `QueuedAtUtc`, then stable `Id` in `MediBridge.Core/Interfaces/Messaging/IMessageQueueRepository.cs`
- [X] T016 Implement duplicate-safe active queue checks and deterministic ordering by `CampaignSubmittedAtUtc ASC, QueuedAtUtc ASC, Id ASC` in `MediBridge.Repository/Repositories/Messaging/MessageQueueRepository.cs`; do not fall back to mutable campaign update time
- [X] T017 Add `UpdateCampaignRequestDto`, `PendingCampaignSummaryDto`, `PendingCampaignPageDto`, `CampaignReviewDetailDto`, `ReviewFileDto`, and `CompanyReviewOutcomeDto` in `MediBridge.Services/DTOs/Campaigns/`; `ReviewFileDto` must expose `fileId`, metadata, review status, `accessUrl`, and `accessExpiresAtUtc`, never `StorageKey`
- [X] T018 Update `CampaignSubmissionDto` to replace `reservedAmount` with `estimatedCost`, `currency`, and `submittedAtUtc`, and update `CampaignReviewResultDto` to include `decisionTimeUtc` and `canResubmit` while preserving the remaining existing JSON names in `MediBridge.Services/DTOs/Campaigns/CampaignSubmissionDto.cs` and `MediBridge.Services/DTOs/Campaigns/CampaignReviewResultDto.cs`
- [X] T019 Update `ReviewDecisionRequestDto` to accept `Decision`, `Reason`, and optional `Notes`, and keep JSON binding compatible with existing callers in `MediBridge.Services/DTOs/Campaigns/ReviewDecisionRequestDto.cs`
- [X] T020 Update `ReviewDecisionRequestDtoValidator` so `Rejected` and `RevisionRequired` require a trimmed non-empty public reason, `Approved` allows no reason, `ChangesRequested` is accepted only as a legacy input alias for `RevisionRequired`, and reason/notes lengths match the OpenAPI contract in `MediBridge.Services/Validators/Campaigns/ReviewDecisionRequestDtoValidator.cs`; add `UpdateCampaignRequestDtoValidator` with title/description/clinical-info limits matching EF mapping in `MediBridge.Services/Validators/Campaigns/UpdateCampaignRequestDtoValidator.cs`
- [X] T021 Add unit tests for canonical `RevisionRequired`, legacy `ChangesRequested` input aliasing, required public reason, notes-aware replay equivalence, final Rejected status, and Draft/RevisionRequired-only company edit-state rules in `tests/unit/MediBridge.UnitTests/Campaigns/CampaignReviewTransitionTests.cs` and `tests/unit/MediBridge.UnitTests/Campaigns/CampaignDraftValidationTests.cs`
- [X] T022 Add migration/schema tests for unique RevisionRequired enum value, nullable `SubmittedAtUtc`, `CampaignSubmissionAttempts` columns/decimal precision/foreign key/unique key, pending and current-attempt indexes, and PriorStatus/ResultingStatus round-trip persistence in `tests/integration/MediBridge.IntegrationTests/Campaigns/CampaignReviewMigrationTests.cs`

**Checkpoint**: Domain names, repository contracts, DTO contracts, and validator behavior are ready. No user-story endpoint work should begin before this checkpoint passes locally.

---

## Phase 3: User Story 1 - Admins Review Submitted Campaigns Before Delivery (Priority: P1) MVP

**Goal**: Admins can list pending campaigns and open a safe review detail package before deciding.

**Review Status**: Complete — manually reviewed on 2026-06-28 for Onion layering, approved-admin identity enforcement, deterministic bounded paging, protected file access, response-contract fidelity, cancellation propagation, and EF Core async safety.

**Independent Test**: Submit a campaign as an approved company, sign in as admin, list pending campaigns, and open review detail. The pending list includes only `PendingReview` in `SubmittedAtUtc`, then campaign-id order and meets the warmed 20-item performance target. Detail includes company identity, campaign text, target summary, active Pending/Approved media, working short-lived signed access, optional files when present, and no raw storage keys/provider credentials.

### Tests for User Story 1

- [X] T023 [P] [US1] Add contract tests for `GET /api/admin/campaigns/pending-review` success envelope, `descriptionSummary`, fixed `PendingReview` status, readiness issues, pagination fields, `PageSize <= 100`, unauthorized envelope, and non-admin forbidden envelope in `tests/contract/MediBridge.ContractTests/Admin/AdminPendingCampaignReviewContractTests.cs`
- [X] T024 [P] [US1] Add contract tests for `GET /api/admin/campaigns/{campaignId}/review-detail` success envelope with `reviewableMediaAssets[*].accessUrl/accessExpiresAtUtc`, not-found, unauthorized, forbidden, and storage-provider 503 envelopes in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewDetailContractTests.cs`
- [X] T025 [P] [US1] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs` proving Draft, Approved, Rejected, RevisionRequired, Active, Paused, Cancelled, Completed, and deleted campaigns are excluded; pending campaigns order by `SubmittedAtUtc ASC, Id ASC`; page 1/page 2 do not duplicate or skip equal-time rows; after one warm-up request a 20-item page completes within 1 second
- [X] T026 [P] [US1] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewDetailTests.cs` proving detail includes active Pending and Approved media plus optional active files; each returned signed URL is HTTPS and expires within the configured lifetime; deleted/superseded files are absent; serialized responses contain no `StorageKey`, provider credential, raw path, or internal admin note
- [X] T027 [P] [US1] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewReadinessTests.cs` proving Pending media satisfies submission-package visibility but produces a missing-approved-media readiness issue, while blank campaign text, no target snapshot, and no active reviewable media produce distinct actionable readiness issues

### Implementation for User Story 1

- [X] T028 [US1] Add `ListPendingCampaignsAsync(adminUserId, pageNumber, pageSize, cancellationToken)` and `GetReviewDetailAsync(adminUserId, campaignId, cancellationToken)` signatures to `IAdminCampaignReviewService` in `MediBridge.Services/Interfaces/IAdminCampaignReviewService.cs`; both return service DTOs only
- [X] T029 [US1] Implement `ListPendingCampaignsAsync` in `AdminCampaignReviewService` using approved-Admin validation and the single bounded repository page projection; map description summary, target count, approved-media flag, and readiness issues; preserve repository `SubmittedAtUtc, Id` order; do not loop and issue target/file queries per campaign in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T030 [US1] Implement `GetReviewDetailAsync` in `AdminCampaignReviewService` using approved-Admin validation, PendingReview-only campaign lookup, company identity, active reviewable/optional file metadata, target count, and readiness issues; inject and call `IFileAccessService.GetSignedAccessAsync(adminUserId, fileId)` for every returned file to populate URL/expiry; never map `StorageKey`; allow `FileStorageUnavailableException` to reach the existing 503 mapper in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T031 [US1] Add `GET api/admin/campaigns/pending-review` endpoint to `AdminCampaignsController` that accepts `PageNumber` and `PageSize`, delegates to the service, and wraps success/errors in the standard envelope in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`
- [X] T032 [US1] Add `GET api/admin/campaigns/{campaignId}/review-detail` endpoint to `AdminCampaignsController` that delegates to the service, returns only `CampaignReviewDetailDto` in the standard envelope, and maps storage-provider outage to the existing safe 503 envelope in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`
- [X] T033 [US1] Add Swagger response attributes for 200/401/403 on pending list and 200/401/403/404/503 on review detail in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`; response types must match `ApiEnvelope<PendingCampaignPageDto>` and `ApiEnvelope<CampaignReviewDetailDto>`
- [X] T034 [US1] Ensure repository/service pagination defaults to `PageNumber = 1`, `PageSize = 20`, and caps `PageSize` at 100 in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T035 [US1] Run `dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "AdminPendingCampaignReview|AdminCampaignReviewDetail"` and fix failures in the files touched by US1
- [X] T036 [US1] Run `dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminPendingCampaignList|AdminCampaignReviewDetail|AdminCampaignReviewReadiness"` and require passing exclusion, stable-order pagination, warmed performance, signed-access, leak-safety, and readiness tests before continuing

**Checkpoint**: US1 is independently demoable when an admin can list pending campaigns and inspect review detail safely.

---

## Phase 4: User Story 2 - Admins Approve Campaigns and Unlock Queue Creation (Priority: P1)

**Goal**: Admins can approve compliant pending campaigns; approval records history and creates deterministic pending queue rows exactly once.

**Independent Test**: Review a pending campaign with campaign text, target snapshot, and separately approved media; approve it once and replay approval with the same key. Campaign becomes `Approved`, one review row is appended, queue rows are created once per target, replay returns the original result, and media asset review status is unchanged.

**Review Status**: Complete — manually reviewed on 2026-06-29 for Onion architecture, approved-admin and owning-company approval guards, transaction atomicity, idempotent replay/conflict handling, deterministic queue timestamps/order, cancellation propagation, and absence of moderation wallet/delivery side effects. Company account/profile status is locked through approval commit to prevent concurrent suspension/deletion races; Phase 5 submission/resubmission coverage remains pending.

### Tests for User Story 2

- [X] T037 [P] [US2] Update contract tests so `POST /api/admin/campaigns/{campaignId}/review` accepts canonical `Approved` decision and returns `decisionTimeUtc`, `canResubmit = false`, and standard envelope in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs`
- [X] T038 [P] [US2] Add contract tests proving missing idempotency key returns 400, conflicting replay returns 409, unsupported decision returns 400, and stale/non-pending campaign returns 409 in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs`
- [X] T039 [P] [US2] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalPrerequisiteTests.cs` proving a campaign submitted with active Pending media appears in review detail but approval fails until one active media row is separately Approved; campaign approval never changes Pending/Rejected asset status; deleted/superseded Approved media does not satisfy approval
- [X] T040 [P] [US2] Add integration test proving approval fails when campaign text is blank or no target snapshots exist in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalPrerequisiteTests.cs`
- [X] T041 [P] [US2] Update `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalQueueTests.cs` to seed an active Approved campaign media asset, then prove approval creates exactly one active queue row per submitted target and no duplicate queue/history rows on identical replay
- [X] T042 [P] [US2] Add integration test proving queue row ordering by `CampaignSubmittedAtUtc`, `QueuedAtUtc`, then `Id` in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminQueueOrderingWorkflowTests.cs`
- [X] T043 [P] [US2] Add `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignModerationNoWalletEffectsTests.cs` proving initial submission, RevisionRequired resubmission, approval, rejection, and revision-required decisions leave company `AvailableBalance`/`ReservedBalance` and wallet transaction/ledger counts unchanged and create no delivery, charge, earning, expiry, or payout records; rewrite `AdminCampaignRejectionReleaseTests.cs` and the wallet branches in `AdminCampaignConcurrentReviewTests.cs` to the same invariant

### Implementation for User Story 2

- [X] T044 [US2] Update `CampaignReviewTransitionPolicy.ValidateNewDecision` to enforce only decision-level rules: current status must be PendingReview, Rejected/RevisionRequired require a public reason, and Rejected/Approved/RevisionRequired are the canonical result states; keep `ChangesRequested` as input alias only and do not pass campaign/file/repository objects into the policy in `MediBridge.Services/Services/CampaignReviewTransitionPolicy.cs`
- [X] T045 [US2] Update `ReviewCampaignAsync` approval path to load current campaign, company approval state, submitted targets, and active Approved campaign media before history/queue mutation; throw distinct actionable `WorkflowValidationException` messages for blank campaign text, no targets, no active Approved media, or unapproved/deleted company in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T046 [US2] Delete `ReleaseReservedFundsAndCancelQueuesAsync`, `AddCampaignLedgerEntryAsync`, financial-key generation, wallet imports, and every wallet/wallet-transaction/wallet-ledger call from `MediBridge.Services/Services/AdminCampaignReviewService.cs`; approval, rejection, revision, replay, and conflict paths must not read or lock wallet/delivery/payout repositories
- [X] T047 [US2] Update approval history creation to store `PriorStatus = PendingReview`, `ResultingStatus = Approved`, canonical decision, reason/notes, admin id, idempotency key, and decision timestamp in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T048 [US2] Update approval result mapping to include queued count, `decisionTimeUtc`, `canResubmit = false`, canonical `decision = Approved`, and `status = Approved` in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T049 [US2] Update `ApproveAndQueueAsync` to create missing queue rows only for submitted target snapshots and to preserve existing active queue rows without duplicates in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T050 [US2] Update queue-row creation to require non-null `Campaign.SubmittedAtUtc`, copy it to `CampaignSubmittedAtUtc`, set `QueuedAtUtc` and `CreatedAtUtc` to approval time, and never derive submission time from `UpdatedAtUtc` or approval time in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T051 [US2] Update replay handling to compare canonical decision plus normalized reason and notes, then return the stored prior/resulting status, stored decision timestamp, can-resubmit flag, and current queue count without appending history or queue rows in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T052 [US2] Update conflicting replay handling to compare decision, reason, and notes and return `WorkflowConflictException` without state mutation in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T053 [US2] Ensure `GET api/admin/campaigns/{campaignId}/queue` returns `campaignSubmittedAtUtc` and `queuedAtUtc` separately and orders by `CampaignSubmittedAtUtc`, `QueuedAtUtc`, then `Id` in `MediBridge.Services/Services/AdminCampaignReviewService.cs` and `MediBridge.Services/DTOs/Campaigns/QueueRowDto.cs`
- [X] T054 [US2] Run `dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "AdminCampaignReview"` and fix failures in US2 files
- [X] T055 [US2] Run `dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminCampaignApproval|AdminQueueOrdering|AdminCampaignModerationNoWalletEffects"` and fix failures in US2 files

**Checkpoint**: US2 is independently demoable when campaign approval creates deterministic queue rows once and has no wallet/delivery side effects.

---

## Phase 5: User Story 3 - Admins Reject or Return Campaigns With Reasons (Priority: P1)

**Goal**: Admins can reject final campaigns or return fixable campaigns as `RevisionRequired`; companies can see public outcomes and only revision-required campaigns can be edited/resubmitted.

**Independent Test**: Reject one pending campaign with a reason and return another for revision with a reason. Rejected campaign cannot be content-edited, asset-managed, resubmitted, approved, or queued. The owning company updates a RevisionRequired campaign through the explicit PUT endpoint, manages Pending/Rejected assets, resubmits it with refreshed targets and `SubmittedAtUtc`, retains prior history, and produces no wallet effects.

**Review Status**: Complete — manually reviewed on 2026-06-29 for Onion architecture, identity and approval revalidation, owning-company authorization, SQL lock and transaction boundaries, submission idempotency, async and cancellation safety, public outcome data minimization, and absence of moderation wallet or delivery side effects.

### Tests for User Story 3

- [X] T056 [P] [US3] Update contract tests so `POST /api/admin/campaigns/{campaignId}/review` accepts canonical `Rejected` and `RevisionRequired` decisions and returns `canResubmit` correctly in `tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs`
- [X] T057 [P] [US3] Add contract tests for `PUT /api/company/campaigns/{campaignId}` and `GET /api/company/campaigns/{campaignId}/review-outcome` covering success envelopes, validation, unauthorized, forbidden non-owner, not-found, and invalid-state conflict in `tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignReviewOutcomeContractTests.cs` and `tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignDraftContractTests.cs`
- [X] T058 [P] [US3] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRejectionFinalityTests.cs` proving Rejected campaigns deny the explicit content-update endpoint, asset upload, replacement, delete, submit, and admin approval; assert campaign fields, `SubmittedAtUtc`, history count, file rows, queue rows, wallet balances, wallet transactions, and wallet ledger rows remain unchanged
- [X] T059 [P] [US3] Rewrite `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRevisionWorkflowTests.cs` to prove: RevisionRequired content PUT succeeds for owner; PendingReview/non-owner PUT fails; asset operations allow RevisionRequired; resubmission accepts active Pending/Approved media, replaces targets, records a later `SubmittedAtUtc` and one append-only submission attempt, preserves review history, moves to PendingReview, and leaves wallets unchanged; identical current-key retry returns the stored attempt, different key while PendingReview conflicts, and historical-key reuse after another revision conflicts
- [X] T060 [P] [US3] Add integration test proving rejection and revision-required decisions require non-empty public reason and return standard 400 envelopes when reason is missing in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewReasonValidationTests.cs`
- [X] T061 [P] [US3] Add integration test proving company review outcome hides internal notes and exposes only status, public reason, decision time, `canEdit`, `canResubmit`, and queued count for owned campaigns in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReviewOutcomeTests.cs`

### Implementation for User Story 3

- [X] T062 [US3] Update `CampaignReviewTransitionPolicy` so `Rejected` maps to terminal `CampaignStatus.Rejected` and `RevisionRequired` maps to `CampaignStatus.RevisionRequired` in `MediBridge.Services/Services/CampaignReviewTransitionPolicy.cs`
- [X] T063 [US3] Update `ReviewCampaignAsync` rejection path to append history with `PriorStatus = PendingReview`, `ResultingStatus = Rejected`, public reason, internal notes, admin id, idempotency key, and one UTC timestamp; set campaign Rejected without creating/cancelling queue rows or reading/mutating wallets in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T064 [US3] Update `ReviewCampaignAsync` revision path to append history with `PriorStatus = PendingReview`, `ResultingStatus = RevisionRequired`, public reason, internal notes, admin id, key, and one UTC timestamp; set campaign RevisionRequired without creating/cancelling queue rows or reading/mutating wallets in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T065 [US3] Implement `UpdateCampaignAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs`: validate DTO, approved company and ownership, lock campaign, require Draft or RevisionRequired, trim/update title/description/clinical research info, set only `UpdatedAtUtc`, and preserve `SubmittedAtUtc` plus review history; refactor asset upload/replacement/delete state checks to the same Draft-or-RevisionRequired predicate and deny Rejected/other states
- [X] T066 [US3] Rewrite `SubmitCampaignAsync` in `MediBridge.Services/Services/CampaignWorkflowService.cs`: validate key; lock campaign; first handle append-only submission-attempt replay/conflict; accept new submission only from Draft or RevisionRequired; require text and one active Pending/Approved media file; replace target snapshots; calculate estimated EGP cost; lock/read wallet only for affordability; add no Reserve/Release transaction/ledger rows; set new `SubmittedAtUtc`/`UpdatedAtUtc`; move to PendingReview; append `CampaignSubmissionAttempt`; preserve review history; return stored/new `estimatedCost`, `currency`, and submission time
- [X] T067 [US3] Add `UpdateCampaignAsync` and `GetReviewOutcomeAsync` signatures to `ICampaignWorkflowService` in `MediBridge.Services/Interfaces/ICampaignWorkflowService.cs`; update signature uses `UpdateCampaignRequestDto`, and outcome remains an owning-company read
- [X] T068 [US3] Implement `GetReviewOutcomeAsync` using owning-company authorization, latest review history, queue count, and public-only mapping in `MediBridge.Services/Services/CampaignWorkflowService.cs`
- [X] T069 [US3] Add `PUT api/company/campaigns/{campaignId}` and `GET api/company/campaigns/{campaignId}/review-outcome` actions to `CampaignsController`; both must get caller id, delegate once to `ICampaignWorkflowService`, use standard envelopes, and contain no state/ownership business decisions in `MediBridge.APIs/Controllers/CampaignsController.cs`
- [X] T070 [US3] Add Swagger response attributes for campaign update 200/400/401/403/404/409 and review outcome 200/401/403/404 in `MediBridge.APIs/Controllers/CampaignsController.cs`; ensure the submit response type reflects `estimatedCost`, `currency`, and `submittedAtUtc`, not `reservedAmount`
- [X] T071 [US3] Run `dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "CompanyCampaignDraft|CompanyCampaignReviewOutcome|AdminCampaignReview"` and require passing update-route, outcome, canonical-decision, legacy-alias, and envelope tests
- [X] T072 [US3] Run `dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminCampaignRejectionFinality|AdminCampaignRevisionWorkflow|AdminCampaignReviewReasonValidation|CompanyCampaignReviewOutcome|CampaignSubmission"` and require passing edit-state, media-gate, refreshed-submission, final-rejection, ownership, and no-wallet-effect assertions

**Checkpoint**: US3 is independently demoable when rejected campaigns are terminal and revision-required campaigns can be corrected/resubmitted with clear company-visible reasons.

---

## Phase 6: User Story 4 - Review Decisions Remain Auditable (Priority: P2)

**Goal**: Every moderation decision is append-only, traceable, role-protected, and safe under stale/concurrent requests.

**Independent Test**: Apply approval, rejection, and revision-required decisions across multiple campaigns and revisions. Verify every decision records reviewer, decision, public reason/notes, prior status, resulting status, timestamp, and idempotency behavior. Verify non-admin attempts do not mutate state.

**Review Status**: Complete — manually reviewed on 2026-06-29 for Onion architecture, approved-admin identity and owning-company approval revalidation, append-only history, safe audit metadata, transactional locking and idempotency, stale/concurrent decision handling, cancellation propagation, and absence of moderation wallet/delivery side effects.

### Tests for User Story 4

- [X] T073 [P] [US4] Add unit tests for replay equivalence across decision, reason, and notes in `tests/unit/MediBridge.UnitTests/Campaigns/CampaignReviewTransitionTests.cs`
- [X] T074 [P] [US4] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewAuditHistoryTests.cs` proving history is append-only across RevisionRequired, owning-company update/resubmission, and second review; verify first and second records retain independent prior/resulting states, reasons, notes, reviewers, keys, and timestamps
- [X] T075 [P] [US4] Add integration tests in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewAuthorizationAuditTests.cs` proving anonymous/non-admin review requests are rejected by ASP.NET authorization with 401/403 envelopes before the service executes and do not append history/audit events or mutate campaign, queue, file, or wallet state; do not expect `AdminCampaignReviewService` to audit these policy denials
- [X] T076 [P] [US4] Rewrite `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignConcurrentReviewTests.cs` so concurrent conflicting decisions produce one final outcome and one winning history row; seed required active Approved media and targets; assert wallet balances and transaction/ledger counts remain unchanged regardless of winner
- [X] T077 [P] [US4] Add integration test proving audit event metadata for review decisions excludes JWTs, raw request bodies, provider credentials, raw storage keys, and internal stack traces in `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewAuditSafetyTests.cs`

### Implementation for User Story 4

- [X] T078 [US4] Update review-history insert code to always populate reviewer id, canonical decision, reason, notes, prior status, resulting status, idempotency key, and UTC timestamp in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T079 [US4] Update audit event creation for completed approval/rejection/revision-required decisions, idempotent replays, and service-detected conflicts using safe metadata only in `MediBridge.Services/Services/AdminCampaignReviewService.cs`; never include JWTs, raw bodies, storage keys, reasons/notes that are not required for the audit category, or policy-level authorization denials that occur before service invocation
- [X] T080 [US4] Ensure campaign row locking and review idempotency lookup happen inside one `IDomainUnitOfWork.ExecuteInTransactionAsync` block for every review decision in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T081 [US4] Ensure repository methods used by review decisions apply SQL Server update locks to the campaign row inside the domain transaction and do not lock wallet, wallet transaction, wallet ledger, delivery, payout, or unrelated company rows in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`
- [X] T082 [US4] Update stale-decision error mapping so already-reviewed, cancelled, deleted, or non-pending campaigns return conflict/not-found envelopes without appending review history in `MediBridge.Services/Services/AdminCampaignReviewService.cs`
- [X] T083 [US4] Run `dotnet test tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "CampaignReviewTransition"` and fix failures in US4 files
- [X] T084 [US4] Run `dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminCampaignReviewAudit|AdminCampaignConcurrentReview"` and fix failures in US4 files

**Checkpoint**: US4 is complete when audit history is append-only, safe, and concurrency-resistant.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify the full feature end to end, remove ambiguity, and leave the repo ready for implementation review.

**Review Status**: Complete — manually reviewed on 2026-06-29 for Onion architecture, approved Admin/Company identity revalidation, ownership and role authorization, shared transaction and SQL locking behavior, idempotent approval/rejection/revision decisions, cancellation propagation, deterministic queue creation, response-envelope consistency, protected-file handling, and absence of moderation wallet/delivery/payout side effects.

- [X] T085 [P] Reconcile `specs/007-campaign-review-moderation/quickstart.md` against implemented routes and DTOs; verify it still documents company PUT update, non-financial submit/resubmit, Pending/Approved submission media, Approved campaign-approval media, signed access, explicit submission ordering, and zero wallet effects
- [X] T086 [P] Reconcile `specs/007-campaign-review-moderation/contracts/campaign-review-moderation-api.yaml` against controller routes and serialized DTO names; validate every `$ref`, required property, canonical enum value, 503 review-detail response, update response, and non-financial submission response
- [X] T087 [P] Add or update XML comments for new admin/company moderation endpoints in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`
- [X] T088 [P] Add or update XML comments for company review-outcome endpoint in `MediBridge.APIs/Controllers/CampaignsController.cs`
- [X] T089 Run `dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj` and fix only failures caused by campaign review moderation changes
- [X] T090 Run `dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj` and fix only failures caused by campaign review moderation changes
- [X] T091 Run `dotnet test tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj` and fix only failures caused by campaign review moderation changes
- [X] T092 Run `dotnet test MediBridge.slnx` from repository root; report exact passed/failed/skipped totals in the implementation handoff; distinguish any unrelated baseline failures from feature regressions without editing task definitions during execution
- [X] T093 Perform constitution compliance review for layering, thin controllers, repository/unit-of-work persistence, JWT/role authorization, response envelope, exception handling, queue determinism, and no-wallet-effects in all changed `MediBridge.*` files
- [X] T094 Perform final forbidden-side-effect searches in `MediBridge.Services/Services/AdminCampaignReviewService.cs` and `MediBridge.Services/Services/CampaignWorkflowService.cs`: review service must contain no wallet/delivery/payout repository calls; submit/resubmit may read a wallet only for affordability but must contain no `StageAvailableBalanceChangeAsync`, `StageReservedBalanceChangeAsync`, Reserve/Release transaction creation, wallet ledger insertion, delivery creation, payout, expiry, charge, or earn call
- [X] T095 Perform a final code search for response contract drift and ensure all new endpoints return `ApiEnvelope<T>` or mapped envelope errors in `MediBridge.APIs/Controllers/AdminCampaignsController.cs`
- [X] T096 Perform a final code search for response contract drift and ensure company review outcome returns `ApiEnvelope<CompanyReviewOutcomeDto>` or mapped envelope errors in `MediBridge.APIs/Controllers/CampaignsController.cs`

### Known Baseline Failures Before Feature Implementation

These failures existed during the 2026-06-27 artifact review. They are not permission to ignore new failures, and they must be rechecked because feature tasks may touch the same tests:

- `CampaignDraftValidationTests.CampaignAsset_ContentTypeMatchesPersistenceLimit` passes an unknown 120-character MIME type to an allowlist validator, so it fails for allowlist reasons instead of isolating persistence length.
- `ConfigurationSecretGuardTests.TrackedAppSettings_DoNotContainDeployableCredentials` conflicts with `docs/runtime-configuration.md`, which explicitly permits current tracked development credentials. Do not expose credential values in logs or reports.
- `AuthAuditSafetyTests.RegistrationAuditEvent_Should_NotStorePasswordsTokensOrBodies` expects one audit row, while registration now creates Registration and ContactVerificationIssued audit rows.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies. Start here.
- **Phase 2 Foundational**: Depends on Phase 1. Blocks every user story.
- **Phase 3 US1**: Depends on Phase 2. This is the MVP because admins need list/detail before deciding.
- **Phase 4 US2**: Depends on Phase 2 and can start after US1 contracts/DTOs are stable. It does not require US3 or US4.
- **Phase 5 US3**: Depends on Phase 2 and can start after the `RevisionRequired` status exists. It does not require US2 approval queue work.
- **Phase 6 US4**: Depends on Phase 2 and should run after at least one decision path from US2 or US3 exists.
- **Phase 7 Polish**: Depends on the desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent after foundation. Delivers pending list and review detail.
- **US2 (P1)**: Independent after foundation, but benefits from US1 read models. Delivers approval and queue creation.
- **US3 (P1)**: Independent after foundation. Delivers rejection, revision-required, and company review outcome.
- **US4 (P2)**: Depends on decision paths from US2/US3 to fully test audit and concurrency.

### MVP Scope

The smallest useful MVP is Phase 1 + Phase 2 + Phase 3 (US1). However, for the business gate to actually unlock delivery, complete Phase 4 (US2) immediately after MVP validation.

### Within Each User Story

- Write the listed tests first and verify they fail for the missing behavior.
- Implement Core and Repository changes before Services when a task requires new persistence data.
- Implement Services before Controllers.
- Run the story-specific test commands at each checkpoint before moving to another story.
- Keep unrelated wallet, delivery, expiry, settlement, payout, and reporting code untouched unless a listed task names it.

---

## Parallel Execution Examples

### US1 Parallel Start After Phase 2

```text
Task: "T023 Add pending-list contract tests in tests/contract/MediBridge.ContractTests/Admin/AdminPendingCampaignReviewContractTests.cs"
Task: "T024 Add review-detail contract tests in tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewDetailContractTests.cs"
Task: "T025 Add pending-list integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs"
Task: "T026 Add review-detail leak-safety integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewDetailTests.cs"
Task: "T027 Add readiness integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewReadinessTests.cs"
```

### US2 Parallel Start After Phase 2

```text
Task: "T037 Update approval contract tests in tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs"
Task: "T039 Add approved-media prerequisite tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalPrerequisiteTests.cs"
Task: "T041 Add queue creation idempotency tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignApprovalQueueTests.cs"
Task: "T043 Add no-wallet-effects tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignModerationNoWalletEffectsTests.cs"
```

### US3 Parallel Start After Phase 2

```text
Task: "T056 Update rejection/revision contract tests in tests/contract/MediBridge.ContractTests/Admin/AdminCampaignReviewContractTests.cs"
Task: "T057 Add company outcome contract tests in tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignReviewOutcomeContractTests.cs"
Task: "T058 Add rejected-finality integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignRejectionFinalityTests.cs"
Task: "T061 Add company outcome integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReviewOutcomeTests.cs"
```

### US4 Parallel Start After Decision Paths Exist

```text
Task: "T073 Add replay equivalence unit tests in tests/unit/MediBridge.UnitTests/Campaigns/CampaignReviewTransitionTests.cs"
Task: "T074 Add append-only history integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewAuditHistoryTests.cs"
Task: "T076 Add concurrent review integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignConcurrentReviewTests.cs"
Task: "T077 Add audit safety integration tests in tests/integration/MediBridge.IntegrationTests/Campaigns/AdminCampaignReviewAuditSafetyTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1.
2. Complete Phase 2.
3. Complete Phase 3 (US1).
4. Stop and validate admin pending-list and review-detail endpoints.
5. Continue to Phase 4 so approved campaigns can actually create pending queue rows.

### Incremental Delivery

1. US1: Admins can inspect pending campaigns safely.
2. US2: Admins can approve and create deterministic queue rows once.
3. US3: Admins can reject/finalize or return for revision, and companies can see outcomes.
4. US4: Audit, idempotency, and concurrency become complete and review-ready.

### Guardrails For Smaller Executor

- Do not invent new architecture. Use the existing four projects and files named in tasks.
- Do not put business rules in controllers. Controllers should only authenticate, validate HTTP shell concerns, call services, and wrap envelopes.
- Do not call EF Core `DbContext` from `MediBridge.APIs` or `MediBridge.Services`.
- Do not change wallet semantics in this feature. If a task seems to require wallet mutation, stop and re-read `spec.md`, `research.md`, and `quickstart.md`.
- Prefer extending existing DTOs and tests over creating duplicate concepts with similar names.
- Preserve current public routes unless a task explicitly tells you to add a new route from `contracts/campaign-review-moderation-api.yaml`.
- After each story checkpoint, run the exact test command listed in that phase.
