# Research: Phase 8 Interaction & Payments

## Decision: Extend the Existing Doctor Message Surface

**Decision**: Add read tracking and Accept/Reject interaction to the existing Doctor messages controller/service instead of creating a new interaction controller or separate service boundary.

**Rationale**: Phase 7 already owns Doctor message retrieval and asset access, including approved Doctor resolution, Cairo current-day visibility, safe envelopes, and ownership checks. Extending that surface keeps the user workflow coherent and lets controllers stay HTTP-only.

**Alternatives considered**:

- Create `DoctorInteractionsController`: rejected because it would split one Doctor message workflow across two controllers without adding a domain boundary.
- Put settlement in a wallet controller: rejected because the Doctor interaction, not a direct wallet command, is the business event.

## Decision: Read Tracking is Non-Financial and First-Write-Wins

**Decision**: `PUT /api/doctor/messages/{deliveryId}/read` records `ReadAtUtc` only when absent and never changes delivery status, reservation status, wallet balances, financial transactions, ledger entries, or financial audit records.

**Rationale**: The backend plan locks pay-on-interaction; opening or reading a message is useful engagement evidence but not a billable event. Preserving the first read time makes replays harmless and supports later response-speed calculations without noisy updates.

**Alternatives considered**:

- Update `ReadAtUtc` on every open: rejected because it destroys first-open evidence.
- Charge on read/open: rejected by the MVP payment rule.
- Write financial audit records for every read: rejected because clarification limited audit evidence to settlement and settlement-blocking events.

## Decision: Require Client `Idempotency-Key` for Interactions

**Decision**: `POST /api/doctor/messages/{deliveryId}/interact` requires a client `Idempotency-Key`. The raw key is transient request input only; persistent interaction evidence, logs, audit records, diagnostics, responses, and task evidence store or display only normalized hashes/fingerprints or non-sensitive conflict categories. Same key, same delivery, same outcome, and matching normalized feedback returns the original result. Same key with different content conflicts. A different key for an already settled delivery is allowed only when it matches the persisted final outcome and feedback; otherwise it conflicts.

**Rationale**: The backend plan requires idempotency keys for charge and earn operations. Binding request replay to a client key plus normalized request fingerprint gives deterministic retry behavior and lets contract tests distinguish replay from conflict.

**Alternatives considered**:

- Delivery-only idempotency: rejected because it cannot distinguish a retry from an accidental different feedback/outcome submission.
- Optional key: rejected because financial retry behavior would vary by caller.
- Raw key storage: rejected because raw idempotency material must not leak into persistence, logs, audit, diagnostics, responses, or task evidence.

## Decision: One Atomic Settlement per Delivery Interaction

**Decision**: Accept and Reject both settle the stored reservation exactly once inside one Unit of Work transaction. The operation changes delivery status/reservation, stores interaction/feedback evidence, debits company Reserved, credits Doctor Available, writes Charge/Earn transaction and ledger evidence, records platform-fee evidence, and creates required safe audit evidence.

**Rationale**: The feature moves money, so partial delivery, wallet, ledger, or audit state would be worse than a failed request. Keeping settlement in one service-owned transaction satisfies constitution wallet determinism and makes failure injection tests clear.

**Alternatives considered**:

- Split company charge and doctor earn into separate commits: rejected because it can strand money between company and doctor.
- Publish settlement for asynchronous wallet processing: rejected for MVP because it complicates exactly-once guarantees and recovery.
- Recalculate current doctor price/fee policy at interaction time: rejected because Phase 7 snapshots are authoritative.

## Decision: Stable Lock Order for Concurrent Settlement

**Decision**: Interaction settlement locks the delivery first, then company wallet, then doctor wallet, then checks/stages idempotency, wallet transactions, ledger entries, and audit evidence.

**Rationale**: The delivery is the unique business aggregate for the interaction, and wallet rows are the mutable financial resources. A documented lock order plus state rechecks prevents duplicate settlement under overlapping requests and reduces deadlock risk.

**Alternatives considered**:

- Lock wallets before delivery: rejected because ineligible deliveries would lock financial rows unnecessarily.
- Rely on optimistic concurrency alone: rejected because duplicate financial effects require stronger serialization around wallets and transactions.

## Decision: Charge/Earn Ledger Shape Uses Existing Wallet Concepts

