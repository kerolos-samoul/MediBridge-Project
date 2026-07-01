# Cloudinary and One-Time Secret Delivery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace metadata-only file uploads with private Cloudinary storage, issue and deliver email contact-verification OTPs, deliver password-reset tokens safely, and enforce the production verification gate before account approval and login.

**Architecture:** Preserve the existing four-layer Onion structure. `MediBridge.Core` owns provider-neutral contracts and lifecycle state; `MediBridge.Services` owns authorization, issuance, validation, orchestration, and compensation; `MediBridge.Repository` owns EF Core, SQL Server, Cloudinary, and SMTP/MailKit implementations; `MediBridge.APIs` owns HTTP binding and dependency composition. External calls never occur inside a SQL transaction, raw one-time secrets exist only in process memory and outbound test/email messages, and database/provider partial failures use explicit compensation or retryable state.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core Web API, EF Core 8 + SQL Server, CloudinaryDotNet 1.29.2, MailKit 4.17.0, FluentValidation, xUnit, Testcontainers/LocalDB test hosts.

---

## Confirmed baseline and decisions

- `CampaignsController.UploadAsset` currently reads only `FileName`, `ContentType`, and `Length`; it never opens the `IFormFile` stream. `CampaignWorkflowService.UploadAssetAsync` accepts a caller-generated storage key and writes only `StoredFile` metadata.
- No `IFileStorageProvider`, Cloudinary implementation, signed-access endpoint, replacement endpoint, or deletion endpoint is registered.
- Registration creates `ApplicationUser` and profile rows but no `ContactVerificationFlow`.
- `ForgotPasswordAsync` creates `(plaintext, hash)` but discards `plaintext`; tests bypass issuance by inserting flows directly.
- The remote database already has `FailedAttemptCount`, `HashVersion`, `LastSentAtUtc`, `MaxAttemptCount`, `ResendCount`, `ResendWindowStartedAtUtc`, `SupersededAtUtc`, and `MaxAttemptsReachedAtUtc` columns, but the current entity, configuration, and model snapshot omit them. A new idempotent reconciliation migration must converge both the existing remote database and a clean database.
- Email verification becomes mandatory for new and legacy doctor/company login and for an admin `Approve` decision. The historical Phase 2 rule allowing approved/unverified login must be replaced in the specification and tests.
- Registration commits the account and hashed OTP flow before email delivery. Delivery failure returns a successful registration result with `VerificationDeliveryStatus = "Failed"`; the client uses the resend endpoint. This avoids emailing an OTP for a rolled-back transaction and avoids a duplicate-registration trap.
- Forgot-password remains enumeration-safe: known user, unknown user, and provider failure all return the same `202 Accepted` envelope.
- Rejected campaign assets are retained for audit. Replacement creates a new stored-file row and links the old row through `SupersededByFileId`. Explicit deletion is limited to company-owned Pending/Rejected assets on Draft campaigns; Approved assets are retained.

## Target file map

### New production files

- `MediBridge.Core/Interfaces/Files/IFileStorageProvider.cs` — provider-neutral upload, signed URL, and delete contract.
- `MediBridge.Core/Interfaces/Notifications/IEmailDelivery.cs` — semantic OTP/reset email contract.
- `MediBridge.Core/Enums/StorageObjectState.cs` — Active, DeletionPending, Deleted.
- `MediBridge.Repository/Storage/CloudinaryStorageOptions.cs` — validated Cloudinary configuration.
- `MediBridge.Repository/Storage/CloudinaryFileStorageProvider.cs` — Cloudinary SDK adapter.
- `MediBridge.Repository/Auditing/DatabaseAuditLogger.cs` — scoped persistence for provider-cleanup and security audit events.
- `MediBridge.Repository/Notifications/SmtpEmailOptions.cs` — validated SMTP configuration.
- `MediBridge.Repository/Notifications/MailKitEmailDelivery.cs` — MailKit implementation with development recipient override.
- `MediBridge.Services/Config/ContactVerificationOptions.cs` — OTP length, expiry, attempts, and resend limits.
- `MediBridge.Services/Config/PasswordResetOptions.cs` — reset lifetime and public reset-link base URI.
- `MediBridge.Services/DTOs/Auth/ResendContactVerificationRequestDto.cs` — public resend request.
- `MediBridge.Services/DTOs/Files/FileAccessDto.cs` — short-lived signed URL response.
- `MediBridge.Services/Interfaces/IFileAccessService.cs` — authorized read/delete operations.
- `MediBridge.Services/Interfaces/FileStorageUnavailableException.cs` — provider-neutral 503 failure signal.
- `MediBridge.Services/Services/FileAccessService.cs` — ownership/role checks and deletion state machine.
- `MediBridge.APIs/Controllers/FilesController.cs` — signed access and constrained delete endpoints.
- `MediBridge.Repository/Migrations/20260624000100_ReconcileOtpAndFileStorageLifecycle.cs` — conditional OTP-column reconciliation and file lifecycle columns/indexes.
- `MediBridge.Repository/Migrations/20260624000100_ReconcileOtpAndFileStorageLifecycle.Designer.cs` — generated migration model metadata.

### Existing production files to modify

