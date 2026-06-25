# Feature Specification: Wallet and Campaign Workflow

**Feature Branch**: `[006-wallet-campaign-workflow]`  
**Created**: 2026-06-19  
**Status**: Draft  
**Input**: User description: "Company wallet and campaign E2E workflow blockers after real HTTP smoke testing. Approved company accounts do not automatically get a wallet row, so company wallet query, top-up, idempotency replay, and conflict scenarios return 404. The full campaign workflow cannot be completed through public HTTP APIs because campaign submission requires at least one approved campaign asset, but campaign-file upload requires an existing owned draft campaign. There is no exposed HTTP draft-campaign flow/admin campaign moderation flow that allows: create draft campaign, upload campaign asset, admin approve asset/campaign, submit/review, create queue. Eligible doctors require PricePerMessage > 0, but newly approved doctors have PricePerMessage = null, so they cannot be targeted unless admin pricing is exposed/handled."

## Clarifications

### Session 2026-06-19

- Q: How should admin doctor price updates handle null, zero, negative, or over-precise values? -> A: Admin doctor price updates must be positive EGP amounts with at most two decimals; null, zero, negative, or over-precise values are rejected.
- Q: What payment behavior should company wallet top-up use for this graduation project? -> A: Use a mock payment flow that creates an internal payment record, automatically marks it succeeded, credits the wallet, and records audit and ledger evidence without real payment gateway integration, redirects, webhooks, callbacks, provider credentials, or provider configuration.
- Q: Who can verify campaign queue rows through public secured workflows? -> A: Admins can inspect queue rows; companies see aggregate campaign queue counts for their own campaigns.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Approved Companies Can Fund Campaigns (Priority: P1)

As an approved pharmaceutical company, I need an active wallet to exist before I query or top up funds so that campaign funding workflows do not fail immediately after account approval.

**Why this priority**: Companies cannot pay for campaign delivery until wallet query, top-up, idempotent replay, and conflict handling all work for newly approved company accounts.

**Independent Test**: Approve a company account, then use only secured company-facing requests to query the company wallet, top it up, replay the same top-up, and submit a conflicting retry.

**Acceptance Scenarios**:

1. **Given** a company account has been approved, **When** the company requests its wallet, **Then** the system returns one active company wallet with zero or current EGP balances instead of a not-found result.
2. **Given** an approved company has no wallet because of older data or a prior incomplete approval, **When** the company submits a valid top-up, **Then** the system creates the active company wallet and credits the top-up in one completed operation.
3. **Given** a successful top-up used an idempotency key, **When** the same top-up is replayed with the same idempotency key, amount, and currency, **Then** the system returns the original financial result, including the original transaction reference, without applying a second credit.
4. **Given** a successful top-up used an idempotency key, **When** the same key is reused by the same company for the same operation with a different amount or currency, **Then** the system rejects the request as an idempotency conflict without changing the wallet balance.
5. **Given** an approved company enters a valid top-up amount, **When** the company starts checkout, **Then** the system records an internal mock payment as succeeded, credits the wallet, records payment and wallet audit evidence, and returns a successful payment result without redirecting outside MediBridge.

---

### User Story 2 - Companies Can Prepare a Reviewable Campaign (Priority: P1)

As an approved pharmaceutical company, I need to create a draft campaign and upload its asset before submission so that the campaign can meet the existing moderation requirement for at least one approved asset.

**Why this priority**: The current workflow has a dependency loop: submission requires an approved asset, while asset upload requires a draft campaign that companies cannot create through the public workflow.

**Independent Test**: Using only secured company-facing requests, create a draft campaign, upload a campaign media asset to that draft, view the draft with its asset review status, and verify submission is rejected until at least one asset has been approved by the admin workflow.

**Acceptance Scenarios**:

