# Tasks: Identity and Approval (Phase 2)

**Input**: Design documents from `/specs/002-identity-approval/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/identity-approval-api.yaml`, `quickstart.md`

**Tests**: Tests are required before Phase 2 is considered complete because the spec includes measurable security, authorization, token lifecycle, and architecture success criteria. For the current temporary architecture-build pass, prioritize correctly layered domain models, repository contracts, EF Core SQL Server infrastructure, DbContext wiring, and service/API contracts. Do not focus on running the full test suite yet; keep test tasks for traceability and execute them in the later behavior/database verification pass.

**Constitution Note**: Every task must follow `.specify/memory/constitution.md`. Do not weaken or reinterpret it. Controllers must stay HTTP-only. Business decisions belong in `MediBridge.Services`. Persistence must target SQL Server through EF Core implementations in `MediBridge.Repository`, behind Repository + Unit of Work abstractions. `MediBridge.Core` must stay free of ASP.NET Identity, EF Core framework types, and other infrastructure concerns. Secured routes must use JWT and role-aware authorization. All API responses must use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`. Global exception middleware must remain the error boundary. Queue, wallet, campaign, delivery, settlement, payout, file content upload/storage/review, pricing, and platform-fee behavior are out of scope for Phase 2.

**Organization**: Tasks are grouped by user story to enable independently testable delivery.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel with other `[P]` tasks in the same phase because it touches different files and has no dependency on incomplete tasks.
- **[Story]**: User story label, required only for user-story phases.
- Every task includes exact file paths.

## Phase 1: Setup and Project Readiness

**Purpose**: Prepare package references, test project structure, and configuration surfaces shared by all Phase 2 stories.

**Manual Senior Review 2026-06-02**: Phase 1 setup remains complete. Package references, folder scaffolding, unit test project setup, and solution inclusion are present, and the full Phase 2 validation suite passes.

- [X] T001 Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` version `8.0.11`, `Microsoft.EntityFrameworkCore.SqlServer` version `8.0.11`, and `Microsoft.EntityFrameworkCore.Design` version `8.0.11` to `MediBridge.Repository/MediBridge.Repository.csproj` if missing; keep `PrivateAssets=all` on the design package.
- [X] T002 Add `FluentValidation` version `11.10.0` to `MediBridge.Services/MediBridge.Services.csproj` if missing.
- [X] T003 Add or verify `Microsoft.AspNetCore.Authentication.JwtBearer` version `8.0.11` in `MediBridge.APIs/MediBridge.APIs.csproj`; do not add unrelated authentication providers.
- [X] T004 [P] Create directory `MediBridge.Core/Enums/` for Phase 2 enums.
- [X] T005 [P] Create directory `MediBridge.Core/Entities/Identity/` for identity lifecycle entities.
- [X] T006 [P] Create directory `MediBridge.Core/Entities/Profiles/` for Doctor and Company profile entities.
- [X] T007 [P] Create directory `MediBridge.Core/Interfaces/Identity/` for identity repository and token contracts.
- [X] T008 [P] Create directory `MediBridge.Services/DTOs/Auth/` for auth request/response DTOs.
- [X] T009 [P] Create directory `MediBridge.Services/DTOs/Admin/` for admin account decision DTOs.
- [X] T010 [P] Create directory `MediBridge.Services/Validators/Auth/` for auth validators.
- [X] T011 [P] Create directory `MediBridge.Services/Validators/Admin/` for admin validators.
- [X] T012 [P] Create directory `MediBridge.Repository/Data/` for `DbContext` and database setup.
- [X] T013 [P] Create directory `MediBridge.Repository/Configurations/Identity/` for EF Core entity configurations.
- [X] T014 [P] Create directory `tests/unit/MediBridge.UnitTests/` for service-level unit tests.
- [X] T015 Create `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj` targeting `net8.0` with references to `MediBridge.Core` and `MediBridge.Services`, plus test packages matching existing xUnit style.
- [X] T016 Add `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj` to `MediBridge.slnx` using `dotnet sln .\MediBridge.slnx add .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj`.

---

## Phase 2: Foundational Identity Model and Infrastructure

**Purpose**: Create shared entities, enums, repository contracts, service contracts, options, and startup wiring that block all user stories.

**Critical**: No user-story implementation should begin until this phase is complete.

**Manual Senior Review 2026-06-02**: Phase 2 foundational identity model and infrastructure are complete. Architecture review confirms Core remains EF/HTTP-free, controllers remain HTTP-only, Services own identity decisions, Repository owns EF Core/SQL Server and ASP.NET Identity infrastructure, and async token lifecycle paths use transactional repository boundaries. A logout refresh-family revocation edge case found during review was fixed and covered by regression test `Logout_WithRotatedToken_RevokesRefreshFamily`.

