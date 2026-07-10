# Research: Phase 8 Interaction & Payments

## Decision: Extend the existing Doctor messages API surface

**Rationale**: Phase 7 already exposes doctor current-day messages and delivery-scoped asset access under `api/doctor/messages`. Read tracking and interaction settlement are natural mutations of the same doctor-owned delivery resource, and keeping them on `DoctorMessagesController` preserves route consistency and OpenAPI discoverability.

**Alternatives considered**:

- Create a separate `DoctorInteractionsController`: rejected because it splits one delivery workflow across controllers without improving boundaries.
- Put interaction on a company or wallet controller: rejected because the actor and authorization owner is the Doctor, even though wallet effects touch both parties.

## Decision: Read tracking is naturally idempotent and non-financial

**Rationale**: The backend plan separates `ReadAtUtc` from Accept/Reject. Read tracking should set the first read timestamp only, preserve it on retries, and create no wallet transaction, settlement state, or earnings. This avoids turning a UI display action into a billable event.

**Alternatives considered**:

- Require an idempotency key for read: rejected because first-write-wins by delivery and Doctor owner is sufficient and simpler to test.
- Update read timestamp on every open: rejected because it destroys the first-open audit signal.
- Treat interaction as implicit read: rejected by clarification; settlement must not mutate `ReadAtUtc`.

## Decision: Interaction requires the existing `Idempotency-Key` header convention

**Rationale**: MediBridge already uses `Idempotency-Key` for financial mutations and validates keys to 8-128 characters. Reusing the header enables consistent OpenAPI documentation, contract tests, replay handling, and operator expectations. The interaction operation scope is Doctor + delivery + normalized key + normalized request payload.

**Alternatives considered**:

- Derive idempotency only from delivery id: rejected because it cannot distinguish safe client retries from conflicting request payloads as explicitly as the existing financial-mutation pattern.
- Make the key optional: rejected because optional behavior would create two replay models and increase contract/test ambiguity.

## Decision: Record interaction operation evidence separate from wallet transactions when needed

**Rationale**: Wallet transaction unique keys protect Charge and Earn effects, but client replay classification also needs the client key, Doctor actor, selected decision, and normalized feedback. A small interaction operation record or equivalent repository-backed evidence lets the service return same-result replays and reject conflicting replays before writing duplicate financial effects.

**Alternatives considered**:

- Store the client idempotency key only on both wallet transactions: rejected because the wallet transaction uniqueness is by operation type and key, and one interaction has two operation types.
- Store replay evidence only in audit logs: rejected because audit logs are not the right consistency boundary for financial mutation idempotency.
- Add fields directly to `DoctorAdDelivery`: acceptable only if it can support unique Doctor/delivery/key replay classification cleanly; otherwise the dedicated evidence record is clearer.

## Decision: Accept and Reject share identical settlement

**Rationale**: The locked billing rule says Accept or Reject is a billable interaction. Both outcomes convert the existing reservation into final company charge and doctor earnings. The only difference is the delivery final status and optional feedback.

**Alternatives considered**:

- Charge only Accept: rejected because it contradicts the backend plan.
- Credit different doctor earnings for Reject: rejected because snapshots already define one price, one fee, and one earnings amount per delivery.

## Decision: Use stored activation snapshots for all monetary values

**Rationale**: Phase 7 snapshots price, fee percent, rounded fee amount, doctor earnings, and reserved amount at activation. Phase 8 must validate and use those values rather than rereading current pricing or fee policy, so later admin changes cannot alter already delivered messages.

**Alternatives considered**:

- Recalculate fee at interaction time: rejected because it can create mismatches with the reserved amount and violates snapshotting.
- Use the current doctor price at interaction time: rejected because activation is the reservation point and stores the authoritative price.

## Decision: Deterministic Charge/Earn keys are delivery-scoped

**Rationale**: Existing Phase 7 keys are `delivery:reserve:{deliveryId}` and `delivery:release:{deliveryId}`. Adding `delivery:charge:{deliveryId}` and `delivery:earn:{deliveryId}` keeps financial idempotency deterministic, replayable, and independent of raw client keys while still linking replay evidence to the normalized `Idempotency-Key`.

**Alternatives considered**:

- Use the client key directly as wallet transaction idempotency key: rejected because one interaction creates two financial operations and client keys should not leak into logs/ledger material.
- Use random transaction keys: rejected because retries would not be deterministic.

## Decision: Company Charge debits Reserved and Doctor Earn credits Available

**Rationale**: The wallet state machine says interaction Charge decreases company Reserved and Earn increases doctor Available. The platform fee is retained as the difference between the charged reserved amount and doctor earnings; no separate Phase 8 wallet movement is needed unless a future platform wallet is introduced.

**Alternatives considered**:

- Move company Reserved back to Available before charging: rejected because it creates unnecessary intermediate state and more failure points.
- Create a platform wallet credit in Phase 8: rejected as out of current scope and not present in the existing wallet model.

## Decision: Lock order is delivery, company wallet, doctor wallet, replay evidence, transaction/ledger writes

**Rationale**: The delivery row determines eligibility and contains company/doctor identifiers and snapshots. Locking it first prevents two settlements from progressing on the same delivery. Wallet locks then serialize balance changes, and transaction/ledger writes can rely on already rechecked state.

**Alternatives considered**:

- Lock wallets before delivery: rejected because wallet lookup depends on delivery ownership and increases the chance of holding wallet locks for ineligible deliveries.
- Rely only on optimistic concurrency: rejected because settlement is financial and should use explicit update locks plus rechecks.

## Decision: Current Egypt business date gates read and interaction

**Rationale**: Phase 7 established that doctors see only current Egypt-day deliveries, and overdue Active deliveries are expiry-job responsibility. Doctor actions after the business date changes must not settle stale messages even if expiry has not yet processed them.

**Alternatives considered**:

- Allow interaction until expiry job runs: rejected because it extends visibility/actionability past the delivery day.
- Use host local date: rejected because the project uses DST-aware `Africa/Cairo`.

## Decision: Feedback is optional, trimmed, and capped at 1,000 characters

**Rationale**: Clarification selected optional feedback with empty-after-trim treated as absent and a 1,000-character stored limit. That supports useful doctor context without making settlement depend on feedback or over-expanding storage/contracts. The later 15-character scoring threshold does not affect Phase 8 acceptance.

**Alternatives considered**:

- Require feedback on Reject: rejected because the spec says feedback is optional.
- Store exact whitespace: rejected because normalized replay and reporting behavior should be predictable.
- Allow 4,000 characters because the existing column does: rejected because clarified product limit is 1,000.

## Decision: Safe audit records are diagnostic, not idempotency authority

**Rationale**: Audit events should prove successful reads, settlements, replays, conflicts, and consistency failures without exposing idempotency material or balances. Financial correctness remains in domain state, transaction records, ledger entries, and interaction operation evidence.

**Alternatives considered**:

- Use audit rows to decide replay outcomes: rejected because audit is not designed as a transactional consistency boundary.
- Log full payloads or raw idempotency keys: rejected by security and privacy requirements.

## Decision: Performance validation uses point reads and concurrency replay scenarios

**Rationale**: Phase 8 endpoints are point mutations, so performance risk is lock duration, bounded lookup count, and contention behavior rather than pagination. The profile focuses on eligible reads/interactions and duplicate/conflict concurrent retries.

**Alternatives considered**:

- Reuse Phase 7 inbox performance profile only: rejected because settlement has different wallet and transaction behavior.
- Skip performance evidence: rejected because the spec defines measurable latency outcomes.