1. **Given** an approved company, **When** it creates a campaign draft with required campaign content and targeting intent, **Then** the system creates an owned draft campaign that is not yet eligible for queue creation.
2. **Given** an owned draft campaign, **When** the company uploads a valid campaign asset, **Then** the asset is linked to the draft campaign with a pending review status.
3. **Given** a draft campaign with no approved asset, **When** the company submits the campaign for review, **Then** the system rejects submission and explains that at least one campaign asset must be approved.
4. **Given** a pending or rejected asset on an owned draft campaign, **When** the company uploads a replacement, **Then** the new stored file is linked to the campaign for review and the prior asset remains retained for audit.
5. **Given** a pending or rejected asset on an owned draft campaign, **When** the company deletes the asset, **Then** the asset becomes unavailable for submission while approved assets remain immutable and retained.

---

### User Story 3 - Admins Can Unlock Eligible Doctor Targeting (Priority: P1)

As an admin, I need to set or update a doctor's message price so that newly approved doctors can become eligible campaign targets when their price is greater than zero.

**Why this priority**: Newly approved doctors have no price by default, and the targeting workflow excludes doctors without a positive price.

**Independent Test**: Approve a doctor with no message price, confirm the doctor is excluded from campaign targeting, set a positive price as an admin, then confirm the doctor can be selected by a matching campaign.

**Acceptance Scenarios**:

1. **Given** a newly approved doctor has no price per message, **When** a company previews eligible targets, **Then** that doctor is excluded and identified as not priced.
2. **Given** an admin sets a positive EGP price per message for an approved doctor, **When** a matching company previews eligible targets, **Then** the doctor is eligible and the price is included in the campaign cost estimate.
3. **Given** an admin sets a doctor price to null, zero, negative, or a value with more than two decimal places, **When** the price change is submitted, **Then** the system rejects the invalid price without corrupting existing campaign snapshots.
4. **Given** a doctor price changes after a campaign has already been submitted, **When** the submitted campaign is reviewed or queued, **Then** the campaign continues to use the price snapshot captured at submission time.

---

### User Story 4 - Admins Can Review Assets and Campaigns to Create Queues (Priority: P1)

As an admin, I need to review campaign assets and submitted campaigns so that an approved campaign can create deterministic queue rows for eligible doctors.

**Why this priority**: A complete smoke-testable campaign workflow requires admin moderation and queue creation after company submission.

**Independent Test**: Create a company, wallet funds, priced doctor, draft campaign, and uploaded asset; approve the asset; submit the campaign; approve the campaign; then verify queue rows exist once for the eligible target doctors.

**Acceptance Scenarios**:

1. **Given** an uploaded campaign asset is pending review, **When** an admin approves it, **Then** the asset becomes usable for campaign submission and the review decision is retained.
2. **Given** an approved company has a draft campaign with an admin-approved asset, at least one eligible priced doctor, and sufficient wallet funds, **When** the company submits it for review, **Then** the campaign moves to pending review with target and price snapshots, required funds are reserved, and the campaign becomes available for admin campaign review.
3. **Given** a submitted campaign has at least one approved asset, at least one eligible priced doctor, and sufficient reserved company funds, **When** an admin approves the campaign, **Then** the system marks the campaign approved and creates one queued item per eligible targeted doctor.
4. **Given** a submitted campaign fails content, asset, targeting, or funding review, **When** an admin rejects it with a reason, **Then** the campaign is not queued and any reserved company funds are released.
5. **Given** campaign approval is retried after a timeout or replay, **When** the approval has already created queue rows, **Then** the system returns the existing approval result without creating duplicate queue rows.

---

### User Story 5 - End-to-End HTTP Smoke Workflow Completes (Priority: P2)

As a tester, I need a complete public workflow that exercises company wallet funding, campaign draft creation, asset approval, doctor pricing, campaign review, and queue creation so that real HTTP smoke tests can prove the backend plan works end to end.

**Why this priority**: The smoke path verifies that separately implemented account approval, wallet, file, campaign, pricing, and queue capabilities are actually connected through role-aware public workflows.

**Independent Test**: Starting from approved company, doctor, and admin accounts, complete the full funded campaign workflow using only public secured requests and verify every response uses the standard envelope with no raw errors.

**Acceptance Scenarios**:

1. **Given** approved company, approved doctor, and admin accounts, **When** the smoke workflow funds the company wallet, sets doctor pricing, creates a draft campaign, uploads and approves an asset, submits and approves the campaign, **Then** queue rows are created for eligible doctors.
2. **Given** any workflow step fails validation, **When** the caller receives the error, **Then** the response explains the unmet business requirement and keeps previous committed steps consistent.
3. **Given** a caller lacks the required role for a wallet, campaign, asset, pricing, review, or queue action, **When** the action is attempted, **Then** access is denied without exposing another user's private or financial data.
4. **Given** a campaign has been approved and queued, **When** an admin inspects queue rows, **Then** doctor-level queue rows are visible for verification; **When** the owning company views the campaign status, **Then** only aggregate queue counts are visible.

### Edge Cases

- A company was approved before wallet automation existed; wallet query and top-up still return or create one active company wallet.
- Two concurrent first top-ups race for the same company with no wallet; exactly one active wallet is created, and each unique valid top-up is applied once.
- A top-up is replayed with the same idempotency key and identical payload after the original result was committed; no duplicate credit occurs.
- A top-up is replayed with the same idempotency key but different payload; no balance change occurs.
- A draft campaign is created by one company and accessed by another company; ownership enforcement denies the second company.
- Campaign submission is attempted with no asset, only rejected assets, or pending assets; submission is denied until at least one asset is approved.
- A campaign asset is rejected after upload; the company can upload a replacement asset only while the asset is Pending/Rejected and the campaign remains Draft or returned for revision.
- A company requests signed access for a stored file; access is authorized by file ownership/role and returns a short-lived signed URL instead of exposing storage credentials.
- A company attempts to replace or delete an approved campaign asset; the request is rejected and the approved asset is retained.
- A doctor matches campaign filters but has no positive price; the doctor is excluded from target counts and queue creation.
- A company balance becomes insufficient between draft creation and submission; submission is denied or returned for funding before admin approval.
- Campaign approval is retried after a partial failure; approval, fund reservation, review history, and queue creation complete once or roll back together.
- Queue creation for multiple doctors has ties on queue time; ordering remains FIFO by campaign submission time, then stable queue identifier.
- Delivery activation, doctor daily delivery-limit processing, delivered-message expiry, and doctor interaction settlement are outside this feature; this workflow only creates ordered queued rows and cancels pending rows when the campaign is rejected, cancelled, or expired.
- Campaign rejection, cancellation, or expiry cancels pending queue rows and releases reserved company funds that have not been charged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST ensure every approved company has exactly one active company wallet available for company wallet query and top-up workflows.
- **FR-002**: System MUST create a missing active company wallet during company approval or, for legacy and recovery cases, atomically during the first valid wallet query or top-up.
- **FR-003**: System MUST return an active company wallet with EGP available and reserved balances for an approved company instead of returning not found solely because no wallet row existed.
- **FR-004**: System MUST process company top-ups as atomic financial operations that create or use the active company wallet, credit available balance, create an append-only transaction, and create immutable ledger evidence.
- **FR-005**: System MUST require idempotency keys for retriable company top-ups and MUST deduplicate by company, operation type, and idempotency key.
- **FR-006**: System MUST return the original result, including the original internally generated transaction reference, for a top-up replay by the same company with the same operation type, idempotency key, amount, and currency.
- **FR-007**: System MUST reject an idempotency-key replay when any financial payload field conflicts with the original request, without changing balances.
- **FR-008**: System MUST reject company wallet top-ups with non-positive amounts, currencies other than EGP, or amounts with more than two decimal places.
- **FR-009**: System MUST implement company top-up through a mock payment flow only: the company enters an amount, starts checkout, the system creates an internal payment transaction record, automatically marks it succeeded, credits the wallet, and returns a successful payment response.
- **FR-010**: System MUST NOT integrate with real payment gateways, third-party redirects, webhooks, callback endpoints, payment-provider credentials, or payment-provider configuration for this feature.
- **FR-011**: System MUST store each mock top-up payment with payment identifier, company identifier, amount, succeeded status, creation time, internally generated transaction reference, wallet balance before credit, and wallet balance after credit.
- **FR-012**: System MUST record audit and ledger evidence for mock top-ups exactly as a successful real payment would, so the wallet and campaign funding workflow can be demonstrated and tested end to end.
- **FR-013**: System MUST allow an approved company to create an owned draft campaign before asset upload or campaign submission.
- **FR-014**: System MUST allow an approved company to upload campaign media assets only to its own draft or revision-required campaigns through backend-controlled private storage.
- **FR-014A**: System MUST allow an approved company to upload a replacement only for Pending or Rejected campaign assets on an owned Draft or revision-required campaign; approved assets are immutable and retained.
- **FR-014B**: System MUST allow an approved company to delete only Pending or Rejected campaign assets on an owned Draft or revision-required campaign; approved assets are immutable and retained.
- **FR-014C**: System MUST provide authorized signed access to stored files through `GET /api/files/{fileId}`, returning only `{ FileId, Url, ExpiresAtUtc }`.
- **FR-015**: System MUST retain campaign asset review state and admin review history for approved, rejected, pending, replaced, and deleted asset decisions.
- **FR-016**: System MUST prevent campaign submission until the campaign has at least one approved campaign asset.
- **FR-017**: System MUST allow an approved company to preview eligible doctor targets and campaign cost using only doctors that match targeting filters, are approved, are marketplace-eligible, and have a positive price per message.
- **FR-018**: System MUST allow an admin to set or update an approved doctor's price per message only to a positive EGP amount with at most two decimal places and retain price-change history.
- **FR-019**: System MUST reject admin doctor price updates with null, zero, negative, or more than two decimal places, and doctors without a currently valid positive price remain out of campaign target eligibility.
- **FR-020**: System MUST snapshot selected doctor target facts and price per message when a campaign is submitted so later doctor price changes do not alter the submitted campaign's cost basis.
- **FR-021**: System MUST validate company wallet sufficiency before campaign submission is accepted for review, using the submitted target and price snapshots plus the active platform fee policy.
- **FR-022**: System MUST reserve the company funds required for a submitted campaign in one completed operation with campaign submission and target snapshot creation.
- **FR-023**: System MUST release reserved company funds when a submitted campaign is rejected, withdrawn, cancelled, or expires before chargeable delivery.
- **FR-024**: System MUST allow admins to approve or reject campaign assets with a decision, reason when rejected, reviewer, and review time.
- **FR-025**: System MUST allow admins to approve, reject, or return submitted campaigns for revision with a decision, reason when not approved, reviewer, and review time.
- **FR-026**: System MUST create queue rows for approved campaigns only after campaign approval succeeds.
- **FR-027**: System MUST create at most one active queued item per approved campaign and target doctor.
- **FR-028**: System MUST make campaign approval and queue creation idempotent so approval retries do not duplicate review history, queue rows, or wallet reservations.
- **FR-029**: System MUST order pending queue rows for a doctor by campaign submission time, then stable queue identifier for same-time ties; no priority ordering is introduced.
- **FR-030**: System MUST leave newly created campaign queue rows in deterministic pending order until an existing or future delivery workflow activates them, or until campaign rejection, cancellation, or expiry cancels them.
- **FR-031**: System MUST cancel pending queue rows when the campaign is rejected after revision, cancelled, expired, or otherwise no longer deliverable.
- **FR-032**: System MUST NOT implement delivered-message expiry in this feature; Egypt business-day expiry belongs to the delivery activation and doctor interaction workflow.
- **FR-033**: System MUST enforce role-aware access: company users manage only their own wallets, draft campaigns, assets, submissions, and campaign status views; admins manage doctor pricing, campaign/asset review, and queue row verification; doctors cannot manage company campaigns or wallets.
- **FR-034**: System MUST return every workflow success and validation failure using the standard response envelope `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-035**: System MUST keep validation errors user-actionable for missing wallet, insufficient funds, missing approved asset, unpriced doctors, unauthorized ownership, invalid top-up, and invalid review transition cases.
- **FR-036**: System MUST keep wallet balance updates, wallet transactions, wallet ledger entries, payment transaction records, campaign submission, campaign review state, and queue creation atomic when they belong to the same business action.
- **FR-037**: System MUST provide smoke-testable public secured workflows for company wallet query/top-up, mock payment checkout, draft campaign creation, campaign asset upload, campaign asset replacement/delete for eligible draft assets, signed file access, admin asset review, admin doctor pricing, campaign submission, admin campaign review, admin queue row verification, and company-owned aggregate queue counts.
- **FR-038**: System MUST allow admins to inspect doctor-level queue rows for approved campaigns and MUST limit companies to aggregate queue counts for their own campaigns.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-012` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for company wallet and mock payment behavior.
  - `FR-013` to `FR-022` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for campaign draft, targeting, submission, and funding behavior.
  - `FR-023` to `FR-032` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for moderation, queue, cancellation, campaign expiry boundaries, and explicit delivery-expiry exclusion.
  - `FR-033` to `FR-038` target `MediBridge.Services` and `MediBridge.APIs` for authorization, response, validation, atomic orchestration, and smoke-test workflow exposure.