[X] T017 Create `MediBridge.Core/Enums/UserRole.cs` with exact values `Admin`, `Doctor`, and `Company`; include a code comment that `Company` is the internal role code for the constitution term `Pharmaceutical Company`.
[X] T018 Create `MediBridge.Core/Enums/AccountStatus.cs` with exact values `Pending`, `Approved`, `Rejected`, `Suspended`, and `Inactive`.
[X] T019 Create `MediBridge.Core/Enums/AdminAccountDecisionType.cs` with exact values `Approve`, `Reject`, `Suspend`, `Inactivate`, and `Reactivate`.
[X] T020 Create `MediBridge.Core/Enums/AuthAuditEventType.cs` with exact values `Registration`, `LoginSuccess`, `LoginDenied`, `Refresh`, `Logout`, `PasswordResetCompleted`, `ContactVerificationCompleted`, `RefreshReuseDetected`, `AdminDecision`, and `AccountResubmission`.
[X] T021 Create `MediBridge.Core/Enums/ContactVerificationChannel.cs` with exact values `Email` and `Phone`.
[X] T022 Create `MediBridge.Core/Entities/Identity/ApplicationUser.cs` as a pure domain identity entity with `Id`, `Email`, `PhoneNumber`, `UserRole Role`, `AccountStatus AccountStatus`, `bool EmailVerified`, `bool PhoneVerified`, `DateTime CreatedAtUtc`, nullable `DateTime ApprovedAtUtc`, nullable `DateTime LastStatusChangedAtUtc`, `bool IsDeleted`, and nullable `DateTime DeletedAtUtc`; do not reference ASP.NET Identity types in Core.
[X] T023 Create `MediBridge.Core/Entities/Profiles/DoctorProfile.cs` with fields from `data-model.md`: `Id`, `UserId`, `ApplicationUser User`, `Specialization`, `ExperienceYears`, `Location`, verification metadata fields, `CreatedAtUtc`, and nullable `UpdatedAtUtc`.
[X] T024 Create `MediBridge.Core/Entities/Profiles/CompanyProfile.cs` with fields from `data-model.md`: `Id`, `UserId`, `ApplicationUser User`, `CompanyName`, `LicenseNumber`, `ContactName`, verification metadata fields, `CreatedAtUtc`, and nullable `UpdatedAtUtc`.
[X] T025 Create `MediBridge.Core/Entities/Identity/RefreshCredential.cs` with `Id`, `TokenHash`, `UserId`, `ApplicationUser User`, `FamilyId`, `ExpiresAtUtc`, nullable `RevokedAtUtc`, nullable `RevocationReason`, nullable `ReplacedByTokenHash`, and `CreatedAtUtc`.
[X] T026 Create `MediBridge.Core/Entities/Identity/PasswordResetFlow.cs` with `Id`, nullable `UserId`, nullable `ApplicationUser User`, `TokenHash`, `ExpiresAtUtc`, nullable `ConsumedAtUtc`, `CreatedAtUtc`, and `RequestCorrelationId`.
[X] T027 Create `MediBridge.Core/Entities/Identity/ContactVerificationFlow.cs` with `Id`, `UserId`, `ApplicationUser User`, `ContactVerificationChannel Channel`, `DestinationHash`, `TokenHash`, `ExpiresAtUtc`, nullable `ConsumedAtUtc`, and `CreatedAtUtc`.
[X] T028 Create `MediBridge.Core/Entities/Identity/AdminAccountDecision.cs` with `Id`, `AdminUserId`, `ApplicationUser AdminUser`, `TargetUserId`, `ApplicationUser TargetUser`, `AdminAccountDecisionType Decision`, `AccountStatus ResultingAccountStatus`, nullable `Reason`, nullable `Notes`, and `CreatedAtUtc`.
[X] T029 Create `MediBridge.Core/Entities/Identity/AccountResubmission.cs` with `Id`, `UserId`, `ApplicationUser User`, `SubmittedAtUtc`, `UpdatedProfileFields`, and `UpdatedVerificationMetadata`.
[X] T030 Create `MediBridge.Core/Entities/Identity/AuthenticationAuditEvent.cs` with `Id`, `AuthAuditEventType EventType`, nullable `ActorUserId`, nullable `TargetUserId`, nullable `UserRole Role`, `Outcome`, nullable `Reason`, `CorrelationId`, and `CreatedAtUtc`.
[X] T031 Create `MediBridge.Core/Interfaces/Identity/IIdentityUnitOfWork.cs` with methods needed by services: save changes, access users/profiles/tokens/flows/decisions/resubmissions/audit repositories, and begin atomic operations.
[X] T032 Create `MediBridge.Core/Interfaces/Identity/IRefreshCredentialRepository.cs` with methods to add, find by token hash, find active family credentials, and revoke credentials by user/family.
[X] T033 Create `MediBridge.Core/Interfaces/Identity/IProfileRepository.cs` with methods for DoctorProfile and CompanyProfile creation, lookup by user id, and duplicate license checks.
[X] T034 Create `MediBridge.Core/Interfaces/Identity/IAuthTokenService.cs` with methods to create access tokens, create refresh token plaintext+hash pairs, hash supplied tokens, and create password/verification token hashes.
[X] T035 Create `MediBridge.Services/DTOs/Auth/VerificationMetadataDto.cs` with `DocumentType`, `OriginalFileName`, `ContentType`, `SizeBytes`, and `Reference` properties.
[X] T036 Create `MediBridge.Services/DTOs/Auth/AuthResultDto.cs` with `AccessToken`, `RefreshToken`, `UserRole Role`, and `DateTime ExpiresAtUtc`.
[X] T037 Create `MediBridge.Services/DTOs/Auth/RegistrationResultDto.cs` with `UserId`, `UserRole Role`, and `AccountStatus AccountStatus`.
[X] T038 Create `MediBridge.Services/Interfaces/IAuthService.cs` with methods for register doctor, register company, login, refresh, logout, forgot password, reset password, verify contact, and resubmit registration.
[X] T039 Create `MediBridge.Services/Interfaces/IAdminAccountService.cs` with methods for paged pending account listing and account decisions.
[X] T040 Create `MediBridge.APIs/Config/JwtOptions.cs` with `Issuer`, `Audience`, `SigningKey`, `AccessTokenMinutes`, and `RefreshTokenDays`.
[X] T041 Create `MediBridge.Repository/Data/Identity/MediBridgeIdentityUser.cs` extending `IdentityUser` with Phase 2 account fields and mapping support to/from the Core `ApplicationUser` domain model.
[X] T153 Create `MediBridge.Repository/Data/MediBridgeDbContext.cs` inheriting from `IdentityDbContext<MediBridgeIdentityUser>` and exposing `DbSet` properties for every Phase 2 persistence entity while keeping Core free of ASP.NET Identity dependencies.
[X] T042 Create EF Core configuration files in `MediBridge.Repository/Configurations/Identity/` for `MediBridgeIdentityUser`, `DoctorProfile`, `CompanyProfile`, `RefreshCredential`, `PasswordResetFlow`, `ContactVerificationFlow`, `AdminAccountDecision`, `AccountResubmission`, `AccountResubmissionToken`, and `AuthenticationAuditEvent`; do not configure Core `ApplicationUser` as an ASP.NET Identity entity.
[X] T043 Configure uniqueness in EF configurations: unique active email where supported by Identity, unique phone when provided, unique Company `LicenseNumber`, unique `RefreshCredential.TokenHash`, unique reset/verification token hashes.
[X] T044 Configure relationships and delete behavior in EF configurations so profile/token/decision/audit rows preserve audit history and do not cascade hard-delete financial or audit data.
[X] T045 Create `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs` to register `MediBridgeDbContext`, Identity stores, repository implementations, and `IIdentityUnitOfWork`.
[X] T046 Create `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs` to register `IAuthService`, `IAdminAccountService`, validators, and token helpers.
[X] T047 Update `MediBridge.APIs/Program.cs` to call the Repository and Services identity registration extensions before `builder.Build()`.
[X] T048 Update `MediBridge.APIs/Program.cs` to call `app.UseAuthentication()` before `app.UseAuthorization()`.
[X] T049 Update `MediBridge.APIs/appsettings.json` with empty-safe `Jwt` settings keys and no secrets committed.
[X] T050 Update `MediBridge.APIs/appsettings.Development.json` with development-only placeholder JWT settings suitable for local tests, not production secrets.
[X] T051 Create an EF Core migration for Phase 2 identity schema in `MediBridge.Repository/Migrations/` using `dotnet ef migrations add Phase2IdentityApproval --project .\MediBridge.Repository --startup-project .\MediBridge.APIs`.
[X] T052 Run `dotnet build .\MediBridge.slnx` and fix compile errors before starting user-story tasks.
[X] T154 Create `MediBridge.Core/Entities/Identity/AccountResubmissionToken.cs` with `Id`, `UserId`, `ApplicationUser User`, `TokenHash`, `ExpiresAtUtc`, nullable `ConsumedAtUtc`, `CreatedAtUtc`, and `CreatedByAdminDecisionId`.
[X] T155 Create `MediBridge.Core/Interfaces/Identity/IPasswordResetFlowRepository.cs` with methods to add reset flows, find unconsumed flow by token hash, and mark flow consumed.
[X] T156 Create `MediBridge.Core/Interfaces/Identity/IContactVerificationFlowRepository.cs` with methods to add verification flows, find unconsumed flow by token hash, and mark flow consumed.
[X] T157 Create `MediBridge.Core/Interfaces/Identity/IAdminAccountDecisionRepository.cs` with methods to add decisions and query decisions by target user id.
[X] T158 Create `MediBridge.Core/Interfaces/Identity/IAccountResubmissionRepository.cs` with methods to add resubmissions and query resubmissions by user id.
[X] T159 Create `MediBridge.Core/Interfaces/Identity/IAccountResubmissionTokenRepository.cs` with methods to add resubmission tokens, find unconsumed token by token hash, and mark token consumed.
[X] T160 Create `MediBridge.Core/Interfaces/Identity/IAuthenticationAuditEventRepository.cs` with methods to append audit events and query audit events by target user id for tests.
[X] T161 Update `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs` to register implementations for refresh credentials, profiles, password reset flows, contact verification flows, admin account decisions, account resubmissions, account resubmission tokens, authentication audit events, and `IIdentityUnitOfWork`.
[X] T162 Create an explicit temporary Admin bootstrap path in `MediBridge.Repository/Data/Identity/AdminIdentitySeed.cs` or an equivalent controlled seed component so `FR-004` is satisfied before Admin approval endpoints are exercised; the seed must create or identify an Admin account without adding public Admin self-registration.
[X] T163 In `MediBridge.Services/Services/AdminAccountService.cs`, when `ApplyDecisionAsync` rejects an account, generate a one-time plaintext resubmission token for delivery by the approved Phase 2 mechanism, store only its hash in `AccountResubmissionToken`, set an expiry, link it to `AdminAccountDecision`, and never persist the plaintext token.