- `MediBridge.Core/Entities/Files/StoredFile.cs`
- `MediBridge.Core/Entities/Identity/ContactVerificationFlow.cs`
- `MediBridge.Core/Interfaces/Files/IStoredFileRepository.cs`
- `MediBridge.Core/Interfaces/Identity/IAuthTokenService.cs`
- `MediBridge.Core/Interfaces/Identity/IContactVerificationFlowRepository.cs`
- `MediBridge.Core/Enums/AuthAuditEventType.cs`
- `MediBridge.Repository/Configurations/Files/StoredFileConfiguration.cs`
- `MediBridge.Repository/Configurations/Identity/IdentityLifecycleConfigurations.cs`
- `MediBridge.Repository/Repositories/Files/StoredFileRepository.cs`
- `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs`
- `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`
- `MediBridge.Repository/MediBridge.Repository.csproj`
- `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`
- `MediBridge.Services/DTOs/Auth/RegistrationResultDto.cs`
- `MediBridge.Services/DTOs/Auth/VerifyContactRequestDto.cs`
- `MediBridge.Services/DTOs/Campaigns/CampaignAssetUploadRequestDto.cs`
- `MediBridge.Services/Interfaces/IAuthService.cs`
- `MediBridge.Services/Interfaces/ICampaignWorkflowService.cs`
- `MediBridge.Services/Services/AuthTokenService.cs`
- `MediBridge.Services/Services/AuthService.cs`
- `MediBridge.Services/Services/AdminAccountService.cs`
- `MediBridge.Services/Services/CampaignWorkflowService.cs`
- `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`
- `MediBridge.APIs/Contracts/WorkflowActionResultMapper.cs`
- `MediBridge.APIs/Controllers/AuthController.cs`
- `MediBridge.APIs/Controllers/CampaignsController.cs`
- `MediBridge.APIs/Program.cs`
- `MediBridge.APIs/appsettings.json`
- `MediBridge.APIs/appsettings.Development.json`
- `docs/runtime-configuration.md`

### Test files to add or modify

- `tests/unit/MediBridge.UnitTests/Files/CloudinaryStorageMappingTests.cs`
- `tests/unit/MediBridge.UnitTests/Files/CampaignAssetStorageOrchestrationTests.cs`
- `tests/unit/MediBridge.UnitTests/Auth/OneTimeSecretHashingTests.cs`
- `tests/unit/MediBridge.UnitTests/Auth/ContactVerificationPolicyTests.cs`
- `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj`
- `tests/contract/MediBridge.ContractTests/Files/FileStorageContractTests.cs`
- `tests/contract/MediBridge.ContractTests/AccountRecoveryContractTests.cs`
- `tests/contract/MediBridge.ContractTests/IdentityRegistrationContractTests.cs`
- `tests/integration/MediBridge.IntegrationTests/TestHost/InMemoryFileStorageProvider.cs`
- `tests/integration/MediBridge.IntegrationTests/TestHost/InMemoryEmailDelivery.cs`
- `tests/integration/MediBridge.IntegrationTests/TestHost/ConfiguredWebAppFactory.cs`
- `tests/contract/MediBridge.ContractTests/TestHost/InMemoryFileStorageProvider.cs`
- `tests/contract/MediBridge.ContractTests/TestHost/InMemoryEmailDelivery.cs`
- `tests/contract/MediBridge.ContractTests/TestHost/ContractWebAppFactory.cs`
- `tests/integration/MediBridge.IntegrationTests/Files/CloudinaryWorkflowIntegrationTests.cs`
- `tests/integration/MediBridge.IntegrationTests/ContactVerificationIssuanceTests.cs`
- `tests/integration/MediBridge.IntegrationTests/ContactVerificationResendTests.cs`
- `tests/integration/MediBridge.IntegrationTests/ContactVerificationIntegrationTests.cs`
- `tests/integration/MediBridge.IntegrationTests/ContactVerificationTokenGateTests.cs`
- `tests/integration/MediBridge.IntegrationTests/PasswordResetDeliveryIntegrationTests.cs`
- `tests/integration/MediBridge.IntegrationTests/PasswordResetIntegrationTests.cs`
- `tests/integration/MediBridge.IntegrationTests/PasswordResetReplayTests.cs`
- `tests/integration/MediBridge.IntegrationTests/Workflow/ConfigurationSecretGuardTests.cs`

## Task 1: Update authoritative contracts and security rules

**Files:**
- Modify: `specs/002-identity-approval/spec.md`
- Modify: `specs/002-identity-approval/contracts/identity-approval-api.yaml`
- Modify: `specs/002-identity-approval/data-model.md`
- Modify: `specs/002-identity-approval/quickstart.md`
- Modify: `specs/006-wallet-campaign-workflow/spec.md`
- Modify: `specs/006-wallet-campaign-workflow/contracts/wallet-campaign-workflow-api.yaml`
- Modify: `docs/backend-plan.md`

- [ ] **Step 1: Replace the obsolete login rule**

Change the identity specification so doctor/company token issuance requires both `AccountStatus == Approved` and `EmailVerified == true`. Add an acceptance scenario that an admin approval attempt against an unverified pending account returns `409` and leaves the account Pending. Keep the seeded admin exempt because it is created verified.

- [ ] **Step 2: Define the contact-verification HTTP contract**

Use these request/response shapes in `identity-approval-api.yaml`:

```yaml
/api/auth/resend-contact-verification:
  post:
    requestBody:
      required: true
      content:
        application/json:
          schema:
            type: object
            required: [contact, channel]
            properties:
              contact: { type: string }
              channel: { type: string, enum: [Email] }
    responses:
      '202': { description: Accepted without account enumeration }

VerifyContactRequest:
  type: object
  required: [contact, channel, verificationToken]
  properties:
    contact: { type: string }
    channel: { type: string, enum: [Email] }
    verificationToken: { type: string, pattern: '^[0-9]{6}$' }
```

Extend registration response data with `VerificationRequired`, `VerificationChannel`, `MaskedVerificationDestination`, `VerificationExpiresAtUtc`, and `VerificationDeliveryStatus`.

- [ ] **Step 3: Define the file contract**

Document these routes:

```text
POST   /api/company/campaigns/{campaignId}/assets
POST   /api/company/campaigns/{campaignId}/assets/{assetId}/replacement
GET    /api/files/{fileId}
DELETE /api/company/campaigns/{campaignId}/assets/{assetId}
```

