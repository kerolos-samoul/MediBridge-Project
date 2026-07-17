# Phase 0 Research: Admin Tools (Phase 11)

## Decision: Consolidate Existing Admin Workflows Through a Work Queue

**Decision**: Implement `GET /api/admin/work-queue` as a read-model aggregator over existing pending account, protected file, campaign review, enforcement, and withdrawal sources. Do not replace current decision endpoints.

**Rationale**: The backend already has separate Admin account, file, campaign, pricing, fee-policy, delivery-job, and activity-enforcement surfaces. A queue aggregator gives admins one triage surface while preserving tested workflow boundaries and reducing rework.

**Alternatives considered**:

- Replace all admin endpoints with one generic decision API: rejected because it would blur domain-specific validation, increase regression risk, and weaken contract clarity.
- Keep only separate existing lists: rejected because the Phase 11 requirement asks for a consolidated operational view.

## Decision: Reuse Existing Decision Workflows and Harden Gaps

**Decision**: Treat account approval, file review, campaign moderation, pricing, fee policy, and enforcement actions as existing workflows to reuse or harden. Phase 11 tasks should add missing audit, concurrency, DTO privacy, and work-queue projection coverage where gaps exist.

**Rationale**: Earlier phases already implemented these workflows. Reusing them keeps scope strict and avoids duplicate state machines.

**Alternatives considered**:

- Reimplement all admin workflows under a new service: rejected as unnecessary and risky.
- Limit Phase 11 only to new payouts/statistics: rejected because the backend plan requires Admin controls to be coherent and queue-visible.

## Decision: Separate Pricing Deactivation From Monetary Price

**Decision**: Add a distinct pricing-deactivation action and active/inactive pricing state. Do not represent inactive pricing with null, zero, or another numeric price.

**Rationale**: Monetary validation remains simple and historical delivery snapshots stay explainable. A separate action also makes paid campaign eligibility and audit history explicit.

**Alternatives considered**:

- Null price means inactive: rejected because null already risks ambiguous missing-data semantics.
- Zero price means inactive: rejected because zero can leak into financial calculations and conflicts with positive price validation.
- Either null or zero means inactive: rejected because two inactive encodings complicate tests and reporting.

## Decision: Withdrawal Requests Create Pending Holds

**Decision**: A valid withdrawal request atomically moves the requested amount from withdrawable available earnings into a pending withdrawal hold. Rejection and eligible failure release the hold; paid finalizes it.

**Rationale**: A hold prevents the same doctor earnings from being requested twice while an admin decision or payout stub action is pending. It gives deterministic wallet invariants for retries and concurrent attempts.

**Alternatives considered**:

- Do not hold funds until admin approval: rejected because concurrent requests could overdraw withdrawable earnings.
- Hold only after approval: rejected because Requested withdrawals would not reserve the requested amount.
- Mark requests only with no wallet effect: rejected because wallet availability would be misleading.

## Decision: Payout Stub Only, No Payout Destination Data

**Decision**: Phase 11 records payout status and payout reference only. It does not collect bank account, card, mobile wallet, saved payout method, or free-text payout destination data.

**Rationale**: The feature is a payout-management stub, not a regulated payout integration. Avoiding destination data reduces privacy/compliance scope while still allowing admins to track out-of-system payout references.

**Alternatives considered**:

- Free-text payout destination: rejected because it would collect sensitive payment information without provider controls.
- Saved payout methods: rejected as a separate product area requiring verification, lifecycle, masking, and compliance work.
- Real bank transfer integration: rejected by specification scope.

## Decision: Admin Statistics Are Live, Read-Only, and Financially Conservative

**Decision**: Admin statistics are computed from committed source evidence for a bounded period. If financial evidence is inconsistent, affected financial totals are withheld and flagged while unrelated non-financial totals are returned.

**Rationale**: Admins need operational visibility, but questionable money totals must not be presented as authoritative. Returning non-financial totals keeps the endpoint useful during financial evidence issues.

**Alternatives considered**:

- Return flagged financial totals: rejected because users may still treat them as real totals.
- Fail the entire statistics response: rejected because it hides unrelated operational data.
- Exclude inconsistent rows silently: rejected because it creates misleading totals.

## Decision: Authorization Boundaries

**Decision**: Admin tools require Admin JWT authorization. Doctor withdrawal submission and own withdrawal listing require Doctor JWT plus owner, approved account, active status, and non-suspended status for new requests.

**Rationale**: Admin capabilities expose operational data across users. Doctor withdrawal submission is the one non-admin capability and must be strictly owner-scoped and eligibility-checked.

**Alternatives considered**:

- Allow suspended doctors to request withdrawals: rejected because new payout requests should require trusted active state.
- Admin manually decides eligibility after any doctor request: rejected because it creates avoidable pending work and potential over-hold ambiguity.

## Decision: Idempotency and Concurrency Strategy

**Decision**: Use existing concurrency tokens and transactional Unit of Work boundaries for withdrawal transitions. If request idempotency keys are introduced for doctor submission or admin payout actions, raw keys must not be stored, logged, audited, or returned.

**Rationale**: The codebase already has optimistic concurrency and idempotency safety patterns for campaigns and doctor interactions. Withdrawal and payout actions need at-most-once financial effects under retry and concurrent attempts.

**Alternatives considered**:

- Rely only on client retries without server idempotency/concurrency guards: rejected because wallet effects must be deterministic.
- Store raw idempotency keys: rejected due to existing security posture.

## Decision: Date Windows and Pagination

**Decision**: Use `PageNumber`/`PageSize` pagination with default 20 and maximum 100 for admin list endpoints. Admin statistics date windows are capped at 90 days and default to the latest 90 days ending on the current Egypt business date when omitted.

**Rationale**: This matches Phase 10 reporting bounds and the platform's existing pagination conventions while bounding expensive operational reads.

**Alternatives considered**:

- Unbounded statistics: rejected because it risks slow queries and large responses.
- Cursor pagination for all admin lists: deferred because existing admin endpoints use page-number semantics and Phase 11 does not need a new pagination model.

## Decision: Performance Profile

**Decision**: Validate work queue, withdrawal list, and statistics endpoints against a warmed p95 target under 2 seconds for a 90-day seeded operational dataset.

**Rationale**: Admin tooling must remain usable under realistic operating volume, and the target aligns with Phase 10 reporting expectations.

**Alternatives considered**:

- No performance profile: rejected because statistics and cross-domain queue reads can become expensive.
- Sub-second global target: rejected as unnecessary for back-office admin operations.