---

## Phase 3: User Story 1 - Pending Account Registration (Priority: P1) MVP

**Goal**: Doctor and Company users can register with profile and verification metadata, and accounts are created with `AccountStatus.Pending` without receiving access credentials.

**Independent Test**: Submit Doctor and Company registrations, confirm `{ Code, Message, Data }` responses contain `Pending`, confirm duplicates and missing metadata fail, and confirm no approved account or token is created.

**Manual Senior Review 2026-06-02**: Phase 3 pending registration remains complete. Architecture review confirms controllers delegate only to service interfaces, Services own registration decisions through Core abstractions, Repository owns Identity/EF persistence, pending Doctor/Company profiles persist verification metadata only, duplicate email/phone/license paths stay service-level, and registration does not issue access or refresh tokens.

### Tests for User Story 1

- [X] T053 [P] [US1] Add contract tests for `POST /api/auth/register-doctor` success, validation failure, duplicate email, and envelope shape in `tests/contract/MediBridge.ContractTests/IdentityRegistrationContractTests.cs`.
- [X] T054 [P] [US1] Add contract tests for `POST /api/auth/register-company` success, duplicate license, validation failure, and envelope shape in `tests/contract/MediBridge.ContractTests/CompanyRegistrationContractTests.cs`.
- [X] T055 [P] [US1] Add integration tests proving Doctor registration creates `ApplicationUser.AccountStatus = Pending` and a `DoctorProfile` with metadata-only verification fields in `tests/integration/MediBridge.IntegrationTests/DoctorRegistrationIntegrationTests.cs`.
- [X] T056 [P] [US1] Add integration tests proving Company registration creates `ApplicationUser.AccountStatus = Pending`, a `CompanyProfile`, and enforces unique `LicenseNumber` in `tests/integration/MediBridge.IntegrationTests/CompanyRegistrationIntegrationTests.cs`.
- [X] T057 [P] [US1] Add unit tests for registration validation rules in `tests/unit/MediBridge.UnitTests/RegistrationValidationTests.cs`.

