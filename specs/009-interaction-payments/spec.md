# Feature Specification: Interaction & Payments (Phase 8)

**Feature Branch**: `[009-interaction-payments]`  
**Created**: 2026-07-10  
**Status**: Draft  
**Input**: User description: "Phase 8: Interaction & Payments in backend plan"

## Clarifications

### Session 2026-07-10

- Q: What retry identity should Phase 8 use for doctor interaction settlement? → A: Require `Idempotency-Key` header for interaction requests; same doctor + delivery + key + same decision replays, conflicting payloads return conflict.
- Q: How should optional interaction feedback be normalized and limited? → A: Trim feedback, treat empty-after-trim as no feedback, and cap stored feedback at 1,000 characters.
- Q: Must a doctor read a delivery before Accept or Reject? → A: Interaction does not require prior read; `ReadAtUtc` changes only via the read endpoint.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record Message Reads Without Payment Settlement (Priority: P1)

As a doctor, I need opening a delivered message to be recorded without making a campaign decision so that I can review the content before choosing Accept or Reject.

**Why this priority**: Read tracking is explicitly separate from billable interaction and must not create financial effects.

**Independent Test**: Authenticate as a doctor with an active current-day delivery, mark it read more than once, and confirm `ReadAtUtc` is set once while company and doctor wallet balances and financial records remain unchanged.

**Acceptance Scenarios**:

1. **Given** an authenticated doctor owns an Active delivery for the current Egypt business date and it has no read timestamp, **When** the doctor opens the message, **Then** the delivery records one read timestamp and no company charge, doctor earning, or wallet transaction is created.
2. **Given** the same doctor opens the same delivery again, **When** read tracking is retried, **Then** the original read timestamp remains authoritative and no duplicate audit or financial effect is created.
3. **Given** an unauthenticated caller, a non-Doctor role, or a different doctor attempts to mark a delivery read, **When** the operation is requested, **Then** access is denied without exposing whether the delivery exists.

---

### User Story 2 - Settle Accept or Reject Exactly Once (Priority: P1)

As a doctor, I need to Accept or Reject an active message and have the company charge and my earnings settled exactly once so that campaign decisions and wallet balances remain trustworthy.

**Why this priority**: Pay-on-interaction is the official MVP billing rule; double charging or double earning would break the core financial model.

**Independent Test**: Prepare an Active delivery with a Reserved reservation, retry the same Accept or Reject request concurrently and sequentially, and confirm the delivery reaches one final interaction state with one company Charge and one doctor Earn effect.

**Acceptance Scenarios**:

1. **Given** an authenticated doctor owns an Active current-day delivery with `ReservationStatus` Reserved, **When** the doctor accepts the message, **Then** the delivery becomes Accepted, interaction time is recorded, the company reserved balance decreases by the reserved amount, the doctor available balance increases by the stored doctor earnings, and append-only Charge and Earn records are created exactly once.
2. **Given** an authenticated doctor owns an Active current-day delivery with `ReservationStatus` Reserved, **When** the doctor rejects the message, **Then** the delivery becomes Rejected and the same one-time financial settlement occurs as for an acceptance.
3. **Given** an interaction request is retried with the same Doctor actor, delivery, `Idempotency-Key`, and decision after the first request succeeded, **When** the retry is processed, **Then** the existing accepted or rejected result is returned without creating another charge, earning, balance mutation, or interaction timestamp.
4. **Given** the Active current-day delivery has not been marked read, **When** the doctor submits Accept or Reject, **Then** interaction settlement can still succeed and `ReadAtUtc` remains unchanged.
5. **Given** two workers or requests attempt to settle the same delivery at the same time, **When** processing completes, **Then** exactly one final interaction decision wins and the losing request receives either the settled result for the same decision or a safe conflict for a different decision.

---

### User Story 3 - Capture Optional Doctor Feedback With the Interaction (Priority: P2)

As a doctor, I need to include optional feedback when I accept or reject a message so that companies and later scoring/reporting workflows can use the response context.

**Why this priority**: Feedback adds business value, but the payment settlement must be correct even when no feedback is supplied.

**Independent Test**: Settle deliveries with no feedback, short feedback, and qualifying feedback, then confirm feedback fields are stored with the interaction while settlement behavior remains identical.

**Acceptance Scenarios**:

1. **Given** a doctor submits Accept or Reject without feedback, **When** settlement succeeds, **Then** the interaction is valid and feedback fields remain empty.
2. **Given** a doctor submits Accept or Reject with feedback text, **When** settlement succeeds, **Then** the text is stored with a feedback timestamp and is associated with the final interaction.
3. **Given** feedback contains leading or trailing whitespace, **When** the interaction is submitted, **Then** the stored feedback is trimmed, and if it becomes empty it is treated as no feedback.
4. **Given** feedback exceeds 1,000 characters after trimming, **When** the interaction is submitted, **Then** the request is rejected and no partial settlement is created.

