# Research: Campaign & Queue (Phase 5)

## Decision: Extend existing Phase 3 domain records instead of introducing parallel Phase 5 aggregates

**Rationale**: Phase 3 already introduced `Campaign`, `CampaignTarget`, `DoctorMessageQueue`, `Wallet`, `WalletTransaction`, `WalletLedgerEntry`, profile, and audit foundations. Extending those contracts preserves one authoritative model and keeps later delivery, reporting, and wallet phases aligned.

**Alternatives considered**:

- Create separate submission or queue staging tables for Phase 5. Rejected because it duplicates lifecycle state already represented by campaign status and queue item records.
- Store campaign submission details only in service DTOs. Rejected because idempotency, auditability, target snapshots, and later review need persistent evidence.

## Decision: Implement company doctor search as a service-owned query through repository contracts

**Rationale**: Doctor eligibility combines profile state, account approval, marketplace status, soft-delete state, positive price, filters, and deterministic sorting. Keeping this query behind Core contracts and Repository implementation preserves the constitution while allowing SQL Server to handle filtering and pagination efficiently.

**Alternatives considered**:

- Filter doctors in controllers. Rejected because controllers must remain HTTP-only.
- Load all doctors and filter in memory. Rejected because it violates scalability expectations and makes pagination nondeterministic.

## Decision: Campaign submission is all-or-nothing with mandatory company-scoped idempotency

**Rationale**: Campaign submission writes the campaign, target snapshots, asset references, audit events, and initial review state together. A required idempotency key prevents duplicate campaigns under retries, and all-or-nothing target validation avoids silent audience changes.

**Alternatives considered**:

- Accept valid targets and skip invalid ones. Rejected because it surprises companies and complicates review of intended audience.
- Optional idempotency only when clients provide a key. Rejected because duplicate campaign prevention would become inconsistent and harder to test.

## Decision: Require at least one approved campaign asset at submission

**Rationale**: Phase 4 built campaign asset upload and readiness checks. Requiring an approved asset makes campaigns reviewable, prevents text-only promotional content from bypassing file review expectations, and gives admin review enough supporting material.

**Alternatives considered**:

- Make all files optional. Rejected because it weakens the review gate and leaves unclear what is deliverable.
- Require all asset types. Rejected because some campaigns may not need media, voice, and research attachments at the same time.

## Decision: Queue rows are created only when a campaign becomes approved, with uniqueness by campaign and doctor

**Rationale**: The review gate must prevent unapproved promotional content from reaching doctors. Creating queue rows only after approval keeps Phase 5 aligned with compliance requirements and later daily injection behavior. A campaign/doctor uniqueness rule makes queue creation retry-safe.

**Alternatives considered**:

- Create queue rows at submission time and mark them blocked. Rejected because it introduces premature queue state and raises risk of unapproved content appearing in delivery pipelines.
- Leave all queue creation to Phase 7 daily jobs. Rejected because Phase 5 exit criteria require approved campaigns to produce queued items.

## Decision: Company wallet top-up is MVP gateway-stub only, credits available balance, and creates transaction plus ledger atomically

**Rationale**: Phase 5 needs company balances for future reservation but does not need production payment integration. The stub allows deterministic validation of top-up idempotency, minimum amount, money precision, balance mutation, and append-only financial records without expanding scope.

**Alternatives considered**:

- Integrate a production payment gateway. Rejected as explicitly out of Phase 5 scope.
- Record top-up transactions without updating wallet balance. Rejected because company wallet query would not reflect usable funds.
- Update balance without ledger entries. Rejected because wallet history and auditability require transaction/ledger evidence.

## Decision: Use existing response envelope and role policies for company endpoints

**Rationale**: Phase 1 and Phase 2 established uniform responses, JWT, role-aware authorization, and approval gates. Reusing those conventions keeps company doctor search, campaigns, and wallet endpoints consistent with the rest of the API.

**Alternatives considered**:

- Return raw DTOs for new company endpoints. Rejected because it violates the API contract gate.
- Add a new auth pattern for company workflows. Rejected because existing JWT role policies already cover the actor model.
