# Feature Specification: Phase 6 Campaign Review & Moderation

**Feature Branch**: `[007-campaign-review-moderation]`
**Created**: 2026-06-26
**Status**: Draft
**Input**: User description: "Phase 6: Campaign Review & Moderation in backend plan"

## Clarifications

### Session 2026-06-26

- Q: What content must be present before a campaign can enter moderation? -> A: Require campaign text, a target snapshot, and at least one active reviewable campaign media asset whose review status is Pending or Approved. Voice notes and clinical references are optional but reviewed when present. At least one campaign media asset must be Approved before the campaign itself can be approved.
- Q: Can rejected campaigns be edited or resubmitted? -> A: Rejected campaigns are final and cannot be edited or resubmitted; revision-required campaigns can be edited and resubmitted.
- Q: Does campaign approval itself approve media assets, or must asset approval happen separately first? -> A: Campaign approval requires at least one separately approved campaign media asset; optional submitted files are inspected during campaign review.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Admins Review Submitted Campaigns Before Delivery (Priority: P1)

As an admin, I need to see submitted pharmaceutical campaigns awaiting moderation so that no promotional content can reach doctors without an explicit review decision.

**Why this priority**: Campaign moderation is the required gate between company submission and doctor delivery.

**Independent Test**: Submit a campaign as an approved company, then sign in as an admin and verify the campaign appears in the pending review queue with enough content, company, target, and asset information to make a decision.

**Acceptance Scenarios**:

1. **Given** a company has submitted a campaign for moderation, **When** an admin opens the pending campaign list, **Then** the submitted campaign appears with its status, company identity, submitted content summary, target summary, submitted time, and review readiness indicators.
2. **Given** a submitted campaign includes active reviewable promotional media plus optional voice notes, clinical references, or supporting attachments, **When** an admin views the campaign review detail, **Then** the admin can inspect every reviewable file through short-lived authorized access without exposing private storage details.
3. **Given** a campaign is still a draft, already approved, rejected, cancelled, or completed, **When** an admin opens the pending campaign list, **Then** that campaign is not listed as pending review.

---

### User Story 2 - Admins Approve Campaigns and Unlock Queue Creation (Priority: P1)

As an admin, I need to approve compliant campaigns so that approved campaigns can create deterministic queued messages for their selected doctors.

**Why this priority**: Approval is the business event that allows a reviewed campaign to proceed toward delivery.

**Independent Test**: Review a submitted campaign with valid content and targets, approve it once, replay the approval, and verify the campaign becomes approved while queue rows are created once per eligible target.

**Acceptance Scenarios**:

1. **Given** a submitted campaign is pending review, has at least one separately approved campaign media asset, and has at least one valid selected target, **When** an admin approves it, **Then** the campaign becomes approved, the approval decision is recorded, and one pending queue row is created for each selected target doctor.
2. **Given** an approved campaign has already created its pending queue rows, **When** the same approval action is retried, **Then** the original approved result is returned without duplicating review history or queue rows.
3. **Given** a campaign has not been approved, **When** any delivery activation process looks for eligible campaigns, **Then** the campaign is not eligible for delivery.

---

### User Story 3 - Admins Reject or Return Campaigns With Reasons (Priority: P1)

As an admin, I need to reject non-compliant campaigns as final outcomes or return fixable campaigns with actionable reasons so that companies understand the final rejection or what must change before revision resubmission.

**Why this priority**: Pharmaceutical content must be blocked when unsuitable, and companies need clear moderation outcomes.

**Independent Test**: Reject a pending campaign with a reason, then verify it cannot create queue rows and the owning company can view the rejection reason.

**Acceptance Scenarios**:

1. **Given** a campaign is pending review, **When** an admin rejects it with a reason, **Then** the campaign becomes rejected, no new queue rows are created, and the decision reason is retained.
2. **Given** a campaign needs correction but may be resubmitted, **When** an admin returns it for revision with notes, **Then** the campaign is visible to the owning company as needing revision, can be edited for resubmission, and remains ineligible for delivery.
3. **Given** a rejected or revision-required campaign has an admin reason, **When** the owning company views the campaign, **Then** the company can see the status, decision time, and reason without seeing internal-only admin data.

---

### User Story 4 - Review Decisions Remain Auditable (Priority: P2)

As an auditor or admin, I need every campaign review decision to preserve reviewer, decision, notes, and timestamp so that moderation history is traceable.