---

### User Story 4 - Protect Stale, Expired, and Invalid Deliveries (Priority: P2)

As the platform operator, I need stale or inconsistent deliveries to be rejected from interaction settlement so that expired messages and broken reservations cannot create financial damage.

**Why this priority**: Settlement depends on previous reservation and expiry phases; Phase 8 must preserve their invariants under retries, missed jobs, and data conflicts.

**Independent Test**: Attempt to read and interact with expired, prior-day, already settled, released, missing-reservation, and cross-owner deliveries and confirm no unauthorized or invalid financial mutation occurs.

**Acceptance Scenarios**:

1. **Given** a delivery is Expired, Released, not Active, not Reserved, or belongs to a prior Egypt business date, **When** a doctor attempts to Accept or Reject it, **Then** the request is rejected and no company or doctor wallet balance changes.
2. **Given** a delivery has inconsistent settlement evidence or missing wallet relationships, **When** interaction is attempted, **Then** the operation fails safely, records an operational issue, and creates no partial financial effect.
3. **Given** the same delivery has already been Accepted or Rejected, **When** a different decision is submitted later, **Then** the system rejects the change and preserves the original final state and ledger evidence.

### Edge Cases

- A doctor opens a delivery immediately before or after submitting an interaction; read tracking remains independent and must not alter settlement, and settlement must not alter `ReadAtUtc`.
- The Egypt business date changes before the expiry job has processed a still-Active delivery; the delivery is no longer eligible for read or interaction settlement through doctor operations.
- The company reserved balance no longer matches the delivery's reserved amount; settlement fails safely without creating a Charge or Earn transaction.
- The doctor wallet is absent, deleted, or otherwise unavailable at settlement time; the operation fails safely and leaves the delivery and company reservation unchanged.
- A retry repeats the same Doctor actor, delivery, and `Idempotency-Key` with different decision, feedback, or other request data; the system rejects the conflicting replay without changing the original result.
- Feedback is empty, whitespace-only, shorter than the future activity-score counting threshold, or omitted entirely; settlement remains valid, whitespace-only feedback is stored as no feedback, and scoring/reporting interpretation is left to later phases.
- Unauthorized roles, cross-doctor access, malformed delivery identifiers, and missing deliveries are denied without leaking protected delivery, campaign, or wallet details.
- A storage or campaign-content issue prevents message display; read tracking fails safely and does not mutate financial state.
- Financial settlement partially fails after one candidate change has begun; the delivery state, reservation state, company balance, doctor balance, Charge record, Earn record, and audit evidence all commit together or none commit.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose an authenticated Doctor-only operation for marking the authenticated doctor's own current-day delivery as read.
- **FR-002**: Read tracking MUST be allowed only for deliveries owned by the authenticated doctor whose `DeliveryDateEgypt` is the current Egypt business date and whose content remains visible to the doctor.
- **FR-003**: The first successful read MUST record `ReadAtUtc`; repeated read attempts MUST preserve the original read timestamp and MUST NOT create any company charge, doctor earning, reservation change, wallet balance mutation, or settlement transaction.
- **FR-004**: The system MUST expose an authenticated Doctor-only operation for submitting exactly one interaction decision, Accept or Reject, for the authenticated doctor's own delivery.
- **FR-004A**: Every interaction request MUST include a required `Idempotency-Key` header normalized by trimming and validated to 8-128 characters before settlement begins.
- **FR-005**: A delivery MUST be eligible for interaction settlement only when it is Active, belongs to the current Egypt business date, has no prior interaction, has `ReservationStatus` Reserved, and has a valid reserved amount, price snapshot, platform-fee snapshot, platform-fee amount, and doctor-earnings amount from activation.
- **FR-006**: Accept and Reject MUST both be billable interactions and MUST follow the same settlement rules; opening or reading a message MUST NOT be billable.
- **FR-006A**: Interaction settlement MUST NOT require a prior read timestamp and MUST NOT set or change `ReadAtUtc`; `ReadAtUtc` changes only through the read-tracking operation.
- **FR-007**: On successful interaction, the system MUST set the delivery final status to Accepted or Rejected, record `InteractedAtUtc`, preserve the selected decision, and change `ReservationStatus` from Reserved to Charged.
- **FR-008**: On successful interaction, the system MUST decrease the campaign owner's company reserved balance by exactly the delivery's `ReservedAmount`.
- **FR-009**: On successful interaction, the system MUST increase the interacting doctor's available balance by exactly the delivery's stored `DoctorEarnings`, where doctor earnings equals the delivery price snapshot minus the rounded platform fee amount captured for that delivery.
- **FR-010**: Successful settlement MUST create append-only financial evidence for both sides: one company Charge transaction for the reserved amount and one doctor Earn transaction for the doctor earnings, each linked to the delivery and protected by deterministic operation-level idempotency.
- **FR-011**: The delivery state change, reservation state change, company reserved-balance decrease, doctor available-balance increase, Charge transaction, Earn transaction, and financial audit evidence for one interaction MUST complete atomically.
- **FR-012**: Repeated, overlapping, or resumed processing of the same interaction operation MUST create no duplicate delivery transition, balance mutation, Charge transaction, Earn transaction, or audit evidence.
- **FR-013**: If the same Doctor actor, delivery, `Idempotency-Key`, decision, and normalized feedback are replayed after successful settlement, the system MUST return the existing settled outcome without additional financial effect.
- **FR-014**: If a replay or concurrent request uses the same Doctor actor, delivery, and `Idempotency-Key` but attempts a different decision, feedback, or materially different request data, the system MUST reject it as a conflict and preserve the original settled outcome.
- **FR-014A**: If a request uses a different `Idempotency-Key` after the delivery is already Accepted or Rejected, the system MUST return the existing settled outcome only when the requested decision matches the existing final delivery state; it MUST reject a different requested decision as a conflict. In both cases, it MUST NOT mutate delivery state, feedback, wallets, transactions, ledgers, or audit settlement evidence.
- **FR-015**: The system MUST reject interaction settlement for Expired, Released, Charged, Accepted, Rejected, prior-day, future-day, cross-owner, missing-reservation, missing-wallet, inconsistent-balance, or otherwise ineligible deliveries without creating a partial financial effect.
- **FR-016**: Feedback text MUST be optional on interaction. When supplied, it MUST be trimmed before storage; empty-after-trim feedback MUST be treated as no feedback; feedback longer than 1,000 characters after trimming MUST be rejected before any settlement mutation begins; when feedback is omitted, settlement MUST remain valid.
- **FR-017**: Feedback storage MUST NOT change the financial meaning of Accept or Reject. Feedback quality review, company feedback listing, analytics, and activity-score usage are outside Phase 8 except for preserving the submitted feedback data for later phases.
- **FR-018**: Interaction requests MUST be rate limited according to the project-sensitive interaction endpoint category.
- **FR-019**: All read and interaction responses MUST use the standard response envelope `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-020**: Authorization, validation, stale-state, idempotency conflict, financial consistency, and unexpected failures MUST use safe error handling without raw stack traces, wallet internals, storage credentials, idempotency material, or protected campaign details.
- **FR-021**: The system MUST audit successful read tracking, successful interaction settlement, rejected conflicting replays, and financial consistency failures with safe metadata sufficient for operational investigation.
- **FR-022**: Phase 8 MUST NOT release expired reservations, activate queued messages, compute weekly enforcement, update activity scores, send notifications, expose company analytics or feedback reports, approve withdrawals, or add admin payout tooling.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-006`, `FR-013` to `FR-020` including `FR-014A`, and `FR-022` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for doctor-owned read and interaction operations, idempotency-key validation, authorization, response contracts, and scope boundaries.
  - `FR-007` to `FR-012` and `FR-021` target `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` for delivery state, reservation state, wallet settlement, idempotency, atomic financial records, and audit evidence; `MediBridge.APIs` remains a transport adapter only.
