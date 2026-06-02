# Feature Specification: Database & Core Models (Phase 3)

**Feature Branch**: `[003-database-core-models]`  
**Created**: 2026-06-01  
**Status**: Draft  
**Input**: User description: "Phase 3: Database & Core Models in backend plan"

## Clarifications

### Session 2026-06-01

- Q: What uniqueness scope should wallet transaction idempotency use? -> A: Store idempotency as `OperationType + IdempotencyKey`, unique together.
- Q: How should invalid or corrected audit/history records be handled? -> A: Audit/history is append-only; corrections use new records.
- Q: How should soft-deleted records behave in normal data access? -> A: Soft-deleted records keep relationships and are excluded from active queries.
- Q: How should monetary inputs with more than two decimal places be handled? -> A: Reject monetary inputs with more than 2 decimals.
- Q: Should persistent campaign analytics read models be included in Phase 3? -> A: Defer reporting read models to Phase 10.
- Q: How are constitution-required `Refund` and `Withdraw` wallet semantics represented? -> A: `Refund` is an explicit transaction type. `Withdraw` is represented by the workflow `WithdrawRequest -> WithdrawApproved / WithdrawRejected -> WithdrawPayout`; `WithdrawPayout` is the final ledger-impacting withdrawal transaction.
- Q: What validation surface should Phase 3 use? -> A: Repository, integration, migration, and service-boundary tests only; Phase 3 must not add a public or diagnostic controller.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persist Core Marketplace Records (Priority: P1)

As a backend engineer, I need the system to reliably store the core MediBridge records for doctors, companies, campaigns, queues, deliveries, wallets, withdrawals, files, policies, and audits so later workflows can build on consistent business data.

**Why this priority**: Every later campaign, delivery, wallet, reporting, and admin workflow depends on these records existing with clear ownership and lifecycle fields.

**Independent Test**: Apply the initial data model to a clean backend environment, create sample records for each core entity, and confirm they can be saved and read through the approved application boundaries.

**Acceptance Scenarios**:

1. **Given** a clean backend environment, **When** the Phase 3 data model is applied, **Then** the system has persistent records for identity, doctors, companies, campaigns, campaign targets, queue items, deliveries, wallets, wallet transactions, withdrawal requests, stored files, policy history, review history, and audit history.
2. **Given** a sample doctor, company, and campaign, **When** records are created for targeting and delivery preparation, **Then** all required ownership and relationship links are present for later authorization and reporting.
3. **Given** a record with financial history or audit importance, **When** a delete-style action is requested by later workflows, **Then** the record can be marked inactive without erasing historical evidence and is excluded from normal active-record queries.

---

### User Story 2 - Protect Queue, Delivery, and Wallet Integrity (Priority: P1)

As a platform operator, I need queue ordering, delivery uniqueness, wallet balances, and wallet transactions to remain consistent so money and message delivery cannot drift under retries or concurrent processing.

**Why this priority**: Phase 3 establishes the invariants that later delivery jobs and interaction workflows rely on for correctness.

**Independent Test**: Create representative queue, delivery, and wallet transaction records, then verify duplicate deliveries, duplicate idempotent transactions, invalid money precision, and out-of-order queue reads are rejected or ordered as required.

**Acceptance Scenarios**:

1. **Given** multiple queued campaign messages for the same doctor, **When** later delivery workflows request pending items, **Then** items are ordered by `QueuedAtUtc ASC` and then `Id ASC`, where `QueuedAtUtc` is the persisted FIFO key derived from campaign submission time or queue insertion time.
2. **Given** an active delivery already exists for a doctor, campaign, and Egypt delivery date, **When** a duplicate delivery is attempted, **Then** the system prevents the duplicate.
3. **Given** a wallet balance change is recorded, **When** the change is committed, **Then** an append-only wallet transaction and immutable wallet ledger entries exist in the same completed operation.
4. **Given** a retried financial operation with the same operation type and idempotency key, **When** the operation is submitted again, **Then** the system prevents duplicate financial effect.
5. **Given** a delivery fails, is cancelled, or is not delivered, **When** reserved or charged company funds must be returned, **Then** a `Refund` transaction records the return without mutating prior ledger evidence.
6. **Given** a doctor withdraws earnings, **When** the withdrawal completes, **Then** the ledger shows `WithdrawRequest`, `WithdrawApproved` or `WithdrawRejected`, and final `WithdrawPayout` behavior without using a vague standalone `Withdraw` transaction type.