**Why this priority**: Campaign moderation is a compliance-sensitive workflow and must be explainable after the fact.

**Independent Test**: Apply multiple review decisions across campaign submissions and revision-required resubmissions, then verify the decision history shows who decided, what was decided, when it happened, and why.

**Acceptance Scenarios**:

1. **Given** an admin approves, rejects, or returns a campaign for revision, **When** the decision completes, **Then** the review history includes campaign identity, reviewer identity, decision, reason or notes, and decision time.
2. **Given** a campaign is revised and resubmitted after a prior revision-required decision, **When** a new decision is made, **Then** the new decision is appended without overwriting the prior decision history. Rejected campaigns never enter this flow because rejection is terminal.
3. **Given** a non-admin attempts to make or alter a review decision, **When** the action is submitted, **Then** the system denies the action and leaves the campaign and review history unchanged.

### Edge Cases

- A campaign submission has missing campaign text, no target snapshot, or no active Pending/Approved campaign media asset; submission is rejected with an actionable validation message.
- A pending campaign has no separately approved campaign media asset; the campaign remains reviewable, but approval is rejected until an admin separately approves at least one active campaign media asset.
- Two admins attempt conflicting decisions on the same pending campaign at nearly the same time; only one final decision is accepted, and the other receives the current campaign state.
- A company edits campaign text or manages campaign assets while it is pending review; the operation is blocked. Company editing is allowed only in Draft or RevisionRequired state.
- A company tries to edit or resubmit a rejected campaign; the request is denied because rejection is final.
- A company tries to view another company's rejection reason or submitted content; ownership rules deny access.
- An admin submits a rejection or revision request without a reason; the decision is rejected because non-approval outcomes require an actionable reason.
- A campaign is approved after target doctors or campaign content changed since submission; approval uses the submitted review package and target snapshot, not later mutable profile data.
- A campaign has already been cancelled by the company before review; admin approval or rejection is denied because the campaign is no longer pending review.
- Queue row creation partially fails during approval; the approval and all queue changes complete together or roll back together.
- Delivery activation, daily delivery limits, message expiry, doctor interactions, charges, and doctor earnings are outside this feature and do not run during moderation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow admins to list campaigns that are currently submitted and pending moderation.
- **FR-002**: System MUST exclude draft, approved, rejected, revision-required, cancelled, completed, and deleted campaigns from the pending moderation list.
- **FR-003**: System MUST provide admins with review details for each pending campaign, including company identity, submitted campaign text, every active reviewable campaign media asset regardless of Pending or Approved review status, target snapshot, submission time, current review status, and any submitted voice notes, clinical references, or supporting attachments.
- **FR-004**: System MUST protect reviewable files and attachments so admins can inspect them through short-lived authorized access URLs with explicit expiration times without exposing raw storage keys, raw storage locations, provider credentials, or internal-only fields.
- **FR-005**: System MUST allow only admins to approve, reject, or return submitted campaigns for revision.
- **FR-006**: System MUST require a decision reason for rejection and revision-required decisions.
- **FR-007**: System MUST allow approval only when the campaign is pending review, belongs to an approved company, contains campaign text, includes a target snapshot with at least one selected doctor, and has at least one separately approved campaign media asset.
- **FR-007A**: System MUST NOT treat campaign approval as approval of the required media asset; required campaign media asset approval is a separate prerequisite completed before campaign approval.
- **FR-008**: System MUST prevent draft, rejected, revision-required, cancelled, completed, deleted, or already approved campaigns from being approved as if they were pending review.
- **FR-009**: System MUST change an approved campaign to Approved status and record the approval reviewer, decision, notes when provided, and decision time.
- **FR-010**: System MUST create pending doctor queue rows only after campaign approval succeeds.
- **FR-011**: System MUST create at most one active pending queue row for each approved campaign and submitted target doctor.
- **FR-012**: System MUST make campaign approval and queue creation idempotent so approval retries return the existing approved result without duplicating queue rows or decision history.
- **FR-013**: System MUST preserve deterministic queue ordering for approved campaign rows by submitted campaign time, then queue creation time, then stable row identifier for same-time ties.
- **FR-014**: System MUST ensure unapproved campaigns are never eligible for delivery activation.
- **FR-015**: System MUST change a rejected campaign to Rejected status, record the rejection reviewer, reason, and decision time, and prevent new queue rows from being created.
- **FR-016**: System MUST treat rejected campaigns as final outcomes that cannot be edited, resubmitted, approved, or queued.
- **FR-017**: System MUST change a returned campaign to RevisionRequired status, record the reviewer, reason, and decision time, allow company edits for resubmission, and keep it ineligible for delivery until resubmitted and approved.
- **FR-017A**: System MUST provide the owning company with an authenticated campaign-update operation that can change campaign title, description, and optional clinical research information only while the campaign is Draft or RevisionRequired. The existing asset upload, replacement, and deletion operations MUST also allow RevisionRequired and MUST deny PendingReview, Rejected, Approved, Active, Paused, Completed, Cancelled, deleted, or non-owned campaigns.
- **FR-018**: System MUST allow the owning company to view its campaign review outcome, including status, public-facing decision reason, decision time, and whether resubmission is allowed.
- **FR-019**: System MUST prevent companies from viewing internal-only admin notes or another company's campaign review outcome.
- **FR-020**: System MUST append campaign review history for every completed approval, rejection, and revision-required decision without overwriting prior decisions.
- **FR-021**: System MUST preserve reviewer identity, decision type, reason or notes, campaign identity, prior status, resulting status, and decision time for audit.
- **FR-022**: System MUST reject concurrent or stale review decisions when the campaign is no longer in the expected pending review state.
- **FR-023**: System MUST keep campaign status changes, review history, and queue row creation atomic for an approval decision.
- **FR-024**: System MUST keep campaign status changes and review history atomic for rejection and revision-required decisions.
- **FR-025**: System MUST return user-actionable validation messages for missing review content, missing targets, invalid status transitions, missing rejection reasons, unauthorized access, stale review decisions, and attempts to edit or resubmit final rejected campaigns.
- **FR-026**: System MUST return workflow successes and failures using the standard response envelope `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-027**: System MUST NOT reserve or release funds, create wallet transactions or wallet ledger entries, charge companies, credit doctors, create or expire deliveries, enforce daily delivery limits, or settle interactions during initial campaign submission, revision resubmission, or any campaign moderation decision. Submission and resubmission MUST validate current company wallet affordability without mutating wallet balances; reservation occurs later when a queued message becomes an Active delivery.
- **FR-028**: System MUST allow resubmission after revision only as a new pending review attempt that retains prior decision history, refreshes the submitted target snapshot, and records a new submission timestamp.
- **FR-029**: System MUST store `SubmittedAtUtc` separately from mutable creation/update timestamps. The pending moderation list MUST order by `SubmittedAtUtc` ascending and then campaign identifier ascending, and approval-created queue rows MUST copy that submission timestamp into `CampaignSubmittedAtUtc` before applying queue-creation time and stable row identifier tie-breakers.
- **FR-030**: System MUST persist an append-only campaign submission attempt for every successful initial submission and revision resubmission, including campaign id, idempotency key, submitted time, target count, estimated cost, and currency. An identical retry with the current attempt's key while the campaign remains PendingReview MUST return the stored result without replacing targets or adding records; a conflicting/reused historical key or a different key while already PendingReview MUST return conflict without mutation.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-004` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for campaign review visibility and protected review content access.
  - `FR-005` to `FR-019` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for moderation decisions, campaign state transitions, company-visible outcomes, and queue row creation.
  - `FR-020` to `FR-030` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for audit history, concurrency control, atomicity, response contracts, submission idempotency, deterministic submission ordering, and explicit wallet/delivery exclusions.