`GET /api/files/{fileId}` returns `{ FileId, Url, ExpiresAtUtc }`. Replacement is allowed only for Pending/Rejected assets on a Draft campaign. Delete is allowed only for Pending/Rejected assets on a Draft campaign. Approved assets are immutable and retained.

- [ ] **Step 4: Verify contract contradictions are gone**

Run:

```powershell
rg -n "Approved account.*without contact verification|contact verification.*not required" specs/002-identity-approval docs/backend-plan.md
```

Expected: no active requirement still permits approved/unverified doctor or company login.

- [ ] **Step 5: Commit the contract change**

```powershell
git add specs/002-identity-approval specs/006-wallet-campaign-workflow docs/backend-plan.md
git commit -m "docs: require delivered contact verification and real file storage"
```

## Task 2: Introduce provider-neutral storage and delivery contracts

**Files:**
- Create: `MediBridge.Core/Interfaces/Files/IFileStorageProvider.cs`
- Create: `MediBridge.Core/Interfaces/Notifications/IEmailDelivery.cs`
- Create: `MediBridge.Core/Enums/StorageObjectState.cs`
- Modify: `MediBridge.Core/Entities/Files/StoredFile.cs`

- [ ] **Step 1: Add the storage contract**

```csharp
namespace MediBridge.Core.Interfaces.Files;

public sealed record FileStorageUpload(
    string Folder,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);

public sealed record FileStorageUploadResult(string StorageKey, string ResourceType);

public sealed record SignedFileUrl(Uri Url, DateTime ExpiresAtUtc);

public interface IFileStorageProvider
{
    Task<FileStorageUploadResult> UploadAsync(
        FileStorageUpload request,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<SignedFileUrl> CreateSignedReadUrlAsync(
        string storageKey,
        string resourceType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        string resourceType,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Add the email contract**

```csharp
namespace MediBridge.Core.Interfaces.Notifications;

