# Feature Specification: Admin Tools (Phase 11)

**Feature Branch**: `[013-admin-tools]`  
**Created**: 2026-07-13  
**Status**: Draft  
**Input**: User description: "Phase 11: Admin Tools in backend plan"

## Clarifications

### Session 2026-07-13

- Q: What payout destination information should Phase 11 capture? → A: No payout destination captured; admins record payout reference only.
- Q: How should admin statistics handle inconsistent financial evidence? → A: Withhold affected financial totals; return non-financial totals.
- Q: How should admins deactivate doctor pricing? → A: Separate deactivate action; no inactive numeric price.
- Q: Which doctors may create new withdrawal requests? → A: Approved, active, non-suspended doctors only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Triage Admin Work Queue (Priority: P1)

As an admin, I need one reliable operational work queue for accounts, protected files, campaigns, enforcement items, and withdrawal requests so that the platform can keep approvals and risk decisions moving without missing pending work.

**Why this priority**: Phase 11 consolidates operational controls. Admins need a complete and secure view of pending decisions before pricing, payout, and reporting controls are useful.

**Independent Test**: Sign in as an admin with pending accounts, files, campaigns, violations, and withdrawals in the system; request the admin work queue; confirm only decision-ready items appear with safe summaries, counts by category, stable ordering, and no sensitive payloads.

**Acceptance Scenarios**:

1. **Given** pending doctor and company accounts exist, **When** an admin views the work queue, **Then** the queue includes each pending account with verification status, submitted timing, role type, current review state, and the next allowed admin decision.
2. **Given** pending protected files and submitted campaigns exist, **When** an admin views the work queue, **Then** the queue includes review-ready file and campaign items without exposing raw storage locations, provider credentials, or internal stack traces.
3. **Given** doctors have rolling violation counts or active enforcement state, **When** an admin views the work queue, **Then** the queue identifies warning-stage and action-eligible doctors with current status, rolling count, and latest action summary.
4. **Given** withdrawal requests are waiting for review or payout status update, **When** an admin views the work queue, **Then** the queue shows request amount, doctor public summary, request age, current status, and next allowed payout actions.
5. **Given** more items exist than one page, **When** the admin pages through the queue, **Then** every matching item is reachable exactly once with stable ordering by urgency, requested or submitted time, and stable item identifier.

---

### User Story 2 - Manage Approval and Moderation Decisions (Priority: P1)

As an admin, I need to approve, reject, or request correction for accounts, protected files, and campaign submissions so that only verified participants and approved promotional material become active or visible.

**Why this priority**: Account, file, and campaign decisions protect the marketplace from unverified users and unreviewed promotional content.

**Independent Test**: Prepare pending doctor and company accounts, protected files, and submitted campaigns; apply approval, rejection, and correction-required decisions as an admin; confirm state transitions, required reasons, public/private reason separation, and audit records.

**Acceptance Scenarios**:

1. **Given** a doctor or company account is pending verification, **When** an admin approves it with required review evidence, **Then** the account becomes approved for its role and the decision is recorded with actor, time, prior state, resulting state, and reason when provided.
2. **Given** a pending account, protected file, or submitted campaign has a review problem, **When** an admin rejects or requests correction with a required public reason, **Then** the owner can see the actionable public reason while internal admin notes remain hidden.
3. **Given** an account, file, or campaign has already reached a final or non-reviewable state, **When** an admin tries to apply an incompatible decision, **Then** the request is rejected without changing the existing state or duplicating audit history.
4. **Given** two admins attempt conflicting decisions on the same item, **When** the decisions are processed, **Then** only one decision succeeds and the other receives the current state without overwriting the accepted decision.

---

### User Story 3 - Manage Doctor Pricing and Platform Fee Policy (Priority: P1)

As an admin, I need to set doctor message prices and platform fee policy with history so that campaign activation uses approved commercial terms and historical deliveries remain financially explainable.

**Why this priority**: Pricing and fee policy directly affect company spend, doctor earnings, platform fee totals, and later reporting reconciliation.

