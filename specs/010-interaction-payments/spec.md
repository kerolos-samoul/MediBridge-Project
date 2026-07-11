# Feature Specification: Interaction & Payments (Phase 8)

**Feature Branch**: `[010-interaction-payments]`  
**Created**: 2026-07-11  
**Status**: Draft  
**Input**: User description: "Phase 8: Interaction & Payments in backend plan"

## Clarifications

### Session 2026-07-11

- Q: What idempotency contract should doctor interaction requests use? → A: Require client `Idempotency-Key`; replay same key/content returns original result, same key/different content conflicts.
- Q: How should optional feedback validation affect settlement? → A: Trim feedback; allow omitted or empty; reject unsafe or over-limit feedback before settlement.
- Q: What maximum feedback length should Phase 8 enforce? → A: 2,000 characters after trimming.
- Q: Which Phase 8 events require audit evidence? → A: Audit successful settlement, idempotency conflicts, and reservation/snapshot anomalies.
- Q: Which Phase 8 doctor message actions should be rate limited? → A: Rate-limit both read tracking and Accept/Reject interaction.
- Q: What unsafe-markup policy should optional feedback use? → A: Treat feedback as plain text only; allow ordinary letters, numbers, whitespace, line breaks, and punctuation except markup delimiters, and reject `<` or `>`, encoded angle brackets `&lt;` or `&gt;`, Markdown links/images, and `javascript:` or `data:` URI schemes before settlement.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Track Message Reads Without Billing (Priority: P1)

As a doctor, I need opening or reading a current-day message to be recorded without creating a financial charge so that I can review campaign content before deciding whether to accept or reject it.

**Why this priority**: Read tracking is part of the interaction workflow, but the locked payment rule says billing happens only on Accept or Reject.

**Independent Test**: Authenticate as a doctor with an Active current-day delivery, mark it as read more than once, and confirm `ReadAtUtc` is recorded once while the delivery remains unsettled and all wallet balances and financial records remain unchanged.

**Acceptance Scenarios**:

1. **Given** an authenticated doctor owns an Active delivery for the current Egypt business day, **When** the doctor marks the message as read, **Then** the delivery records the first read time and no charge, earning, fee, or wallet transaction is created.
2. **Given** the same delivery already has a read time, **When** the doctor marks it as read again, **Then** the original read time is preserved and no financial effect occurs.
3. **Given** the owning doctor reads a delivery that has already been Accepted or Rejected, **When** read tracking is requested, **Then** the first read time is recorded or replayed without changing the settled outcome, reservation status, balances, wallet transactions, ledger entries, or settlement evidence.
4. **Given** a message belongs to another doctor, another role, an unauthenticated caller, or is outside the allowed delivery visibility window, **When** read tracking is requested, **Then** access is denied without exposing protected delivery details.
5. **Given** a doctor sends read-tracking requests above the allowed interaction threshold, **When** additional requests arrive during the same limit window, **Then** the system throttles them safely without changing read state or financial state.

---

### User Story 2 - Settle Accept or Reject Exactly Once (Priority: P1)

As a doctor, I need to accept or reject a delivered message and optionally leave feedback so that my response is captured and the reserved company funds are settled into the final company charge, platform fee, and doctor earning.

**Why this priority**: Accept/Reject is the billable business event for the MVP and unlocks reliable campaign spend, doctor earnings, and later reporting.

**Independent Test**: Prepare an Active current-day delivery with a valid reservation and activation snapshots, submit Accept or Reject with optional feedback, and confirm one final delivery outcome, one company charge, one platform fee record, and one doctor earning using the stored snapshots.

**Acceptance Scenarios**:

1. **Given** an Active current-day delivery with a Reserved reservation, stored price snapshot, stored platform-fee percentage, and no prior interaction, **When** the owning doctor submits Accept, **Then** the delivery becomes Accepted, the interaction time is recorded, optional feedback is stored, the company reservation is converted to a final charge exactly once, and the doctor's earning is credited exactly once.
2. **Given** the same valid delivery, **When** the owning doctor submits Reject, **Then** the delivery becomes Rejected and the same settlement guarantees apply because Reject is a billable interaction.
3. **Given** the price snapshot is 100.00 EGP and the stored platform-fee percentage is 12.345%, **When** the delivery is settled, **Then** the platform fee is rounded to 12.35 EGP and doctor earnings equal 87.65 EGP.
4. **Given** feedback is supplied with the interaction, **When** settlement succeeds, **Then** the normalized feedback remains linked to that interaction without changing the financial formula; feedback shorter than 15 characters is stored but remains ineligible for later feedback-score credit.

