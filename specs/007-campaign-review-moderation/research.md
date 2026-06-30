# Research: Phase 6 Campaign Review & Moderation

## Decision: Separate Campaign Moderation From Asset Approval

**Rationale**: Entering moderation requires at least one active reviewable campaign media asset whose review status is Pending or Approved. Campaign approval requires at least one separately Approved campaign media asset. Campaign review inspects the full review package, including optional voice notes, clinical references, or supporting attachments, but it does not turn pending media into approved media. This lets asset and campaign review proceed as separate auditable gates without requiring an asset to be approved before the campaign can enter the moderation queue.

**Alternatives considered**:

- Campaign approval also approves media: rejected because it collapses two review gates and could approve campaign content without a distinct asset audit trail.
- Require an Approved campaign media asset before campaign submission: rejected because it makes a pending campaign without approved media impossible even though campaign approval, not moderation entry, is the separately approved-media gate.
- Require every optional file to be separately approved before campaign review: rejected because optional attachments should be inspectable during campaign review without blocking all campaigns on non-required materials.

## Decision: Rejected Campaigns Are Final, RevisionRequired Campaigns Are Resubmittable

**Rationale**: Clarification made rejection a terminal company-visible outcome. Fixable campaigns use `RevisionRequired`; companies can edit and resubmit those while prior review history remains append-only. This avoids unclear behavior where a rejected campaign might later become pending again.

**Alternatives considered**:

- Allow rejected campaigns to be edited and resubmitted: rejected because it makes rejection and revision-required semantically redundant.
- Clone rejected campaigns into new drafts: rejected for this feature because it adds duplicate/copy semantics not required by the moderation gate.

## Decision: Add or Map a RevisionRequired Campaign State

**Rationale**: Existing domain status values include `Draft`, `PendingReview`, `Approved`, and `Rejected`, while the clarified spec requires a distinct revision-required outcome. Planning should either add `RevisionRequired` to the campaign lifecycle or map an existing persisted value with an explicit, non-ambiguous field. Adding a distinct status is the clearest testable model.

**Alternatives considered**:

- Return changes requested campaigns to `Draft`: rejected because companies and tests cannot distinguish a never-submitted draft from a returned-for-revision campaign.
- Store revision state only in the latest review history row: rejected because common campaign list/detail filtering would require history inspection for a core lifecycle state.

## Decision: Admin Pending Review List and Detail Are First-Class Read Surfaces

**Rationale**: Admins need to identify pending campaigns and inspect campaign text, active Pending/Approved media, optional submitted materials, target snapshot, company identity, and review readiness before making a decision. Separate list and detail surfaces keep list payloads small while making review detail complete enough for moderation.

**Alternatives considered**:

- Use only a generic campaign list endpoint: rejected because pending moderation needs review-readiness filters and admin-specific review detail.
- Put all detail fields on the pending list: rejected because it bloats pagination and repeats protected file access information unnecessarily.

## Decision: Campaign Review Decisions Are Idempotent by Campaign and Idempotency Key

**Rationale**: Admin approval creates queue rows, so retries after timeouts must return the original result without duplicating review history or queue rows. The service should compare replay decision and public reason/notes against the stored review decision and reject conflicting replays.

**Alternatives considered**:

- Always reject duplicate review requests: rejected because safe retry behavior is required for approval and queue creation.
- Use campaign id alone for idempotency: rejected because it would prevent explicit stale/conflicting decision handling and make replay semantics unclear.

## Decision: Queue Rows Are Created During Approval and Never Activated Here

**Rationale**: Approval is the moderation event that unlocks queued delivery eligibility. This feature creates pending queue rows once per submitted target doctor using submitted campaign time, queue creation time, and stable row id for ordering. Daily limit checks, delivery activation, expiry, carry-over, and settlement remain later workflow responsibilities.

**Alternatives considered**:

- Queue on campaign submission: rejected because content has not passed campaign moderation.
- Queue through a background job only: rejected because moderation smoke tests need deterministic approval-to-queue verification.
- Activate deliveries immediately on approval: rejected because delivery timing, daily limits, and wallet reservation are explicitly out of scope.

## Decision: Moderation Has No Wallet Effects