- **CA-002 Controller Boundary**: Controllers are HTTP-only and delegate review listing, review decisions, transition validation, audit recording, protected content access, and queue creation to service use cases.
- **CA-003 SQL Persistence Boundary**: Data requirements use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core directly.
- **CA-004 Response Contract**: All public workflow responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Moderation validation failures and unexpected errors are handled by global exception and validation response paths, with no raw stack traces exposed.
- **CA-006 Security**: Admin review flows require JWT and Admin authorization. Company review-outcome views require JWT and owning-company authorization. Doctor users cannot review campaigns or access company review outcomes.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Submission and revision resubmission validate affordability and create target snapshots without reserving funds. This feature creates pending queue rows only after approval, never activates deliveries, and performs no wallet mutation.
- **CA-008 Queue Determinism**: Approved campaigns create at most one pending queue row per submitted target doctor. Pending rows are ordered by submitted campaign time, then queue creation time, then stable row identifier. Daily delivery limits, expiry behavior, retry carry-over, and activation are outside this feature and belong to the delivery workflow.
- **CA-009 Wallet Determinism**: No debit, credit, reservation, release, charge, earn, fee, payout, wallet transaction, or wallet ledger entry is triggered by campaign submission, revision resubmission, or moderation. Campaign approval only unlocks queued delivery eligibility; funds are reserved later when a message becomes Active.