- **CA-002 Controller Boundary**: Controllers remain HTTP-only adapters that authenticate the caller, accept request data, and delegate read tracking, interaction eligibility, settlement, feedback, idempotency, and audit behavior to service use cases.
- **CA-003 SQL Persistence Boundary**: Delivery, wallet, transaction, idempotency, and audit persistence uses SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services must not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: Read and interaction operations return `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` for success and safe failures.
- **CA-005 Error Handling**: Validation, authorization, stale-state, idempotency, concurrency, consistency, and unexpected failures are mapped through global exception handling and safe domain errors; raw stack traces are never exposed.
- **CA-006 Security**: Read and interaction operations require JWT Doctor authorization, authenticated-owner scoping, and interaction endpoint rate limiting. Company, Admin, unauthenticated, and cross-doctor access is denied without revealing protected existence details.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Phase 8 starts only from existing Active, Reserved deliveries and settles only Accept or Reject. Read tracking is non-financial. Expiry release, delivery activation, reporting, enforcement, notifications, withdrawals, and payouts remain outside this feature.
- **CA-008 Queue Determinism**: Queue ordering, daily limits, activation, expiry, retry, and carry-over behavior are inherited from earlier phases. Phase 8 does not create, reorder, reactivate, cancel, or carry over queue rows. It only settles eligible Active deliveries already produced by the delivery workflow.
- **CA-009 Wallet Determinism**: Accept or Reject debits the company Reserved balance by the delivery's reserved amount using a Charge transaction and credits the doctor Available balance by stored doctor earnings using an Earn transaction. Platform fee handling uses the delivery's stored rounded fee snapshot; the platform retains the difference between charged reserved amount and doctor earnings. Both financial effects and their ledger evidence commit atomically under deterministic idempotency.

