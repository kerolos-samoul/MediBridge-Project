# Research: Wallet and Campaign Workflow

## Decision: Use Mock Payment Checkout for Company Top-Up

**Rationale**: The graduation project needs a demonstrable wallet and campaign funding workflow without depending on Paymob, Stripe, third-party redirects, callback URLs, webhooks, provider credentials, or provider configuration. A mock payment record that automatically succeeds lets smoke tests exercise wallet creation, idempotency, transaction, ledger, and audit behavior exactly as a successful real payment would.

**Alternatives considered**:

- Real payment gateway integration: rejected because it adds external credentials, callbacks, operational setup, and failure modes outside the feature goal.
- Admin-only manual credit: rejected because companies need to demonstrate a self-service checkout/pay workflow.
- In-memory fake payment only: rejected because top-ups require persisted payment evidence and auditability.

## Decision: Ensure Company Wallet on Approval and First Use

**Rationale**: Newly approved companies should be able to query and top up their wallets immediately, while legacy or partially migrated approved companies need self-healing behavior. Creating on approval is the steady-state path; first wallet query/top-up atomically creates the missing active wallet as recovery.

**Alternatives considered**:

- Approval-only wallet creation: rejected because older approved company records could still return 404.
- Top-up-only wallet creation: rejected because wallet query should also work immediately after approval.
- Manual data repair: rejected because the public workflow must be smoke-testable without manual seeding beyond account fixtures.

## Decision: Top-Up Idempotency Uses Company, Operation Type, and Idempotency Key with Request Payload Conflict Checks

**Rationale**: Phase 3 already defines wallet transaction uniqueness by operation and idempotency key. For mock top-up payment records, the service layer scopes replay lookup by company plus idempotency key, compares only client-supplied request fields (`amount` and `currency`), and returns the original result including the stored transaction reference for identical retries. Internally generated transaction references are audit/payment evidence and are not replay request inputs.

**Alternatives considered**:

- Globally unique idempotency key only: rejected because independent companies could accidentally reuse the same key and should not conflict with each other.
- Always reject duplicate keys: rejected because retry-safe top-up replay should be client-friendly after timeouts.
- Allow duplicate payment records with same key: rejected because it risks double crediting.

## Decision: Campaign Submission Captures Target and Price Snapshots and Reserves Funds

**Rationale**: Submission is the first point where approved assets, eligible priced doctors, cost estimate, platform fee policy, and company wallet sufficiency are all known. Capturing snapshots and reserving funds at submission prevents admin review from depending on mutable doctor prices or wallet balance changes.

**Alternatives considered**:

- Reserve funds at draft creation: rejected because drafts may not have assets or final targets.
- Reserve funds at admin approval only: rejected because companies could submit unfunded campaigns and create review churn.
- Recalculate prices at approval: rejected because later doctor price changes would alter an already-submitted campaign.

## Decision: Admin Doctor Price Updates Are Positive-Only

**Rationale**: The clarification outcome requires admin price updates to accept only positive EGP amounts with at most two decimals. Null, zero, negative, or over-precise submissions are validation failures; doctors without a valid positive price remain ineligible for targeting.

**Alternatives considered**:

- Null or zero as an unprice action: rejected by clarification.
- Separate clear-price action in this feature: rejected because it is not required for the smoke workflow.

## Decision: Queue Creation Occurs on Admin Campaign Approval

**Rationale**: Approval is the point where content moderation is complete. Queue rows are created exactly once per approved campaign and target doctor, ordered by campaign submission time with the stable queue identifier as tie-breaker. Retries of approval return existing results without duplicating review history, wallet reservation, or queue rows.

**Alternatives considered**:

- Queue on submission: rejected because campaign content may still be rejected.
- Queue on asset approval: rejected because campaign-level review may still fail.
- Background job only: rejected because smoke tests need deterministic HTTP verification of queue creation.

## Decision: Queue Verification Visibility Is Admin Rows and Company Aggregates

**Rationale**: Admins need doctor-level queue rows to verify moderation and queue creation. Companies need enough visibility to confirm their own campaign was queued, but should not receive doctor-level queue internals. Aggregate counts by status for owned campaigns satisfy smoke testing and privacy.

**Alternatives considered**:

- Company doctor-level queue rows: rejected to avoid unnecessary exposure of doctor-level delivery internals.
- Admin-only queue visibility: rejected because companies need aggregate campaign status after approval.

## Decision: Campaign Asset Review Is Business Review State Only

**Rationale**: The workflow blocker is that submission requires an approved campaign asset. This phase can use existing stored-file metadata and review statuses to model upload and moderation. File content malware scanning, real storage-provider concerns, and retrieval behavior remain outside scope.

**Alternatives considered**:

- Full storage and scanning pipeline: rejected as beyond the smoke workflow.
- Submission without asset approval: rejected because it conflicts with the existing moderation rule.

## Decision: Atomic Boundaries Follow Business Actions

**Rationale**: Wallet/payment money effects, submission target snapshots and reservation, campaign review with queue creation, and rejection/cancellation release behavior must each commit or roll back together. The existing `IDomainUnitOfWork.ExecuteInTransactionAsync` pattern should be used for these grouped operations.

**Alternatives considered**:

- Independent saves per record type: rejected because partial failures could credit wallets without ledger evidence or approve campaigns without queue rows.
- Controller-managed transaction scopes: rejected by the constitution; orchestration belongs in services and persistence boundaries belong in Repository/UoW.