**Decision**: Charge debits company Reserved by stored `ReservedAmount`; Earn credits Doctor Available by stored `DoctorEarnings`. Both write immutable wallet transactions and ledger entries with deterministic delivery-scoped keys. Platform fee is recorded as distinct settlement evidence but does not require a platform wallet in Phase 8.

**Rationale**: Existing Phase 7 activation already moved company Available to Reserved and stored the fee/earning split. Interaction only finalizes the reserved money and credits the doctor. Platform-fee evidence is needed for reporting but a platform wallet is not required by the current plan.

**Alternatives considered**:

- Credit company Available during charge: rejected because the reserved amount is meant to be consumed, not released.
- Add a platform wallet now: rejected as an accounting expansion outside Phase 8.
- Use a single combined transaction for charge and earn: rejected because company and doctor wallet histories need distinct owner records.

## Decision: Feedback Normalization and Validation

**Decision**: Feedback is optional plain text. Provided feedback is trimmed before validation. Omitted, empty, and whitespace-only feedback are allowed. Ordinary letters, numbers, whitespace, line breaks, and punctuation are allowed except markup delimiters. Feedback containing literal `<` or `>`, encoded angle brackets `&lt;` or `&gt;` in any casing, Markdown links/images, `javascript:` or `data:` URI schemes, or feedback longer than 2,000 characters after trimming blocks settlement and creates no mutation. Stored feedback with at least 15 non-whitespace characters is marked eligible for later feedback-score credit.

**Rationale**: This preserves the billable Accept/Reject workflow without forcing doctors to write comments, while keeping text abuse and storage predictable. The 15-character marker aligns with the backend plan's later activity-score formula.

**Alternatives considered**:

- Require feedback for Reject: rejected because not in the locked MVP rule.
- Silently drop invalid feedback and settle: rejected because it hides client errors and can surprise users.
- Store invalid feedback as pending review: rejected because it expands moderation scope.

## Decision: Audit Only Financially Meaningful or Anomalous Events

**Decision**: Successful settlements, idempotency conflicts, and reservation/snapshot anomalies create safe audit evidence. Ordinary successful read tracking does not create financial audit records.

**Rationale**: The backend plan requires financial events to be audit logged. Read tracking is explicitly non-financial and would create high-volume low-value audit noise.

**Alternatives considered**:

- Audit every read and interaction attempt: rejected as noisy and unnecessary for financial reconciliation.
- Audit only successful settlement: rejected because idempotency conflicts and anomalies are important fraud/debug evidence.

## Decision: Apply Doctor Interaction Rate Limiting to Read and Interact

**Decision**: Both read tracking and Accept/Reject interaction actions use the Doctor interaction rate-limit protection. Throttled requests produce safe envelopes and no domain/financial mutation.

**Rationale**: Both actions are Doctor message actions that can be abused for scraping, load, or repeated interaction attempts. Using the existing policy avoids inventing a new limit while satisfying the backend plan's interaction-endpoint rate limiting rule.

**Alternatives considered**:

- Limit only Accept/Reject: rejected because read tracking also touches protected message state.
- Limit only failed/conflicting attempts: rejected because high-volume valid reads can still create load.
- Defer to planning: rejected because rate-limit scope changes tests and controller attributes.

## Decision: Current-Day Eligibility Uses Captured Cairo Snapshot

**Decision**: Read tracking and interaction use one captured DST-aware `Africa/Cairo` business-date snapshot per request. Active deliveries from prior or future Egypt dates are not interactable.

**Rationale**: Phase 7 already made current-day visibility authoritative. Phase 8 must not let doctors settle stale messages because expiry was delayed.

**Alternatives considered**:

- Use UTC date or host-local date: rejected because it conflicts with the backend plan time boundary.
- Allow interaction while Active regardless of delivery date: rejected because day-end expiry is a locked business rule.

## Decision: Performance Profile Reuses SQL Server Testcontainers

**Decision**: Measure warmed read and interaction requests under the same style as Phase 7: Release build, Server GC, SQL Server 2022 Testcontainers, dedicated CPU/RAM/storage prerequisites, and explicit skipped-prerequisite reporting.

**Rationale**: Interaction settlement includes SQL row locks and wallet writes, so performance evidence must use the real SQL-backed path rather than in-memory tests.

**Alternatives considered**:

- Unit-only performance timing: rejected because it misses SQL locking and transaction cost.
- Make performance tests mandatory on every local run: rejected because the profile requires dedicated resources.