- **CA-002 Controller Boundary**: Controllers are HTTP-only and delegate wallet, campaign, asset review, doctor pricing, fund reservation, and queue decisions to service use cases.
- **CA-003 SQL Persistence Boundary**: Data requirements use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core directly.
- **CA-004 Response Contract**: All public workflow responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Workflow exceptions and validation failures are handled through the global exception and validation response path, with no raw stack traces exposed.
- **CA-006 Security**: Secured flows require JWT and role-aware authorization for Pharmaceutical Company, Admin, and Doctor roles where applicable. Queue verification exposes doctor-level queue rows only to admins; companies receive aggregate queue counts only for their own campaigns.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains for this workflow. Company wallet creation, top-up idempotency, campaign fund reservation, queue ordering, campaign cancellation/expiry boundaries, and the exclusion of delivery activation and delivered-message expiry are defined in this specification.
- **CA-008 Queue Determinism**: Queue rows are created only for approved campaigns, one active queued row per campaign and doctor, ordered per doctor by campaign submission time then queue identifier. This feature does not activate queue rows, evaluate doctor daily delivery limits, or expire delivered messages; those behaviors belong to delivery activation and doctor interaction workflows.
- **CA-009 Wallet Determinism**: Company approval or first wallet use ensures an active wallet. Mock top-up creates an internal succeeded payment, credits company available balance, and records payment, wallet transaction, audit, and ledger evidence atomically. Campaign submission reserves estimated company funds from available to reserved using target price snapshots and platform fee policy. Campaign rejection, cancellation, or expiry releases uncharged reserved funds. Charge, earn, platform fee, refund, and withdrawal settlement remain governed by existing wallet transaction semantics. All retriable money-moving actions require actor/company-scoped operation type plus idempotency key uniqueness where applicable, and atomic wallet balance, transaction, and ledger commits.

