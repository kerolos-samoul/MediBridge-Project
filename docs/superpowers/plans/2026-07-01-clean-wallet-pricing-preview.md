# Clean Wallet, Pricing, Preview, and Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reimplement the useful missing wallet checkout, admin pricing, campaign preview, queue summary, and route-compatibility behavior on top of the current `development` architecture.

**Architecture:** Extend the existing wallet and campaign services without replacing them. Persist mock payments through a new repository owned by `MediBridge.Repository`, reuse existing wallet transaction/ledger idempotency, reuse doctor eligibility and price-history persistence, and expose thin HTTP controllers. Campaign submission and review remain affordability-only and must not reserve, release, or otherwise mutate wallet balances.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core Web API, FluentValidation, Entity Framework Core, SQL Server, xUnit.

---

## File Map

- `MediBridge.Core/Entities/Payments/MockPaymentTransaction.cs`: persisted mock checkout record.
- `MediBridge.Core/Interfaces/Payments/IPaymentRepository.cs`: payment persistence abstraction.
- `MediBridge.Repository/Configurations/Payments/MockPaymentTransactionConfiguration.cs`: SQL schema, constraints, and indexes.
- `MediBridge.Repository/Repositories/Payments/PaymentRepository.cs`: payment queries and writes.
- `MediBridge.Repository/Data/MediBridgeDbContext.cs`: `MockPaymentTransactions` set.
- `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`: payment repository exposure.
- `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`: payment repository registration.
- `MediBridge.Services/Services/CompanyWalletService.cs`: wallet self-repair and mock checkout orchestration.
- `MediBridge.Services/Services/AdminPricingService.cs`: admin-only doctor price update and history write.
- `MediBridge.Services/Services/CampaignWorkflowService.cs`: target preview and owned queue summary.
- `MediBridge.APIs/Controllers/CompanyWalletController.cs`: mock checkout endpoint.
- `MediBridge.APIs/Controllers/AdminPricingController.cs`: doctor price endpoint.
- `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`: preview and summary endpoints.
- `MediBridge.APIs/Controllers/FilesController.cs`: compatibility aliases that delegate to `IFileWorkflowService`.
- `MediBridge.APIs/Controllers/AdminFilesController.cs`: admin campaign-asset review alias.
- `tests/unit/MediBridge.UnitTests`: entity and validator tests.
- `tests/contract/MediBridge.ContractTests`: route and envelope tests.
- `tests/integration/MediBridge.IntegrationTests`: real SQL persistence and workflow tests.

### Task 1: Mock Payment Contract and Persistence Tests

**Files:**
- Create: `tests/unit/MediBridge.UnitTests/Wallets/MockPaymentTransactionTests.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/MockCheckoutIntegrationTests.cs`
- Modify: `tests/contract/MediBridge.ContractTests/CompanyWalletContractTests.cs`

- [ ] **Step 1: Write failing entity tests**

Cover positive two-decimal EGP amounts, succeeded-only status, non-negative before/after balances, and no provider credential fields.

- [ ] **Step 2: Write failing integration tests**

Exercise:

```csharp
POST /api/company/wallet/mock-checkout
Idempotency-Key: checkout-{guid}
{ "amount": 150.25, "currency": "EGP" }
```

Assert one wallet, one `WalletTransaction`, one available-credit ledger entry, one `MockPaymentTransaction`, replay returns the original payment, conflicting replay returns 409, and an approved company without a wallet is repaired inside the transaction.

- [ ] **Step 3: Verify RED**