### Implementation for User Story 1

- [X] T058 [P] [US1] Create `MediBridge.Services/DTOs/Auth/RegisterDoctorRequestDto.cs` with email, password, phone number, specialization, experience years, location, and `VerificationMetadataDto`.
- [X] T059 [P] [US1] Create `MediBridge.Services/DTOs/Auth/RegisterCompanyRequestDto.cs` with email, password, phone number, company name, license number, contact name, and `VerificationMetadataDto`.
- [X] T060 [P] [US1] Create `MediBridge.Services/Validators/Auth/RegisterDoctorRequestValidator.cs` validating required fields, email format, password minimum 8 characters, non-negative experience, positive metadata size, and non-empty metadata values.
- [X] T061 [P] [US1] Create `MediBridge.Services/Validators/Auth/RegisterCompanyRequestValidator.cs` validating required fields, email format, password minimum 8 characters, license number, positive metadata size, and non-empty metadata values.
- [X] T062 [US1] Implement `RegisterDoctorAsync` in `MediBridge.Services/Services/AuthService.cs` to create an `ApplicationUser` with role `Doctor`, account status `Pending`, soft-delete false, and a `DoctorProfile` in one unit of work.
- [X] T063 [US1] Implement `RegisterCompanyAsync` in `MediBridge.Services/Services/AuthService.cs` to create an `ApplicationUser` with role `Company`, account status `Pending`, soft-delete false, and a `CompanyProfile` in one unit of work.
- [X] T064 [US1] Add duplicate detection in `MediBridge.Services/Services/AuthService.cs` for email, phone when provided, and company license, returning conflict results through service DTOs instead of throwing raw exceptions.
- [X] T065 [US1] Add registration audit events in `MediBridge.Services/Services/AuthService.cs` through `IAuditLogger` or Phase 2 audit persistence, without storing passwords, tokens, request bodies, or response bodies.
- [X] T066 [US1] Create `MediBridge.APIs/Controllers/AuthController.cs` with `POST /api/auth/register-doctor` and `POST /api/auth/register-company`; controller must inject only `MediBridge.Services.Interfaces.IAuthService`.
- [X] T067 [US1] Apply the Phase 1 registration rate-limit policy to registration actions in `MediBridge.APIs/Controllers/AuthController.cs`.
- [X] T068 [US1] Ensure registration endpoints return the standard envelope via existing API envelope helpers and preserve HTTP `201`, `400`, `409`, and `429` semantics.
- [X] T069 [US1] Run `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter Registration` and confirm registration contract tests pass.
- [X] T070 [US1] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter Registration` and confirm registration integration tests pass.

---

## Phase 4: User Story 2 - Approved Login and Token Lifecycle (Priority: P1)

**Goal**: Approved users can log in, refresh, and log out securely; pending/rejected/suspended/inactive/deleted users cannot receive tokens; refresh tokens rotate and detect reuse.

**Independent Test**: Create users with each account status, attempt login/refresh/logout/reuse flows, and verify token issuance/revocation outcomes.

**Manual Senior Review 2026-06-02**: Phase 4 approved login and token lifecycle is complete. Manual architecture review confirms controllers remain HTTP/envelope-only, Services own login/refresh/logout decisions through Core contracts, Repository owns SQL Server/EF Core token locking and revocation, and Core remains infrastructure-free. Async review confirms token lifecycle mutations run through `IIdentityUnitOfWork.ExecuteInTransactionAsync`; refresh rotation uses row-level update locking, reuse detection revokes active family credentials, and logout now revokes the full refresh family for the authenticated user. Verification passed with `dotnet build .\MediBridge.slnx`, `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Login|Refresh|Logout"`, related configuration/logging regression tests, and architecture/scope guard tests.

### Tests for User Story 2

- [X] T071 [P] [US2] Add contract tests for `POST /api/auth/login`, `POST /api/auth/refresh`, and `POST /api/auth/logout` envelope and status semantics in `tests/contract/MediBridge.ContractTests/AuthTokenContractTests.cs`.
- [X] T072 [P] [US2] Add integration tests proving login denies `Pending`, `Rejected`, `Suspended`, `Inactive`, and soft-deleted users in `tests/integration/MediBridge.IntegrationTests/LoginApprovalGateTests.cs`.
- [X] T073 [P] [US2] Add integration tests proving `Approved` users receive role-bearing access tokens and refresh tokens in `tests/integration/MediBridge.IntegrationTests/ApprovedLoginIntegrationTests.cs`.
- [X] T074 [P] [US2] Add integration tests proving refresh token rotation makes the previous token unusable in `tests/integration/MediBridge.IntegrationTests/RefreshTokenRotationTests.cs`.
- [X] T075 [P] [US2] Add integration tests proving reuse of a rotated/revoked refresh token revokes the active token family in `tests/integration/MediBridge.IntegrationTests/RefreshTokenReuseDetectionTests.cs`.
- [X] T076 [P] [US2] Add integration tests proving logout revokes the submitted refresh token in `tests/integration/MediBridge.IntegrationTests/LogoutRevocationTests.cs`.
- [X] T077 [P] [US2] Add unit tests for token lifecycle service decisions in `tests/unit/MediBridge.UnitTests/RefreshCredentialLifecycleTests.cs`.

### Implementation for User Story 2