---

### User Story 3 - Retry or Race Interactions Safely (Priority: P1)

As a platform operator, I need repeated, delayed, and overlapping interaction requests to converge on one financial outcome so that network retries cannot double-charge a company or double-credit a doctor.

**Why this priority**: The feature moves money. Duplicate settlement is a high-severity failure even when the visible delivery state looks correct.

**Independent Test**: Submit the same interaction repeatedly and concurrently, including forced interruptions around settlement, and confirm at most one accepted final state and one complete financial effect exist for the delivery.

**Acceptance Scenarios**:

1. **Given** an interaction request with a client `Idempotency-Key` is retried with the same outcome and matching request content after the first request settled successfully, **When** the retry is processed, **Then** the existing settled result is returned without creating any additional wallet or ledger records.
2. **Given** an interaction request reuses a client `Idempotency-Key` with a different outcome or materially different feedback, **When** the conflicting request is processed, **Then** it is rejected without changing the delivery or wallet state.
3. **Given** two workers attempt to settle the same delivery at the same time, **When** both complete, **Then** one outcome wins, every losing attempt observes the final state safely, and the total financial effect remains exactly one charge, one fee, and one earn.
4. **Given** settlement is interrupted before all related changes can be completed, **When** the operation ends or is retried, **Then** either all delivery, reservation, wallet, transaction, and ledger changes are preserved together or none are preserved.
5. **Given** a successful settlement or settlement-blocking idempotency conflict occurs, **When** the request completes, **Then** safe audit evidence records the outcome without storing sensitive idempotency material or wallet internals.
6. **Given** a doctor sends Accept or Reject requests above the allowed interaction threshold, **When** additional requests arrive during the same limit window, **Then** the system throttles them safely without changing delivery, feedback, or wallet state.

---

### User Story 4 - Reject Ineligible Interaction Attempts (Priority: P2)

As a doctor and as a pharmaceutical company, I need expired, missing, inconsistent, or unauthorized deliveries to be protected from settlement so that stale messages and broken reservations cannot create incorrect spend or earnings.

**Why this priority**: Interaction settlement must respect the same delivery-day, ownership, and reservation rules established by earlier phases.

**Independent Test**: Attempt read tracking and interaction against expired, already settled, missing-reservation, cross-doctor, and non-current deliveries and confirm no unauthorized response includes protected data and no invalid attempt mutates balances.

**Acceptance Scenarios**:

1. **Given** a delivery has expired or its Egypt delivery day is no longer actionable, **When** a doctor attempts Accept or Reject, **Then** settlement is refused and no charge or earning is created.
2. **Given** a delivery lacks a valid Reserved reservation or its reservation evidence conflicts with the delivery snapshots, **When** interaction is attempted, **Then** the delivery remains unchanged, the issue is recorded safely for operations and audit review, and no financial effect occurs.
3. **Given** a company user, admin user, unauthenticated caller, or another doctor attempts to interact with a delivery, **When** the request is processed, **Then** authorization fails without disclosing whether the protected delivery exists.

### Edge Cases

