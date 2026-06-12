# Feature Specification: File Storage, Verification & Security Plumbing (Phase 4)

**Feature Branch**: `[004-file-storage-security]`  
**Created**: 2026-06-06  
**Status**: Draft  
**Input**: User description: "Phase 4: File Storage, Verification & Security Plumbing. Storage provider credentials must be stored as secrets and never in appsettings or tracked configuration."

## Clarifications

### Session 2026-06-06

- Q: Should Phase 4 require safety scanning before uploaded files become usable? -> A: Defer safety scanning to a later phase; Phase 4 records scan status only.
- Q: What file types and size limits should Phase 4 allow? -> A: Define per purpose: documents 10 MB, audio 25 MB, video 100 MB.
- Q: How long should private file access grants remain valid? -> A: 10 minutes.
- Q: What upload rate limit should Phase 4 enforce? -> A: 20 upload attempts per user per hour.

### Session 2026-06-07

- Q: What exact file types should Phase 4 allow? -> A: Documents `application/pdf` and `application/vnd.openxmlformats-officedocument.wordprocessingml.document`; images `image/jpeg` and `image/png`; audio `audio/mpeg`; video `video/mp4`; extensions `.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, `.mp4`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upload Private Verification Files (Priority: P1)

As a doctor or pharmaceutical company user, I need to submit verification documents privately so admins can review my account without exposing sensitive files to unauthorized users.

**Why this priority**: Verification documents are required for trusted onboarding, and mishandling them creates privacy and compliance risk.

**Independent Test**: Submit a valid verification document as a pending doctor or company user, then confirm the file is accepted, recorded for review, and inaccessible to unrelated users.

**Acceptance Scenarios**:

1. **Given** an authenticated doctor or company account awaiting verification, **When** the user uploads a valid verification document, **Then** the system stores the file privately and records owner, purpose, size, type, upload time, and review status.
2. **Given** another doctor or company account, **When** that account attempts to access the uploaded verification document, **Then** access is denied and the file contents are not disclosed.
3. **Given** an admin reviewing an account, **When** the admin opens the verification file, **Then** the admin can access the file and see the review context needed to approve or reject it.

---

### User Story 2 - Review and Audit Files (Priority: P1)

As an admin, I need to approve or reject uploaded verification and campaign-supporting files with reasons so future support and compliance reviews can explain each decision.

**Why this priority**: MediBridge relies on trust decisions for medical professionals, pharmaceutical companies, and promotional content.

**Independent Test**: Review a submitted file as an admin, record both approval and rejection outcomes in separate cases, and confirm the decision history remains available without modifying the original upload record.

**Acceptance Scenarios**:

1. **Given** a pending verification file, **When** an admin approves it, **Then** the file status changes to approved and the decision records reviewer, decision time, and review note.
2. **Given** a pending verification file, **When** an admin rejects it, **Then** the file status changes to rejected and the rejection reason is visible to authorized users who need to resolve it.
3. **Given** a prior file review decision, **When** a later correction is needed, **Then** the system preserves the original decision and records a new linked correction or superseding decision.
4. **Given** a user replaces a rejected or outdated file, **When** the replacement upload succeeds, **Then** the original file remains historically linked, the replacement file becomes pending review, and the original file is not treated as usable evidence or an approved asset.

---

### User Story 3 - Store Campaign and Message Attachments Safely (Priority: P2)

As a pharmaceutical company user, I need to attach campaign media, voice notes, and clinical research files so campaign reviewers and future delivery workflows can use the approved materials safely.

**Why this priority**: Later campaign phases depend on trusted file metadata and controlled access before content can be sent to doctors.

**Independent Test**: Attach valid files to a campaign draft, verify the files are tied to the campaign and company owner, and confirm unapproved or invalid files cannot be used as approved campaign assets.

**Acceptance Scenarios**:

1. **Given** an authenticated company user editing its own campaign draft, **When** the user uploads valid campaign media or research files, **Then** the files are recorded under the campaign and remain pending review where review is required.
2. **Given** a company user who does not own a campaign, **When** the user attempts to attach or view campaign files for that campaign, **Then** access is denied.
3. **Given** a campaign references a file that is rejected, deleted, missing, or still pending required review, **When** later workflows check campaign readiness, **Then** the file is not treated as an approved deliverable asset.

---

### User Story 4 - Protect Storage Configuration and File Access (Priority: P1)

As a platform operator, I need storage credentials and file access controls handled securely so secrets are not leaked and private files cannot be reached through public configuration or guessable links.

**Why this priority**: File storage contains sensitive verification and medical-promotion materials, and leaked credentials can compromise all stored assets.

**Independent Test**: Inspect tracked configuration, run startup/configuration validation, and attempt unauthorized file access to confirm credentials are secret-only and private files require authorization.

**Acceptance Scenarios**:

1. **Given** the application is configured for external file storage, **When** tracked configuration files are inspected, **Then** no storage provider credential, credential URL, access key, secret key, or token is present.
2. **Given** required storage secrets are missing in an environment that enables uploads, **When** the application starts or storage is used, **Then** the system fails with a clear operator-facing configuration error and does not silently fall back to insecure public storage.
3. **Given** a private file exists, **When** an unauthorized or anonymous user tries to access it, **Then** the file contents and storage location are not disclosed.
4. **Given** an authorized user receives access to a private file, **When** the access grant is older than 10 minutes, **Then** the file can no longer be opened through that grant.

### Edge Cases

- A file upload has no file, zero bytes, or exceeds the configured maximum size for its purpose; the system rejects it with a clear user-facing reason.
- A file uses an unsupported content type or suspicious extension; the system rejects it before storing it as usable content.
- A file upload is interrupted after metadata is created or after remote storage succeeds; the system must leave no usable orphaned file reference and must allow safe retry.
- A user replaces a rejected or outdated file; the original metadata and review history remain intact, the new file receives its own metadata and pending review state, and the two files are linked.
- A user deletes a file they are allowed to manage; the file becomes unavailable, deletion is audited, and historical metadata remains available for authorized audit review.
- A user exceeds 20 upload attempts within one hour; additional upload attempts are denied until the rate-limit window resets.
- A storage provider credential is accidentally added to tracked configuration; validation and review must detect the violation before release.
- A user attempts to access a file by guessing an identifier or reusing another user's file reference; authorization must deny access.
- A user attempts to use an expired private file access grant; access is denied and the user must request a new authorization-checked grant.
- A pending, rejected, quarantined, deleted, or missing file is referenced by a later workflow; the workflow must treat the file as unavailable.
- A reviewer updates a decision at the same time another reviewer acts on the same file; the system must prevent lost review decisions.
- A file owner is soft-deleted or later rejected; historical file metadata and review decisions remain auditable, while normal file access and replacement are restricted to Admin users.
- A storage provider temporarily fails during upload, deletion, or retrieval; users receive a clear failure without exposing provider errors or secrets.
- A malware or safety scan is unavailable; Phase 4 records the scan status for future use but does not require a passing scan before file approval or campaign readiness.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow authenticated Doctor and Pharmaceutical Company users to upload verification documents tied to their own account profile.
- **FR-002**: System MUST allow authenticated Pharmaceutical Company users to upload campaign media, voice notes, and clinical research attachments tied only to campaigns they own.
- **FR-003**: System MUST record stored file metadata including owner type, owner id, related campaign when applicable, purpose, original file name, content type, size, storage reference, visibility, upload time, review status, and review metadata.
- **FR-004**: System MUST store file contents outside tracked application configuration and keep only non-secret storage references in application records.
- **FR-005**: System MUST require storage provider credentials to come from secret configuration, environment-specific secret storage, or an equivalent protected operator channel; credentials MUST NOT be stored in appsettings, source-controlled files, logs, API responses, audit events, or database records.
- **FR-006**: System MUST validate at startup or first storage use that required storage secrets are present when file upload features are enabled.
- **FR-007**: System MUST reject file uploads that exceed the configured size limit, use unsupported content types, or have unsafe file names.
- **FR-007A**: System MUST allow upload size limits by file category: documents up to 10 MB each, images up to 10 MB each, audio up to 25 MB each, and video up to 100 MB each.
- **FR-007B**: System MUST allow only these content types: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `image/jpeg`, `image/png`, `audio/mpeg`, and `video/mp4`.
- **FR-007C**: System MUST allow only these file extensions: `.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, and `.mp4`; extension and content type must both match the requested file purpose.
- **FR-008**: System MUST prevent user-supplied file names from determining the private storage location or public access path.
- **FR-009**: System MUST keep verification documents and review-required campaign attachments private by default.
- **FR-010**: System MUST authorize file upload, retrieval, review, replacement, and deletion attempts based on user role, owner relationship, file purpose, and review state.
- **FR-011**: System MUST allow Admin users to view pending verification and campaign-supporting files that require review.
- **FR-012**: System MUST allow Admin users to approve, reject, quarantine, or request replacement for reviewable files with a recorded reason where applicable.
- **FR-013**: System MUST preserve file review history as append-only records; corrections or superseding decisions MUST be linked to the original decision rather than deleting or mutating the historical evidence.
- **FR-014**: System MUST expose only non-secret, authorization-checked access grants to private file content when a user is allowed to view or download it.
- **FR-014A**: System MUST expire private file access grants after 10 minutes.
- **FR-015**: System MUST avoid logging raw file contents, storage credentials, private access tokens, or provider error payloads that may contain secrets.
- **FR-016**: System MUST emit audit events for file upload, retrieval by privileged users, review decisions, deletion, replacement, failed authorization, and storage configuration validation failures.
- **FR-017**: System MUST support safe retry for interrupted uploads without creating duplicate usable file records or leaving approved records without stored content.
- **FR-017A**: System MUST limit each authenticated user to 20 upload attempts per hour across verification and campaign file upload workflows.
- **FR-018**: System MUST support owner-initiated replacement upload for rejected or outdated files while preserving prior file metadata and review history.
- **FR-018A**: System MUST link a replacement file to the file it replaces, mark the original file unavailable for normal evidence or asset use, and set the replacement file to pending review.
- **FR-018B**: System MUST support deleting or making unavailable a file when the actor is authorized, while preserving historical metadata, review history, and audit evidence.
- **FR-019**: System MUST restrict deleted, rejected, quarantined, or missing files from being treated as approved verification evidence or approved campaign assets.
- **FR-019B**: System MUST restrict normal access and replacement for files whose Doctor or Pharmaceutical Company owner is soft-deleted, rejected, or otherwise no longer active; Admin users may retain audit access.
- **FR-019A**: System MUST record file safety scan status when available, but Phase 4 MUST NOT require a passing safety scan before file approval or campaign readiness.
- **FR-020**: System MUST keep file metadata persistence within the approved repository and unit-of-work boundaries.
- **FR-021**: System MUST keep file storage operations behind service abstractions so controllers do not directly access storage provider clients or persistence infrastructure.
- **FR-022**: System MUST return API-visible results using the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` response envelope.
- **FR-023**: System MUST handle file, storage, validation, and authorization errors through the global error handling approach without exposing raw stack traces, secrets, private storage references, or provider diagnostics to users.
- **FR-024**: System MUST secure file workflows with JWT authentication and role-aware authorization for Doctor, Pharmaceutical Company, and Admin users.
- **FR-025**: System MUST NOT implement campaign submission, campaign delivery jobs, doctor message viewing, wallet settlement, reporting read models, or public file galleries in Phase 4.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-003`, `FR-013`, `FR-016`, `FR-018`, `FR-019` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`
  - `FR-004` to `FR-012`, `FR-014`, `FR-015`, `FR-017`, `FR-021`, `FR-023`, `FR-024` -> `MediBridge.Services`, `MediBridge.APIs`
  - `FR-020` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`
  - `FR-022`, `FR-025` -> `MediBridge.APIs`, `MediBridge.Services`
