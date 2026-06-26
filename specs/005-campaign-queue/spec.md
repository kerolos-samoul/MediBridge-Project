# Feature Specification: Campaign & Queue (Phase 5)

**Feature Branch**: `[005-campaign-queue]`
**Created**: 2026-06-12
**Status**: Draft
**Input**: User description: "Phase 5: Campaign & Queue"

## Clarifications

### Session 2026-06-18

- Q: What maximum number of target doctors should a single campaign allow? -> A: 100 target doctors per campaign.
- Q: What should happen when a campaign submission includes any invalid selected target? -> A: Reject the entire campaign submission.
- Q: What content and asset minimum should a campaign submission require? -> A: Title, description, clinical research information, and at least one approved campaign asset.
- Q: Should campaign submission require idempotency protection? -> A: Require an idempotency key for every campaign submission.
- Q: What default ordering should eligible doctor search use? -> A: Activity score descending, price ascending, then stable identifier ascending.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find Eligible Doctors (Priority: P1)

As a pharmaceutical company user, I need to search and filter approved, active doctors who can receive campaign messages so I can select an appropriate audience before submitting a campaign.

**Why this priority**: Campaign creation depends on companies being able to find eligible doctors without exposing inactive, unpriced, suspended, or unrelated records.

**Independent Test**: Sign in as an approved company user, request doctor listings with combinations of specialization, experience, location, activity score, and price filters, and confirm only eligible doctors are returned with stable pagination.

**Acceptance Scenarios**:

1. **Given** approved active doctors with valid prices and other doctors that are pending, suspended, soft-deleted, or missing a positive price, **When** a company searches eligible doctors, **Then** only approved active doctors with a positive price are returned.
2. **Given** a company applies specialization, experience, location, activity score, and price filters, **When** matching doctors exist, **Then** the result includes only doctors matching all requested filters.
3. **Given** more doctors match than fit on one page, **When** the company requests a page, **Then** the result uses standard pagination, does not exceed the maximum allowed page size, and is ordered by activity score descending, price ascending, then stable identifier ascending.
4. **Given** a Doctor or Admin user attempts to use the company doctor-search workflow, **When** the request is made, **Then** access is denied.

---

### User Story 2 - Submit Campaign for Review (Priority: P1)

As a pharmaceutical company user, I need to create a campaign with selected target doctors and supporting content so the campaign can enter admin review before any doctor receives it.

**Why this priority**: This is the core company workflow for starting promotion while preserving the review gate required for pharmaceutical content.

**Independent Test**: Create a campaign as an approved company with a valid title, description, clinical research information, at least one approved campaign asset, and selected eligible doctors; then confirm the campaign is owned by the company, has target snapshots, and is pending review.

**Acceptance Scenarios**:

1. **Given** an approved company and a set of eligible doctors, **When** the company submits a campaign with valid content and selected targets, **Then** the campaign is saved with status `PendingReview` and no queue item is created yet.
2. **Given** selected doctors with targeting attributes and current prices, **When** the campaign is submitted, **Then** each target stores a snapshot of specialization, experience, location, activity score, and price at selection time.
3. **Given** the selected target price snapshots have a total cost and the submitting company's wallet available balance is at least that total, **When** the campaign is submitted, **Then** the sufficiency check passes without reserving, charging, or mutating wallet funds.
4. **Given** the selected target price snapshots have a total cost and the submitting company's wallet available balance is below that total, **When** the campaign is submitted, **Then** the entire campaign submission is rejected and no campaign, target, queue, wallet transaction, wallet ledger, reserve, or charge record is created.
5. **Given** a campaign references campaign media, voice notes, or clinical research attachments, **When** the campaign is submitted, **Then** only files owned by the company, related to the campaign context, and usable as approved campaign assets are accepted.
6. **Given** a company user attempts to target an ineligible doctor or a doctor whose price is missing or zero, **When** the campaign is submitted, **Then** the entire campaign submission is rejected and no campaign or target records are created.
7. **Given** a company user attempts to create a campaign for another company or attach another company's files, **When** the request is made, **Then** access is denied and no campaign is created.

---

### User Story 3 - Queue Approved Campaigns Deterministically (Priority: P1)

As a platform operator, I need approved campaigns to create per-doctor queue items in deterministic FIFO order so the later daily injector can activate messages predictably and fairly.

**Why this priority**: Queue correctness is the contract between campaign review and later delivery, wallet reservation, and doctor inbox workflows.