---

### User Story 3 - Enforce Application Data Boundaries (Priority: P2)

As a backend engineer, I need all application code to access persisted business data through approved repository and unit-of-work boundaries so controllers and services do not bypass the domain model.

**Why this priority**: Clean data boundaries keep later phases maintainable and protect the project constitution from architecture drift.

**Independent Test**: Inspect project references and application code paths to confirm persistence implementation details are isolated and application use cases access persisted records only through the approved abstractions.

**Acceptance Scenarios**:

1. **Given** a service needs to save or query a core entity, **When** it accesses persistence, **Then** it uses repository and unit-of-work abstractions rather than infrastructure-specific objects.
2. **Given** an API controller handles a request, **When** it delegates work, **Then** it remains HTTP-focused and does not perform direct persistence operations.
3. **Given** domain records are defined, **When** they are inspected, **Then** core business types do not depend on HTTP or persistence infrastructure.

---

### User Story 4 - Capture Policy and Audit History (Priority: P2)

As an admin or compliance reviewer, I need price, fee, review, file, authentication-sensitive, financial, and admin action history to be retained so important decisions can be explained later.

**Why this priority**: MediBridge handles approvals, promotional content, and financial events, all of which need traceability before production workflows expand.

**Independent Test**: Record sample history events for price changes, platform fee changes, campaign review, file review, authentication-sensitive actions, financial events, and admin actions, then confirm they remain queryable and linked to the relevant actor or business record.

**Acceptance Scenarios**:

1. **Given** an admin changes a doctor price or platform fee policy, **When** the change is recorded, **Then** the previous and new policy facts are available for audit.
2. **Given** a campaign or file review decision is made, **When** the decision is recorded, **Then** the reviewer, decision, reason, and time are retained.
3. **Given** an authentication-sensitive, financial, or admin event occurs, **When** it is logged, **Then** the event includes enough metadata for support and compliance review without exposing secrets.
4. **Given** an audit or history record needs correction, **When** the correction is made, **Then** the original record remains unchanged and a new correction record explains the change.

### Edge Cases

