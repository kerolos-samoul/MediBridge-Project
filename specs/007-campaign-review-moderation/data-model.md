# Data Model: Phase 6 Campaign Review & Moderation

## Persistence Direction

This feature reuses existing campaign, stored-file, target, review-history, queue, and audit foundations. New or expanded behavior must be exposed through service-owned use cases and persisted through Repository + Unit of Work abstractions; EF Core configuration and SQL Server migrations remain in `MediBridge.Repository`.

## Entities

### Campaign

Reuses existing `Campaign`.

**Fields used or required**:

- `Id`
- `CompanyId`
- `Title`
- `Description`
- `MediaFileId`
- `VoiceNoteFileId`
- `ClinicalResearchInfo`
- `Status`
- `SubmittedAtUtc` (nullable until the campaign first enters `PendingReview`; refreshed for each revision resubmission)
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `IsDeleted`
- `DeletedAtUtc`

**Required lifecycle states**:

```text
Draft -> PendingReview
PendingReview -> Approved
PendingReview -> Rejected
PendingReview -> RevisionRequired
RevisionRequired -> PendingReview
Rejected is terminal
Approved -> Active/Paused/Completed/Cancelled in later workflows
```

**Validation rules**:

- Pending moderation list includes only active, non-deleted campaigns with `Status = PendingReview`.
- Pending moderation list orders by `SubmittedAtUtc` ascending, then `Id` ascending. `UpdatedAtUtc` is never used as the submission-order key.
- Approval is allowed only from `PendingReview`.
- Rejection is allowed only from `PendingReview` and is final.
- Revision-required is allowed only from `PendingReview`; campaign title, description, clinical research information, and campaign assets are editable only in `Draft` or `RevisionRequired`.
- `PendingReview`, `Rejected`, `Approved`, `Active`, `Paused`, `Completed`, `Cancelled`, deleted, and non-owned campaigns reject company edit and asset-management operations.
- Submission from `Draft` and resubmission from `RevisionRequired` refresh target snapshots, set `SubmittedAtUtc`, validate current wallet affordability, and move to `PendingReview` without mutating wallet balances or creating wallet transaction/ledger records.
- Submission requires at least one active campaign media asset whose review status is Pending or Approved.
- Campaign approval requires campaign text, a target snapshot with at least one selected doctor, and at least one separately approved campaign media asset.
- Campaign approval must not approve pending media assets.

### Campaign Review Package

Read model assembled by services from campaign, company, target, stored-file, and review-history data.

**Fields**:

- `CampaignId`
- `CompanyId`
- `CompanyName`
- `Title`
- `Description`
- `SubmittedAtUtc`
- `Status`
- `ReviewableMediaAssets` (active Pending and Approved campaign media)
- `OptionalVoiceNotes`
- `OptionalClinicalReferences`
- `OptionalSupportingAttachments`
- `TargetCount`
- `TargetSummary`
- `LatestReviewDecision`
- `ReviewReadinessIssues`

**Validation rules**:

- Admin review detail must not expose raw storage keys or provider credentials.
- Every active reviewable file maps through `IFileAccessService` to a short-lived `AccessUrl` and `AccessExpiresAtUtc`; storage-provider failure maps to the safe 503 workflow response.
- Missing text, missing target snapshot, or missing separately approved media appears as review-readiness issues and blocks approval.
- Optional submitted files are visible for inspection but do not replace the required approved campaign media asset.

### Campaign Submission Attempt

Append-only record used for initial submission and revision-resubmission idempotency without wallet transactions.

**Fields**:

- `Id`
- `CampaignId`
- `IdempotencyKey`
- `SubmittedAtUtc`
- `TargetCount`
- `EstimatedCost`
- `Currency` (`EGP`)
- `CreatedAtUtc`

**Validation rules**:

- `(CampaignId, IdempotencyKey)` is unique.
- A successful Draft or RevisionRequired submission appends one attempt in the same transaction as target replacement and the PendingReview transition.
- While the campaign remains PendingReview, an identical retry using the current attempt key returns the stored attempt result without replacing targets or adding history/audit rows.
- A different key while already PendingReview, reuse of a prior historical attempt key after revision, or a payload/state conflict fails without mutation.
- Submission attempts are never updated or deleted and never reference wallet transactions.

### Stored File / Campaign Media Asset

Reuses existing `StoredFile` with `OwnerType = Campaign` and `Purpose = CampaignMedia` for required campaign media.

**Fields used**:

- `Id`
- `OwnerType`
- `OwnerId`
- `Purpose`
- `OriginalFileName`
- `ContentType`
- `SizeBytes`
- `StorageKey`
- `Visibility`
- `ReviewStatus`
- `CreatedAtUtc`
- `ReviewedAtUtc`
- `ReviewedByAdminId`
- `ReviewReason`

**Validation rules**:

- At least one active campaign media asset with `ReviewStatus = Pending` or `Approved` is required before submission.
- At least one active campaign media asset with `ReviewStatus = Approved` is required before campaign approval.
- Campaign approval does not mutate media asset review status.
- Protected file access returns `FileId`, short-lived signed `AccessUrl`, and `AccessExpiresAtUtc` only; provider credentials, `StorageKey`, and raw storage locations remain hidden.

### Campaign Target Snapshot

Reuses existing `CampaignTarget`.

**Fields used**:

- `Id`
- `CampaignId`
- `DoctorId`
- `SpecializationSnapshot`
- `ExperienceYearsSnapshot`
- `LocationSnapshot`
- `ActivityScoreSnapshot`
- `PricePerMessageSnapshot`
- `CreatedAtUtc`

**Validation rules**:

- A pending campaign must have at least one target snapshot before approval.
- Submission and revision resubmission replace the campaign's target rows with a fresh submitted snapshot. Approval uses that submitted target snapshot and does not recalculate doctor eligibility or pricing.
- Queue creation creates at most one active pending queue row for each submitted target doctor.

### Campaign Review History

Reuses and may extend existing append-only `CampaignReviewHistory`.

**Fields used or required**:

- `Id`
- `CampaignId`
- `AdminUserId`
- `Decision`
- `IdempotencyKey`
- `Reason`
- `Notes`
- `PriorStatus`
- `ResultingStatus`
- `CreatedAtUtc`
- `CorrectsHistoryId`

**Validation rules**:

- Every approval, rejection, and revision-required decision appends one review-history record.
- Rejection and revision-required decisions require a public-facing reason.
- Replayed decisions with the same campaign and idempotency key return the prior result when decision and reason match.
- Replayed decisions with conflicting payloads fail without changing campaign status or queue rows.
- Prior/resulting status should be retained directly or derivable without ambiguity for audit.

### Doctor Message Queue Row

Reuses existing `DoctorMessageQueue`.

**Fields used**:

- `Id`
- `CampaignId`
- `DoctorId`
- `QueuedAtUtc`
- `CampaignSubmittedAtUtc`
- `Status`
- `CreatedAtUtc`

**Validation rules**:

- Approval creates pending queue rows only after all approval prerequisites pass.
- One active pending or activated queue row per campaign and doctor is allowed.
- Pending rows are ordered by submitted campaign time, then queue creation time, then stable queue identifier.
- `CampaignSubmittedAtUtc` is copied from `Campaign.SubmittedAtUtc`; `QueuedAtUtc` records approval/queue creation time. These timestamps must not be set to the same fallback value.
- Delivery activation, daily limits, expiry, retry carry-over, and settlement are outside this feature.

### Company Review Outcome

Company-visible read model for an owned campaign.

**Fields**:

- `CampaignId`
- `Status`
- `Decision`
- `PublicReason`
- `DecisionTimeUtc`
- `CanEdit`
- `CanResubmit`
- `QueuedCount`

**Validation rules**:

- Only the owning company and admins can view a campaign outcome.
- Rejected outcomes have `CanEdit = false` and `CanResubmit = false`.
- Revision-required outcomes have `CanEdit = true` and `CanResubmit = true`.
- Internal admin-only notes are never returned to companies.

### Admin Audit Event

Reuses existing append-only `AuditEvent`.

**Workflow use**:

- Records pending list/detail access when policy requires it.
- Records campaign approval, rejection, revision-required decisions, idempotent replays, and service-detected conflict outcomes.
- Records queue creation as part of approval audit metadata.

**Validation rules**:

- Audit metadata must not include secrets, JWTs, raw request bodies, provider credentials, or raw storage keys.
- Anonymous and non-admin denials performed by ASP.NET authorization occur before the moderation service and are verified as non-mutating; this feature does not add a cross-cutting authorization-denial audit pipeline.

## Required Indexes and Constraints

- Campaign list filter index for active pending review browsing on `Status, SubmittedAtUtc, Id` with active rows filtered by the existing soft-delete query behavior.
- Review history lookup by `CampaignId, IdempotencyKey` for idempotent replay.
- Review history browsing by `CampaignId, CreatedAtUtc, Id`.
- Submission-attempt unique lookup by `CampaignId, IdempotencyKey` and current-attempt lookup by `CampaignId, SubmittedAtUtc, Id`.
- Queue duplicate prevention for active rows by `CampaignId + DoctorId` where status is pending or activated.
- Queue ordering support by `DoctorId, Status, CampaignSubmittedAtUtc, QueuedAtUtc, Id`.
- Stored-file lookup for active reviewable/approved campaign media by `OwnerType, OwnerId, Purpose, StorageState, ReviewStatus`.

## Atomic Business Actions

### Company Campaign Update

```text
Validate authenticated approved company and ownership
Lock active campaign
Require Draft or RevisionRequired
Validate and replace title, description, and optional clinical research information
Set UpdatedAtUtc without changing SubmittedAtUtc
Commit
```

### Campaign Submission Or Revision Resubmission

```text
Validate authenticated approved company and ownership
Lock active campaign
Require Draft or RevisionRequired
Validate campaign text and at least one active Pending/Approved campaign media asset
Check campaign submission-attempt idempotency replay/conflict
Resolve eligible priced doctors and replace target snapshots
Calculate estimated cost and validate current company-wallet affordability
Do not mutate AvailableBalance or ReservedBalance
Do not create wallet transactions or wallet ledger entries
Set SubmittedAtUtc and UpdatedAtUtc to the new submission time
Move campaign to PendingReview
Append CampaignSubmissionAttempt with key, time, target count, estimated cost, and EGP currency
Create safe submission audit event
Commit campaign, targets, submission attempt, and audit together
```

### Campaign Approval

```text
Validate admin identity and role
Lock pending campaign
Validate campaign text, approved media, and target snapshot
Check idempotency replay/conflict
Append approval review history
Create missing queue rows once per target doctor
Move campaign to Approved
Create audit event
Commit together
```

Campaign approval does not read or lock wallet, wallet-transaction, wallet-ledger, delivery, or payout rows.

### Campaign Rejection

```text
Validate admin identity and role
Lock pending campaign
Validate required rejection reason
Check idempotency replay/conflict
Append rejection review history
Move campaign to Rejected
Create audit event
Commit together
```

Campaign rejection does not release funds or cancel pre-existing queue rows because a pending campaign has no queue rows and submission created no reservation.

### Campaign Return For Revision

```text
Validate admin identity and role
Lock pending campaign
Validate required revision reason
Check idempotency replay/conflict
Append revision-required review history
Move campaign to RevisionRequired
Create audit event
Commit together
```

Campaign return for revision does not release funds or create/cancel queue rows. The owning company may subsequently update and resubmit the campaign as a new PendingReview attempt.

### Company Review Outcome Read

```text
Validate company identity and ownership
Load campaign and latest review history
Map public status, reason, decision time, and resubmission flags
Return envelope without internal notes
```