- A read request and an interaction request arrive concurrently for the same Active delivery; the interaction may settle once, while read tracking never creates or modifies financial effects.
- A delivery is read after it has already been Accepted or Rejected by the owning doctor; the settled outcome and financial records remain unchanged.
- Read-tracking or interaction requests exceed the configured doctor interaction threshold; throttled requests are rejected safely and do not mutate delivery, feedback, reservation, wallet, transaction, ledger, or audit state except for standard safe request handling.
- A delivery is Active but its delivery date is not the current Egypt business date because expiry has not yet run; interaction is refused and no settlement occurs.
- The company's reserved balance is lower than the delivery's stored reserved amount, or the linked reservation transaction is missing; interaction is refused, the inconsistency is recorded safely, and no partial settlement occurs.
- The rounded platform fee would be zero, equal to, or greater than the price because of invalid historical snapshot data; interaction is refused and no financial effect occurs.
- The same interaction is replayed after a client timeout, service restart, or database retry; the response converges on the original settled result.
- Optional feedback is omitted, empty, whitespace, very long, or contains unsafe markup; omitted, empty, and whitespace-only feedback is allowed after normalization, while feedback longer than 2,000 characters after trimming or feedback containing `<`, `>`, case-insensitive encoded angle brackets `&lt;`/`&gt;`, Markdown links/images, or `javascript:`/`data:` URI schemes prevents the interaction from settling.
- The delivery's campaign, doctor, or company is later deleted or suspended after activation; already Active current-day deliveries remain interactable only when the authenticated doctor owns them and the stored reservation is valid.
- Daylight-saving or host-time differences occur; current-day eligibility uses the authoritative Egypt business-time rule rather than the host machine's local date.
- Unexpected failures are reported through the standard safe error path without exposing stack traces, financial idempotency material, wallet internals, or storage details.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST use the DST-aware `Africa/Cairo` business date and clock to decide whether a delivery is current-day actionable for read tracking and interaction; it MUST NOT use host-local dates or a fixed UTC offset for this decision.
- **FR-002**: The system MUST expose an authenticated Doctor read-tracking operation for a delivery owned by the authenticated doctor.
- **FR-003**: Read tracking MUST set `ReadAtUtc` only when it is currently absent and MUST preserve the first-read time on repeated requests.
- **FR-004**: Read tracking MUST NOT change delivery status, reservation status, company balances, doctor balances, platform fee totals, wallet transactions, or ledger records.
- **FR-005**: The system MUST expose an authenticated Doctor interaction operation for a delivery owned by the authenticated doctor.
- **FR-006**: The interaction operation MUST accept exactly one billable outcome of Accept or Reject and MAY accept optional feedback bound to that interaction.
- **FR-007**: A delivery MUST be eligible for interaction only when it is Active, belongs to the authenticated doctor, is actionable for the current Egypt business date, has not already settled to a different outcome, and has a valid Reserved reservation linked to its activation snapshots.
- **FR-008**: An eligible Accept interaction MUST move the delivery to Accepted, record the interaction time, store valid optional feedback, and settle the associated reservation exactly once.
- **FR-009**: An eligible Reject interaction MUST move the delivery to Rejected, record the interaction time, store valid optional feedback, and settle the associated reservation exactly once.
- **FR-010**: Settlement MUST convert the delivery's stored reserved price amount into a final company charge and MUST reduce the company's reserved balance by exactly that stored amount.
- **FR-011**: Settlement MUST calculate the platform fee from the stored price and stored platform-fee percentage snapshots using `Fee = round(Price * FeePercent, 2)` in EGP, then calculate doctor earnings as `Price - Fee`.
- **FR-012**: Settlement MUST credit the doctor with exactly the stored price minus the rounded platform fee and MUST record the platform fee as distinct financial evidence for later reporting and reconciliation.
- **FR-013**: Settlement MUST reject the interaction with no mutation when stored snapshot or reservation data would produce a zero, negative, or otherwise invalid fee or earning.
- **FR-014**: The delivery outcome, interaction timestamp, feedback, reservation finalization, company balance change, doctor balance change, platform-fee evidence, wallet transactions, and ledger records for one interaction MUST complete atomically.
- **FR-015**: Every charge, earn, and platform-fee financial effect MUST have append-only evidence linked to the delivery and protected by operation-level idempotency material.
- **FR-016**: The interaction operation MUST require a client-supplied `Idempotency-Key` for each attempted Accept or Reject request, and the raw key MUST be used only as transient request input for normalization and hashing; persistent storage, logs, audit records, diagnostics, responses, and task evidence MUST contain only normalized hashes/fingerprints or non-sensitive conflict categories.
- **FR-017**: Repeated requests using the same `Idempotency-Key`, same delivery, same outcome, and matching feedback after successful settlement MUST return the existing settled result without creating duplicate financial records or changing balances.
- **FR-018**: A request that reuses an `Idempotency-Key` with a different delivery, outcome, or materially different feedback MUST be rejected without changing the settled delivery or financial state.
- **FR-019**: A request that conflicts with an already settled delivery outcome or materially different stored feedback MUST be rejected without changing the settled delivery or financial state.
- **FR-020**: Overlapping interaction attempts for one delivery MUST result in at most one final delivery outcome and at most one set of charge, fee, and earn effects.
- **FR-021**: Expired deliveries, non-current deliveries, deleted deliveries, missing deliveries, and deliveries without valid Reserved reservation evidence MUST NOT be settled.
- **FR-022**: Successful settlement, idempotency conflicts, and reservation or snapshot anomalies that block settlement MUST create safe audit evidence without recording raw idempotency material, stack traces, wallet internals, or sensitive financial details beyond the minimum needed for reconciliation.
- **FR-023**: Ordinary read tracking MUST NOT create financial audit records unless it is part of a rejected unauthorized or anomalous request handled by the standard security/error path.
- **FR-024**: Feedback supplied with an interaction MUST be trimmed and validated before settlement; omitted, empty, and whitespace-only feedback MUST remain allowed.
- **FR-025**: Feedback MUST be treated as plain text only: ordinary letters, numbers, whitespace, line breaks, and punctuation are allowed except markup delimiters; feedback containing `<`, `>`, case-insensitive encoded angle brackets `&lt;` or `&gt;`, Markdown links/images such as `[text](url)` or `![alt](url)`, or `javascript:`/`data:` URI schemes, or feedback exceeding 2,000 characters after trimming, MUST prevent the interaction from settling and MUST create no delivery, feedback, or financial mutation.
- **FR-026**: Feedback with fewer than 15 non-whitespace characters MAY be stored but MUST be distinguishable from feedback that qualifies for later feedback-score credit.
- **FR-027**: The system MUST deny unauthenticated callers, non-Doctor roles, and doctors accessing another doctor's delivery for both read tracking and interaction without disclosing protected delivery existence.
- **FR-028**: The system MUST rate-limit both read-tracking requests and Accept/Reject interaction requests using the doctor interaction protection for this feature.
- **FR-029**: Throttled read-tracking or interaction requests MUST create no delivery, feedback, reservation, wallet, transaction, ledger, or financial audit mutation beyond standard safe request handling.
- **FR-030**: Successful read-tracking and interaction responses MUST use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` and MUST include only doctor-safe delivery outcome information.
- **FR-031**: Failure responses MUST be handled by the standard safe error path and MUST NOT expose raw stack traces, storage locations, wallet internals, idempotency material, or sensitive financial details.
- **FR-032**: The feature MUST NOT change daily injection, expiry release, weekly enforcement, activity-score calculation, withdrawal payout, company analytics, campaign reporting, admin settlement correction, or external payment-gateway behavior.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001`, `FR-007` to `FR-026`, and `FR-032` target `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` for business-date eligibility, delivery state transitions, settlement rules, idempotent wallet mutation, audit evidence, feedback validation, persistence, and scope boundaries.
  - `FR-002` to `FR-006` and `FR-027` to `FR-031` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for authorized and rate-limited read and interaction use cases, response contracts, and HTTP-only adapters.
