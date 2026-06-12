# Research: File Storage, Verification & Security Plumbing (Phase 4)

## Decision 1: Use Cloudinary through a service abstraction, not directly from controllers

**Decision**: Add Cloudinary .NET SDK usage only inside `MediBridge.Services` via `IFileStorageProvider`, with a concrete `CloudinaryFileStorageProvider`. `MediBridge.Core`, controllers, and repositories must not reference Cloudinary SDK types.

**Rationale**: Cloudinary's .NET SDK supports upload and delivery capabilities for .NET applications, and current documentation calls out a security-focused SDK update. Keeping the SDK behind a provider abstraction preserves Onion boundaries and leaves room to replace or supplement the provider later. Source: [Cloudinary .NET SDK documentation](https://cloudinary.com/documentation/dotnet_integration).

**Alternatives considered**:

- Direct Cloudinary SDK use in controllers: rejected because it violates thin-controller and testability gates.
- Direct Cloudinary SDK use in Repository: rejected because Repository owns SQL persistence, not external media operations.
- Provider-agnostic implementation only: rejected for Phase 4 because the user provided Cloudinary as the intended provider.

## Decision 2: Configure Cloudinary via secret-only configuration

**Decision**: Use secret configuration/environment variables for the provider credential, with `CLOUDINARY_URL` supported as an operator-provided secret. Do not place the value in `appsettings.json`, `appsettings.Development.json`, source-controlled files, logs, API responses, audit metadata, or database records.

**Rationale**: Cloudinary requires cloud name plus API key and API secret for secure API calls, and its documentation supports configuration via environment variable. This aligns with the user's explicit secret-only requirement and the Phase 4 spec. Source: [Cloudinary .NET SDK configuration documentation](https://cloudinary.com/documentation/dotnet_integration).

**Alternatives considered**:

- Store `CLOUDINARY_URL` in appsettings: rejected by explicit user requirement and secret-safety criteria.
- Split provider secrets into database settings: rejected because database records are not a secret store.
- Log the credential for diagnostics: rejected because logs must never contain provider secrets.

## Decision 3: Upload as private or authenticated assets with generated storage keys

**Decision**: Upload file content using generated storage keys/public IDs under a controlled folder convention, not user file names. Use private or authenticated delivery behavior for private assets.

**Rationale**: Cloudinary upload parameters support delivery types such as `private` and `authenticated`, and its upload reference notes that public IDs can be specified instead of relying on original names. This supports non-guessable storage references and prevents user-controlled filenames from becoming access paths. Source: [Cloudinary Upload API reference](https://cloudinary.com/documentation/image_upload_api_reference).

**Alternatives considered**:

- Use original file name as the storage key: rejected because it increases collision, disclosure, and path-guessing risk.
- Store all files publicly and gate only database references: rejected because private documents must not be reachable by public URLs.
- Store signed URLs in SQL: rejected because access grants must be short-lived and not persisted as reusable tokens.

## Decision 4: Generate 10-minute authorization-checked access grants

**Decision**: Generate a new authorization-checked access grant at request time for allowed users, expiring after 10 minutes. Do not cache or persist the signed URL/token; persist only a non-secret audit event about the grant.

**Rationale**: Cloudinary documents time-limited signed access for private media and exposes an expiration parameter. The spec clarified a 10-minute duration, which is stricter than Cloudinary's documented default and gives deterministic validation. Source: [Cloudinary media access control documentation](https://cloudinary.com/documentation/control_access_to_media).

**Alternatives considered**:

- Permanent private URLs: rejected because the spec requires time-limited authorization-checked access.
- One-hour grants: rejected by clarification; broader exposure than needed.
- Proxy all file bytes through the API: rejected for Phase 4 unless later required by compliance, because signed access grants satisfy the current spec with less server load.

## Decision 5: Purpose-based upload validation before provider upload

**Decision**: Validate upload size, exact MIME type, exact extension, MIME/extension match, empty file, and unsafe file name before sending content to Cloudinary. Enforce documents up to 10 MB, images up to 10 MB, audio up to 25 MB, and campaign video up to 100 MB. Allow only `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `image/jpeg`, `image/png`, `audio/mpeg`, and `video/mp4` with `.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, and `.mp4`.

**Rationale**: Rejecting invalid content before provider upload reduces cost, latency, and orphan cleanup. Purpose-based limits came from clarification and map directly to the Phase 4 success criteria.

**Alternatives considered**:

- One global file limit: rejected because documents, images, audio, and video have different realistic sizes.
- Provider-first upload then validate metadata: rejected because invalid content should not become usable or create avoidable provider objects.
- Mandatory malware scan gate: rejected by clarification; Phase 4 records scan status only.

## Decision 9: Replacement and delete are service-owned workflows

**Decision**: Implement replacement as owner-initiated upload through `FileWorkflowService`: the replacement gets a new `StoredFile`, the original is marked `Replaced`, the original points to the replacement, and all original metadata/review history remains auditable. Implement delete as an authorized service operation that marks the file unavailable, attempts provider deletion through `IFileStorageProvider`, preserves metadata/history, and emits a non-secret audit event.

**Rationale**: Replacement and delete are explicit Phase 4 functional requirements and API contract behaviors. Keeping both workflows in Services preserves thin controllers, lets Repository handle only SQL state transitions, and keeps Cloudinary operations behind the provider abstraction.

**Alternatives considered**:

- Treat replacement request review as actual replacement: rejected because it does not upload a new file or link replacement metadata.
- Hard-delete file metadata: rejected because review/audit history must remain available.
- Let controllers call provider delete directly: rejected because it violates service-owned use cases and controller boundaries.

## Decision 6: Use existing fixed-window rate limiting for uploads

**Decision**: Add an upload rate-limit policy to existing ASP.NET Core rate-limiting configuration: 20 upload attempts per authenticated user per hour.

**Rationale**: The project already has fixed-window rate-limiting infrastructure and response-envelope handling. Reusing it avoids new infrastructure and makes the 20/hour requirement directly testable.

**Alternatives considered**:

- Custom in-service counters: rejected because the API layer already owns request throttling.
- No rate limit: rejected by clarification.
- Per-IP only limits: rejected because the requirement is per authenticated user across upload purposes.

## Decision 7: Append-only file review history

**Decision**: Add `FileReview` records for approval, rejection, quarantine, replacement request, and correction/superseding decisions. Keep `StoredFile.ReviewStatus` as the current state summary for efficient reads while retaining append-only review history.

**Rationale**: The spec requires preserving original review decisions and linked corrections. A history table plus current-state summary matches the Phase 3 audit/history pattern.

**Alternatives considered**:

- Mutate only `StoredFile.ReviewStatus` and reason: rejected because it loses decision history.
- Store review history only as generic audit metadata: rejected because file review needs queryable decision relationships and correction links.

## Decision 8: Treat safety scanning as metadata-only in Phase 4

**Decision**: Add scan-status metadata fields that can record unavailable, pending, passed, failed, or deferred scan state, but do not require a passing scan before approval or campaign readiness in Phase 4.

**Rationale**: This directly implements the clarification while preserving a future extension point for malware scanning. It avoids introducing an unselected external dependency.

**Alternatives considered**:

- Require scan pass for all uploads: rejected by user clarification.
- Omit scan fields entirely: rejected because Phase 4 must record scan status when available.