### Key Entities *(include if feature involves data)*

- **Company Wallet**: Active EGP balance record for an approved company, including available and reserved balances and owner identity.
- **Mock Payment Transaction**: Internal top-up payment record with payment identifier, company identifier, amount, succeeded status, creation time, generated transaction reference, wallet balance before credit, and wallet balance after credit.
- **Wallet Transaction**: Append-only financial operation for top-up, reserve, release, charge, earn, refund, and payout-related events, protected by operation type plus idempotency key where retriable.
- **Wallet Ledger Entry**: Immutable debit or credit evidence for each wallet balance movement.
- **Campaign Draft**: Company-owned campaign record that can receive assets and be edited before submission.
- **Campaign Asset**: Media or supporting file linked to a campaign with pending, approved, rejected, replaced, or deleted lifecycle state. Approved assets are immutable and retained; replacement/delete is constrained to Pending or Rejected assets on Draft or revision-required campaigns.
- **Doctor Price Policy**: Admin-controlled price per message for an approved doctor, with history and two-decimal EGP validation.
- **Campaign Target Snapshot**: Submitted campaign's selected doctor facts and price snapshot used for funding and queue creation.
- **Campaign Review History**: Admin decision trail for campaign approval, rejection, return for revision, and corrections.
- **Doctor Message Queue Row**: Pending campaign item for one doctor, created after campaign approval and ordered deterministically for later delivery activation.
- **Campaign Queue Summary**: Company-visible aggregate status for an owned campaign's queued, activated, cancelled, or expired queue rows without exposing doctor-level queue details.
- **Platform Fee Policy**: Active fee percentage used to estimate campaign funding and snapshot later settlement values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of newly approved company accounts can query an active company wallet immediately after approval in smoke validation.
- **SC-002**: 100% of valid first top-ups for approved companies without an existing wallet create the wallet and credit balance in one completed operation.
- **SC-003**: 100% of top-up replays with the same operation type, idempotency key, and payload avoid duplicate credits; 100% of conflicting replays are rejected without balance changes.
- **SC-004**: 100% of valid mock checkout attempts create a succeeded payment record with payment identifier, company identifier, amount, creation time, generated transaction reference, wallet balance before credit, and wallet balance after credit.
- **SC-005**: 100% of smoke validation confirms no third-party redirect, webhook, callback endpoint, provider credential, or provider configuration is required for wallet top-up.
- **SC-006**: A tester can complete the full company wallet funding, doctor pricing, draft campaign creation, asset upload, asset approval, campaign submission, campaign approval, and queue verification workflow in under 5 minutes using only secured public workflow requests for normal smoke-test fixtures.
- **SC-007**: 100% of campaign submissions without at least one approved campaign asset are rejected with an actionable validation message.
- **SC-008**: 100% of approved doctors with null or non-positive prices are excluded from target eligibility, and 100% of matching approved doctors with positive prices are included in target previews.
- **SC-009**: 100% of campaign submissions with insufficient company funds are rejected before admin approval and do not create queue rows.
- **SC-010**: 100% of approved campaigns in smoke validation create exactly one active queued row for each eligible target doctor and create no duplicate queue rows on approval replay.
- **SC-011**: Queue ordering validation returns pending doctor rows in FIFO order by campaign submission time and stable queue identifier in 100% of sampled cases.
- **SC-012**: Campaign rejection, cancellation, or expiry releases 100% of uncharged reserved company funds and cancels 100% of pending queue rows for the affected campaign.
- **SC-013**: 100% of queue verification smoke tests allow admins to inspect doctor-level queue rows and allow owning companies to view only aggregate queue counts for their campaigns.
- **SC-014**: 100% of workflow responses sampled from success, validation failure, unauthorized, and conflict cases use the standard response envelope and expose no raw stack traces.
- **SC-015**: Real HTTP smoke testing completes the full workflow from approved company wallet funding through approved campaign queue creation with 0 manual data seeding beyond account approval fixtures.

## Assumptions

- Existing account registration and admin approval workflows are available before this feature starts.
- Existing persistent wallet, campaign, file, queue, policy, and audit records from Phase 3 are the authoritative data foundation.
- Approved company wallet creation on approval is the preferred steady-state behavior; first-use wallet creation exists to repair legacy or partial data safely.
- Wallet top-up for this graduation project is demonstration-only mock payment behavior and intentionally excludes real payment provider integration.
- Draft campaign creation does not reserve funds; submission for review is the point where target snapshots are captured and company funds are reserved.
- Campaign queue creation happens on admin campaign approval, not on draft creation or asset approval.
- Platform fee policy defaults to the constitution-defined value unless an admin-configured policy is already active.
- Delivery activation, doctor daily delivery-limit processing, delivered-message expiry, doctor interaction settlement, and payout workflows are outside this feature. This feature defines only queue row creation, FIFO inspection, company aggregate counts, and cancellation/release behavior for campaign rejection, cancellation, or campaign expiry.
- File content scanning and malware analysis are outside this feature; asset review here means business/admin review state for uploaded campaign media.
- Stored campaign assets are private/protected by default. Clients obtain short-lived signed access through the backend and never receive storage provider credentials.