**Rationale**: The clarified spec and current backend master plan state that campaign submission, revision resubmission, and moderation are non-financial. Submission still validates current company-wallet affordability and captures target/price snapshots, but it does not reserve funds or create wallet evidence. Wallet reservation happens later when a queued message becomes Active; doctor earnings and company charges happen on doctor interaction. This decision intentionally supersedes the older Phase 5 implementation and documentation that reserve at submission and release during rejection/revision.

**Alternatives considered**:

- Reserve funds on campaign approval: rejected because the current backend plan reserves when a message becomes Active.
- Preserve the older reserve-on-submission implementation: rejected because it conflicts with the locked backend rule that reservation occurs on delivery activation and creates wallet side effects during revision resubmission.
- Release funds on rejection or revision in this phase: rejected because submission and resubmission do not create a reservation to release.

## Decision: Protected File Access Uses Existing Storage Abstraction

**Rationale**: Admins and owning companies need to inspect reviewable materials without receiving raw storage locations or provider credentials. The existing `IFileAccessService` already performs role/ownership checks and creates short-lived signed URLs. Admin review detail should reuse that service and return `accessUrl` plus `accessExpiresAtUtc` for each active reviewable file rather than duplicating provider logic or exposing `StoredFile.StorageKey`.

**Alternatives considered**:

- Expose storage keys directly: rejected because it leaks provider internals and weakens access control.
- Embed file bytes in review responses: rejected because it is inefficient and complicates HTTP contracts.

## Decision: Revision Editing Requires an Explicit Company Update Surface

**Rationale**: The current campaign workflow exposes draft creation, asset operations, submission, and queue summary, but it has no operation that changes campaign title, description, or clinical research information. `RevisionRequired` cannot be meaningfully resubmittable without an authenticated owning-company update endpoint. Add `PUT /api/company/campaigns/{campaignId}` and allow it only for Draft or RevisionRequired campaigns; the existing asset upload/replacement/deletion operations follow the same state rule.

**Alternatives considered**:

- Treat asset replacement as sufficient revision editing: rejected because an admin may request campaign-text changes.
- Return revision-required campaigns to Draft: rejected because it erases the lifecycle distinction already resolved by clarification.

## Decision: Persist Submission Time Separately

**Rationale**: `Campaign.UpdatedAtUtc` changes when editable fields change and therefore cannot be the durable ordering key for a submitted review attempt. Add nullable `SubmittedAtUtc`, set it when Draft or RevisionRequired moves to PendingReview, and use it for moderation paging and `DoctorMessageQueue.CampaignSubmittedAtUtc`. Pending pages order oldest submission first, then campaign id; queue rows retain submission time, queue creation time, and row id as deterministic tie-breakers.

**Alternatives considered**:

- Continue using `UpdatedAtUtc`: rejected because edits and unrelated updates can change queue and moderation order.
- Use `CreatedAtUtc`: rejected because revision resubmission is a new review attempt and needs a new submission position.

## Decision: Submission Idempotency Uses Its Own Append-Only Attempt Record

**Rationale**: The existing implementation uses a Reserve wallet transaction as the submission idempotency record. Removing reservation from submission removes that replay key. Add `CampaignSubmissionAttempt` with campaign id, idempotency key, submitted time, target count, estimated cost, and currency. The current PendingReview attempt can be replayed identically without replacing targets; revision resubmission requires a new key, while reuse of any historical key conflicts.

**Alternatives considered**:

- Store only the latest key on Campaign: rejected because revision resubmission would overwrite historical retry evidence.
- Continue using a zero-value wallet transaction: rejected because submission is explicitly non-financial and wallet history must contain only real wallet effects.

## Decision: Authorization-Policy Denials Are Not Service Audit Outcomes

**Rationale**: ASP.NET authorization rejects anonymous and non-admin review requests before `AdminCampaignReviewService` executes. This feature verifies that those requests do not mutate campaign or review history, but it does not add a new authorization middleware audit pipeline. Service audit events cover completed decisions, idempotent replays, and service-detected conflicts using safe metadata.

**Alternatives considered**:

- Require `AdminCampaignReviewService` to audit policy denials: rejected because the service is never invoked for those requests.
- Add global authorization-result auditing: deferred because it is a cross-cutting security feature outside campaign moderation scope.