### Key Entities *(include if feature involves data)*

- **Doctor Ad Delivery**: A doctor-owned current-day campaign delivery with read timestamp, Active/Accepted/Rejected/Expired status, interaction timestamp, optional feedback, price and fee snapshots, doctor earnings, reserved amount, reservation status, and concurrency state.
- **Company Wallet**: The campaign owner's wallet whose reserved EGP balance is reduced when an eligible delivery is accepted or rejected.
- **Doctor Wallet**: The interacting doctor's wallet whose available EGP balance is credited with the stored doctor earnings for a successful interaction.
- **Wallet Transaction and Ledger Evidence**: Append-only records for one Charge and one Earn operation linked to the delivery and protected by operation-level idempotency.
- **Interaction Operation Identity**: The retry-safe operation scope for a doctor interaction request, composed of the authenticated Doctor actor, delivery, required normalized `Idempotency-Key`, and request data needed to return same-result replays and reject conflicting replays without duplicate financial effects.
- **Doctor Feedback**: Optional text submitted with Accept or Reject, trimmed before storage, absent when empty after trimming, limited to 1,000 stored characters, and associated with the final interaction for later company reporting, feedback review, analytics, and activity scoring.
- **Financial Audit Event**: Safe operational evidence for read tracking, successful settlement, conflicts, and consistency failures without exposing sensitive wallet or idempotency material.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of successful read-tracking attempts for eligible current-day deliveries record a read timestamp without creating any company charge, doctor earning, reservation-status change, or wallet balance mutation.
- **SC-002**: 100% of successful Accept and Reject interactions create exactly one final delivery decision, one company charge effect, and one doctor earning effect for the delivery.
- **SC-003**: 100% of repeated and concurrent same-decision interaction retries return one settled outcome with no duplicate balance mutation, Charge record, Earn record, or interaction timestamp.
- **SC-004**: 100% of conflicting interaction replays or concurrent different-decision attempts preserve the original final decision and produce no additional financial effect.
- **SC-005**: 100% of expired, prior-day, released, already charged, cross-owner, missing-wallet, and inconsistent-reservation settlement attempts are rejected without partial delivery or wallet mutation.
- **SC-006**: In forced-failure tests, 100% of interaction settlements either commit the delivery transition, reservation transition, both wallet balance changes, both financial records, and audit evidence together or commit none of them.
- **SC-007**: 100% of accepted feedback submissions are associated with the final interaction, while interactions without feedback still settle successfully.
- **SC-008**: At least 95% of measured eligible read and interaction requests complete within 1 second under the project's reproducible backend test profile, with no failed requests caused by unbounded content or wallet lookups.
- **SC-009**: 100% of unauthorized, non-Doctor, and cross-doctor read or interaction attempts are denied without returning protected campaign, delivery, wallet, or storage data.
- **SC-010**: 100% of sampled successful settlements and rejected idempotency or consistency conflicts have safe audit evidence sufficient to identify the delivery, actor, outcome category, and time without exposing secrets, raw stack traces, or idempotency material.

## Assumptions

- Earlier phases already provide approved doctor and company identities, authenticated Doctor role access, active current-day deliveries, delivery ownership, visible campaign content, company and doctor wallets, reservation records, price and platform-fee snapshots, doctor earnings snapshots, append-only wallet transactions, audit plumbing, global exception handling, and rate-limit scaffolding.
- Phase 7 has already activated eligible deliveries and reserved company funds. Phase 8 does not create new deliveries or reservations.
- The authoritative Egypt business date uses the existing DST-aware `Africa/Cairo` rule. Doctor read and interaction actions are limited to deliveries visible for that current business date.
- Platform fee and doctor earnings are calculated at activation and stored on the delivery. Phase 8 validates and uses those stored monetary values rather than inventing or changing pricing policy at interaction time.
- Feedback can be stored at interaction time even when it is shorter than the future activity-score counting threshold; later phases decide how feedback appears in company reporting, quality review, analytics, and scoring. Whitespace-only feedback is treated as no feedback, and stored feedback is capped at 1,000 characters.
- Interaction retry identity uses the existing required `Idempotency-Key` header convention for financial mutations. Read tracking is naturally idempotent by delivery ownership plus the first stored read timestamp and does not require a client retry key. Accept or Reject remains valid without prior read tracking, and settlement does not backfill `ReadAtUtc`.
- Company reporting, feedback listing, notifications, weekly enforcement, activity score recalculation, withdrawal approval, payouts, and admin statistics are delivered by later phases.