- **CA-002 Controller Boundary**: Controllers remain HTTP-only and delegate upload, retrieval, review, validation, authorization, and storage work to services.
- **CA-003 SQL Persistence Boundary**: File metadata, review history, and audit history use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services must not depend on EF Core directly.
- **CA-004 Response Contract**: API-visible file workflow responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: File, storage, authorization, and validation errors are handled by global exception middleware, with no raw stack traces, secrets, private storage references, or provider diagnostics exposed.
- **CA-006 Security**: File workflows require JWT and role-aware authorization for Doctor, Pharmaceutical Company, and Admin users.
- **CA-007 Ambiguity Control**: Queue and wallet behavior are not in Phase 4 scope; no queue or wallet ambiguity is introduced.
- **CA-008 Queue Determinism**: Queueing is out of scope for Phase 4 except that campaign files must expose approved/unavailable state for later queue and delivery workflows.
- **CA-009 Wallet Determinism**: Walleting is out of scope for Phase 4 and no debit, credit, fee, transaction, or settlement behavior is added.

### Key Entities *(include if feature involves data)*

- **Stored File**: Metadata record for a verification document, campaign media file, voice note, or clinical research attachment, including owner, purpose, size, type, visibility, storage reference, and lifecycle state.
- **File Review**: Append-only admin decision record for approval, rejection, quarantine, replacement request, or correction of a file review outcome.
- **File Access Grant**: Short-lived authorization result that allows an approved user to access private file content without revealing storage credentials.
- **File Safety Result**: Review or scan outcome that determines whether a file can become available for verification or campaign use.
- **File Audit Event**: Trace record for upload, privileged retrieval, review, deletion, replacement, failed authorization, and storage configuration validation events.
- **Storage Secret Configuration**: Protected operator-provided settings required for storage access; these values are never persisted as ordinary file metadata.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of tracked configuration files contain no storage provider credentials, credential URLs, access keys, secret keys, private access tokens, or equivalent secret values.
- **SC-002**: 100% of environments with uploads enabled fail validation when required storage secrets are missing, with a clear operator-facing configuration message.
- **SC-003**: 100% of valid verification uploads by authenticated Doctor and Pharmaceutical Company users create private file metadata and make the file available for admin review.
- **SC-004**: 100% of unauthorized file access attempts in automated validation are denied without exposing file contents, private storage references, storage credentials, or provider diagnostics.
- **SC-004A**: 100% of private file access grants older than 10 minutes are rejected in automated validation.
- **SC-005**: 100% of invalid uploads in automated validation, including empty file, unsupported type, unsupported extension, unsafe name, document over 10 MB, image over 10 MB, audio over 25 MB, and video over 100 MB cases, are rejected before becoming usable.
- **SC-006**: 100% of admin file review decisions in automated validation record reviewer, decision, reason where required, and decision time.
- **SC-007**: 100% of review correction scenarios preserve the original review decision and create a linked correction or superseding decision.
- **SC-008**: 100% of interrupted upload retry scenarios avoid duplicate usable file records and avoid approved records without stored content.
- **SC-008A**: 100% of users who exceed 20 upload attempts within one hour are prevented from starting additional uploads until the rate-limit window resets.
- **SC-009**: Architecture validation finds 0 controller references to storage provider clients, 0 controller references to persistence infrastructure, and 0 service references to EF Core infrastructure types.
- **SC-010**: API validation confirms 100% of file workflow responses use the standard response envelope and expose no raw stack traces or secrets.
- **SC-011**: Audit validation confirms 100% of sampled upload, privileged retrieval, review, replacement, deletion, failed authorization, and storage configuration failure events are recorded without secrets.
- **SC-012**: Readiness checks confirm 100% of rejected, quarantined, deleted, replaced, or missing files are unavailable as approved verification evidence or approved campaign assets.
- **SC-013**: 100% of replacement upload scenarios preserve the original file metadata and review history, link the replacement file, and set the replacement file to pending review.
- **SC-014**: 100% of authorized delete scenarios make the file unavailable, preserve historical metadata and review records, and emit a non-secret deletion audit event.
- **SC-015**: 100% of access or replacement attempts for files owned by soft-deleted or rejected Doctor/Pharmaceutical Company owners are denied to non-Admin users in automated validation.