- **CA-002 Controller Boundary**: Controllers remain transport adapters only and delegate authorization context, Egypt-date eligibility, read-tracking semantics, interaction validation, state transitions, and financial settlement to service use cases.
- **CA-003 SQL Persistence Boundary**: Delivery, reservation, feedback, wallet, transaction, and ledger persistence uses SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: Read-tracking and interaction responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation, authorization, stale-state, conflicting-retry, inconsistent-reservation, and unexpected failures use centralized safe error handling and safe audit evidence where required; raw stack traces and sensitive financial details are never returned.
- **CA-006 Security**: Both read tracking and interaction require JWT Doctor authorization, authenticated-owner scoping, and doctor interaction rate limiting. Non-doctor roles and cross-doctor access are denied without protected delivery disclosure.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Read tracking is explicitly non-billable. Accept and Reject are both billable. Settlement uses only stored activation snapshots and the stored Reserved reservation for that delivery.
- **CA-008 Queue Determinism**: Queue ordering, daily limits, activation, expiry, retry, and carry-over rules remain owned by Phase 7. Phase 8 consumes only deliveries already activated by those rules and does not requeue, reprioritize, activate, expire, or carry over messages.
- **CA-009 Wallet Determinism**: Read tracking has no wallet effect. Interaction debits company Reserved for the stored price using a Charge effect, credits the doctor for `Price - RoundedFee` using an Earn effect, records the rounded platform fee separately, and commits every delivery and financial mutation atomically with operation-level idempotency.

### Key Entities *(include if feature involves data)*