- [X] T078 [P] [US2] Create `MediBridge.Services/DTOs/Auth/LoginRequestDto.cs` with username and password.
- [X] T079 [P] [US2] Create `MediBridge.Services/DTOs/Auth/RefreshRequestDto.cs` with refresh token.
- [X] T080 [P] [US2] Create `MediBridge.Services/Validators/Auth/LoginRequestValidator.cs` validating username and password.
- [X] T081 [P] [US2] Create `MediBridge.Services/Validators/Auth/RefreshRequestValidator.cs` validating refresh token is present.
- [X] T082 [US2] Implement `MediBridge.Services/Services/AuthTokenService.cs` for JWT access-token creation, refresh-token generation, and token hashing; never store plaintext refresh tokens.
- [X] T083 [US2] Implement `LoginAsync` in `MediBridge.Services/Services/AuthService.cs` to validate credentials, require `AccountStatus.Approved`, require `IsDeleted = false`, issue role-bearing access token, create refresh credential, and audit success/denial.
- [X] T084 [US2] Implement `RefreshAsync` in `MediBridge.Services/Services/AuthService.cs` to verify refresh token hash, require active credential and approved user, revoke presented credential with reason `Rotated`, create replacement credential, and commit atomically.
- [X] T085 [US2] Implement refresh-token reuse detection in `MediBridge.Services/Services/AuthService.cs`: if a revoked/replaced credential is presented, revoke all active credentials in the same family and audit `RefreshReuseDetected`.
- [X] T086 [US2] Implement `LogoutAsync` in `MediBridge.Services/Services/AuthService.cs` to revoke the submitted refresh credential for the authenticated user session.
- [X] T087 [US2] Add `POST /api/auth/login`, `POST /api/auth/refresh`, and `POST /api/auth/logout` actions to `MediBridge.APIs/Controllers/AuthController.cs`.
- [X] T088 [US2] Apply login, refresh, and logout authorization/rate-limit rules in `MediBridge.APIs/Controllers/AuthController.cs`; logout must require JWT.
- [X] T089 [US2] Configure JWT bearer authentication and role claims in `MediBridge.APIs/Program.cs` or `MediBridge.APIs/Extensions/FoundationServiceCollectionExtensions.cs` without breaking Phase 1 tests.
- [X] T090 [US2] Ensure login/refresh/logout endpoints use standard envelope and preserve HTTP `200`, `400`, `401`, `403`, `409`, and `429` semantics from `contracts/identity-approval-api.yaml`.
- [X] T091 [US2] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Login|Refresh|Logout"` and confirm token lifecycle tests pass.

---

## Phase 5: User Story 3 - Role-Aware Access Control (Priority: P2)

**Goal**: Secured routes distinguish anonymous, Doctor, Company, and Admin users correctly.

**Independent Test**: Exercise representative secured routes with anonymous, wrong-role, and correct-role principals.

**Manual Senior Review 2026-06-02**: Phase 5 role-aware access control is complete. Manual review confirms JWTs emit the raw `role` claim used by bearer validation, authorization policies map exactly to `Admin`, `Doctor`, and `Company`, Admin account endpoints are protected by the Admin-only policy, Auth endpoint anonymity/JWT requirements match the contract, controllers remain service-interface-only, and async controller paths await service calls without blocking or fire-and-forget work. Verification passed with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter RoleAuthorization` and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter LayeringBoundaryTests`.

### Tests for User Story 3

- [X] T092 [P] [US3] Add integration tests for anonymous access denial on Admin account endpoints in `tests/integration/MediBridge.IntegrationTests/RoleAuthorizationIntegrationTests.cs`.
- [X] T093 [P] [US3] Add integration tests proving Doctor and Company tokens cannot access Admin account endpoints in `tests/integration/MediBridge.IntegrationTests/RoleAuthorizationIntegrationTests.cs`.
- [X] T094 [P] [US3] Add integration tests proving Admin tokens can access Admin account endpoints in `tests/integration/MediBridge.IntegrationTests/RoleAuthorizationIntegrationTests.cs`.
- [X] T095 [P] [US3] Extend `tests/integration/MediBridge.IntegrationTests/LayeringBoundaryTests.cs` if needed so new controllers are verified to depend only on `MediBridge.Services.Interfaces`.

### Implementation for User Story 3

- [X] T096 [US3] Add role policy constants for `Admin`, `Doctor`, and `Company` in `MediBridge.APIs/Security/AuthorizationPolicies.cs`.
- [X] T097 [US3] Register role-aware authorization policies in `MediBridge.APIs/Program.cs` or an existing API extension file.
- [X] T098 [US3] Add `[Authorize(Roles = "Admin")]` or equivalent Admin policy to Admin account endpoints in `MediBridge.APIs/Controllers/AdminAccountsController.cs`.
- [X] T099 [US3] Ensure `AuthController` authorization attributes match the contract exactly: registration, login, refresh, forgot password, reset password, verify contact, and rejected-account resubmission remain public endpoints; logout requires JWT; rejected-account resubmission is protected only by the required time-limited single-use `resubmissionToken`, not by JWT.
- [X] T100 [US3] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter RoleAuthorization` and confirm authorization tests pass.

---

## Phase 6: User Story 4 - Admin Account Approval and Rejected Resubmission (Priority: P2)

**Goal**: Admin can list pending accounts, approve/reject/suspend/inactivate/reactivate accounts, audit decisions, revoke tokens on suspension, and rejected users can resubmit corrected metadata back to `Pending`.

**Independent Test**: Admin reviews pending accounts, applies each decision type, verifies account status and audit records, verifies rejected resubmission, and verifies login behavior changes accordingly.

**Manual Senior Review 2026-06-02**: Phase 6 admin approval and rejected resubmission are complete. Manual review confirms controllers remain HTTP-only, Services own approval/resubmission business rules, Repository owns EF Core/SQL Server and ASP.NET Identity infrastructure, resubmission tokens are stored hashed and consumed through update-lock transactional reads, suspension/inactivation revokes active refresh credentials, invalid account-status transitions are rejected before decision/audit/token side effects, and async paths are awaited without sync-over-async or fire-and-forget work. Verification passed with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Admin|Resubmission"`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "Admin|Resubmission"`, and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "LayeringBoundaryTests|IdentityScopeGuardTests|RoleAuthorization"`.