## Assumptions

- Phase 4 builds on Phase 1 API foundations, Phase 2 identity/approval, and Phase 3 file metadata foundations.
- Doctor and Pharmaceutical Company users can upload only their own verification files; Admin users perform verification review.
- Pharmaceutical Company users can upload campaign-supporting files only for campaigns they own.
- File contents are stored in a private external storage service, while SQL Server stores metadata, review state, and audit history.
- Phase 4 upload limits are category-based: documents up to 10 MB, images up to 10 MB, voice notes/audio up to 25 MB, and campaign video files up to 100 MB.
- Allowed Phase 4 document types are `application/pdf` and `application/vnd.openxmlformats-officedocument.wordprocessingml.document` with `.pdf` or `.docx` extensions.
- Allowed Phase 4 image types are `image/jpeg` and `image/png` with `.jpg`, `.jpeg`, or `.png` extensions.
- Allowed Phase 4 audio type is `audio/mpeg` with `.mp3` extension.
- Allowed Phase 4 video type is `video/mp4` with `.mp4` extension.
- Upload attempt limits apply per authenticated user across all Phase 4 upload purposes.
- Storage credentials are supplied through secret configuration in each environment and are never committed to source control or copied into appsettings.
- Private file access uses 10-minute, authorization-checked access grants rather than permanent public links.
- Malware or safety scanning is deferred to a later phase; Phase 4 records scan status only when available.
- Phase 4 does not submit campaigns, activate queues, deliver messages to doctors, settle wallet balances, or build reporting views.