**Independent Test**: Transition a pending campaign to approved through the approved campaign status path, then confirm queue rows are created once per target doctor, ordered by submitted time then identifier, and remain stable under retries.

**Acceptance Scenarios**:

1. **Given** a campaign in `PendingReview` with valid targets, **When** the campaign becomes approved, **Then** one queued item is created for each eligible target doctor.
2. **Given** multiple approved campaigns targeting the same doctor, **When** pending queue items are requested for that doctor, **Then** they are ordered by `QueuedAtUtc ASC` and then `Id ASC`.
3. **Given** queue creation for an approved campaign is retried, **When** queue rows already exist for some or all target doctors, **Then** duplicate queue items are not created.
4. **Given** a target doctor becomes suspended, soft-deleted, or loses a positive price before queue creation, **When** the campaign is approved, **Then** no queued item is created for that doctor and the skipped target is auditable.
5. **Given** a campaign is rejected, cancelled, paused, still pending review, or soft-deleted, **When** queue creation is attempted, **Then** no queued item is created.

---

### User Story 4 - Fund Company Wallet for Future Delivery (Priority: P2)

As a pharmaceutical company user, I need to top up and view my company wallet so future daily delivery activation can reserve funds when messages become active.

**Why this priority**: Queue activation in later phases depends on companies having available funds, but Phase 5 must not charge or reserve money during campaign submission or queueing.

**Independent Test**: Top up a company wallet using the MVP gateway stub, query the wallet, and confirm the available balance and append-only transaction history reflect the top-up exactly once.

**Acceptance Scenarios**:

1. **Given** an approved company user, **When** the company submits a wallet top-up of at least 100 EGP, **Then** the company wallet available balance increases and an append-only top-up transaction is recorded.
2. **Given** the same top-up is retried with the same idempotency key, **When** the request is processed again, **Then** the wallet balance is not increased a second time.
3. **Given** a top-up amount below 100 EGP or with more than two decimal places, **When** the request is submitted, **Then** it is rejected without changing the wallet.
4. **Given** a company queries its wallet, **When** transactions exist, **Then** the response includes available balance, reserved balance, currency, and paginated transaction history for that company only.
5. **Given** a Doctor, Admin, anonymous user, or another company attempts to access a company's wallet workflow, **When** the request is made, **Then** authorization and ownership rules prevent inappropriate access.

### Edge Cases