### Tests for User Story 4

- [X] T101 [P] [US4] Add contract tests for `GET /api/admin/pending-accounts` response envelope, pagination fields, and Admin authorization in `tests/contract/MediBridge.ContractTests/AdminPendingAccountsContractTests.cs`.
- [X] T102 [P] [US4] Add contract tests for `PUT /api/admin/accounts/{id}/decision` approve/reject/suspend/reactivate request/response envelopes in `tests/contract/MediBridge.ContractTests/AdminAccountDecisionContractTests.cs`.
- [X] T103 [P] [US4] Add contract tests for public token-based `POST /api/auth/resubmit-registration` response envelope, required `resubmissionToken`, and status semantics in `tests/contract/MediBridge.ContractTests/AccountResubmissionContractTests.cs`.
- [X] T104 [P] [US4] Add integration tests for pending account listing with `PageNumber` and `PageSize` in `tests/integration/MediBridge.IntegrationTests/AdminPendingAccountsIntegrationTests.cs`.
- [X] T105 [P] [US4] Add integration tests proving approval changes account status to `Approved` and enables next login in `tests/integration/MediBridge.IntegrationTests/AdminApprovalIntegrationTests.cs`.
- [X] T106 [P] [US4] Add integration tests proving rejection requires a reason, changes account status to `Rejected`, preserves denial on login, and stores decision reason in `tests/integration/MediBridge.IntegrationTests/AdminRejectionIntegrationTests.cs`.
- [X] T107 [P] [US4] Add integration tests proving suspension changes account status to `Suspended` and revokes active refresh credentials in `tests/integration/MediBridge.IntegrationTests/AdminSuspensionIntegrationTests.cs`.
- [X] T108 [P] [US4] Add integration tests proving rejected account resubmission with a valid time-limited single-use resubmission token updates metadata, consumes the token, changes status to `Pending`, and still denies token issuance in `tests/integration/MediBridge.IntegrationTests/AccountResubmissionIntegrationTests.cs`.

### Implementation for User Story 4

- [X] T109 [P] [US4] Create `MediBridge.Services/DTOs/Admin/PendingAccountDto.cs` with user id, role, account status, submitted timestamp, and verification metadata.
- [X] T110 [P] [US4] Create `MediBridge.Services/DTOs/Admin/PendingAccountPageDto.cs` with items, page number, page size, and total count.
- [X] T111 [P] [US4] Create `MediBridge.Services/DTOs/Admin/AdminAccountDecisionRequestDto.cs` with decision, reason, and notes.
- [X] T112 [P] [US4] Create `MediBridge.Services/DTOs/Admin/AccountDecisionResultDto.cs` with user id and resulting account status.
- [X] T113 [P] [US4] Create `MediBridge.Services/DTOs/Auth/ResubmissionRequestDto.cs` with required `ResubmissionToken`, optional Doctor/Company profile fields, and required `VerificationMetadataDto`.
- [X] T114 [P] [US4] Create `MediBridge.Services/Validators/Admin/AdminAccountDecisionRequestValidator.cs` requiring reason for `Reject` and `Suspend`.
- [X] T115 [P] [US4] Create `MediBridge.Services/Validators/Auth/ResubmissionRequestValidator.cs` requiring verification metadata and validating positive metadata size.
- [X] T116 [US4] Implement `ListPendingAccountsAsync` in `MediBridge.Services/Services/AdminAccountService.cs` with standard `PageNumber`/`PageSize` defaults and maximum `PageSize = 100`.
- [X] T117 [US4] Implement `ApplyDecisionAsync` in `MediBridge.Services/Services/AdminAccountService.cs` to map decisions to account statuses exactly as `data-model.md` states.
- [X] T118 [US4] In `ApplyDecisionAsync`, require `Reject` and `Suspend` reasons; return validation/business result instead of throwing raw exceptions.
- [X] T119 [US4] In `ApplyDecisionAsync`, revoke active refresh credentials when decision is `Suspend` or `Inactivate`.
- [X] T120 [US4] In `ApplyDecisionAsync`, create `AdminAccountDecision` and `AuthenticationAuditEvent` records in the same unit of work as account status changes.
- [X] T121 [US4] Implement `ResubmitRegistrationAsync` in `MediBridge.Services/Services/AuthService.cs` allowing only valid unexpired unconsumed resubmission tokens for `Rejected` Doctor/Company accounts, updating corrected metadata, consuming the token, creating `AccountResubmission`, changing status to `Pending`, and not issuing tokens.
- [X] T122 [US4] Create `MediBridge.APIs/Controllers/AdminAccountsController.cs` with `GET /api/admin/pending-accounts` and `PUT /api/admin/accounts/{id}/decision`; controller must inject only `IAdminAccountService`.
- [X] T123 [US4] Add `POST /api/auth/resubmit-registration` to `MediBridge.APIs/Controllers/AuthController.cs` as a public token-based endpoint that accepts a time-limited single-use resubmission token; do not require JWT because rejected accounts cannot receive access tokens.
- [X] T124 [US4] Ensure Admin endpoints and resubmission endpoint use the standard envelope and preserve HTTP `200`, `400`, `401`, `403`, `404`, and `429` semantics from the contract.
- [X] T125 [US4] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Admin|Resubmission"` and confirm account approval tests pass.

---

## Phase 7: User Story 5 - Account Recovery and Contact Verification Readiness (Priority: P3)

**Goal**: Password reset and email/phone verification flows are time-limited, single-use, auditable, provider-stub-ready, and do not block token issuance for `Approved` accounts in Phase 2.