**Independent Test**: Change a doctor's price and the platform fee policy as an admin, activate new deliveries after the change, and confirm new activations use the current policy while existing delivery snapshots and historical reports remain unchanged.

**Acceptance Scenarios**:

1. **Given** an admin sets a valid price for an approved doctor with a reason, **When** the change succeeds, **Then** the doctor's current price is updated and an immutable price history entry records previous price, new price, actor, reason, and time.
2. **Given** an admin attempts to set a missing, zero, negative, inactive-marker, or invalid precision doctor price, **When** the request is processed, **Then** the change is rejected and no price history entry is created.
3. **Given** an admin deactivates a doctor's pricing with a reason, **When** the action succeeds, **Then** the doctor becomes ineligible for future paid campaign activation until a new valid positive price is set, and the deactivation is recorded in price history.
4. **Given** an admin sets a valid platform fee percentage with a reason, **When** the policy change succeeds, **Then** a new policy history entry becomes effective for future activation snapshots and prior policy history remains auditable.
5. **Given** campaign deliveries already have price and fee snapshots, **When** an admin changes price, deactivates pricing, or changes fee policy, **Then** existing delivery snapshots, settlement evidence, company reports, and ledger totals do not change.
6. **Given** a non-admin attempts to change price, deactivate pricing, or change platform fee policy, **When** authorization is evaluated, **Then** access is denied without exposing private doctor or policy internals.

---

### User Story 4 - Review Withdrawal Requests and Track Payouts (Priority: P1)

As an admin, I need to review doctor withdrawal requests and track payout status so that earned doctor balances can be paid out with clear approvals, failure handling, and audit evidence.

**Why this priority**: Payouts are the major new Phase 11 operational workflow and affect doctor trust and wallet correctness.

**Independent Test**: Give a doctor settled earnings, submit withdrawal requests, approve and reject requests as admin, mark approved requests paid or failed through the payout stub, and confirm balances, statuses, and ledger evidence remain deterministic.

**Acceptance Scenarios**:

1. **Given** an approved, active, non-suspended doctor has withdrawable settled earnings, **When** the doctor submits a valid withdrawal request, **Then** the requested amount moves from available earnings into a pending withdrawal hold and cannot be requested again while pending.
2. **Given** a doctor requests more than the withdrawable amount or uses an invalid amount, **When** the request is processed, **Then** the request is rejected and wallet balances remain unchanged.
3. **Given** a withdrawal request is in Requested status, **When** an admin approves it with a reason or payout note, **Then** the request moves to Approved status, the held amount remains unavailable, and an auditable decision is recorded.
4. **Given** a withdrawal request is in Requested status, **When** an admin rejects it with a required reason, **Then** the held amount returns to the doctor's available earnings and the rejection is auditable.
5. **Given** an approved withdrawal request is processed through the payout stub, **When** the payout is marked Paid, **Then** the held amount is finalized as paid out with a payout reference and cannot be released or paid again.
6. **Given** an approved withdrawal request fails before funds leave the platform, **When** the payout is marked Failed with a reason, **Then** the held amount returns to available earnings and the failed payout evidence remains visible to admins.
7. **Given** duplicate, retried, or concurrent payout decisions are submitted for the same withdrawal, **When** they are processed, **Then** the resulting status, ledger effect, and payout reference are applied at most once.

---

### User Story 5 - Review System Statistics and Enforcement Outcomes (Priority: P2)

As an admin, I need read-only system statistics and enforcement summaries so that I can monitor platform health, identify bottlenecks, and verify that approvals, pricing, campaigns, interactions, payouts, and enforcement are behaving as expected.

**Why this priority**: Statistics help admins operate the platform, but they should not block the core decision and payout workflows from being independently valuable.

**Independent Test**: Seed the system with accounts, campaigns, deliveries, interactions, wallet activity, withdrawal requests, pricing changes, and enforcement actions; request admin statistics; confirm the totals match source evidence, respect date filters, and expose no protected private content.

**Acceptance Scenarios**:

1. **Given** an admin requests statistics for a valid date range, **When** matching activity exists, **Then** the response includes account counts by role/status, pending review counts, campaign counts by status, delivery and interaction totals, payout status counts, wallet movement totals, pricing policy summaries, and enforcement action counts.
2. **Given** the selected date range contains no activity, **When** an admin requests statistics, **Then** the response succeeds with zero totals and clear period boundaries.
3. **Given** source evidence changes between two statistics requests, **When** the admin requests statistics again, **Then** the response reflects committed source evidence without requiring stored aggregate repair.
4. **Given** a non-admin requests system statistics, **When** authorization is evaluated, **Then** access is denied without returning platform metrics or sensitive operational data.

### Edge Cases

- An admin decision is retried after a timeout; the original decision is returned or preserved without duplicating audit records or applying a second state change.
- A pending account, file, campaign, doctor, or withdrawal is soft-deleted while visible in an admin list; the next decision attempt is rejected safely and audit history remains preserved.
- A doctor becomes suspended or inactive after requesting a withdrawal; existing requested withdrawals remain reviewable, but new withdrawal requests require the doctor to be approved, active, and non-suspended.
- A withdrawal request amount has more than two decimal places, is zero, negative, or exceeds the withdrawable balance; no hold, ledger entry, or request record with financial effect is created.
- An approved withdrawal is marked Paid twice, Failed after Paid, or Rejected after approval; incompatible transitions are rejected without changing wallet balances.
- A platform fee policy, doctor price, or doctor pricing-active state changes while delivery activation is running; each activated delivery uses exactly one committed policy snapshot, and later policy changes do not rewrite the snapshot.
- Statistics date filters begin after they end, exceed the maximum supported reporting window, or contain malformed dates; the request is rejected safely without partial totals.
- Admin statistics encounter inconsistent financial evidence; affected financial totals are withheld and flagged safely while unrelated non-financial totals remain available.
- Unauthorized, unauthenticated, or wrong-role callers attempt any admin tool action; access is denied using the standard safe error path.
- Public and admin responses expose no raw storage keys, provider credentials, raw idempotency material, wallet internals beyond authorized summaries, private doctor contact details, or raw stack traces.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST require authenticated Admin authorization for all admin work queue, approval, moderation, pricing, fee policy, payout review, enforcement oversight, and system statistics capabilities.
- **FR-002**: The system MUST provide an admin work queue that combines pending accounts, pending protected file reviews, pending campaign reviews, enforcement review items, and withdrawal requests with category counts and stable pagination.
- **FR-003**: Admin work queue items MUST expose only safe summaries needed for decision-making and MUST exclude raw storage locations, provider credentials, private contact details not needed for review, raw idempotency material, and stack traces.
- **FR-004**: Admin work queue ordering MUST prioritize decision urgency, then requested or submitted time, then stable item identifier so pagination has no duplicates or gaps.
- **FR-005**: The system MUST allow admins to approve, reject, or request correction for pending doctor and company account verification workflows.
- **FR-006**: Account decisions MUST record actor, target account, role type, prior state, resulting state, public reason when applicable, internal note when applicable, decision time, and correlation evidence.
- **FR-007**: The system MUST allow admins to review protected verification, document, and campaign media files through authorized short-lived access only, without returning raw storage keys or provider credentials.
- **FR-008**: File review decisions MUST support approval, rejection, and correction-required outcomes with required public reasons for non-approval outcomes and append-only history.
- **FR-009**: The system MUST allow admins to review submitted campaigns and apply approval, rejection, or revision-required outcomes while preserving public company-facing reasons separately from internal admin notes.
- **FR-010**: Campaign moderation decisions MUST NOT create duplicate queue rows, wallet charges, delivery records, or settlement evidence when decisions are retried or when conflicting admin decisions occur.
- **FR-011**: The system MUST reject incompatible approval, file review, campaign review, pricing, enforcement, or payout transitions without mutating state or creating duplicate audit history.
- **FR-012**: The system MUST allow admins to set a doctor's current price per message with a required or recorded reason and immutable history of previous price, new price, actor, reason, and time.
- **FR-012A**: The system MUST allow admins to deactivate a doctor's pricing through a separate reasoned action that makes the doctor ineligible for future paid campaign activation until a new valid positive price is set.
- **FR-013**: Doctor price changes MUST reject missing, zero, negative, unsupported precision, inactive-marker, or otherwise invalid monetary values; inactive pricing MUST NOT be represented by null, zero, or another numeric price value.
- **FR-014**: Doctor price changes and pricing deactivation MUST apply only to future eligibility and activation snapshots; existing campaign targets, delivery snapshots, settlements, reports, and ledger records MUST NOT be recalculated.
- **FR-015**: The system MUST allow admins to view the current platform fee policy and create a new platform fee percentage with required reason, effective time, actor, and immutable history.
- **FR-016**: Platform fee percentages MUST be greater than 0 and less than or equal to 100, using the platform-supported percentage precision.
- **FR-017**: Platform fee changes MUST apply only to future activation snapshots; existing delivery snapshots, settlement evidence, company reports, and ledger records MUST NOT be recalculated.
- **FR-018**: The system MUST allow approved, active, non-suspended doctors to submit withdrawal requests from their own withdrawable settled earnings.
- **FR-019**: Withdrawal request submission MUST validate positive amount, supported currency/precision, doctor ownership, approved account state, active account state, non-suspended doctor status, and sufficient withdrawable settled earnings before creating any financial hold.
- **FR-020**: A valid withdrawal request MUST move the requested amount from available withdrawable earnings into a pending withdrawal hold so the same balance cannot be requested again while the request is open.
- **FR-021**: The system MUST allow admins to list withdrawal requests with filters for status, doctor, requested date range, reviewed date range, amount range, and payout reference.
- **FR-022**: Admin withdrawal approval MUST move a Requested withdrawal to Approved status, preserve the held amount, and record actor, reason or note, decision time, and audit evidence.
- **FR-023**: Admin withdrawal rejection MUST move a Requested withdrawal to Rejected status, release the held amount back to the doctor's available earnings, require a reason, and record audit evidence.
- **FR-024**: The payout stub MUST allow an Approved withdrawal to be marked Paid with a payout reference exactly once; the held amount MUST be finalized as paid out and MUST NOT be releasable afterward.
- **FR-025**: The payout stub MUST allow an Approved withdrawal to be marked Failed before funds leave the platform; failure MUST release the held amount back to available earnings and record a required reason.
- **FR-026**: Withdrawal request and payout status transitions MUST be idempotent under retries and safe under concurrent attempts so each request receives at most one final wallet effect.
- **FR-026A**: Phase 11 withdrawal requests and payout records MUST NOT collect, store, display, or validate doctor bank account details, card details, mobile wallet numbers, payout-destination text, or saved payout methods; admins record only a payout reference after an out-of-system payout action.
- **FR-027**: The system MUST allow admins to review doctor enforcement summaries and apply allowed warning, daily-limit reduction, suspension, and reactivation actions using the rules established by the activity and weekly enforcement feature.
- **FR-028**: Admin enforcement actions MUST require a reason and record actor, target doctor, prior state, resulting state, effective time, and audit evidence.
- **FR-029**: The system MUST provide read-only admin statistics for a bounded date range covering account statuses, pending review queues, campaign statuses, delivery outcomes, interaction outcomes, payout statuses, wallet movement summaries, pricing/fee policy summaries, and enforcement action counts.
- **FR-030**: Admin statistics MUST be computed from committed source evidence or clearly marked safe operational summaries; statistics MUST NOT mutate accounts, files, campaigns, deliveries, wallets, pricing policy, payout status, enforcement state, or audit evidence.
- **FR-031**: Admin statistics date filters MUST reject malformed dates, start-after-end ranges, and ranges longer than 90 days unless a later platform-wide reporting rule expands the limit.
- **FR-032**: Admin-visible financial statistics MUST reconcile to wallet transaction and ledger evidence; when financial evidence is inconsistent, affected financial totals MUST be withheld and flagged safely while unrelated non-financial statistics remain available.
- **FR-033**: Successful admin tool responses MUST use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-034**: Validation, authorization, stale-state, concurrency conflict, payout failure, reconciliation inconsistency, and unexpected failures MUST use the standard safe error path with no raw stack traces or sensitive operational details exposed.
- **FR-035**: All admin decisions and financially meaningful withdrawal actions MUST create audit evidence that is safe to inspect and preserves prior history.
- **FR-036**: The feature MUST NOT implement real external bank transfer integration, external payment-gateway settlement, new campaign delivery activation rules, new interaction settlement formulas, company-owned reporting paths, automated enforcement penalties beyond existing rules, or hard deletion of audit-preserved records.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-011` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for admin authorization, queue reads, account/file/campaign decisions, safe summaries, state transitions, and audit evidence.
  - `FR-012` to `FR-017` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for doctor price and platform fee policy validation, history, snapshots, response contracts, and HTTP-only adapters.
  - `FR-018` to `FR-026A` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for doctor withdrawal requests, wallet holds, admin decisions, payout stub status tracking, idempotency, no payout-destination storage, and ledger evidence.
  - `FR-027` to `FR-036` target all layers for enforcement oversight, statistics, safe error handling, audit preservation, explicit exclusions, and standard response shape.
- **CA-002 Controller Boundary**: Controllers remain transport adapters only and delegate queue composition, authorization-sensitive decision validation, pricing policy changes, withdrawal wallet effects, payout status transitions, enforcement actions, statistics calculations, and audit recording to service use cases.
- **CA-003 SQL Persistence Boundary**: Admin work items, account decisions, file review history, campaign review history, pricing history, platform fee policy history, withdrawal requests, wallet transaction and ledger evidence, enforcement actions, and audit records use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: Admin work queue, decisions, pricing, fee policy, withdrawals, payout status, enforcement, and statistics responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation failures, unauthorized access, forbidden role access, stale state, concurrency conflicts, payout failures, inconsistent financial evidence, and unexpected failures use global exception handling and safe messages with no raw stack traces.
- **CA-006 Security**: Admin tools require JWT Admin authorization except doctor-owned withdrawal request submission, which requires the authenticated approved, active, non-suspended Doctor owner. Doctor, Company, pending, rejected, deleted, suspended, and unauthenticated callers cannot access admin capabilities.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Withdrawal requests create a pending hold, approval preserves the hold, paid finalizes it, and rejection or eligible failure releases it. Pricing and fee changes affect future snapshots only.
- **CA-008 Queue Determinism**: Phase 11 does not alter delivery queue ordering, daily activation limits, expiry, retry, carry-over, or campaign delivery activation rules. Campaign approval decisions remain idempotent and must not duplicate queue rows.
- **CA-009 Wallet Determinism**: Withdrawal submission, rejection, paid, and failed transitions are the only Phase 11 wallet-mutating actions. They affect doctor withdrawable earnings through auditable holds, releases, and payout finalization only. Phase 11 does not mutate company wallet top-ups, campaign reservations, expiry releases, interaction charges, doctor earnings settlement, or platform fee settlement formulas.

### Key Entities *(include if feature involves data)*

- **Admin Work Queue Item**: Safe admin-facing representation of a pending account, file, campaign, enforcement, or withdrawal task with category, status, urgency, submitted/requested time, safe owner summary, and next allowed actions.
- **Admin Decision Record**: Audit-preserved decision evidence for account, file, campaign, enforcement, pricing, fee, or payout actions, including actor, target, prior state, resulting state, reason, note, time, and correlation evidence.
- **Doctor Price History Entry**: Immutable record of a doctor price change or separate pricing deactivation action, including previous price, new price when applicable, active/inactive pricing state, admin actor, reason, and effective time.
- **Platform Fee Policy History Entry**: Immutable record of a platform fee policy value and effective time, preserving prior policies for historical reporting and settlement explanation.
- **Withdrawal Request**: Doctor-owned request to convert withdrawable settled earnings into a payout workflow, with amount, status, requested time, held amount behavior, review details, payout reference, and concurrency evidence.
- **Withdrawal Hold Ledger Evidence**: Financial evidence that a requested amount has moved out of available doctor earnings while the withdrawal is open and cannot be requested again.
- **Payout Stub Status**: Admin-controlled payout status evidence for Approved withdrawals, including Paid or Failed outcome, payout reference when paid, reason when failed, actor, and time; it contains no payout destination data.
- **Admin Statistics Snapshot**: Read-only calculated summary over a bounded period for accounts, reviews, campaigns, deliveries, interactions, wallets, withdrawals, pricing policy, fee policy, and enforcement.
- **Enforcement Action Summary**: Admin-visible summary of doctor warning, daily-limit reduction, suspension, reactivation, and rolling violation state produced by earlier enforcement rules.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of admin tool capabilities reject unauthenticated and non-admin access, except doctor-owned withdrawal submission which rejects any non-owning, unapproved, inactive, or suspended doctor.
- **SC-002**: 100% of admin work queue pagination tests return every matching item exactly once with stable ordering and correct category counts.
- **SC-003**: 100% of account, file, campaign, pricing, fee, enforcement, and payout decisions create audit evidence with actor, target, prior state, resulting state, time, and reason when required.
- **SC-004**: 100% of retried or concurrent admin decisions apply at most one successful state transition and never duplicate financial, queue, or audit effects.
- **SC-005**: 100% of doctor price changes, pricing deactivations, and platform fee policy changes apply only to future activation snapshots and leave existing delivery snapshots, settlements, reports, and ledgers unchanged.
- **SC-006**: 100% of invalid doctor price, platform fee, withdrawal amount, and incompatible transition requests are rejected without mutating state.
- **SC-007**: 100% of valid withdrawal requests move the requested amount out of available withdrawable earnings exactly once while the request remains open.
- **SC-008**: 100% of withdrawal rejection and eligible payout failure tests release the held amount back to available earnings exactly once.
- **SC-009**: 100% of paid withdrawal tests finalize the held amount exactly once with a payout reference and prevent later release, rejection, or duplicate payment.
- **SC-010**: 100% of admin statistics totals for sampled periods reconcile to committed source evidence; when financial evidence is inconsistent, affected financial totals are withheld and flagged while unrelated non-financial totals are still returned.
- **SC-011**: At least 95% of warmed admin work queue and statistics requests for a 90-day period complete within 2 seconds under the agreed Phase 11 performance profile.
- **SC-012**: 100% of public, doctor, company, and admin responses use the standard envelope and expose no raw stack traces, raw storage keys, provider credentials, raw idempotency material, private contact data beyond authorized review need, or unauthorized wallet internals.
- **SC-013**: 100% of Phase 11 tests confirm no changes to campaign delivery activation rules, interaction settlement formulas, company-owned reporting paths, external payment-gateway behavior, or hard-deleted audit history.

## Assumptions

- Earlier phases already provide Admin authorization, pending account approval workflows, protected file review, campaign moderation, doctor pricing history, platform fee policy history, delivery snapshots, interaction settlement evidence, activity/weekly enforcement evidence, standard response envelopes, and audit abstractions.
- Phase 11 may consolidate and harden existing admin capabilities while adding missing payout management and admin statistics behavior from the backend plan.
- Doctor withdrawal requests are in scope because payout management requires a request before admin decision and payout status tracking.
- EGP is the only payout currency for Phase 11, and monetary amounts use the platform's two-decimal money rules.
- Withdrawable settled earnings exclude active campaign reservations, pending withdrawal holds, disputed or inconsistent ledger evidence, and any amount already paid or requested in another open withdrawal.
- The payout stub records operational status and references only; payout destination collection, saved payout methods, real bank transfer, card payout, tax withholding, and payment-provider reconciliation are out of scope.
- Platform fee percentages follow the existing platform rule of greater than 0 and less than or equal to 100.
- Admin statistics use a maximum 90-day date range by default to match current reporting bounds and keep read-only calculations predictable.
- Public reasons are visible to affected doctors or companies where applicable; internal admin notes are visible only to authorized admins.