- A company requests a page number below 1, a page size below 1, or a page size above 100; the system must apply standard pagination validation and bounds.
- Multiple eligible doctors have the same activity score and price; doctor search uses the stable identifier tie-breaker so pagination remains deterministic.
- Doctor filters produce no matches; the company receives an empty page rather than an error.
- A doctor qualifies during search but becomes ineligible before campaign submission; the submission revalidates eligibility and rejects the entire campaign submission without creating campaign or target records.
- A campaign target list is empty, contains duplicates, or contains more than 100 target doctors; the request is rejected with a clear user-facing reason.
- A campaign is missing title, description, clinical research information, or at least one approved campaign asset; the request is rejected before review.
- A campaign references a missing, pending, rejected, quarantined, deleted, replaced, or unrelated file; the file is not accepted as a campaign asset.
- A company wallet has less available balance than the sum of selected target price snapshots; the campaign submission is rejected before acceptance without reserving, charging, or otherwise mutating funds.
- A company submits duplicate campaign requests through retries; duplicate campaigns or duplicate target rows must not be created because every campaign submission requires an idempotency key.
- A campaign is approved more than once or queue creation is retried after partial completion; each target doctor receives at most one queue item for that campaign.
- Multiple campaigns receive the same submission or queue time for one doctor; queue ordering uses the stable identifier as the tie-breaker.
- A campaign is cancelled, paused, rejected, completed, or soft-deleted after queue rows were created; queued items that have not been activated are not treated as deliverable.
- Company wallet top-up succeeds in the gateway stub but the transaction record fails; the workflow must not leave the user-facing wallet balance inconsistent with transaction history.
- Company wallet top-up is submitted with insufficient metadata for idempotency; the request is rejected before changing the balance.
- Campaign creation, target snapshot persistence, and initial status change must complete together or fail together.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow approved Pharmaceutical Company users to list eligible doctors using standard pagination.
- **FR-002**: System MUST support doctor filters for specialization, minimum and maximum experience, location, minimum activity score, and minimum and maximum price.
- **FR-002A**: System MUST order eligible doctor search results by activity score descending, price ascending, then stable identifier ascending.
- **FR-003**: System MUST exclude doctors from company filtering and targeting when the doctor is not approved, not active, soft-deleted, suspended, or missing a positive price.
- **FR-004**: System MUST prevent Doctor, Admin, anonymous, pending, rejected, suspended, or otherwise unauthorized accounts from using company-only doctor search.
- **FR-005**: System MUST allow approved Pharmaceutical Company users to create campaigns owned only by their own company account.
- **FR-006**: System MUST require campaign submissions to include title, description, clinical research information, at least one approved campaign asset, and at least one selected eligible doctor target.
- **FR-007**: System MUST put newly submitted campaigns into `PendingReview` rather than `Approved`, `Active`, or queued state.
- **FR-008**: System MUST persist one `CampaignTarget` record for each selected doctor accepted into a campaign.
- **FR-009**: System MUST snapshot each accepted target doctor's specialization, experience, location, activity score, and price at campaign submission time.
- **FR-010**: System MUST require the selected target doctor list to contain unique doctor identifiers only.
- **FR-010A**: System MUST require each campaign submission to contain at least 1 and at most 100 selected target doctors.
- **FR-011**: System MUST revalidate target doctor eligibility during campaign submission even if the doctor appeared in a prior search result.
- **FR-011A**: System MUST reject the entire campaign submission when any selected target validation rule fails, including empty target list, duplicate target doctor, over-100 target count, invalid doctor id, ineligible doctor, or missing positive price.
- **FR-012**: System MUST ensure campaign creation, target snapshot persistence, and initial `PendingReview` status are saved atomically.
- **FR-012A**: System MUST require an idempotency key for every campaign submission and reject submissions without one.
- **FR-012B**: System MUST prevent duplicate campaign or target creation when a campaign submission is retried with the same company and idempotency key.
- **FR-013**: System MUST allow campaign submissions to reference only campaign files that are owned by the submitting company, related to the campaign workflow, stored successfully, and available as approved campaign assets.
- **FR-013A**: System MUST reject campaign submissions that do not reference at least one approved campaign media, voice note, or clinical research attachment asset.
- **FR-014**: System MUST prevent company users from creating, viewing, changing, or attaching files to campaigns owned by another company.
- **FR-015**: System MUST expose company campaign list and detail views for the owning company, including campaign status, selected target count, submission time, and review-visible content summary.
- **FR-015A**: System MUST validate before accepting a campaign submission that the submitting company's wallet available balance is greater than or equal to the sum of the selected target doctor price snapshots.
- **FR-015B**: System MUST reject campaign submission for insufficient company wallet available balance without creating campaign records, target records, queue records, wallet transactions, wallet ledger entries, reservations, charges, or balance changes.
- **FR-016**: System MUST create per-doctor `DoctorMessageQueue` items when a campaign becomes `Approved` through an approved status transition or review outcome.
- **FR-017**: System MUST NOT create queue items while a campaign is `Draft`, `PendingReview`, `Rejected`, `Paused`, `Completed`, `Cancelled`, or soft-deleted.
- **FR-018**: System MUST create at most one queue item for a given campaign and target doctor, including under retries or repeated approval processing.
- **FR-019**: System MUST assign `QueuedAtUtc` from the campaign approval or queue insertion time and preserve `CampaignSubmittedAtUtc` when available.
- **FR-020**: System MUST return pending queue items for each doctor in deterministic FIFO order by `QueuedAtUtc ASC` and then `Id ASC`.
- **FR-021**: System MUST leave daily delivery limits, activation, expiry, read tracking, interaction settlement, reservation, charge, earn, and release behavior out of Phase 5.
- **FR-022**: System MUST record auditable outcomes for campaign creation, target rejection, queue creation, queue skip, wallet top-up, and denied company ownership attempts without exposing secrets.
- **FR-023**: System MUST allow approved Pharmaceutical Company users to query only their own company wallet balance and paginated transaction history.
- **FR-024**: System MUST allow approved Pharmaceutical Company users to top up only their own company wallet through an MVP gateway-stub workflow.
- **FR-025**: System MUST require company wallet top-ups to be at least 100 EGP and denominated in EGP with no more than two decimal places.
- **FR-026**: System MUST create append-only `TopUp` wallet transactions for successful company wallet top-ups.
- **FR-027**: System MUST protect company wallet top-up against duplicate financial effect by operation type and idempotency key.
- **FR-028**: System MUST update company wallet available balance and append the top-up transaction in one atomic operation.
- **FR-029**: System MUST NOT reserve, charge, release, earn, refund, withdraw, or settle funds during campaign creation, campaign approval, or queue creation in Phase 5.
- **FR-030**: System MUST return API-visible results using the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` response envelope.
- **FR-031**: System MUST handle validation, authorization, ownership, campaign, queue, and wallet errors through global error handling without exposing raw stack traces.
- **FR-032**: System MUST secure company campaign, doctor search, queue-triggering, and wallet workflows with JWT authentication and role-aware authorization.
- **FR-033**: System MUST keep persistence access behind repository and unit-of-work abstractions and prevent controllers and services from depending on persistence infrastructure directly.
- **FR-034**: System MUST keep controllers HTTP-only and delegate campaign, targeting, queue, wallet, validation, and ownership decisions to application services.
- **FR-035**: System MUST NOT implement admin campaign moderation screens, daily injector jobs, expiry jobs, doctor inbox, doctor read or interaction endpoints, company analytics, withdrawal workflows, weekly enforcement, activity score jobs, or production payment gateway integration in Phase 5.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-004` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`
  - `FR-005` to `FR-015B` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`
  - `FR-016` to `FR-022` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`
  - `FR-023` to `FR-029` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`
  - `FR-030` to `FR-035` -> all layers preserve API, security, error, scope, and architecture boundaries.