**Independent Test**: Request reset for known/unknown contacts, confirm non-enumerating response, complete reset once, verify refresh revocation, complete contact verification once, and confirm replay fails.

**Manual Senior Review 2026-06-03**: Phase 7 account recovery and contact verification are complete. Manual review confirms `AuthController` stays HTTP-only and delegates to `IAuthService`, `AuthService` owns recovery and verification orchestration through Core unit-of-work abstractions, Repository owns EF Core/SQL Server token-flow persistence, reset and verification token consumption uses transactional update locks with expiry checks, password reset revokes active refresh credentials, contact verification updates only email/phone verification flags, incomplete contact verification does not gate approved login, and async paths are awaited without sync-over-async or fire-and-forget work. Verification passed with `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Password|Verification|Forgot"`, `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "AccountRecoveryContractTests"`, and `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "LayeringBoundaryTests|IdentityScopeGuardTests"`.

### Tests for User Story 5

- [X] T126 [P] [US5] Add contract tests for `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, and `POST /api/auth/verify-contact` envelope and status semantics in `tests/contract/MediBridge.ContractTests/AccountRecoveryContractTests.cs`.
- [X] T127 [P] [US5] Add integration tests proving forgot-password returns the same accepted envelope for known and unknown contacts in `tests/integration/MediBridge.IntegrationTests/ForgotPasswordEnumerationTests.cs`.
- [X] T128 [P] [US5] Add integration tests proving valid password reset consumes the flow and revokes active refresh credentials in `tests/integration/MediBridge.IntegrationTests/PasswordResetIntegrationTests.cs`.
- [X] T129 [P] [US5] Add integration tests proving reset token replay and expired reset tokens are rejected in `tests/integration/MediBridge.IntegrationTests/PasswordResetReplayTests.cs`.
- [X] T130 [P] [US5] Add integration tests proving contact verification consumes once, sets `EmailVerified` or `PhoneVerified`, and rejects replay in `tests/integration/MediBridge.IntegrationTests/ContactVerificationIntegrationTests.cs`.
- [X] T131 [P] [US5] Add integration tests proving incomplete contact verification does not block login for `Approved` accounts in `tests/integration/MediBridge.IntegrationTests/ContactVerificationTokenGateTests.cs`.

### Implementation for User Story 5

- [X] T132 [P] [US5] Create `MediBridge.Services/DTOs/Auth/ForgotPasswordRequestDto.cs` with contact property.
- [X] T133 [P] [US5] Create `MediBridge.Services/DTOs/Auth/ResetPasswordRequestDto.cs` with reset token and new password.
- [X] T134 [P] [US5] Create `MediBridge.Services/DTOs/Auth/VerifyContactRequestDto.cs` with channel and verification token.
- [X] T135 [P] [US5] Create `MediBridge.Services/Validators/Auth/ForgotPasswordRequestValidator.cs` validating contact is present.
- [X] T136 [P] [US5] Create `MediBridge.Services/Validators/Auth/ResetPasswordRequestValidator.cs` validating reset token and password minimum 8 characters.
- [X] T137 [P] [US5] Create `MediBridge.Services/Validators/Auth/VerifyContactRequestValidator.cs` validating channel and token.
- [X] T138 [US5] Implement `ForgotPasswordAsync` in `MediBridge.Services/Services/AuthService.cs` so public response never reveals whether the account exists.
- [X] T139 [US5] Implement `ResetPasswordAsync` in `MediBridge.Services/Services/AuthService.cs` to consume valid reset flows once, update password through Identity, revoke active refresh credentials, and audit completion.
- [X] T140 [US5] Implement `VerifyContactAsync` in `MediBridge.Services/Services/AuthService.cs` to consume valid verification flows once, set `EmailVerified` or `PhoneVerified`, and audit completion.
- [X] T141 [US5] Add `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`, and `POST /api/auth/verify-contact` actions to `MediBridge.APIs/Controllers/AuthController.cs`.
- [X] T142 [US5] Apply Phase 1 rate-limit policies to forgot-password, reset-password, and verify-contact actions in `MediBridge.APIs/Controllers/AuthController.cs`.
- [X] T143 [US5] Ensure no login code checks `EmailVerified` or `PhoneVerified` as a Phase 2 token gate; only `AccountStatus.Approved` and not-deleted state gate token issuance.
- [X] T144 [US5] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Password|Verification|Forgot"` and confirm recovery/verification tests pass.

---

## Phase 8: Polish, Scope Guards, and Constitution Compliance

**Purpose**: Prove Phase 2 is complete, constitution-compliant, and has not leaked later-phase behavior.

- [X] T145 [P] Add or update architecture tests in `tests/integration/MediBridge.IntegrationTests/LayeringBoundaryTests.cs` to confirm `AuthController` and `AdminAccountsController` depend only on `MediBridge.Services.Interfaces`.
- [X] T146 [P] Add integration tests in `tests/integration/MediBridge.IntegrationTests/IdentityScopeGuardTests.cs` scanning routes/source markers to confirm Phase 2 did not implement campaign, queue, delivery, wallet, settlement, payout, file content upload/storage/review, pricing, or platform-fee workflows.
- [X] T147 [P] Add audit safety tests in `tests/integration/MediBridge.IntegrationTests/AuthAuditSafetyTests.cs` confirming audit events do not contain plaintext passwords, plaintext refresh tokens, request bodies, or response bodies.
- [X] T148 [P] Add performance smoke tests or documented QA script updates under `tests/performance/phase2-identity-smoke.ps1` to exercise registration, login, refresh, logout, reset, verification, and approval within the spec’s 2-second QA target.
- [X] T149 Update `specs/002-identity-approval/quickstart.md` if endpoint names or local smoke commands changed during implementation.
- [X] T150 Run `dotnet format .\MediBridge.slnx --verify-no-changes`; fix formatting issues if the command reports any.
- [X] T151 Run `dotnet test .\MediBridge.slnx`; all contract, integration, and unit tests must pass before implementation is considered complete.
- [X] T152 Run a final constitution review against `.specify/memory/constitution.md` and document any deviations in `specs/002-identity-approval/plan.md`; expected result is no deviations.