- A doctor has no configured price or a price of zero; the data model must support this state so later filtering and delivery workflows can exclude the doctor.
- Two queue items have the same queued time for one doctor; a stable identifier must break the tie deterministically.
- Queue ordering must not include priority behavior in Phase 3; carry-over items that exceed a daily limit remain FIFO by `QueuedAtUtc ASC, Id ASC` for the next delivery date.
- A delivery activation is retried after a partial failure; uniqueness and idempotency rules must prevent duplicate delivery or duplicate reservation effects.
- A wallet operation attempts more than two decimal places; the system must reject the monetary value rather than rounding or truncating it.
- A duplicate idempotency key is retried for campaign charging, doctor earning, refunds, top-ups, or withdrawal payout; the retry must not double-charge, double-earn, double-refund, double-top-up, or double-pay.
- A withdrawal request is updated while another actor is reviewing it; concurrency protection must prevent lost decisions.
- A withdrawal is rejected after funds were reserved; reserved money must return to the doctor's available balance.
- A soft-deleted doctor, company, campaign, or wallet still has historical financial or audit relationships; history must remain available while normal active-record queries exclude the soft-deleted record.
- An audit, policy, review, activity, or financial history event is later found to be incorrect; the original record must remain intact and a new correction or reversal record must explain the change.
- A file record represents a private verification document; stored metadata must support private access and review status without making the file public.
- An audit event is created without an authenticated actor, such as a system job or public auth attempt; the event must still be attributable to a system or anonymous source.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST define persistent core records for users, doctors, companies, campaigns or advertisements, campaign targets, campaign reviews, doctor message queues, doctor ad deliveries, wallets, wallet transactions, wallet ledger entries, withdrawal requests, stored files, policy history, activity history, and audit history.
- **FR-002**: System MUST represent and preserve the Doctor, Company, Admin role model and account approval state needed by secured future workflows.
- **FR-003**: System MUST represent and preserve refresh-token records with expiration, revocation state, creation time, user relationship, and replacement tracing for token rotation.
- **FR-004**: System MUST represent doctor profile data including specialization, experience, location, daily message limit, minimum weekly requirement, requested preference changes, activity score, status, price per message, and soft-delete state.
- **FR-005**: System MUST represent company profile data including name, license information, verification metadata, and soft-delete state.
- **FR-006**: System MUST represent campaign records with owning company, content metadata, clinical research information, reviewable status, creation time, and soft-delete state.
- **FR-007**: System MUST represent campaign target records that connect campaigns to doctors and retain targeting snapshots for specialization, experience, location, activity score, and price.
- **FR-008**: System MUST represent campaign review history with campaign, reviewer, decision, notes or reason, and decision time.
- **FR-009**: System MUST represent per-doctor queue items with doctor, campaign, status, `QueuedAtUtc`, and deterministic FIFO ordering by `QueuedAtUtc ASC` then `Id ASC` within each doctor/status query. `QueuedAtUtc` is the persisted FIFO ordering key derived from campaign submission time or queue insertion time. Phase 3 MUST NOT add priority queue behavior.
- **FR-010**: System MUST represent doctor ad deliveries with doctor, campaign, company, Egypt delivery date, delivered time, read time, status, interaction time, optional feedback, price snapshot, platform fee snapshot, fee amount, doctor earnings, reservation amount, reservation status, and concurrency protection.
- **FR-011**: System MUST prevent duplicate active delivery records for the same doctor, Egypt delivery date, and campaign.
- **FR-012**: System MUST represent wallet records for Doctor, Company, and Platform owners with `OwnerUserId`, `OwnerType`, available balance, reserved balance, EGP currency, owner relationship, and soft-delete state.
- **FR-012A**: System MUST preserve relationships for soft-deleted records while excluding them from normal active-record queries by default.
- **FR-013**: System MUST represent wallet transactions as append-only records with wallet, transaction type, amount, related delivery when applicable, idempotency key, creation time, and descriptive metadata. The canonical Phase 3 transaction types are `TopUp`, `Reserve`, `Release`, `Charge`, `Earn`, `Refund`, `WithdrawRequest`, `WithdrawApproved`, `WithdrawRejected`, and `WithdrawPayout`.
- **FR-013A**: System MUST represent immutable `WalletLedgerEntry` records for each balance movement with wallet transaction, wallet, debit/credit direction, amount, available/reserved balance type, EGP currency, creation time, relevant references such as campaign, delivery, doctor, company, or withdrawal request, and idempotency key where needed.
- **FR-013B**: System MUST model `Withdraw` as a withdrawal workflow: `WithdrawRequest -> WithdrawApproved / WithdrawRejected -> WithdrawPayout`. `WithdrawPayout` is the final ledger-impacting withdrawal transaction; Phase 3 MUST NOT introduce a vague standalone `Withdraw` transaction type.
- **FR-013C**: System MUST model refunds explicitly. If money is still reserved, refund behavior moves company reserved balance back to available balance. If money was already charged, a `Refund` transaction returns the amount with compensating ledger entries.
- **FR-014**: System MUST prevent duplicate wallet transactions for the same operation type and idempotency key combination, including retriable campaign charging, doctor earning, refunds, top-ups, and withdrawal payout.
- **FR-015**: System MUST store all monetary values for MediBridge financial records in Egyptian pounds with two-decimal precision.
- **FR-015A**: System MUST reject monetary inputs with more than two decimal places rather than rounding or truncating them.
- **FR-016**: System MUST represent withdrawal requests with doctor, amount, status, admin decision metadata, payout status where applicable, and concurrency protection.
- **FR-017**: System MUST represent stored file metadata with owner type, owner id, purpose, original file name, content type, size, storage key, visibility, review status, creation time, and review metadata.
- **FR-018**: System MUST represent policy and audit history for doctor price changes, platform fee policy changes, activity score snapshots, admin actions, authentication-sensitive events, file/document review actions, and financial events.
- **FR-018A**: System MUST keep audit, policy, review, activity, and financial history records append-only; corrections, reversals, or invalidations MUST be represented by new linked records rather than modifying or deleting the original record.
- **FR-019**: System MUST provide repository abstractions for querying and persisting core records without exposing persistence implementation details to controllers or application services.
- **FR-020**: System MUST provide a unit-of-work boundary so related changes, especially wallet balance changes, wallet transaction records, and wallet ledger entries, can be committed atomically.
- **FR-021**: System MUST ensure services can persist and query core records only through repository and unit-of-work abstractions.
- **FR-022**: System MUST keep controllers HTTP-only and prevent controllers from directly using persistence infrastructure.
- **FR-023**: System MUST validate Phase 3 through repository, integration, migration, and service-boundary tests only; Phase 3 does not add public or diagnostic API behavior.
- **FR-024**: System MUST preserve existing global exception handling and standard response-envelope behavior while avoiding new Phase 3 smoke or diagnostic controller paths.
- **FR-025**: System MUST support JWT and role-aware authorization requirements for future Doctor, Pharmaceutical Company, and Admin data access.
- **FR-026**: System MUST NOT implement campaign creation, queue injection jobs, delivery expiry jobs, doctor interaction settlement, wallet top-up endpoints, withdrawal endpoints, campaign moderation endpoints, file upload endpoints, reporting workflows, persistent reporting read models, public validation endpoints, or diagnostic controllers in Phase 3.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-018A` -> `MediBridge.Core`, `MediBridge.Repository`
  - `FR-019` to `FR-021` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`
  - `FR-022` to `FR-025` -> `MediBridge.APIs`, `MediBridge.Services`, `MediBridge.Core`
  - `FR-026` -> all layers preserve Phase 3 scope limits