- **CA-002 Controller Boundary**: Controllers remain HTTP-only and delegate doctor filtering, campaign submission, ownership checks, queue creation, wallet top-up, and wallet query behavior to services.
- **CA-003 SQL Persistence Boundary**: Campaigns, targets, queue items, wallets, wallet transactions, and audit records use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services must not depend on EF Core directly.
- **CA-004 Response Contract**: API-visible Phase 5 responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation, authorization, ownership, campaign, queue, and wallet errors are handled by global exception middleware, with no raw stack traces exposed.
- **CA-006 Security**: Phase 5 workflows require JWT and role-aware authorization for Pharmaceutical Company users; queue creation from approved campaign transitions is limited to trusted service/admin paths.
- **CA-007 Ambiguity Control**: Queue and wallet behavior is deterministic for Phase 5; no unresolved clarification markers remain.
- **CA-008 Queue Determinism**: Queueing creates per-doctor items only after campaign approval, never before review; pending queue queries order by `QueuedAtUtc ASC, Id ASC`; duplicate queue rows for the same campaign and doctor are prevented; daily limits, expiry, activation, retry carry-over, and reservation skip behavior are deferred to Phase 7.
- **CA-009 Wallet Determinism**: Walleting is limited to company wallet query, company `TopUp`, and read-only campaign submission sufficiency validation. Campaign submission checks available balance against selected target price snapshots before acceptance but does not reserve, charge, release, earn, refund, withdraw, settle, create wallet transactions, create wallet ledger entries, or mutate wallet balances. Successful top-up credits available balance, creates an append-only `TopUp` transaction, uses operation type plus idempotency key for duplicate prevention, requires EGP two-decimal precision and a 100 EGP minimum, and commits balance plus transaction atomically. Reservation, release, charge, earn, refund, withdrawal, fee calculation, and settlement are out of Phase 5 scope.

### Key Entities *(include if feature involves data)*