---

## Dependencies and Execution Order

### Phase Dependencies

- Phase 1 Setup has no dependencies.
- Phase 2 Foundational depends on Phase 1 and blocks all user stories, including remediation tasks T153-T163.
- Temporary architecture-build pass may implement Phase 1 and Phase 2 infrastructure before behavior tests. Do not mark Phase 2 complete until T162 and T163 are also addressed.
- Phase 3 US1 depends on Phase 2 and is the MVP.
- Phase 4 US2 depends on Phase 2 and uses registered/approved users from US1 fixtures in tests.
- Phase 5 US3 depends on Phase 2 and can proceed after token generation support from US2 is available.
- Phase 6 US4 depends on Phase 2 and should run after US1 registration tasks exist; Admin login tests may depend on US2 token issuance.
- Phase 7 US5 depends on Phase 2 and uses token revocation behavior from US2 for reset tests.
- Phase 8 Polish depends on selected user stories being complete.

### User Story Dependencies

- **US1 Pending Account Registration**: MVP; can start immediately after Foundational.
- **US2 Approved Login and Token Lifecycle**: Depends on Foundational; test fixtures may create users directly until US1 endpoints are complete, but final integration should use registered users where practical.
- **US3 Role-Aware Access Control**: Depends on JWT token issuance from US2.
- **US4 Admin Account Approval**: Depends on US1 registered pending accounts and US2 Admin token support.
- **US5 Account Recovery and Contact Verification**: Depends on token/revocation infrastructure from US2.

### Parallel Opportunities

- T004-T015 can run in parallel after package references are checked.
- T017-T040 and T154-T160 are mostly independent Core/DTO/interface files and can be parallelized.
- T053-T057 can be written in parallel because they create separate test files.
- T058-T061 can be written in parallel because they create separate DTO/validator files.
- T071-T077 can be written in parallel because they create separate test files.
- T101-T108 can be written in parallel because they create separate test files.
- T126-T131 can be written in parallel because they create separate test files.
- T145-T148 can be written in parallel after implementation is complete.

---

## Parallel Execution Examples

### User Story 1

```text
Task: T053 Contract tests for Doctor registration in tests/contract/MediBridge.ContractTests/IdentityRegistrationContractTests.cs
Task: T054 Contract tests for Company registration in tests/contract/MediBridge.ContractTests/CompanyRegistrationContractTests.cs
Task: T055 Doctor registration integration tests in tests/integration/MediBridge.IntegrationTests/DoctorRegistrationIntegrationTests.cs
Task: T056 Company registration integration tests in tests/integration/MediBridge.IntegrationTests/CompanyRegistrationIntegrationTests.cs
Task: T057 Registration validation unit tests in tests/unit/MediBridge.UnitTests/RegistrationValidationTests.cs
```

### User Story 2

```text
Task: T072 Login approval gate integration tests in tests/integration/MediBridge.IntegrationTests/LoginApprovalGateTests.cs
Task: T074 Refresh rotation tests in tests/integration/MediBridge.IntegrationTests/RefreshTokenRotationTests.cs
Task: T075 Refresh reuse detection tests in tests/integration/MediBridge.IntegrationTests/RefreshTokenReuseDetectionTests.cs
Task: T076 Logout revocation tests in tests/integration/MediBridge.IntegrationTests/LogoutRevocationTests.cs
```

### User Story 4

```text
Task: T104 Pending account pagination tests in tests/integration/MediBridge.IntegrationTests/AdminPendingAccountsIntegrationTests.cs
Task: T105 Admin approval tests in tests/integration/MediBridge.IntegrationTests/AdminApprovalIntegrationTests.cs
Task: T106 Admin rejection tests in tests/integration/MediBridge.IntegrationTests/AdminRejectionIntegrationTests.cs
Task: T108 Account resubmission tests in tests/integration/MediBridge.IntegrationTests/AccountResubmissionIntegrationTests.cs
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 Setup.
2. Complete Phase 2 Foundational identity model and infrastructure.
3. Complete Phase 3 US1 registration.
4. Stop and validate: Doctor and Company registrations create `Pending` accounts and profiles, validation failures are clean, and no token is issued.

### Secure Incremental Delivery

1. Add US2 token lifecycle after US1 so the approval gate can be exercised.
2. Add US3 role authorization once tokens carry roles.
3. Add US4 Admin approval and resubmission so accounts can move through the lifecycle.
4. Add US5 recovery and contact verification after revocation mechanics are stable.
5. Run Phase 8 constitution and scope guards before any implementation handoff.

### Execution Rules for Smaller Models

- Do not invent endpoint paths. Use `specs/002-identity-approval/contracts/identity-approval-api.yaml`.
- Do not invent account states. Use only `Pending`, `Approved`, `Rejected`, `Suspended`, and `Inactive`.
- Do not store plaintext refresh tokens, reset tokens, verification tokens, resubmission tokens, passwords, request bodies, or response bodies.
- Do not implement actual file upload/storage/review in Phase 2; store verification metadata only.
- Do not implement queue, wallet, campaign, delivery, settlement, payout, pricing, or platform-fee behavior.
- Do not put business rules in controllers. Controllers call service interfaces and return envelopes.
- Do not access EF Core or repositories from controllers.
- Do not reference ASP.NET Identity or EF Core framework types from `MediBridge.Core`; keep Identity and EF Core infrastructure in `MediBridge.Repository` or API wiring.
- Do not introduce any persistence provider other than SQL Server through EF Core.
- Do not require JWT for rejected-account resubmission; use the single-use resubmission token flow.
- Do not skip tests for security decisions; every token/status transition must have test coverage.
