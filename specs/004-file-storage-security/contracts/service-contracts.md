# Service Contracts: File Storage, Verification & Security Plumbing (Phase 4)

These contracts describe service-facing behavior. Exact C# signatures may evolve during implementation, but layer ownership and data boundaries must remain intact.

## Contract Rules

- `MediBridge.Core` owns domain entities, enums, and repository contracts.
- `MediBridge.Services` owns file workflow service contracts and storage-provider abstractions.
- `MediBridge.Repository` implements SQL metadata/review/audit repositories.
- `MediBridge.APIs` owns HTTP controllers and returns response envelopes.
- Controllers must not depend on Cloudinary SDK types, `MediBridgeDbContext`, repository implementations, or provider clients.
- SQL records, logs, API responses, and audit metadata must not contain provider credentials, signed URLs, private access tokens, or raw provider diagnostics.

## IFileWorkflowService

**Purpose**: Orchestrate upload validation, owner authorization, provider storage, metadata persistence, review decisions, private access grants, replacement, deletion, and audit events.

**Required operations**:

- `UploadVerificationDocumentAsync(actorUserId, role, file, cancellationToken)`
- `UploadCampaignFileAsync(actorUserId, campaignId, purpose, file, cancellationToken)`
- `CreatePrivateAccessGrantAsync(actorUserId, role, storedFileId, cancellationToken)`
- `ReviewFileAsync(adminUserId, storedFileId, decision, reason, cancellationToken)`
- `ReplaceFileAsync(actorUserId, role, storedFileId, file, cancellationToken)`
- `DeleteFileAsync(actorUserId, role, storedFileId, cancellationToken)`
- `ListPendingReviewsAsync(adminUserId, pageNumber, pageSize, cancellationToken)`
- `GetReviewHistoryAsync(adminUserId, storedFileId, cancellationToken)`

**Acceptance expectations**:

- Upload validation runs before provider upload.
- Each authenticated user is limited to 20 upload attempts per hour.
- File access grants expire after 10 minutes.
- Review history is append-only.
- Storage failures do not leave approved file records without stored content.
- Replacement upload creates a new file record, links it to the original, marks the original unavailable, and sets the replacement file to pending review.
- Delete makes a file unavailable, attempts provider deletion through `IFileStorageProvider`, preserves metadata/review history, and emits a non-secret audit event.
- Normal access, replacement, and delete are denied for files whose Doctor or Company owner is soft-deleted, rejected, suspended from normal access, or otherwise inactive; Admin audit access may still read metadata and review history.
- All returned DTOs omit provider secrets, signed URLs except immediate access-grant response, and raw provider diagnostics.

## IFileStorageProvider

**Purpose**: Hide Cloudinary implementation details from controllers, Core, and Repository.

**Required operations**:

- `UploadAsync(storageRequest, cancellationToken)`
- `CreatePrivateAccessGrantAsync(storageKey, resourceType, expiresAtUtc, cancellationToken)`
- `DeleteAsync(storageKey, resourceType, cancellationToken)`

**Storage request fields**:

- Generated storage key
- Content stream
- Content type
- Original extension for validation only
- Resource type: image, video, or raw
- Delivery type: private or authenticated
- Non-secret tags/context such as purpose and environment

**Storage response fields**:

- Storage provider name
- Storage key
- Resource type
- Delivery type
- Size
- Content type
- Provider version or etag when available

**Rules**:

- Do not return provider credentials.
- Do not log raw provider error payloads.
- Do not use user-provided filenames as storage keys.
- Use secure URLs and signed/time-limited access for private content.
- Treat delete as best-effort provider cleanup while the service preserves SQL auditability.

## Repository Contract Additions

### IStoredFileRepository

Required Phase 4 capabilities:

- Add pending upload metadata.
- Mark upload as stored with provider metadata.
- Mark upload as failed.
- Fetch file with owner and review state.
- List files by owner, campaign, purpose, and review status.
- Update current review summary with optimistic concurrency.
- Mark file as deleted.
- Link replacement files.
- Mark original files as replaced.
- Check whether a Doctor or Company owner is active for normal access.
- Check whether a file is available as approved evidence or campaign asset.

### IFileReviewRepository

Required capabilities:

- Append file review decision.
- Append correction/superseding decision linked to earlier review.
- List review history for a file.
- Fetch a review by id for correction validation.

### IFileAccessGrantAuditRepository

Required capabilities:

- Append issued grant audit without storing signed URL/token.
- Append denied access audit.
- Append expired grant audit when detected.

## File Validation Contract

Allowed MIME types:

- Documents: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
- Images: `image/jpeg`, `image/png`
- Audio: `audio/mpeg`
- Video: `video/mp4`

Allowed extensions:

- `.pdf`
- `.docx`
- `.jpg`
- `.jpeg`
- `.png`
- `.mp3`
- `.mp4`

Size limits:

- Documents: 10 MB
- Images: 10 MB
- Audio: 25 MB
- Video: 100 MB

Validation rules:

- MIME type and extension must both be allowed.
- MIME type and extension must match the requested file purpose.
- Validation must happen before provider upload.
- Unsupported type, unsupported extension, mismatched type/extension, unsafe filename, empty file, and oversize file all return validation failure without calling `IFileStorageProvider.UploadAsync`.

## DTO Contract Rules

### Upload result DTO

Includes:

- `Id`
- `Purpose`
- `OwnerType`
- `OwnerId`
- `RelatedCampaignId`
- `OriginalFileName`
- `ContentType`
- `SizeBytes`
- `ReviewStatus`
- `UploadStatus`
- `SafetyScanStatus`
- `CreatedAtUtc`
- `ReplacedFileId`

Excludes:

- Provider credential URL
- API key or API secret
- Signed URL
- Private access token
- Raw provider diagnostics

### Access grant DTO

Includes:

- `Url`
- `ExpiresAtUtc`

Rules:

- Returned only from the access-grant endpoint after authorization.
- Not persisted in SQL or audit metadata.
- Expires 10 minutes after creation.

### Replacement result DTO

Includes the same fields as upload result DTO.

Rules:

- The replacement file starts with `ReviewStatus = Pending`.
- The response may include `ReplacedFileId` so the client can show which file was superseded.
- The response must not include storage key, signed URL, credentials, provider diagnostics, or private tokens.

### Review DTO

Includes:

- `Id`
- `StoredFileId`
- `Decision`
- `Reason`
- `ReviewedByAdminId`
- `CreatedAtUtc`
- `CorrectsReviewId`

Rules:

- Rejection, quarantine, replacement request, and correction require reason.
- Corrections keep the original review row intact.

### Delete result DTO

Includes:

- `Id`
- `UploadStatus`
- `DeletedAtUtc`

Rules:

- The result must not include storage key, signed URL, credentials, provider diagnostics, or private tokens.
- Historical metadata and review records remain queryable for authorized audit workflows.