- **CA-002 Controller Boundary**: Controllers remain HTTP-only. Phase 3 validation is performed by repository, integration, migration, and service-boundary tests only; no public or diagnostic controller is added.
- **CA-003 SQL Persistence Boundary**: Data requirements use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services must not depend on EF Core directly.
- **CA-004 Response Contract**: Existing API responses keep the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope. Phase 3 does not add validation or diagnostic API responses.
- **CA-005 Error Handling**: API-visible persistence and validation errors are handled by global exception middleware, with no raw stack traces exposed.
- **CA-006 Security**: Secured future flows require JWT and role-aware authorization for Doctor, Pharmaceutical Company, and Admin access; Phase 3 supplies the data foundation without broadening endpoint access.
- **CA-007 Ambiguity Control**: Queue and wallet behavior is deterministic for Phase 3 data modeling; no unresolved clarification markers remain.
- **CA-008 Queue Determinism**: Queueing scope is limited to persistent model rules: per-doctor FIFO by `QueuedAtUtc ASC` with `Id ASC` tie-break, queued/activated/cancelled status support, no priority behavior, and fields required for later daily limit, retry, expiry, and carry-over workflows.
- **CA-009 Wallet Determinism**: Walleting scope is limited to persistent model rules: Doctor/Company/Platform wallets, available and reserved balances, append-only transactions, immutable ledger entries, explicit `Refund`, canonical withdrawal workflow types (`WithdrawRequest`, `WithdrawApproved`, `WithdrawRejected`, `WithdrawPayout`), idempotency uniqueness by operation type plus idempotency key, two-decimal EGP precision, and atomic balance plus transaction plus ledger commits.

### Key Entities *(include if feature involves data)*