- **Doctor Ad Delivery**: A doctor-owned current-day campaign delivery with Active, Accepted, Rejected, or Expired state, first-read time, interaction time, outcome, optional feedback, activation price and fee snapshots, reservation state, and concurrency state.
- **Interaction Record**: The billable Accept or Reject decision for one delivery, including interaction time, normalized optional feedback, feedback-score eligibility marker, actor identity, normalized client `Idempotency-Key` hash/fingerprint, normalized request fingerprint, and idempotency evidence; it never stores or logs the raw client key.
- **Company Wallet**: The campaign owner's reserved EGP balance, reduced by the delivery's stored reserved amount when Accept or Reject settles.
- **Doctor Wallet**: The doctor's available EGP balance, credited by the stored price minus rounded platform fee when Accept or Reject settles.
- **Wallet Transaction and Ledger Entries**: Append-only financial evidence for Charge, Earn, and platform-fee effects linked to the delivery and protected by operation-level idempotency material.
- **Interaction Audit Evidence**: Safe audit record for successful settlements, idempotency conflicts, and reservation or snapshot anomalies, linked to the delivery and actor without exposing raw idempotency material or wallet internals.
- **Read Tracking Result**: Doctor-facing confirmation that a delivery's first-read time was recorded or already existed, containing no internal wallet or storage-provider details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of tested read-tracking requests for eligible deliveries record the first read time without changing any wallet balance or creating any financial record.
- **SC-002**: 100% of eligible Accept and Reject interactions create exactly one final delivery outcome and one complete set of charge, fee, and earn effects.
- **SC-003**: 100% of repeated and concurrent same-key, same-content interaction retries return or converge on the original settled result with zero duplicate wallet balance changes.
- **SC-004**: 100% of same-key conflicting-content retries and post-settlement conflicting retries are rejected without changing the settled outcome, feedback, balances, or financial records.
- **SC-005**: 100% of sampled settlements use stored activation snapshots and match the formula `RoundedFee = round(Price * FeePercent, 2)` and `DoctorEarnings = Price - RoundedFee`.
- **SC-006**: 100% of forced-failure tests around settlement preserve either all related delivery, reservation, wallet, transaction, and ledger changes or none of them.
- **SC-007**: 100% of expired, non-current, missing-reservation, cross-doctor, non-doctor, and unauthenticated interaction attempts create no charge, fee, earning, or feedback mutation.
- **SC-008**: At least 95% of measured warmed read-tracking and interaction requests for valid current-day deliveries complete within 1 second under the agreed Phase 8 performance profile.
- **SC-009**: 100% of public responses use the standard envelope and expose no raw stack traces, storage credentials, wallet internals, idempotency material, or protected cross-owner delivery data.
- **SC-010**: 100% of successful settlements, idempotency conflicts, and reservation or snapshot anomalies create safe audit evidence; 100% of ordinary successful read-tracking requests create no financial audit record.
- **SC-011**: 100% of omitted, empty, and whitespace-only feedback cases remain eligible for settlement, while 100% of unsafe feedback and feedback longer than 2,000 characters after trimming is rejected before any delivery, feedback, or financial mutation.
- **SC-012**: 100% of valid optional feedback values are stored with the interaction, and 100% of feedback shorter than 15 non-whitespace characters is distinguishable from feedback that qualifies for later feedback-score credit.
- **SC-013**: 100% of read-tracking and Accept/Reject interaction requests above the configured doctor interaction threshold are throttled without changing delivery, feedback, reservation, wallet, transaction, ledger, or financial audit state.

## Assumptions

- Earlier phases already provide approved doctors and companies, current-day Active deliveries, valid Reserved reservations, stored price and platform-fee snapshots, company and doctor wallets, append-only wallet transactions, ownership authorization helpers, and standard response/error handling.
- Egypt business time follows the DST-aware `Africa/Cairo` time zone already established by the backend plan and Phase 7.
- The authoritative interaction date boundary is the same delivery-day visibility boundary used by the doctor today inbox.
- Accept and Reject are both billable interactions; merely opening, reading, viewing assets, or dismissing a message is not billable.
- Stored activation snapshots are authoritative for Phase 8 settlement even if doctor pricing or platform-fee policy changes before interaction.
- Settlement creates reporting-ready financial evidence for company charge, doctor earning, and platform fee, but company-facing analytics and admin correction workflows are delivered by later phases.
- External payment-gateway capture is outside this feature because the MVP payment rule settles from already reserved in-platform wallet funds.