- **Doctor Profile**: Marketplace doctor record searched by companies using specialization, experience, location, activity score, status, and price eligibility.
- **Company Profile**: Approved pharmaceutical company account that owns campaigns and a company wallet.
- **Campaign / Advertisement**: Promotional content submitted by a company with status, title, description, clinical research context, supporting asset references, and lifecycle metadata.
- **Campaign Target**: Doctor selected for a campaign with point-in-time targeting snapshots used to explain why the doctor was selected.
- **Stored Campaign File**: Approved campaign media, voice note, or clinical research attachment that can be referenced by campaign submissions.
- **Doctor Message Queue Item**: Per-doctor pending campaign message created only after approval and ordered deterministically for later daily activation.
- **Company Wallet**: Company-owned EGP balance split into available and reserved amounts; Phase 5 credits available balance only through top-up.
- **Wallet Transaction**: Append-only financial record for a company wallet top-up with amount, operation type, idempotency key, timestamp, and non-secret metadata.
- **Audit Event**: Trace record for campaign creation, target validation, queue creation or skip, wallet top-up, ownership denial, and authorization denial.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of company doctor-search validation cases exclude ineligible doctors, including unapproved, suspended, soft-deleted, and zero-price doctors.
- **SC-002**: 100% of doctor-search responses honor requested filters, standard pagination bounds, a maximum page size of 100, and ordering by activity score descending, price ascending, then stable identifier ascending.
- **SC-003**: 100% of valid campaign submissions by approved company users create a `PendingReview` campaign with target snapshots and zero queue items.
- **SC-004**: 100% of invalid campaign submissions with missing required content, no approved campaign asset, no targets, duplicate targets, more than 100 targets, any ineligible target, unauthorized files, or another company's ownership are rejected without creating any campaign or target records.
- **SC-005**: 100% of campaign submission validation cases persist target snapshots for specialization, experience, location, activity score, and price exactly as observed at submission time.
- **SC-005A**: 100% of campaign submissions without an idempotency key are rejected, and 100% of retries with the same company and idempotency key avoid duplicate campaign and target records.
- **SC-005B**: 100% of campaign submissions where company wallet available balance is below the sum of selected target price snapshots are rejected without creating campaign, target, queue, wallet transaction, wallet ledger, reservation, charge, or balance-change records.
- **SC-006**: 100% of approved campaign queue-creation cases create exactly one queued item per eligible target doctor.
- **SC-007**: 100% of queue retry validation cases avoid duplicate queue items for the same campaign and doctor.
- **SC-008**: 100% of sampled per-doctor pending queue reads return items in `QueuedAtUtc ASC, Id ASC` order, including same-time tie-break cases.
- **SC-009**: 100% of non-approved, inactive, rejected, cancelled, paused, completed, or soft-deleted campaign cases create no new queue items.
- **SC-010**: 100% of valid company wallet top-ups at or above 100 EGP credit available balance and create one append-only `TopUp` transaction.
- **SC-011**: 100% of duplicate top-up retries with the same operation type and idempotency key produce no duplicate financial effect.
- **SC-012**: 100% of top-up attempts below 100 EGP or with more than two decimal places are rejected without balance changes.
- **SC-013**: 100% of company wallet query validation cases restrict wallet balance and transaction history to the owning company.
- **SC-014**: Architecture validation finds 0 controller references to persistence infrastructure and 0 service references to EF Core infrastructure types.
- **SC-015**: API validation confirms 100% of Phase 5 visible responses use the standard response envelope and expose no raw stack traces.
- **SC-016**: Scope validation confirms 0 Phase 5 endpoints or services implement daily injection, delivery expiry, doctor interaction settlement, reporting analytics, withdrawal, weekly enforcement, activity score jobs, or production payment gateway processing.

## Assumptions

- Phase 5 builds on Phase 1 API foundations, Phase 2 identity and approval, Phase 3 domain persistence, and Phase 4 campaign file metadata and asset readiness checks.
- Pharmaceutical Company users must be approved before they can search doctors, create campaigns, top up wallets, or query company wallet data.
- Doctor eligibility for company targeting requires approved account state, active marketplace status, not soft-deleted, and a positive price per message.
- Campaign submissions enter `PendingReview`; moderation decisions and admin review list endpoints are primarily handled by Phase 6, while Phase 5 defines the queue effect when a campaign becomes approved.
- Queue items are created only after approval and are not activated into daily deliveries until Phase 7.
- Company wallet top-up uses an MVP gateway stub that records confirmed top-ups without integrating a production payment provider.
- Company wallet top-up affects only available balance. Reserved balance changes begin in Phase 7 when daily delivery activation reserves funds.
- EGP is the only supported currency for Phase 5 wallet operations.
- Standard pagination uses `PageNumber` and `PageSize`, with default `PageSize` 20 and maximum `PageSize` 100.
- Eligible doctor search defaults to activity score descending, price ascending, then stable identifier ascending.
- A single campaign can target at most 100 doctors.
- Campaign submission is all-or-nothing for target validation; one invalid selected target rejects the whole submission.
- Campaign submission requires title, description, clinical research information, and at least one approved campaign asset.
- Campaign submission requires an idempotency key, scoped to the submitting company, to protect retry behavior.
- Campaign submission wallet sufficiency uses the selected target price snapshots as the estimated future delivery cost and compares that total against the submitting company's current available wallet balance.
- Campaign analytics, feedback, delivery history, and spend reporting remain out of Phase 5 except for basic company-owned campaign list/detail status.
- Audit events must avoid secrets, raw gateway payloads, private file access tokens, and implementation-specific diagnostics.