- **Application User**: Account identity with role, approval state, creation time, and soft-delete state.
- **Refresh Token**: Rotatable login credential record with expiration, revocation, and replacement tracing.
- **Doctor**: Medical professional profile with targeting attributes, platform-controlled limits, activity score, status, price, and deletion history.
- **Company**: Pharmaceutical company profile with license and verification metadata.
- **Campaign / Advertisement**: Promotional content owned by a company, with reviewable status and clinical research information.
- **Campaign Target**: Snapshot of a doctor selected for a campaign at targeting time.
- **Campaign Review History**: Admin decision trail for campaign moderation.
- **Doctor Message Queue**: Per-doctor pending campaign item used by later delivery activation.
- **Doctor Ad Delivery**: Daily active or completed campaign message for a doctor, including interaction and reservation facts.
- **Wallet**: Balance record for a Doctor, Company, or Platform owner, split between available and reserved EGP funds.
- **Wallet Transaction**: Append-only financial operation record for every wallet balance mutation, with duplicate protection by operation type plus idempotency key.
- **Wallet Ledger Entry**: Immutable debit/credit line item created by a wallet transaction against available or reserved balance, used as the detailed financial audit trail.
- **Withdrawal Request**: Doctor payout request and admin decision record.
- **Stored File**: Backend-controlled file metadata for verification documents, campaign media, voice notes, and clinical research attachments.
- **Policy History**: Historical record of doctor pricing and platform fee policy changes.
- **Activity Score History**: Daily or periodic record of computed doctor activity score snapshots.
- **Audit Event**: Trace record for admin, authentication-sensitive, financial, document review, and system events.
- **Correction Record**: Linked history entry that explains a correction, reversal, or invalidation while preserving the original audit/history event.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Initial schema setup completes successfully against a clean SQL Server environment in 100% of validation runs.
- **SC-002**: 100% of core entities listed in this specification can be created and read through repository and unit-of-work boundaries in automated validation.
- **SC-003**: Architecture checks find 0 controller references to persistence infrastructure and 0 service references to EF Core infrastructure types.
- **SC-004**: Duplicate delivery attempts for the same doctor, Egypt delivery date, and campaign are rejected in 100% of automated validation cases.
- **SC-005**: Duplicate wallet transaction attempts for the same operation type and idempotency key are rejected in 100% of automated validation cases, including charge, earn, refund, top-up, and withdrawal payout retries.
- **SC-006**: 100% of monetary fields sampled from wallet, delivery, fee, earning, withdrawal, and transaction records preserve two-decimal EGP precision, and 100% of sampled inputs with more than two decimal places are rejected.
- **SC-007**: Queue queries for a sampled doctor return queued items in FIFO order by `QueuedAtUtc ASC, Id ASC`, including same-timestamp tie-breaking and next-day carry-over ordering, in 100% of automated validation cases.
- **SC-008**: Wallet balance changes and their corresponding append-only transaction records and immutable ledger entries commit together or fail together in 100% of atomicity validation cases.
- **SC-009**: Soft-delete-capable records retain historical financial and audit relationships and are excluded from normal active-record queries in 100% of sampled deletion validation cases.
- **SC-010**: Audit and policy history validation confirms 100% of sampled price, fee, review, financial, authentication-sensitive, and admin events retain actor, target, decision or event type, and timestamp.
- **SC-011**: 100% of sampled audit/history correction scenarios preserve the original record and create a linked correction or reversal record.
- **SC-012**: Phase 3 migration validation confirms existing Identity roles (`Doctor`, `Company`, `Admin`), account approval state, and `RefreshCredential` replacement tracing are preserved in 100% of migration runs.
- **SC-013**: Architecture validation confirms 0 Phase 3 public or diagnostic controllers are added.

## Assumptions

- Phase 1 backend foundation and Phase 2 identity/approval work are available or can be integrated without changing Phase 3 scope.
- Phase 3 migrations must preserve existing Phase 2 Identity tables, Doctor/Company/Admin roles, account approval state, and `RefreshCredential` rotation tracing.
- Phase 3 is a data foundation phase; business workflows such as campaign creation, delivery jobs, settlement, moderation endpoints, file upload, and reporting remain in later phases.
- Persistent campaign analytics and reporting read models are deferred to Phase 10; Phase 3 stores only authoritative core records needed to derive those reports later.
- SQL Server is the required persistence platform for production validation.
- Egypt business-day rules from the backend plan define delivery date and future queue/job behavior.
- Monetary values are denominated in EGP and stored to two decimal places; values with more than two decimal places are rejected at input boundaries.
- Append-only wallet transactions are the source of financial auditability, while wallet balances are maintained for fast reads.
- Immutable wallet ledger entries are the detailed debit/credit audit trail for each wallet transaction.
- No Phase 3 public or diagnostic controller is required; automated tests are the validation surface.
- Soft deletion applies to records with financial history, ownership history, or audit-trail importance; soft-deleted records remain historically linked and are excluded from active-record queries by default.
