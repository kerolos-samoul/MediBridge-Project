# Service Contracts: Campaign & Queue (Phase 5)

All service contracts are implemented in `MediBridge.Services` and consumed by HTTP-only controllers in `MediBridge.APIs`. DTOs must not expose persistence infrastructure types, storage secrets, raw gateway payloads, private file access tokens, stack traces, or internal storage keys.

## ICompanyDoctorSearchService

### SearchEligibleDoctorsAsync

**Purpose**: Return eligible doctors that a Pharmaceutical Company user can select for a campaign.

**Inputs**:

- Actor user id from current-user context
- Filters: specialization, minimum/maximum experience, location, minimum activity score, minimum/maximum price
- Pagination: `PageNumber`, `PageSize`

**Rules**:

- Actor must be an approved active Pharmaceutical Company user.
- Results include only approved active doctors with active marketplace status, not soft-deleted, and positive price.
- Results are sorted by activity score descending, price ascending, then stable identifier ascending.
- Default page size is 20; maximum page size is 100.

**Output**:

- Page metadata: page number, page size, total count
- Doctor items: doctor id, specialization, experience, location, activity score, price per message

## ICampaignWorkflowService

### SubmitCampaignAsync

**Purpose**: Create a company-owned campaign in `PendingReview` with immutable target snapshots.

**Inputs**:

- Actor user id from current-user context
- Idempotency key
- Title
- Description
- Clinical research information
- Approved campaign asset ids
- Target doctor ids

**Rules**:

- Actor must be an approved active Pharmaceutical Company user.
- Idempotency key is required and scoped to company id.
- Submission requires title, description, clinical research information, at least one approved campaign asset, and 1-100 unique target doctors.
- Any invalid, duplicated, or ineligible target rejects the entire submission.
- Any missing, unrelated, unavailable, non-owned, pending, rejected, quarantined, deleted, or replaced campaign asset rejects the entire submission.
- Campaign, target snapshots, idempotency record, and audit event commit atomically.
- New campaign status is `PendingReview`.
- No queue item is created during submission.

**Output**:

- Campaign id
- Status
- Submitted time
- Target count
- Referenced asset ids

### GetCompanyCampaignsAsync

**Purpose**: Return paginated campaign summaries owned by the actor's company.

**Inputs**:

- Actor user id
- Optional status filter
- Pagination

**Rules**:

- Actor must be an approved active Pharmaceutical Company user.
- Only campaigns owned by the actor's company are returned.
- Reporting analytics, deliveries, feedback, and spend totals are out of scope.

**Output**:

- Page metadata
- Campaign summaries: id, title, status, target count, submitted time, content summary

### GetCompanyCampaignDetailAsync

**Purpose**: Return one company-owned campaign detail.

**Inputs**:

- Actor user id
- Campaign id

**Rules**:

- Actor must own the campaign through their company profile.
- Cross-company access is denied.

**Output**:

- Campaign id, title, description, clinical research information, status, submitted time, target count, asset summaries, target snapshot summaries

### CreateQueueForApprovedCampaignAsync

**Purpose**: Create deterministic per-doctor queue rows when a campaign becomes approved.

**Inputs**:

- Campaign id
- Approval or queue insertion timestamp
- Optional actor/system context for audit

**Rules**:

- Campaign must be approved and not soft-deleted.
- Non-approved statuses create no queue rows.
- Each campaign/doctor target gets at most one queue item.
- Target doctor eligibility is rechecked at queue creation.
- Ineligible targets are skipped and audited.
- Retry after partial completion creates only missing queue rows.
- Queue rows are `Queued` and ordered later by `QueuedAtUtc ASC, Id ASC`.
- No delivery activation, daily limit enforcement, wallet reservation, or settlement occurs.

**Output**:

- Campaign id
- Created queue item count
- Skipped target count
- Duplicate existing queue count

## ICompanyWalletService

### GetCompanyWalletAsync

**Purpose**: Return the actor company's wallet balance and transaction history.

**Inputs**:

- Actor user id
- Pagination

**Rules**:

- Actor must be an approved active Pharmaceutical Company user.
- Only the actor company's wallet is returned.
- Transaction page size maximum is 100.

**Output**:

- Wallet id
- Available balance
- Reserved balance
- Currency
- Paginated transactions

### TopUpCompanyWalletAsync

**Purpose**: Credit company available balance through an MVP gateway-stub top-up.

**Inputs**:

- Actor user id
- Idempotency key
- Amount in EGP
- Optional safe reference/description

**Rules**:

- Actor must be an approved active Pharmaceutical Company user.
- Idempotency key is required.
- Amount must be at least 100 EGP.
- Amount must have no more than two decimal places.
- Top-up credits available balance only.
- Wallet balance update, `TopUp` transaction, ledger entry, and audit event commit atomically.
- Duplicate retry with the same operation type and idempotency key does not create duplicate financial effect.
- Raw gateway payloads and secrets are never persisted.

**Output**:

- Wallet id
- Transaction id
- Available balance
- Reserved balance
- Currency
- Idempotency status
