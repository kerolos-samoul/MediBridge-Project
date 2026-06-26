# Tasks: File Storage, Verification & Security Plumbing (Phase 4)

**Input**: Design documents from `/specs/004-file-storage-security/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Required by the Phase 4 plan and quickstart. Write the test tasks before implementation tasks in each phase. Run the focused test command after each story checkpoint.

**Constitution Note**: Every task must preserve Onion layering, SQL Server persistence through EF Core in `MediBridge.Repository` behind Repository + Unit of Work abstractions, service-owned business logic in `MediBridge.Services`, HTTP-only controllers in `MediBridge.APIs`, JWT/role authorization, standard API envelope responses, global exception middleware behavior, and secret-only Cloudinary configuration.

**Executor Note**: These tasks are intentionally explicit for a smaller execution model. Do not infer extra features. Do not implement campaign submission, queue activation, doctor message viewing, wallet settlement, reporting read models, public file galleries, or mandatory malware scanning.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other marked tasks in the same phase because it edits different files and does not depend on incomplete tasks.
- **[Story]**: User story label from [spec.md](./spec.md). Setup, Foundational, and Polish tasks have no story label.
- Each task includes an exact target file path.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add required package/configuration surfaces and create empty folders/files so later tasks have stable homes.

- [X] T001 Add `CloudinaryDotNet` package reference to `MediBridge.Services\MediBridge.Services.csproj`.
- [X] T002 Create Phase 4 service config folder marker by adding `MediBridge.Services\Config\FileStorageOptions.cs`.
- [X] T003 [P] Create Phase 4 file DTO folder marker by adding `MediBridge.Services\DTOs\Files\FileDtos.cs`.
- [X] T004 [P] Create Phase 4 file validator folder marker by adding `MediBridge.Services\Validators\Files\FileUploadRequestValidator.cs`.
- [X] T005 [P] Create Phase 4 contract test file shell in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T006 [P] Create Phase 4 unit test file shell in `tests\unit\MediBridge.UnitTests\Phase4FileValidationTests.cs`.
- [X] T007 [P] Create Phase 4 integration test file shell in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T008 [P] Create Phase 4 architecture test file shell in `tests\integration\MediBridge.IntegrationTests\Phase4FileSecurityBoundaryTests.cs`.
- [X] T009 [P] Create Phase 4 storage fake test helper in `tests\integration\MediBridge.IntegrationTests\Phase4FakeFileStorageProvider.cs`.

**Checkpoint**: Build may still fail because files are shells, but all planned Phase 4 file paths exist.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define shared domain, persistence, service, provider, configuration, and API infrastructure required before any user story can work.

**Critical**: No user story work should start until this phase is complete.

**Manual Senior Review 2026-06-08**: Phase 2 foundation is complete for the file-storage/security feature. Manual review confirms Core owns only pure file domain entities, enums, and repository contracts; Repository owns EF Core mappings, SQL Server migration, DbSets, repositories, and Unit of Work exposure; Services own file options, validation, DTOs, workflow/provider abstractions, and the Cloudinary SDK boundary; APIs only wire rate-limit defaults, authorization policy names, and startup option validation. Async paths are awaited and there is no sync-over-async/fire-and-forget work in the Phase 2 file foundation. Focused validation passed with `dotnet build .\MediBridge.slnx`, `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase4FileValidation"` (4/4), and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileSecurityBoundary"` (3/3). Remaining file-workflow behavior belongs to later story phases and remains unchecked below.

### Tests First

- [X] T010 [P] Add failing enum coverage tests for new file statuses in `tests\unit\MediBridge.UnitTests\Phase4FileValidationTests.cs`.
- [X] T011 [P] Add failing options validation tests for exact allowed MIME types/extensions, 10 MB document/image limits, 25 MB audio limit, 100 MB video limit, 10-minute grants, and missing Cloudinary secret in `tests\unit\MediBridge.UnitTests\Phase4FileValidationTests.cs`.
- [X] T012 [P] Add failing architecture tests proving Core has no Cloudinary/EF/HTTP references and controllers have no provider/DbContext references in `tests\integration\MediBridge.IntegrationTests\Phase4FileSecurityBoundaryTests.cs`.
- [X] T013 [P] Add failing secret hygiene test that scans tracked config for pasted Cloudinary credential material in `tests\integration\MediBridge.IntegrationTests\Phase4FileSecurityBoundaryTests.cs`.

### Domain and Contracts

- [X] T014 Add `FileReviewDecision`, `StoredFileUploadStatus`, `StoredFileSafetyScanStatus`, `StoredFileStorageResourceType`, `StoredFileStorageDeliveryType`, and `FileAccessGrantOutcome` enums to `MediBridge.Core\Enums\Phase3DomainEnums.cs`.
- [X] T015 Extend `StoredFile` with `RelatedCampaignId`, `StorageProvider`, `StorageResourceType`, `StorageDeliveryType`, `UploadStatus`, `SafetyScanStatus`, `SafetyScanCheckedAtUtc`, `DeletedAtUtc`, `ReplacedByFileId`, and `ConcurrencyStamp` in `MediBridge.Core\Entities\Files\StoredFile.cs`.
- [X] T016 [P] Create append-only `FileReview` entity in `MediBridge.Core\Entities\Files\FileReview.cs`.
- [X] T017 [P] Create non-secret `FileAccessGrantAudit` entity in `MediBridge.Core\Entities\Files\FileAccessGrantAudit.cs`.
- [X] T018 Update `IStoredFileRepository` with Phase 4 methods for pending upload, upload completion, upload failure, owner/campaign queries, review summary update, delete, replacement link, and readiness check in `MediBridge.Core\Interfaces\Files\IStoredFileRepository.cs`.
- [X] T019 [P] Create `IFileReviewRepository` in `MediBridge.Core\Interfaces\Files\IFileReviewRepository.cs`.
- [X] T020 [P] Create `IFileAccessGrantAuditRepository` in `MediBridge.Core\Interfaces\Files\IFileAccessGrantAuditRepository.cs`.
- [X] T021 Add `FileReviews` and `FileAccessGrantAudits` repository properties to `IDomainUnitOfWork` in `MediBridge.Core\Interfaces\IDomainUnitOfWork.cs`.

### Persistence

- [X] T022 Add `DbSet<FileReview>` and `DbSet<FileAccessGrantAudit>` to `MediBridge.Repository\Data\MediBridgeDbContext.cs`.
- [X] T023 Update `StoredFileConfiguration` with Phase 4 columns, max lengths, indexes, nullable backward-compatible fields, and concurrency token in `MediBridge.Repository\Configurations\Files\StoredFileConfiguration.cs`.
- [X] T024 [P] Create `FileReviewConfiguration` with append-only review indexes and admin/file foreign keys in `MediBridge.Repository\Configurations\Files\FileReviewConfiguration.cs`.
- [X] T025 [P] Create `FileAccessGrantAuditConfiguration` with non-secret grant audit indexes and file/user foreign keys in `MediBridge.Repository\Configurations\Files\FileAccessGrantAuditConfiguration.cs`.
- [X] T026 Update `StoredFileRepository` to implement all Phase 4 `IStoredFileRepository` methods in `MediBridge.Repository\Repositories\Files\StoredFileRepository.cs`.
- [X] T027 [P] Create `FileReviewRepository` in `MediBridge.Repository\Repositories\Files\FileReviewRepository.cs`.
- [X] T028 [P] Create `FileAccessGrantAuditRepository` in `MediBridge.Repository\Repositories\Files\FileAccessGrantAuditRepository.cs`.
- [X] T029 Register `IFileReviewRepository` and `IFileAccessGrantAuditRepository` in `MediBridge.Repository\Extensions\RepositoryServiceCollectionExtensions.cs`.
- [X] T030 Update `DomainUnitOfWork` to expose file review and access-grant audit repositories in `MediBridge.Repository\UnitOfWork\DomainUnitOfWork.cs`.
- [X] T031 Add EF Core migration named `Phase4FileStorageSecurity` for StoredFile extensions, FileReviews, and FileAccessGrantAudits in `MediBridge.Repository\Migrations\*Phase4FileStorageSecurity*.cs`.

### Services and Provider Abstractions

- [X] T032 Implement `FileStorageOptions` with defaults for 10 MB documents, 10 MB images, 25 MB audio, 100 MB video, 10-minute grants, allowed MIME types (`application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `image/jpeg`, `image/png`, `audio/mpeg`, `video/mp4`), allowed extensions (`.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, `.mp4`), and `UploadsEnabled` in `MediBridge.Services\Config\FileStorageOptions.cs`.
- [X] T033 [P] Create `CloudinaryStorageOptions` with non-secret cloud name, secure URL flag, folder prefix, and environment-secret validation helpers in `MediBridge.Services\Config\CloudinaryStorageOptions.cs`.
- [X] T034 [P] Create `IFileStorageProvider` plus request/response records in `MediBridge.Services\Interfaces\IFileStorageProvider.cs`.
- [X] T035 [P] Create `IFileWorkflowService` contract in `MediBridge.Services\Interfaces\IFileWorkflowService.cs`.
- [X] T036 [P] Create file workflow DTOs for upload result, access grant, review request/result, pending page, and review history in `MediBridge.Services\DTOs\Files\FileDtos.cs`.
- [X] T037 Implement upload validation rules for empty files, unsafe names, exact MIME type plus extension matching, purpose mapping, and category size limits in `MediBridge.Services\Validators\Files\FileUploadRequestValidator.cs`.
- [X] T038 Create `CloudinaryFileStorageProvider` that uploads private/authenticated assets, generates 10-minute private access URLs, deletes assets, and redacts provider errors in `MediBridge.Services\Services\CloudinaryFileStorageProvider.cs`.
- [X] T039 Register `FileStorageOptions`, `CloudinaryStorageOptions`, `IFileStorageProvider`, and `IFileWorkflowService` in `MediBridge.Services\Extensions\IdentityServiceCollectionExtensions.cs`.

### API Wiring

- [X] T040 Add `FileUpload` rate limit policy name and default policy slot to `RateLimitPolicyNames.All` and `RateLimitingOptions.Policies` in `MediBridge.APIs\Config\RateLimitOptions.cs`.
- [X] T041 Configure the `FileUpload` rate limit default as 20 permits per 3600 seconds with queue limit 0 in `MediBridge.APIs\Config\RateLimitOptions.cs`.
- [X] T042 Add deterministic file workflow authorization policy constants for Admin file review, Company campaign file upload, Doctor verification upload, and authenticated file access in `MediBridge.APIs\Security\AuthorizationPolicies.cs`; if an existing policy exactly matches one of these, document the reuse in code comments in that same file rather than adding a duplicate.
- [X] T043 Register Phase 4 service options validation and keep secret values out of appsettings binding in `MediBridge.APIs\Extensions\ServiceCollectionExtensions.cs`.

**Checkpoint**: Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase4FileValidation"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileSecurityBoundary"`. Both focused suites should pass after foundation implementation.

---

## Phase 3: User Story 1 - Upload Private Verification Files (Priority: P1) MVP

**Goal**: Doctor and Pharmaceutical Company users can upload private verification documents tied to their own account profile, creating reviewable metadata without exposing contents to unrelated users.

**Independent Test**: Submit a valid verification document as a pending Doctor or Company user, verify private stored-file metadata is created, verify it is visible to admins, and verify unrelated users cannot access it.

**Manual Senior Review 2026-06-08**: Phase 3 US1 MVP is complete for private verification uploads. Manual review confirms controllers remain HTTP-only and delegate workflow decisions to `IFileWorkflowService`; service orchestration owns role/profile checks, validation-before-provider-upload, generated storage keys, pending/stored metadata transitions, and non-secret audit writes; DTOs omit storage keys, signed URLs, provider diagnostics, credentials, and private tokens; fake storage proves invalid uploads do not reach the provider; and `FileUpload` rate limiting is partitioned by authenticated user after authentication runs in the pipeline. Async paths are awaited with no sync-over-async or fire-and-forget work. Focused validation passed with `dotnet build .\MediBridge.slnx`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"` (6/6), `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileWorkflow"` (10/10), and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileSecurityBoundary"` (3/3). Phase 4 and later access/review/campaign/delete behavior remains intentionally unchecked below.

### Tests for User Story 1

- [X] T044 [P] [US1] Add failing contract tests for `POST /api/files/verification-documents` success, 400 validation, 401 unauthenticated, 403 wrong role, and 429 upload rate limit in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T045 [P] [US1] Add failing integration tests for valid Doctor verification document upload and private metadata creation in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T046 [P] [US1] Add failing integration tests for valid Company verification document upload and private metadata creation in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T047 [P] [US1] Add failing integration tests proving empty file, unsafe file name, unsupported MIME type, unsupported extension, mismatched MIME/extension pair, and document/image over 10 MB are rejected before fake provider upload in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T048 [P] [US1] Add failing integration test proving upload attempt 21 within one hour returns envelope 429 for the same authenticated user in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.

### Implementation for User Story 1

- [X] T049 [US1] Implement `UploadVerificationDocumentAsync` owner role checks, validation-before-provider-upload, generated storage key, pending metadata, provider upload, stored metadata completion, and audit events in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T050 [US1] Create `FilesController` with `POST /api/files/verification-documents`, `[Authorize]`, `FileUpload` rate-limit policy, multipart upload binding, user id extraction, service delegation, and envelope 201/400/401/403/429 responses in `MediBridge.APIs\Controllers\FilesController.cs`.
- [X] T051 [US1] Ensure verification upload result DTO omits `StorageKey`, provider credentials, signed URLs, provider diagnostics, and private tokens in `MediBridge.Services\DTOs\Files\FileDtos.cs`.
- [X] T052 [US1] Update fake storage provider to count upload calls and prove invalid files never reach provider upload in `tests\integration\MediBridge.IntegrationTests\Phase4FakeFileStorageProvider.cs`.
- [X] T053 [US1] Run US1 focused validation with `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileWorkflow"`.

**Checkpoint**: User Story 1 is independently demoable as the MVP.

---

## Phase 4: User Story 4 - Protect Storage Configuration and File Access (Priority: P1)

**Goal**: Storage secrets stay out of tracked configuration, required secrets are validated, unauthorized users cannot access private files, and authorized access grants expire after 10 minutes.

**Independent Test**: Inspect tracked configuration, start or resolve services without the Cloudinary secret to trigger clear validation failure, request access as authorized and unauthorized users, and verify grants older than 10 minutes are rejected.

**Manual Senior Review 2026-06-09**: Phase 4 US4 security/access work is complete for storage configuration protection, private access grants, inactive-owner denial, and delete/unavailable behavior. Manual review confirms controllers remain HTTP-only and delegate to `IFileWorkflowService`; Cloudinary SDK usage stays inside `MediBridge.Services`; EF Core and SQL persistence remain isolated in `MediBridge.Repository`; grant and delete audits avoid signed URLs, private tokens, provider signatures, credentials, and raw provider diagnostics; tracked appsettings files contain no Cloudinary credential material; and async paths are awaited with no sync-over-async or fire-and-forget workflow operations. Review fixed the Cloudinary private access grant path to use the SDK signed private download helper instead of hand-built unsigned URLs, and removed credential-shaped fake test literals. Focused validation passed with `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase4FileValidation"` (5/5), `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"` (14/14), and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileSecurityBoundary|FullyQualifiedName~Phase4FileWorkflow"` (27/27). Later Phase 5 review/replacement implementation, Phase 6 campaign files, and Phase 7 polish remain intentionally unchecked.

### Tests for User Story 4

- [X] T054 [P] [US4] Add failing contract tests for `POST /api/files/{fileId}/access` returning 200, 401, 403, and 404 envelope responses without private storage references in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T055 [P] [US4] Add failing integration tests for missing `CLOUDINARY_URL` when uploads are enabled and for no secret value in `appsettings.json` or `appsettings.Development.json` in `tests\integration\MediBridge.IntegrationTests\Phase4FileSecurityBoundaryTests.cs`.
- [X] T056 [P] [US4] Add failing integration tests for authorized Doctor, Company owner, and Admin private access grant creation in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T057 [P] [US4] Add failing integration tests for unrelated user, anonymous user, guessed file id, expired grant, deleted file, rejected file, quarantined file, and missing file access denial in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T058 [P] [US4] Add failing integration tests proving issued/denied grant audit records never persist signed URL, private token, provider signature, or credentials in `tests\integration\MediBridge.IntegrationTests\Phase4FileSecurityBoundaryTests.cs`.
- [X] T065 [P] [US4] Add failing integration tests proving files owned by soft-deleted, rejected, suspended, or otherwise inactive Doctor/Company owners deny normal access and replacement for non-Admin users in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T066 [P] [US4] Add failing contract tests for `DELETE /api/files/{fileId}` returning 200, 401, 403, and 404 envelope responses without private storage references in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T067 [P] [US4] Add failing integration tests proving authorized delete marks the file unavailable, preserves metadata/review history, attempts provider deletion through the fake provider, and writes a non-secret deletion audit event in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.

### Implementation for User Story 4

- [X] T059 [US4] Implement `CreatePrivateAccessGrantAsync` authorization checks, availability checks, 10-minute expiry calculation, provider grant creation, non-secret issued/denied audit writes, and redacted errors in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T060 [US4] Add `POST /api/files/{fileId}/access` endpoint to `FilesController` with JWT authorization, service delegation, and envelope 200/401/403/404 responses in `MediBridge.APIs\Controllers\FilesController.cs`.
- [X] T061 [US4] Implement Cloudinary secret validation that reads `CLOUDINARY_URL` from environment/secret configuration only and fails clearly when uploads are enabled and the secret is missing in `MediBridge.Services\Config\CloudinaryStorageOptions.cs`.
- [X] T062 [US4] Ensure `CloudinaryFileStorageProvider` creates signed time-limited access grants with `ExpiresAtUtc = now + 10 minutes` and never logs or returns provider diagnostics outside the grant DTO in `MediBridge.Services\Services\CloudinaryFileStorageProvider.cs`.
- [X] T063 [US4] Update `FileAccessGrantAuditRepository` to persist only non-secret grant audit fields and never signed URL/token data in `MediBridge.Repository\Repositories\Files\FileAccessGrantAuditRepository.cs`.
- [X] T064 [US4] Run preliminary US4 focused validation for secret configuration, access grants, and access-audit coverage with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileSecurityBoundary|FullyQualifiedName~Phase4FileWorkflow"` and `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"`.
- [X] T068 [US4] Implement owner-active checks for normal access, replacement, and delete attempts, allowing Admin audit access while denying non-Admin access for soft-deleted/rejected/suspended Doctor or Company owners in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T069 [US4] Implement `DeleteFileAsync` with authorization checks, owner-active checks, provider deletion through `IFileStorageProvider`, SQL unavailable/delete state update, preserved metadata/history, and non-secret audit event in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T070 [US4] Add `DELETE /api/files/{fileId}` endpoint to `FilesController` with JWT authorization, service delegation, envelope 200/401/403/404 responses, then rerun US4 focused validation in `MediBridge.APIs\Controllers\FilesController.cs`.

**Checkpoint**: Storage configuration and private access behavior are independently verifiable.

---

## Phase 5: User Story 2 - Review and Audit Files (Priority: P1)

**Goal**: Admin users can list pending files, approve, reject, quarantine, request replacement, and record corrections while preserving append-only review history.

**Independent Test**: Review submitted files as an Admin, verify current status updates, verify reason requirements, and verify corrections link to earlier decisions without mutating the original review record.

**Manual Senior Review 2026-06-09**: Phase 5 US2 admin review/replacement work is complete for pending review listing, append-only admin decisions, correction links, optimistic review-summary conflict protection, and owner-initiated replacement of rejected or replacement-requested verification files. Manual review confirms controllers remain HTTP-only and delegate review/replacement workflow to `IFileWorkflowService`; Core owns only pure DTO-adjacent contracts and file domain abstractions; EF Core persistence remains in `MediBridge.Repository`; Cloudinary/provider operations stay inside `MediBridge.Services`; Phase 5 DTOs and envelopes do not expose storage keys, signed URLs, credentials, private tokens, or provider diagnostics; replacement validation occurs before fake provider upload; review history rows are append-only; original replaced files become unavailable while original metadata and review history remain auditable; and async paths are awaited with no sync-over-async or fire-and-forget operations. Focused validation passed with `dotnet build .\MediBridge.slnx`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"` (29/29), and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileWorkflow"` (39/39). Phase 6 campaign file upload and Phase 7 polish remain intentionally unchecked.

### Tests for User Story 2

- [X] T071 [P] [US2] Add failing contract tests for `GET /api/admin/files/pending` and `PUT /api/admin/files/{fileId}/review` success, 400, 401, 403, and 404 envelope responses in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T072 [P] [US2] Add failing integration tests for Admin pending-review listing ordered by creation time in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T073 [P] [US2] Add failing integration tests for approve, reject with reason, quarantine with reason, replacement request with reason, and missing reason validation in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T074 [P] [US2] Add failing integration tests proving correction reviews preserve the original review and link `CorrectsReviewId` in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T075 [P] [US2] Add failing integration tests for concurrent review update conflict using `StoredFile.ConcurrencyStamp` in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T076 [P] [US2] Add failing contract tests for `POST /api/files/{fileId}/replacement` returning 201, 400, 401, 403, 404, and 429 envelope responses without storage keys, signed URLs, credentials, or provider diagnostics in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T077 [P] [US2] Add failing integration tests proving owner-initiated replacement of a rejected or outdated verification file creates a new pending file, links `ReplacedFileId`, marks the original unavailable, and preserves original metadata/review history in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.
- [X] T078 [P] [US2] Add failing integration tests proving replacement upload rejects invalid MIME types/extensions, mismatched MIME/extension pairs, unsafe filenames, and oversize replacement files before fake provider upload in `tests\integration\MediBridge.IntegrationTests\Phase4FileWorkflowIntegrationTests.cs`.

### Implementation for User Story 2

- [X] T079 [US2] Implement `ReviewFileAsync`, `ListPendingReviewsAsync`, and `GetReviewHistoryAsync` with Admin-only checks, reason validation, append-only review insert, current StoredFile review summary update, correction links, and audit events in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T080 [US2] Implement review request validation for required reason on Rejected, Quarantined, ReplacementRequested, and Correction decisions in `MediBridge.Services\Validators\Files\FileReviewRequestValidator.cs`.
- [X] T081 [US2] Create `AdminFilesController` with `GET /api/admin/files/pending`, `PUT /api/admin/files/{fileId}/review`, `GET /api/admin/files/{fileId}/reviews`, Admin policy, service delegation, and envelope responses in `MediBridge.APIs\Controllers\AdminFilesController.cs`.
- [X] T082 [US2] Update `FileReviewRepository` to append decisions, list history, and fetch correction targets without mutating prior review rows in `MediBridge.Repository\Repositories\Files\FileReviewRepository.cs`.
- [X] T083 [US2] Update `StoredFileRepository` review-summary update to enforce optimistic concurrency and current status transitions in `MediBridge.Repository\Repositories\Files\StoredFileRepository.cs`.
- [X] T084 [US2] Run preliminary US2 focused validation for admin review, correction, and concurrency coverage with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4FileWorkflow"` and `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"`.
- [X] T085 [US2] Implement `ReplaceFileAsync` with owner role checks, owner-active checks, same purpose/campaign relationship preservation, validation-before-provider-upload, generated storage key, new pending replacement metadata, original file `Replaced` state, `ReplacedByFileId` link, preserved original review history, and audit events in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T086 [US2] Add `POST /api/files/{fileId}/replacement` endpoint to `FilesController` with JWT authorization, `FileUpload` rate-limit policy, multipart upload binding, service delegation, and envelope 201/400/401/403/404/429 responses in `MediBridge.APIs\Controllers\FilesController.cs`.
- [X] T087 [US2] Update `StoredFileRepository` replacement methods to create replacement links, mark originals as `Replaced`, preserve original history, prevent replaced files from readiness checks, then rerun US2 focused validation in `MediBridge.Repository\Repositories\Files\StoredFileRepository.cs`.

**Checkpoint**: Admin review and append-only audit behavior are independently verifiable.

---

## Phase 6: User Story 3 - Store Campaign and Message Attachments Safely (Priority: P2)

**Goal**: Pharmaceutical Company users can upload campaign media, voice notes, and clinical research files for owned campaigns, and rejected/deleted/missing files are unavailable for later campaign readiness checks.

**Independent Test**: Attach valid files to an owned campaign draft, verify campaign ownership is enforced, verify purpose-based limits, and verify unavailable file states are not treated as approved campaign assets.

### Tests for User Story 3

- [X] T088 [P] [US3] Add failing contract tests for `POST /api/campaigns/{campaignId}/files` success, invalid purpose, 401, 403, 404, and 429 envelope responses in `tests\contract\MediBridge.ContractTests\FileWorkflowContractTests.cs`.
- [X] T089 [P] [US3] Add failing integration tests for owned campaign image media upload, voice note upload, campaign video upload, and clinical research document upload in `tests\integration\MediBridge.IntegrationTests\Phase4CampaignFileIntegrationTests.cs`.
- [X] T090 [P] [US3] Add failing integration tests for non-owner company upload denial and non-company role denial in `tests\integration\MediBridge.IntegrationTests\Phase4CampaignFileIntegrationTests.cs`.
- [X] T091 [P] [US3] Add failing integration tests for document over 10 MB, image over 10 MB, audio over 25 MB, video over 100 MB, unsupported MIME type, unsupported extension, mismatched MIME/extension pair, and unsafe filename rejection in `tests\integration\MediBridge.IntegrationTests\Phase4CampaignFileIntegrationTests.cs`.
- [X] T092 [P] [US3] Add failing integration tests proving rejected, quarantined, deleted, replaced, missing, and pending-required-review files are unavailable as approved campaign assets in `tests\integration\MediBridge.IntegrationTests\Phase4CampaignFileIntegrationTests.cs`.

### Implementation for User Story 3

- [X] T093 [US3] Implement `UploadCampaignFileAsync` with Company role check, campaign ownership check, allowed purpose validation, purpose-based size/type validation, generated storage key, provider upload, metadata completion, and audit events in `MediBridge.Services\Services\FileWorkflowService.cs`.
- [X] T094 [US3] Add `POST /api/campaigns/{campaignId}/files` endpoint to `FilesController` with Company policy, `FileUpload` rate-limit policy, multipart binding, service delegation, and envelope 201/400/401/403/404/429 responses in `MediBridge.APIs\Controllers\FilesController.cs`.
- [X] T095 [US3] Update `StoredFileRepository` campaign queries and readiness check for approved evidence/assets in `MediBridge.Repository\Repositories\Files\StoredFileRepository.cs`.
- [X] T096 [US3] Ensure file DTOs include `RelatedCampaignId`, `Purpose`, `ReviewStatus`, `UploadStatus`, and `SafetyScanStatus` but exclude `StorageKey` and signed URLs in `MediBridge.Services\DTOs\Files\FileDtos.cs`.
- [X] T097 [US3] Run US3 focused validation with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4CampaignFile"` and `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~FileWorkflowContract"`.

**Checkpoint**: Campaign-supporting file uploads are independently verifiable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validate the whole feature, remove accidental leaks, and make the implementation clean for review.

- [X] T098 [P] Add or update Phase 4 API examples without secrets in `MediBridge.APIs\MediBridge.APIs.http`.
- [X] T099 [P] Update quickstart command expectations if implementation file names or migration name differ in `specs\004-file-storage-security\quickstart.md`.
- [X] T100 Run full build with `dotnet build .\MediBridge.slnx` and record any necessary fixes in changed source files.
- [X] T101 Run full test suite with `dotnet test .\MediBridge.slnx` and fix failures in the relevant changed source or test files.
- [X] T102 Run secret scan `rg -n "cloudinary://.*:.*@|CLOUDINARY_URL\\s*[:=]\\s*.+|api_secret\\s*[:=]" . --glob "!**/.git/**"` and remove any accidental credential material from changed files.
- [X] T103 Run architecture scan for forbidden references to `CloudinaryDotNet` outside `MediBridge.Services`, `MediBridgeDbContext` inside controllers/services, and EF Core types inside `MediBridge.Core` using `rg` over `MediBridge.Core`, `MediBridge.Services`, and `MediBridge.APIs`.
- [X] T104 Confirm no Phase 4 task added campaign submission, queue activation, doctor message viewing, wallet settlement, reporting read models, public file galleries, or mandatory malware scanning by reviewing `MediBridge.APIs\Controllers`, `MediBridge.Services\Services`, and `MediBridge.Repository\Migrations`.
- [X] T105 Update `specs\004-file-storage-security\tasks.md` task checkboxes only for tasks actually completed during implementation.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies.
- **Phase 2 Foundational**: Depends on Phase 1 and blocks every user story.
- **Phase 3 US1 MVP**: Depends on Phase 2.
- **Phase 4 US4 Security/Access**: Depends on Phase 2 and can run after US1 test data helpers exist; it does not require US2 or US3.
- **Phase 5 US2 Review**: Depends on Phase 2 and benefits from US1 upload records; it does not require US3.
- **Phase 6 US3 Campaign Files**: Depends on Phase 2 and uses the same upload/review foundation; it does not require US2 completion except for approved asset validation.
- **Phase 7 Polish**: Depends on all implemented user stories.

### User Story Dependencies

- **US1 (P1, MVP)**: Start after Foundational. No dependency on other stories.
- **US4 (P1)**: Start after Foundational. Uses stored files created by US1 tests but can seed its own file metadata.
- **US2 (P1)**: Start after Foundational. Can seed pending files directly or use US1 upload flow.
- **US3 (P2)**: Start after Foundational. Requires campaign records from Phase 3 and can seed campaign ownership directly in tests.

### Within Each User Story

- Write tests first and verify they fail for the expected missing behavior.
- Implement Core/Repository changes before service orchestration if new data access is required.
- Implement service behavior before controller endpoint behavior.
- Run focused story validation before moving to another story.
- Keep controllers HTTP-only and delegate business rules to `FileWorkflowService`.

---

## Parallel Execution Examples

### Phase 2 Foundation Parallel Batch

```text
Run together after T010-T013 are written:
- T016 FileReview entity
- T017 FileAccessGrantAudit entity
- T019 IFileReviewRepository
- T020 IFileAccessGrantAuditRepository
- T024 FileReviewConfiguration
- T025 FileAccessGrantAuditConfiguration
- T027 FileReviewRepository
- T028 FileAccessGrantAuditRepository
- T033 CloudinaryStorageOptions
- T034 IFileStorageProvider
- T035 IFileWorkflowService
- T036 file DTOs
```

### User Story 1 Parallel Test Batch

```text
Run together before US1 implementation:
- T044 contract tests
- T045 Doctor upload integration tests
- T046 Company upload integration tests
- T047 invalid upload integration tests
- T048 rate-limit integration test
```

### User Story 4 Parallel Test Batch

```text
Run together before US4 implementation:
- T054 access contract tests
- T055 secret configuration tests
- T056 authorized grant tests
- T057 unauthorized/expired grant tests
- T058 non-secret grant audit tests
- T065 inactive-owner access/replacement tests
- T066 delete contract tests
- T067 delete integration tests
```

### User Story 2 Parallel Test Batch

```text
Run together before US2 implementation:
- T071 admin file contract tests
- T072 pending-review listing tests
- T073 review decision tests
- T074 correction history tests
- T075 concurrency tests
- T076 replacement contract tests
- T077 replacement integration tests
- T078 invalid replacement upload tests
```

### User Story 3 Parallel Test Batch

```text
Run together before US3 implementation:
- T088 campaign file contract tests
- T089 owned campaign upload tests
- T090 ownership denial tests
- T091 limit/type/name rejection tests
- T092 campaign asset availability tests
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 setup.
2. Complete Phase 2 foundation.
3. Complete Phase 3 US1 only.
4. Validate US1 with focused contract and integration tests.
5. Stop for review if a minimal private verification upload workflow is enough for a demo.

### Incremental Delivery

1. Deliver US1 verification upload.
2. Deliver US4 secret/access protection.
3. Deliver US2 admin review and append-only history.
4. Deliver US3 campaign-supporting attachments.
5. Run Phase 7 polish and full validation.

### Review Rules for Every Completed Phase

- No secrets in tracked config or logs.
- No provider SDK types outside `MediBridge.Services`.
- No EF Core types in `MediBridge.Core` or API controllers.
- No direct `MediBridgeDbContext` usage in controllers or services.
- Every endpoint returns the standard envelope.
- Every secured endpoint requires JWT and the intended role policy.
- Every changed test should fail before its implementation and pass after implementation.