### Key Entities *(include if feature involves data)*

- **Campaign**: Company-owned promotional submission with moderation status, submitted content package, target snapshot, explicit `SubmittedAtUtc`, mutable update time, and lifecycle state.
- **Campaign Review History**: Append-only record of review decisions, including reviewer identity, decision, reason or notes, prior status, resulting status, and decision time.
- **Campaign Submission Attempt**: Append-only idempotency record for one successful initial submission or revision resubmission, preserving key, submitted time, target count, estimated cost, and currency without using wallet transactions as replay evidence.
- **Campaign Review Package**: Reviewable set of submitted campaign text, at least one active Pending/Approved campaign media asset, target snapshot, and company identity, plus any optional voice notes, clinical references, or supporting attachments. Campaign approval additionally requires at least one active Approved campaign media asset.
- **Doctor Message Queue Row**: Pending delivery candidate created only after campaign approval for one campaign and one submitted target doctor.
- **Company Review Outcome**: Company-visible moderation result containing status, public-facing decision reason, decision time, and resubmission eligibility. Rejected outcomes are final; revision-required outcomes are editable and resubmittable.
- **Admin Audit Event**: Compliance-sensitive record that an admin performed or attempted a campaign moderation action.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of submitted pending campaigns appear in deterministic `SubmittedAtUtc`, then campaign-id order, and a warmed normal page of 20 campaigns returns within 1 second in the local/integration fixture.
- **SC-002**: 100% of draft, approved, rejected, revision-required, cancelled, completed, and deleted campaigns are excluded from the pending moderation list.
- **SC-003**: 100% of approved campaign decisions create exactly one pending queue row per submitted target doctor and create no duplicates on approval replay.
- **SC-004**: 100% of unapproved, rejected, revision-required, or cancelled campaigns remain ineligible for delivery activation.
- **SC-005**: 100% of rejection and revision-required decisions without a reason are rejected with an actionable validation message.
- **SC-006**: 100% of completed review decisions preserve reviewer, decision, reason or notes, prior status, resulting status, and decision time in audit history.
- **SC-007**: 100% of stale or concurrent conflicting review decisions preserve a single final campaign outcome and report the current state to the losing reviewer.
- **SC-008**: 100% of company review-outcome views expose status, public reason, and decision time only to the owning company.
- **SC-009**: 100% of rejected campaign edit or resubmission attempts are denied without changing campaign status or review history.
- **SC-010**: 100% of sampled moderation success, validation failure, unauthorized, and stale-decision responses use the standard response envelope and expose no raw stack traces.
- **SC-011**: Admins can complete a normal campaign review decision in under 2 minutes when the submitted review package is complete.
- **SC-012**: 100% of sampled review files can be opened by an authorized admin through a short-lived access URL, while sampled responses expose no storage key, provider credential, or raw storage location.

## Assumptions

- Company registration, company approval, campaign draft creation, target selection, and campaign submission already exist or are delivered by earlier phases.
- Submitted campaigns contain immutable campaign text for that review attempt, at least one active Pending/Approved campaign media asset, and a target snapshot. At least one active campaign media asset must become separately Approved before campaign approval.
- Campaign assets and attachments are already stored through the backend-controlled file storage abstraction before moderation begins.
- Asset malware scanning remains a deferred compliance gate unless a prior phase has already supplied a concrete scanning result.
- Approval creates pending queue rows but does not activate deliveries; delivery activation is handled by a later Egypt-time delivery workflow.
- Submission and revision resubmission validate affordability without mutating the wallet. Wallet reservation and financial settlement happen after moderation, when delivery activation and doctor interaction workflows run.
- Rejected campaigns remain visible to the owning company with a public-facing reason, but cannot be edited or resubmitted; internal admin-only notes remain hidden.
- Revision-required campaigns can be corrected and resubmitted while preserving prior moderation history.