Run:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter MockPaymentTransactionTests
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter MockCheckoutIntegrationTests
```

Expected: compile or route failures because payment types and `/mock-checkout` do not exist.

### Task 2: Mock Payment Implementation and Migration

**Files:**
- Create: `MediBridge.Core/Entities/Payments/MockPaymentTransaction.cs`
- Create: `MediBridge.Core/Interfaces/Payments/IPaymentRepository.cs`
- Create: `MediBridge.Repository/Configurations/Payments/MockPaymentTransactionConfiguration.cs`
- Create: `MediBridge.Repository/Repositories/Payments/PaymentRepository.cs`
- Create: `MediBridge.Services/DTOs/Payments/MockTopUpRequestDto.cs`
- Create: `MediBridge.Services/DTOs/Payments/MockPaymentResultDto.cs`
- Create: `MediBridge.Services/Validators/Payments/MockTopUpRequestValidator.cs`
- Modify: `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`
- Modify: `MediBridge.Repository/Data/MediBridgeDbContext.cs`
- Modify: `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`
- Modify: `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`
- Modify: `MediBridge.Services/Interfaces/ICompanyWalletService.cs`
- Modify: `MediBridge.Services/Services/CompanyWalletService.cs`
- Modify: `MediBridge.APIs/Controllers/CompanyWalletController.cs`

- [ ] **Step 1: Add the payment model and repository**

Use a company-scoped unique idempotency key and link each payment to the wallet and wallet transaction:

```csharp
Task<MockPaymentTransaction?> FindByCompanyAndIdempotencyForUpdateAsync(
    string companyId,
    string idempotencyKey,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Extend the existing wallet service**

Add:

```csharp
Task<MockPaymentResultDto> CreateMockTopUpAsync(
    string actorUserId,
    string? idempotencyKey,
    MockTopUpRequestDto request,
    CancellationToken cancellationToken = default);
```

The method must reuse the existing `TopUp` transaction and ledger semantics, create missing approved-company wallets, preserve reserved balance, and never call an external payment provider.

- [ ] **Step 3: Add the thin controller route**

Return the standard envelope from `POST /api/company/wallet/mock-checkout`.

- [ ] **Step 4: Generate a fresh migration**

Run:

```powershell
dotnet ef migrations add AddMockPaymentTransactionsClean --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Inspect the migration to confirm it only creates `MockPaymentTransactions`, indexes, constraints, and foreign keys.

- [ ] **Step 5: Verify GREEN**

Run the Task 1 test commands and the existing `Phase5CompanyWalletIntegrationTests`.

### Task 3: Admin Doctor Pricing

**Files:**
- Create: `MediBridge.Services/DTOs/Pricing/SetDoctorPriceRequestDto.cs`
- Create: `MediBridge.Services/DTOs/Pricing/DoctorPriceDto.cs`
- Create: `MediBridge.Services/Validators/Pricing/SetDoctorPriceRequestDtoValidator.cs`
- Create: `MediBridge.Services/Interfaces/IAdminPricingService.cs`
- Create: `MediBridge.Services/Services/AdminPricingService.cs`
- Create: `MediBridge.APIs/Controllers/AdminPricingController.cs`
- Modify: `MediBridge.Core/Interfaces/Identity/IProfileRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs`
- Modify: `MediBridge.Core/Interfaces/Policies/IPolicyHistoryRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Policies/PolicyHistoryRepository.cs`
- Modify: `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/AdminDoctorPricingIntegrationTests.cs`
- Create: `tests/contract/MediBridge.ContractTests/AdminDoctorPricingContractTests.cs`

- [ ] **Step 1: Write failing pricing tests**

Assert that `PUT /api/admin/doctors/{doctorId}/price` accepts a positive two-decimal EGP price, updates an approved active doctor, appends price history with reason, rejects invalid values, hides unknown/ineligible doctors as 404, and rejects non-admin callers.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter AdminDoctorPricingIntegrationTests
```

Expected: 404 because the route is not registered.

- [ ] **Step 3: Add locked doctor lookup and history reason support**

Expose repository operations that load the doctor row for update and append `DoctorPriceHistory` through `IPolicyHistoryRepository`. Do not expose EF types outside `MediBridge.Repository`.

- [ ] **Step 4: Add service, validator, and controller**

The service performs admin and doctor eligibility checks inside one domain transaction. The controller only extracts the actor id and maps the envelope.

- [ ] **Step 5: Verify GREEN**

Run the pricing tests plus `Phase3PolicyHistoryTests`.

### Task 4: Campaign Target Preview and Queue Summary

**Files:**
- Create: `MediBridge.Services/DTOs/Campaigns/TargetPreviewDto.cs`
- Create: `MediBridge.Services/DTOs/Campaigns/QueueSummaryDto.cs`
- Modify: `MediBridge.Services/Interfaces/ICampaignWorkflowService.cs`
- Modify: `MediBridge.Services/Services/CampaignWorkflowService.cs`
- Modify: `MediBridge.Core/Interfaces/Messaging/IMessageQueueRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Messaging/MessageQueueRepository.cs`
- Modify: `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/CampaignPreviewAndQueueSummaryIntegrationTests.cs`
- Modify: `tests/contract/MediBridge.ContractTests/CompanyCampaignContractTests.cs`

- [ ] **Step 1: Write failing preview and summary tests**

Create campaigns through `POST /api/company/campaigns/drafts`. Assert:

```text
GET /api/company/campaigns/{campaignId}/target-preview
GET /api/company/campaigns/{campaignId}/queue-summary
```

Preview count and estimated cost must match the same eligible, positively priced doctors used by current submission. Queue summary must aggregate statuses without returning doctor IDs. Other companies receive 404.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter CampaignPreviewAndQueueSummaryIntegrationTests
```

Expected: 404 because the routes do not exist.

- [ ] **Step 3: Add service reads**

Add:

```csharp
Task<TargetPreviewDto> PreviewTargetsAsync(string actorUserId, string campaignId, CancellationToken cancellationToken = default);
Task<QueueSummaryDto> GetQueueSummaryAsync(string actorUserId, string campaignId, CancellationToken cancellationToken = default);
```

Both methods use read-only ownership checks. Preview shares the all-null `EligibleDoctorSearchCriteria` used by `SubmitExistingCampaignAsync`. Summary uses one grouped aggregate query by `QueueItemStatus`.

- [ ] **Step 4: Add thin controller actions**

Return standard 200 envelopes and preserve current authorization policy.

- [ ] **Step 5: Verify GREEN and wallet immutability**

Run the new tests plus campaign submission, admin review, and queue creation suites. Assert wallet transaction and ledger counts remain unchanged across submit/review.

### Task 5: File Route Compatibility

**Files:**
- Modify: `MediBridge.APIs/Controllers/FilesController.cs`
- Modify: `MediBridge.APIs/Controllers/AdminFilesController.cs`
- Modify: `tests/contract/MediBridge.ContractTests/FileWorkflowContractTests.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/FileCompatibilityAliasIntegrationTests.cs`

- [ ] **Step 1: Write failing alias tests**

Cover:

```text
POST   /api/company/campaigns/{campaignId}/assets
POST   /api/company/campaigns/{campaignId}/assets/{assetId}/replacement
DELETE /api/company/campaigns/{campaignId}/assets/{assetId}
GET    /api/files/{fileId}
POST   /api/admin/campaign-assets/{assetId}/review
```

- [ ] **Step 2: Verify RED**

Run the alias tests and confirm the routes are missing.

- [ ] **Step 3: Add aliases only**

Each alias extracts the same actor and DTO input as the current generic route and calls the existing `IFileWorkflowService` method. No file persistence, storage-provider calls, or lifecycle rules may be duplicated in controllers.

- [ ] **Step 4: Verify GREEN**

Run file workflow, campaign file, access, replacement, deletion, and review suites.

### Task 6: OTP and Security Regression

**Files:**
- No production configuration or secret files are changed.
- Existing tests: `tests/integration/MediBridge.IntegrationTests/ContactVerificationIntegrationTests.cs`
- Existing tests: `tests/integration/MediBridge.IntegrationTests/PasswordResetIntegrationTests.cs`
- Existing tests: `tests/integration/MediBridge.IntegrationTests/AuthAuditSafetyTests.cs`

- [ ] **Step 1: Confirm current behavior**

Run OTP generation, resend invalidation, expiry, max-attempt, reset-token replay, and audit-redaction tests.

- [ ] **Step 2: Keep optional HMAC hardening deferred**

Do not introduce a required one-time-secret key without a deployment secret and rotation strategy. Record this as a remaining security hardening option rather than silently reusing the JWT key or changing appsettings.

### Task 7: Full Verification and Real-Service Smoke

**Files:**
- Migration generated in Task 2.
- No appsettings secret changes.

- [ ] **Step 1: Build and full automated tests**

Run:

```powershell
dotnet build .\MediBridge.slnx --nologo
dotnet test .\MediBridge.slnx --no-build --nologo --verbosity minimal
```

- [ ] **Step 2: Inspect and apply migrations safely**

Run:

```powershell
dotnet ef migrations list --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
dotnet ef database update --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Verify the database migration history and the new table/constraints.

- [ ] **Step 3: Realistic API smoke**

Use the configured SQL Server, SMTP, and Cloudinary settings without printing secrets. Exercise registration/OTP, file upload/access/review, wallet/mock checkout, doctor pricing, draft/asset/preview/submit, admin review/queue, and company queue summary. Pause for the user-provided OTP only if the configured email flow requires it.

- [ ] **Step 4: Verify clean Git scope**

Run:

```powershell
git status --short
git diff --check
git diff --stat origin/development...HEAD
```

Confirm no appsettings secrets, dirty 007 migrations, or unrelated files are present.

### Task 8: Commit and Push

- [ ] **Step 1: Commit with Kerolos Samoul identity**

Configure repository-local Git author identity if necessary, stage only intended files, and create focused commits.

- [ ] **Step 2: Push only after all required verification passes**

Push `codex/clean-wallet-pricing-preview` to `origin`; do not push to `main`.