public interface IEmailDelivery
{
    Task SendContactVerificationAsync(
        string destination,
        string oneTimeCode,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        string destination,
        string resetToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}
```

Do not add a method that returns, logs, or persists the raw secret.

- [ ] **Step 3: Add storage lifecycle metadata**

```csharp
public enum StorageObjectState
{
    Active = 1,
    DeletionPending = 2,
    Deleted = 3
}
```

Add these properties to `StoredFile`:

```csharp
public string StorageResourceType { get; set; } = "raw";
public StorageObjectState StorageState { get; set; } = StorageObjectState.Active;
public string? SupersededByFileId { get; set; }
public DateTime? DeletedAtUtc { get; set; }
```

- [ ] **Step 4: Build the Core project**

```powershell
dotnet build .\MediBridge.Core\MediBridge.Core.csproj --nologo
```

Expected: build succeeds without adding EF Core, ASP.NET Core, Cloudinary, or MailKit references to Core.

- [ ] **Step 5: Commit the contracts**

```powershell
git add MediBridge.Core
git commit -m "feat: add storage and email delivery abstractions"
```

## Task 3: Implement Cloudinary infrastructure and safe configuration

**Files:**
- Modify: `MediBridge.Repository/MediBridge.Repository.csproj`
- Create: `MediBridge.Repository/Storage/CloudinaryStorageOptions.cs`
- Create: `MediBridge.Repository/Storage/CloudinaryFileStorageProvider.cs`
- Modify: `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`
- Modify: `MediBridge.APIs/Program.cs`
- Modify: `MediBridge.APIs/appsettings.json`
- Modify: `MediBridge.APIs/appsettings.Development.json`
- Modify: `docs/runtime-configuration.md`
- Test: `tests/unit/MediBridge.UnitTests/Files/CloudinaryStorageMappingTests.cs`

- [ ] **Step 1: Add the SDK package**

```powershell
dotnet add .\MediBridge.Repository\MediBridge.Repository.csproj package CloudinaryDotNet --version 1.29.2
```

Add a project reference from `tests/unit/MediBridge.UnitTests/MediBridge.UnitTests.csproj` to `MediBridge.Repository/MediBridge.Repository.csproj` so provider mapping is tested without moving infrastructure code into Services.

- [ ] **Step 2: Add validated options**

```csharp
namespace MediBridge.Repository.Storage;

public sealed class CloudinaryStorageOptions
{
    public const string SectionName = "CloudinaryStorage";
    public string CloudinaryUrl { get; set; } = string.Empty;
    public string FolderPrefix { get; set; } = "medibridge";
    public bool UseSecureUrls { get; set; } = true;
    public int SignedUrlMinutes { get; set; } = 5;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CloudinaryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(FolderPrefix);
        if (SignedUrlMinutes is < 1 or > 60)
            throw new InvalidOperationException("Cloudinary signed URL lifetime must be between 1 and 60 minutes.");
    }
}
```

- [ ] **Step 3: Write mapping tests before the provider**

Test that `image/*` maps to Cloudinary `image`, `video/*` maps to `video`, and every other accepted type maps to `raw`. Test that folder and public ID components are generated by the provider, never accepted from the HTTP caller.

Run:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter CloudinaryStorageMappingTests
```

Expected: tests fail because `CloudinaryFileStorageProvider` does not exist.

- [ ] **Step 4: Implement the provider**

`UploadAsync` must:

1. Reject a non-readable stream or non-positive declared length.
2. Select `ImageUploadParams`, `VideoUploadParams`, or `RawUploadParams` from the content type.
3. Set `Folder = $"{FolderPrefix}/{request.Folder.Trim('/')}"`, `UseFilename = false`, `UniqueFilename = true`, and `Overwrite = false`.
4. Pass the caller cancellation token.
5. Require a successful Cloudinary result with non-empty `PublicId`.
6. Return `PublicId` as `StorageKey` and the selected resource type.
7. Translate Cloudinary/network failures into `FileStorageUnavailableException` without provider credentials or raw response bodies.

`CreateSignedReadUrlAsync` must use an authenticated/private delivery URL, set an expiry no greater than `SignedUrlMinutes`, and return HTTPS only. `DeleteAsync` must call `DestroyAsync` with the stored resource type and treat Cloudinary `not found` as idempotent success.

- [ ] **Step 5: Register Cloudinary and validate startup**

In `RepositoryServiceCollectionExtensions.AddMediBridgeRepository`, bind/validate `CloudinaryStorageOptions`, register one `Cloudinary` client, and register `IFileStorageProvider` as scoped. Remove no existing repository registrations.

`Program.cs` must fail startup when uploads are enabled but Cloudinary configuration is absent. It must not print `CloudinaryUrl`.

- [ ] **Step 6: Remove committed secret values**

Keep only these non-secret defaults:

```json
"FileStorage": { "UploadsEnabled": true },
"CloudinaryStorage": {
  "CloudinaryUrl": "",
  "FolderPrefix": "medibridge",
  "UseSecureUrls": true,
  "SignedUrlMinutes": 5
}
```

Document `CloudinaryStorage__CloudinaryUrl` as an environment/user-secret setting. Rotate any credential previously committed before implementation deployment.

- [ ] **Step 7: Run the provider tests and configuration guard**

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter CloudinaryStorageMappingTests
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter ConfigurationSecretGuardTests
```

Expected: mapping tests pass; configuration guard proves no credential-shaped values are committed.

- [ ] **Step 8: Commit Cloudinary infrastructure**

```powershell
git add MediBridge.Repository MediBridge.APIs docs/runtime-configuration.md tests/unit tests/integration
git commit -m "feat: add private Cloudinary storage provider"
```

## Task 4: Persist actual uploads with compensation

**Files:**
- Modify: `MediBridge.Services/DTOs/Campaigns/CampaignAssetUploadRequestDto.cs`
- Modify: `MediBridge.Services/Interfaces/ICampaignWorkflowService.cs`
- Modify: `MediBridge.Services/Services/CampaignWorkflowService.cs`
- Modify: `MediBridge.APIs/Controllers/CampaignsController.cs`
- Modify: `MediBridge.APIs/Contracts/WorkflowActionResultMapper.cs`
- Create: `MediBridge.Services/Interfaces/FileStorageUnavailableException.cs`
- Create: `MediBridge.Repository/Auditing/DatabaseAuditLogger.cs`
- Modify: `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`
- Modify: `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/unit/MediBridge.UnitTests/Files/CampaignAssetStorageOrchestrationTests.cs`
- Test: `tests/contract/MediBridge.ContractTests/Campaigns/CompanyCampaignAssetContractTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/Files/CloudinaryWorkflowIntegrationTests.cs`

- [ ] **Step 1: Change the service boundary**

Remove `StorageKey` from `CampaignAssetUploadRequestDto`; the caller may supply only file name, content type, and length. Change the interface to:

```csharp
Task<CampaignAssetDto> UploadAssetAsync(
    string companyUserId,
    string campaignId,
    CampaignAssetUploadRequestDto request,
    Stream content,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Write failing orchestration tests**

Cover these cases with a recording fake `IFileStorageProvider`:

- valid stream uploads exactly once and persists the provider-returned key;
- validation or ownership failure performs no provider call;
- provider failure persists no `StoredFile`;
- database failure after upload calls provider delete once with the returned key;
- provider-delete compensation failure is audited and the original database exception is preserved;
- cancellation reaches upload/delete calls.

Run:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter CampaignAssetStorageOrchestrationTests
```

Expected: tests fail against metadata-only orchestration.

- [ ] **Step 3: Implement upload-before-persist with compensation**

Use this order in `CampaignWorkflowService`:

```csharp
await EnsureCompanyOwnsDraftAsync(companyUserId, campaignId, cancellationToken);

var upload = await fileStorageProvider.UploadAsync(
    new FileStorageUpload(
        $"campaigns/{campaignId}",
        request.OriginalFileName,
        request.ContentType,
        request.SizeBytes),
    content,
    cancellationToken);

try
{
    return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionToken =>
    {
        await EnsureCompanyOwnsDraftForUpdateAsync(companyUserId, campaignId, transactionToken);
        var asset = CreateStoredFile(request, upload, campaignId);
        await domainUnitOfWork.StoredFiles.AddStoredFileAsync(asset, transactionToken);
        return ToCampaignAssetDto(asset);
    }, cancellationToken);
}
catch
{
    try
    {
        await fileStorageProvider.DeleteAsync(
            upload.StorageKey,
            upload.ResourceType,
            CancellationToken.None);
    }
    catch (Exception cleanupException)
    {
        await RecordStorageCleanupFailureAsync(upload.StorageKey, cleanupException);
    }

    throw;
}
```

Add the provider-neutral exception used at the HTTP mapping boundary:

```csharp
namespace MediBridge.Services.Interfaces;

public sealed class FileStorageUnavailableException : Exception
{
    public FileStorageUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

If compensation deletion fails, record a System audit event containing only file ID/storage-key fingerprint and provider error category; never include credentials or a signed URL.

Implement the referenced helper with the existing `IAuditLogger` abstraction:

```csharp
private Task RecordStorageCleanupFailureAsync(string storageKey, Exception exception)
{
    var fingerprint = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(storageKey)));

    return auditLogger.LogAsync(new AuditEvent(
        AuditEventCategory.System,
        $"FileStorageCompensationFailed:{exception.GetType().Name}",
        currentUserContext.UserId,
        currentUserContext.Role,
        "StoredFileObject",
        fingerprint,
        currentUserContext.CorrelationId,
        DateTime.UtcNow));
}
```

Implement `DatabaseAuditLogger` in Repository by mapping the provider-neutral `MediBridge.Core.Interfaces.AuditEvent` to `MediBridge.Core.Entities.Policies.AuditEvent`, setting cleanup failures to `AuditOutcome.Failed`, and calling `MediBridgeDbContext.SaveChangesAsync`. Register it as scoped in place of `NoopAuditLogger`; persist only the action, actor, target fingerprint, correlation ID, category, and timestamp. Do not persist exception messages or provider payloads.

- [ ] **Step 4: Open the actual HTTP stream**

In `CampaignsController.UploadAsset`:

```csharp
await using var content = file.OpenReadStream();
var request = new CampaignAssetUploadRequestDto(
    Path.GetFileName(file.FileName),
    file.ContentType,
    file.Length);
var asset = await campaignWorkflowService.UploadAssetAsync(
    companyUserId,
    campaignId,
    request,
    content,
    cancellationToken);
```

Map `FileStorageUnavailableException` to `503` with `"File storage is temporarily unavailable."`.

- [ ] **Step 5: Add purpose-specific validation**

For campaign media, permit configured `image/jpeg`, `image/png`, `image/webp`, and supported video types; reject executable/archive types, zero-byte streams, declared sizes over the campaign limit, filename path traversal, and content-type/extension mismatches. The controller performs cheap shape checks; the service validator owns the authoritative rule.

- [ ] **Step 6: Run focused tests**

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "CampaignAssetStorageOrchestrationTests|CampaignAssetUpload"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter CompanyCampaignAssetContractTests
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter CloudinaryWorkflowIntegrationTests
```

Expected: the unit/contract/integration tests pass using the in-memory provider; no test reaches real Cloudinary.

- [ ] **Step 7: Commit upload orchestration**

```powershell
git add MediBridge.Services MediBridge.APIs tests
git commit -m "feat: persist campaign asset bytes through storage provider"
```

## Task 5: Add signed access, replacement, deletion, and lifecycle migration

**Files:**
- Create: `MediBridge.Services/DTOs/Files/FileAccessDto.cs`
- Create: `MediBridge.Services/Interfaces/IFileAccessService.cs`
- Create: `MediBridge.Services/Services/FileAccessService.cs`
- Create: `MediBridge.APIs/Controllers/FilesController.cs`
- Modify: `MediBridge.APIs/Controllers/CampaignsController.cs`
- Modify: `MediBridge.Core/Interfaces/Files/IStoredFileRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Files/StoredFileRepository.cs`
- Modify: `MediBridge.Repository/Configurations/Files/StoredFileConfiguration.cs`
- Create: `MediBridge.Repository/Migrations/20260624000100_ReconcileOtpAndFileStorageLifecycle.cs`
- Create: `MediBridge.Repository/Migrations/20260624000100_ReconcileOtpAndFileStorageLifecycle.Designer.cs`
- Modify: `MediBridge.Repository/Migrations/MediBridgeDbContextModelSnapshot.cs`
- Modify: `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`
- Test: `tests/contract/MediBridge.ContractTests/Files/FileStorageContractTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/Files/CloudinaryWorkflowIntegrationTests.cs`

- [ ] **Step 1: Write failing access-control contract tests**

Prove anonymous access is `401`; wrong owner is `403`; missing/deleted file is `404`; owning company and admin receive a short-lived HTTPS URL; no response exposes Cloudinary credentials or raw configuration.

- [ ] **Step 2: Implement signed access**

`FileAccessService.GetSignedAccessAsync` loads active metadata, resolves the authenticated user from `IIdentityUnitOfWork`, permits Admin or the owning company, denies `DeletionPending/Deleted`, and asks `IFileStorageProvider` for a five-minute signed URL. `FilesController` returns `ApiEnvelope<FileAccessDto>` only.

Register `IFileAccessService` in `IdentityServiceCollectionExtensions`; no controller may resolve repositories or `IFileStorageProvider` directly.

- [ ] **Step 3: Implement replacement semantics**

`POST /api/company/campaigns/{campaignId}/assets/{assetId}/replacement` must require Draft campaign ownership and an old Pending/Rejected asset. Upload the new object first; in one SQL transaction insert the new `StoredFile` and set `old.SupersededByFileId = new.Id`. If SQL fails, delete the new Cloudinary object. Do not physically delete the old reviewed object automatically.

- [ ] **Step 4: Implement safe delete state machine**

For Pending/Rejected Draft assets:

1. transactionally set `StorageState = DeletionPending`;
2. call provider delete outside the transaction;
3. on success set `StorageState = Deleted` and `DeletedAtUtc`;
4. on provider failure leave `DeletionPending`, return `503`, and permit idempotent retry;
5. signed access and campaign submission ignore non-Active files.

- [ ] **Step 5: Generate an idempotent reconciliation migration**

The migration must use guarded SQL for OTP columns already present on the remote database and normal EF operations for new file fields. The guarded pattern is:

```csharp
migrationBuilder.Sql("""
IF COL_LENGTH('dbo.ContactVerificationFlows', 'FailedAttemptCount') IS NULL
    ALTER TABLE [dbo].[ContactVerificationFlows]
    ADD [FailedAttemptCount] int NOT NULL CONSTRAINT [DF_ContactVerificationFlows_FailedAttemptCount] DEFAULT 0;
""");
```

Repeat the guarded form for every OTP lifecycle column listed in the baseline. Add file lifecycle columns and indexes through EF migration operations. Do not recreate or delete historical migration IDs in `__EFMigrationsHistory`.

- [ ] **Step 6: Verify migration on both database shapes**

Run once against a clean disposable SQL Server database and once against a disposable database pre-seeded with the existing OTP columns. Both runs must apply successfully and produce an identical EF model.

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Migration|FileStorageLifecycle"
```

- [ ] **Step 7: Run file lifecycle tests**

```powershell
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter FileStorageContractTests
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter CloudinaryWorkflowIntegrationTests
```

Expected: replacement preserves the old review record, deletion is idempotent, unauthorized access is denied, and cleanup retries do not expose deleted content.

- [ ] **Step 8: Commit file lifecycle behavior**

```powershell
git add MediBridge.Core MediBridge.Repository MediBridge.Services MediBridge.APIs tests
git commit -m "feat: add signed file access and lifecycle operations"
```

## Task 6: Model OTP lifecycle and add SMTP delivery

**Files:**
- Modify: `MediBridge.Core/Entities/Identity/ContactVerificationFlow.cs`
- Modify: `MediBridge.Core/Interfaces/Identity/IContactVerificationFlowRepository.cs`
- Modify: `MediBridge.Repository/Configurations/Identity/IdentityLifecycleConfigurations.cs`
- Modify: `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs`
- Create: `MediBridge.Services/Config/ContactVerificationOptions.cs`
- Create: `MediBridge.Services/Config/PasswordResetOptions.cs`
- Create: `MediBridge.Repository/Notifications/SmtpEmailOptions.cs`
- Create: `MediBridge.Repository/Notifications/MailKitEmailDelivery.cs`
- Modify: `MediBridge.Repository/MediBridge.Repository.csproj`
- Modify: `MediBridge.Repository/Extensions/RepositoryServiceCollectionExtensions.cs`
- Modify: `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`
- Modify: `MediBridge.APIs/appsettings.json`
- Modify: `MediBridge.APIs/appsettings.Development.json`
- Modify: `docs/runtime-configuration.md`
- Test: `tests/integration/MediBridge.IntegrationTests/Workflow/ConfigurationSecretGuardTests.cs`
- Modify: `tests/contract/MediBridge.ContractTests/TestHost/ContractWebAppFactory.cs`

- [ ] **Step 1: Map the existing database lifecycle fields**

Add these properties to `ContactVerificationFlow` with the existing database defaults:

```csharp
public int FailedAttemptCount { get; set; }
public string HashVersion { get; set; } = "hmac-sha256-v1";
public DateTime? LastSentAtUtc { get; set; }
public int MaxAttemptCount { get; set; } = 5;
public int ResendCount { get; set; }
public DateTime? ResendWindowStartedAtUtc { get; set; }
public DateTime? SupersededAtUtc { get; set; }
public DateTime? MaxAttemptsReachedAtUtc { get; set; }
```

Configure lengths/defaults and indexes for `(UserId, Channel, CreatedAtUtc)` and active-flow lookup.

- [ ] **Step 2: Extend repository operations**

Add methods to find the latest active flow with an update lock, supersede all active flows for a user/channel, record a failed attempt, and mark sent/consumed. Every state-changing verification operation runs inside `IIdentityUnitOfWork.ExecuteInTransactionAsync`.

- [ ] **Step 3: Add validated options**

```csharp
public sealed class ContactVerificationOptions
{
    public const string SectionName = "ContactVerification";
    public int OtpLength { get; set; } = 6;
    public int ExpirationMinutes { get; set; } = 10;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 5;
    public int MaxResendsPerWindow { get; set; } = 5;
    public int ResendWindowMinutes { get; set; } = 60;
    public string OneTimeSecretHashingKey { get; set; } = string.Empty;
}
```

`PasswordResetOptions` contains `ExpirationMinutes = 60` and an HTTPS `ResetLinkBaseUri`. Validate `OneTimeSecretHashingKey` as a separate secret with at least 32 bytes; do not reuse the JWT signing key.

- [ ] **Step 4: Add MailKit**

```powershell
dotnet add .\MediBridge.Repository\MediBridge.Repository.csproj package MailKit --version 4.17.0
```

- [ ] **Step 5: Implement email delivery**

`MailKitEmailDelivery` creates a new `SmtpClient` per send, connects with configured TLS, authenticates, sends a minimal HTML+text message, disconnects in `finally`, and never logs body/token/OTP. Apply `OverrideRecipientEmail` only when `AllowOverrideRecipientEmail` is true and the host environment is Development; startup must fail if override is enabled in Production.

- [ ] **Step 6: Register test-safe fakes**

Add `InMemoryEmailDelivery` and `InMemoryFileStorageProvider` to both integration and contract test hosts via `services.RemoveAll<T>()`. Test configuration sets `FileStorage:UploadsEnabled=false`, supplies a test-only `ContactVerification:OneTimeSecretHashingKey`, and then replaces the real providers. This prevents real Cloudinary/SMTP access while allowing endpoint tests through the fakes. Each fake stores state in memory for the current factory only. `InMemoryEmailDelivery` exposes methods that retrieve the latest OTP/reset token by destination and must never be registered in the production service collection.

- [ ] **Step 7: Add configuration security tests**

Assert committed JSON contains no SMTP password, Cloudinary URL, one-time-secret hashing key, plaintext OTP, or reset token. Assert production startup rejects recipient override.

- [ ] **Step 8: Commit delivery infrastructure**

```powershell
git add MediBridge.Core MediBridge.Repository MediBridge.Services MediBridge.APIs docs tests
git commit -m "feat: add secure one-time secret email delivery"
```

## Task 7: Issue, resend, and verify contact OTPs

**Files:**
- Modify: `MediBridge.Core/Interfaces/Identity/IAuthTokenService.cs`
- Modify: `MediBridge.Core/Enums/AuthAuditEventType.cs`
- Modify: `MediBridge.Services/Services/AuthTokenService.cs`
- Modify: `MediBridge.Services/DTOs/Auth/RegistrationResultDto.cs`
- Modify: `MediBridge.Services/DTOs/Auth/VerifyContactRequestDto.cs`
- Create: `MediBridge.Services/DTOs/Auth/ResendContactVerificationRequestDto.cs`
- Create: `MediBridge.Services/Validators/Auth/ResendContactVerificationRequestValidator.cs`
- Modify: `MediBridge.Services/Interfaces/IAuthService.cs`
- Modify: `MediBridge.Services/Services/AuthService.cs`
- Modify: `MediBridge.Services/Services/AdminAccountService.cs`
- Modify: `MediBridge.APIs/Controllers/AuthController.cs`
- Test: `tests/unit/MediBridge.UnitTests/Auth/OneTimeSecretHashingTests.cs`
- Test: `tests/unit/MediBridge.UnitTests/Auth/ContactVerificationPolicyTests.cs`
- Test: `tests/contract/MediBridge.ContractTests/IdentityRegistrationContractTests.cs`
- Test: `tests/contract/MediBridge.ContractTests/AccountRecoveryContractTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/ContactVerificationIssuanceTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/ContactVerificationResendTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/ContactVerificationIntegrationTests.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/ContactVerificationTokenGateTests.cs`

- [ ] **Step 1: Write failing issuance and gate tests**

Cover:

- doctor and company registration create exactly one active Email flow and deliver one OTP;
- only `TokenHash`/`DestinationHash` are stored; raw OTP is absent from every database string column and audit record;
- registration reports masked destination, expiry, and delivery status;
- approval before verification returns `409` and remains Pending;
- approved but unverified legacy doctor/company login returns `403`;
- verified+approved login returns `200`.

- [ ] **Step 2: Add numeric-code generation and HMAC hashing**

Extend `IAuthTokenService` with:

```csharp
string CreateNumericCode(int length);
string HashOneTimeSecret(string normalizedDestination, string plaintextSecret);
bool VerifyOneTimeSecret(string normalizedDestination, string plaintextSecret, string expectedHash);
```

Generate digits with `RandomNumberGenerator.GetInt32(0, 10)` and compute HMAC-SHA256 with `OneTimeSecretHashingKey`. Use `CryptographicOperations.FixedTimeEquals` for verification.

- [ ] **Step 3: Issue the OTP during registration**

Inside the registration SQL transaction:

1. create user/profile;
2. create numeric OTP in memory;
3. store destination hash and HMAC token hash with 10-minute expiry and max attempts;
4. add `ContactVerificationIssued` audit event;
5. return an internal result containing DTO + raw OTP.

After commit, call `IEmailDelivery.SendContactVerificationAsync`. Set DTO delivery status to `Sent` or `Failed`; on failure add `ContactVerificationDeliveryFailed` audit without the OTP.

- [ ] **Step 4: Implement resend**

Add `POST /api/auth/resend-contact-verification`. For an existing unverified account, enforce cooldown and resend-window limits under an update lock, supersede the prior flow, create a fresh hashed flow/code, commit, then deliver. For unknown/already-verified contacts return the same `202` envelope without data. Cooldown violations return `429`; resend-window exhaustion returns `429` until the window resets.

- [ ] **Step 5: Implement verification attempts**

`VerifyContactAsync` must normalize `Contact`, find user and latest active Email flow under a lock, reject consumed/superseded/expired/max-attempt flows, verify HMAC in constant time, increment `FailedAttemptCount` on mismatch, set `MaxAttemptsReachedAtUtc` at the configured limit, and consume/set `EmailVerified` on success. All outcomes add audit events that contain reason categories only.

- [ ] **Step 6: Enforce approval and login gates**

Before `AdminAccountService` maps `Approve`, require `targetUser.EmailVerified`; throw `WorkflowConflictException("Contact verification is required before approval.")`. In `AuthService.LoginAsync` and refresh validation, require `EmailVerified` for Doctor/Company. The seeded admin remains verified.

- [ ] **Step 7: Add complete OTP edge coverage**

Tests must prove success, wrong code, expired code, five failed attempts, resend cooldown, resend-window exhaustion, superseded-token rejection, consumed replay rejection, concurrent verification consumes once, and no login/approval before verification.

Run:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "OneTimeSecret|ContactVerification"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "IdentityRegistration|AccountRecovery"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "ContactVerification|AdminDecision|Login"
```

- [ ] **Step 8: Commit contact verification**

```powershell
git add MediBridge.Core MediBridge.Repository MediBridge.Services MediBridge.APIs tests
git commit -m "feat: issue and enforce contact verification OTPs"
```

## Task 8: Deliver password-reset tokens and exercise reset end to end

**Files:**
- Modify: `MediBridge.Core/Enums/AuthAuditEventType.cs`
- Modify: `MediBridge.Services/Services/AuthService.cs`
- Modify: `tests/contract/MediBridge.ContractTests/AccountRecoveryContractTests.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/PasswordResetDeliveryIntegrationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/ForgotPasswordEnumerationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/PasswordResetIntegrationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/PasswordResetReplayTests.cs`

- [ ] **Step 1: Write the failing delivery E2E test**

Register and verify an account, request forgot-password, capture the email from `InMemoryEmailDelivery`, extract the raw reset token, assert the database contains only its hash, call reset-password, login with the new password, and assert old refresh credentials are revoked.

- [ ] **Step 2: Preserve the plaintext token only in memory**

Replace:

```csharp
var (_, tokenHash) = authTokenService.CreateOneTimeToken();
```

with a transaction result that returns both values to the service method:

```csharp
var issued = await identityUnitOfWork.ExecuteInTransactionAsync(async ct =>
{
    var (plaintextToken, tokenHash) = authTokenService.CreateOneTimeToken();
    var expiresAtUtc = DateTime.UtcNow.AddMinutes(passwordResetOptions.ExpirationMinutes);
    await identityUnitOfWork.PasswordResetFlows.AddAsync(
        new PasswordResetFlow
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            RequestCorrelationId = currentUserContext.CorrelationId ?? Guid.NewGuid().ToString("N")
        }, ct);
    return (plaintextToken, expiresAtUtc);
}, cancellationToken);

await emailDelivery.SendPasswordResetAsync(
    user.Email,
    issued.plaintextToken,
    issued.expiresAtUtc,
    cancellationToken);
```

The implementation must not place the raw token in a DTO, database field, audit reason, exception, or log.

- [ ] **Step 3: Preserve enumeration resistance on provider failure**

Known contact, unknown contact, and SMTP failure must all return the same `202 Accepted` envelope. Record `PasswordResetRequested`, `PasswordResetDeliverySucceeded`, or `PasswordResetDeliveryFailed` audit events without destination or token plaintext.

- [ ] **Step 4: Keep reset consumption atomic**

Retain update-lock lookup, expiry check, single-use consumption, password replacement, refresh revocation, and concurrent-consumption behavior. Add an invalid-token test, an expired-token test, a consumed-token test, and a delivery-to-reset success test.

- [ ] **Step 5: Run recovery tests**

```powershell
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter AccountRecoveryContractTests
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "ForgotPassword|PasswordReset"
```

Expected: success path uses the captured delivered token; invalid, expired, used, concurrent, enumeration, and SMTP-failure cases pass.

- [ ] **Step 6: Commit password-reset delivery**

```powershell
git add MediBridge.Core MediBridge.Services tests
git commit -m "feat: deliver password reset tokens securely"
```

## Task 9: Full verification, live smoke, and rollback evidence

**Files:**
- Modify: `docs/runtime-configuration.md`
- Create during QA: `e2e-test-data-YYYYMMDD-HHMMSS.json`

- [ ] **Step 1: Build everything**

```powershell
dotnet build .\MediBridge.slnx --nologo
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run all automated suites**

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --no-build
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --no-build
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --no-build
```

Expected: all tests pass; report exact totals.

- [ ] **Step 3: Verify Onion boundaries**

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --no-build --filter "LayeringBoundaryTests|IdentityScopeGuardTests|Phase3LayeringBoundaryTests"
```

Expected: Core has no EF/HTTP/Cloudinary/MailKit dependency; Services has no EF/ASP.NET/Cloudinary/MailKit dependency; controllers reference services only.

- [ ] **Step 4: Run a focused real-provider E2E smoke**

Against the configured remote SQL Server, Cloudinary, and SMTP account:

1. register a unique doctor and company;
2. retrieve OTPs only from the configured mailbox/dev-safe override;
3. verify contact;
4. prove login and admin approval fail before verification and succeed in the required order;
5. approve accounts and login;
6. upload a valid PNG and verify Cloudinary public ID plus DB metadata;
7. obtain a signed URL and fetch the bytes;
8. upload a disposable replacement and verify supersession;
9. delete a separate disposable Pending/Rejected file and verify Cloudinary deletion plus DB lifecycle state;
10. request password reset, retrieve the email token, reset, and login with the new password.

Persist generated accounts, IDs, masked tokens, storage keys, idempotency keys, response records, and DB checks to a timestamped JSON artifact. Do not persist full JWTs, SMTP credentials, Cloudinary credentials, OTP plaintext, or reset-token plaintext.

- [ ] **Step 5: Verify database effects and secret absence**

Check:

- exactly one active/successfully consumed verification flow per completed challenge;
- old OTP flows are superseded and cannot verify;
- attempt/cooldown/max-attempt fields change as expected;
- reset flow stores only hash and becomes consumed;
- uploaded `StoredFiles.StorageKey` equals a real Cloudinary public ID;
- replacement links old to new;
- deletion state matches provider state;
- no plaintext OTP/reset token exists in SQL text columns;
- configuration files and test output contain no credentials.

- [ ] **Step 6: Exercise rollback/cleanup paths**

With the in-memory provider and SMTP fake, force:

- Cloudinary upload failure — no metadata row;
- SQL failure after Cloudinary upload — uploaded object deleted;
- compensation delete failure — System audit emitted and cleanup state retained;
- SMTP failure after registration — account/hashed flow remain, delivery status Failed, resend succeeds;
- SMTP failure during forgot-password — caller still receives 202, failure is audited;
- cancellation — stream, Cloudinary request, SMTP request, and SQL command terminate without partial committed state beyond documented two-phase behavior.

- [ ] **Step 7: Update operational documentation**

Document required environment variables, key rotation, Cloudinary folder/resource retention, signed URL lifetime, SMTP TLS, development override safeguards, migration order, deletion retry procedure, and the rule that operators must never query or log raw one-time secrets.

- [ ] **Step 8: Final commit**

```powershell
git add docs tests
git commit -m "test: verify file storage and one-time secret delivery"
```

## Self-review checklist

- Cloudinary bytes, real public ID, signed access, replacement, constrained deletion, validation, provider failure mapping, and cleanup are covered.
- Contact flow covers registration issuance, email delivery, hashed storage, resend, expiry, attempt limits, supersession, auditing, approval gate, and login gate.
- Password reset covers delivery, hash-only persistence, enumeration resistance, invalid/expired/used tokens, refresh revocation, and live success flow.
- Verification includes build, unit, contract, integration, live E2E, SQL effects, provider effects, boundary checks, and secret scanning.
- The plan does not apply migrations to the remote database automatically; deployment approval remains a separate operational gate.
