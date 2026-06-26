# Data Model: Campaign & Queue (Phase 5)

## Persistence Direction

Phase 5 persists campaign, targeting, queue, wallet, and audit data in SQL Server through Entity Framework Core implementations in `MediBridge.Repository`. `MediBridge.Core` defines pure entities, enums, and repository/service-facing contracts only. Controllers never depend on EF Core or persistence infrastructure.

Phase 5 extends existing Phase 3 entities and repositories. Implementation may add fields, indexes, repository methods, and DTOs needed by Phase 5, but must not rename existing business fields in a way that breaks Phase 3 and Phase 4 tests.

## Entities

### DoctorProfile

Marketplace doctor record searched by companies.

**Existing fields used**:

- `Id`: stable doctor profile identifier
- `UserId`: owning user account
- `Specialization`: filterable specialty
- `ExperienceYears`: filterable experience
- `Location`: filterable location
- `ActivityScore`: sortable/filterable score
- `Status`: must be active for eligibility
- `PricePerMessage`: must be positive for eligibility
- `IsDeleted`: excluded from eligibility when true

**Phase 5 validation rules**:

- Eligible search and targeting include only approved active doctor accounts with active marketplace status, not soft-deleted, and `PricePerMessage > 0`.
- Search supports specialization, minimum/maximum experience, location, minimum activity score, and minimum/maximum price filters.
- Default search order is `ActivityScore DESC`, `PricePerMessage ASC`, then `Id ASC`.
- Pagination uses `PageNumber` and `PageSize`, default page size 20, maximum page size 100.

### CompanyProfile

Approved pharmaceutical company account that owns campaigns and wallet workflows.

**Existing fields used**:

- `Id`: company profile identifier
- `UserId`: owning user account
- `CompanyName`: display name
- `IsDeleted`: prevents normal company workflows when true

**Phase 5 validation rules**:

- Company workflows require a JWT user with Pharmaceutical Company role, approved account state, active profile, and ownership of requested campaign or wallet.
- Companies cannot access another company's campaigns, files, or wallet.

### Campaign

Promotional campaign submitted by a company for review.

**Existing fields used or extended**:

- `Id`: campaign identifier
- `CompanyId`: owning company profile
- `Title`: required
- `Description`: required
- `ClinicalResearchInfo`: required in Phase 5
- `MediaFileId`: optional approved campaign media asset
- `VoiceNoteFileId`: optional approved voice note asset
- Additional approved asset references as needed for multiple related campaign files
- `Status`: starts as `PendingReview` for submitted campaigns
- `CreatedAtUtc`: submission timestamp
- `UpdatedAtUtc`: lifecycle update timestamp
- `IsDeleted`: excluded from normal company operations when true

**Phase 5 validation rules**:

- Submission requires title, description, clinical research information, at least one approved campaign asset, 1-100 unique eligible target doctors, and an idempotency key.
- Submission is all-or-nothing: any invalid selected target, missing required content, unauthorized file, duplicate target, missing idempotency key, or over-100 target list rejects the whole submission.
- Newly submitted campaigns are `PendingReview` and create zero queue rows.
- Company list/detail views return only campaigns owned by the authenticated company.

**State transitions in Phase 5**:

```text
New submission -> PendingReview
PendingReview -> Approved (trusted review transition from Phase 6/admin path)
Approved -> queue creation side effect
Rejected/Paused/Completed/Cancelled/Deleted -> no new queue creation
```

### CampaignTarget

Point-in-time doctor selection snapshot for a campaign.

**Existing fields used**:

- `Id`: target identifier
- `CampaignId`: submitted campaign
- `DoctorId`: selected doctor
- `SpecializationSnapshot`: copied from doctor at submission
- `ExperienceYearsSnapshot`: copied from doctor at submission
- `LocationSnapshot`: copied from doctor at submission
- `ActivityScoreSnapshot`: copied from doctor at submission
- `PricePerMessageSnapshot`: copied from doctor at submission
- `CreatedAtUtc`: snapshot creation timestamp

**Phase 5 validation rules**:

- `CampaignId + DoctorId` is unique for a campaign.
- Target list must contain 1-100 unique doctors.
- Snapshot values are written in the same atomic operation as campaign creation.
- Snapshot values are not recalculated if doctor profile details change after submission.

### CampaignSubmissionRequest

Persistent idempotency record for campaign submission retries.

**Fields**:

- `Id`: unique idempotency record identifier
- `CompanyId`: submitting company profile
- `IdempotencyKey`: client-provided retry key
- `CampaignId`: created campaign when successful
- `RequestHash`: optional normalized safe hash of request content for conflict detection
- `Status`: `Succeeded` or `FailedValidation`
- `CreatedAtUtc`: first request timestamp
- `CompletedAtUtc`: nullable completion timestamp

**Validation rules**:

- `CompanyId + IdempotencyKey` must be unique.
- Missing idempotency key rejects the request before creating campaign or target rows.
- A retry with the same company and idempotency key returns the existing successful campaign result or a safe conflict if the request content differs.
- No raw request body, file token, or secret is stored.

### StoredFile

Approved campaign asset referenced by a campaign submission.

**Existing fields used**:

- `Id`: file identifier
- `OwnerType`: must identify company/campaign ownership allowed by Phase 4 rules
- `OwnerId`: must match submitting company or campaign workflow context
- `Purpose`: `CampaignMedia`, `VoiceNote`, or `ClinicalResearchAttachment`
- `UploadStatus`: must be stored
- `ReviewStatus`: must be approved
- `SafetyScanStatus`: Phase 5 does not require a passing scan beyond Phase 4 readiness rules
- `DeletedAtUtc` / replacement fields: unavailable files are rejected

**Phase 5 validation rules**:

- At least one approved campaign asset is required.
- Missing, pending, rejected, quarantined, deleted, replaced, unrelated, or non-owned files are rejected.
- File readiness is checked through `IStoredFileRepository.IsAvailableAsApprovedAssetAsync` or an equivalent repository/service abstraction.

### DoctorMessageQueue

Per-doctor pending campaign item used by later daily delivery activation.

**Existing fields used**:

- `Id`: stable queue tie-breaker
- `DoctorId`: targeted doctor
- `CampaignId`: approved campaign
- `QueuedAtUtc`: persisted FIFO ordering key from approval/queue insertion time
- `CampaignSubmittedAtUtc`: retained source submission timestamp when available
- `Status`: `Queued`, `Activated`, or `Cancelled`
- `CreatedAtUtc`: creation timestamp

**Phase 5 validation rules**:

- Queue rows are created only when a campaign becomes `Approved`.
- No queue rows are created for `Draft`, `PendingReview`, `Rejected`, `Paused`, `Completed`, `Cancelled`, or soft-deleted campaigns.
- At most one queue row exists for a campaign and doctor.
- Queue creation revalidates target doctor eligibility at approval time and audits skipped targets.
- Pending queue reads order by `QueuedAtUtc ASC`, then `Id ASC`.
- Phase 5 does not activate deliveries, enforce daily limits, expire messages, reserve funds, or settle payments.

### Wallet

Company-owned EGP balance used by later delivery reservation.

**Existing fields used**:

- `Id`: wallet identifier
- `OwnerType`: `Company`
- `OwnerId`: company profile identifier
- `OwnerUserId`: owning company user where available
- `AvailableBalance`: credited by top-up
- `ReservedBalance`: unchanged by Phase 5 top-up
- `Currency`: `EGP`
- `ConcurrencyToken`: prevents lost balance updates

**Phase 5 validation rules**:

- Wallet query and top-up are limited to the owning company.
- Top-up credits available balance only.
- Reserved balance changes remain out of scope until daily delivery activation.
- Balances cannot become negative.

### WalletTransaction

Append-only financial operation record for company wallet top-up.

**Existing fields used**:

- `Id`: transaction identifier
- `WalletId`: company wallet
- `OperationType`: `TopUp`
- `IdempotencyKey`: required duplicate-protection key
- `Amount`: top-up amount
- `Description`: safe display text
- `Metadata`: safe non-secret gateway-stub metadata
- `CreatedAtUtc`: transaction timestamp

**Phase 5 validation rules**:

- Top-up amount must be at least 100 EGP.
- Top-up amount must have no more than two decimal places.
- `OperationType + IdempotencyKey` prevents duplicate financial effect.
- Metadata must not contain raw gateway payloads, secrets, private tokens, request bodies, or response bodies.

### WalletLedgerEntry

Immutable accounting line item for the top-up transaction.

**Fields used**:

- `WalletTransactionId`: owning top-up transaction
- `WalletId`: company wallet
- `Direction`: credit for company available balance
- `Amount`: positive EGP amount
- `BalanceType`: available
- `Currency`: `EGP`
- `CompanyId`: company reference
- `IdempotencyKey`: copied from top-up operation
- `CreatedAtUtc`: ledger timestamp

**Phase 5 validation rules**:

- Ledger entry insertion is atomic with wallet balance update and wallet transaction creation.
- Ledger entries are immutable.

### AuditEvent

Append-only trace record for sensitive or business-significant actions.

**Phase 5 event categories**:

- Company doctor search denial
- Campaign submission success
- Campaign submission validation failure
- Target validation failure
- Campaign asset validation failure
- Queue creation success
- Queue creation skipped target
- Queue creation duplicate retry
- Company wallet top-up success
- Company wallet top-up validation failure
- Ownership or authorization denial

**Validation rules**:

- Audit metadata contains no secrets, raw gateway payloads, private file access tokens, request bodies, response bodies, or stack traces.
- Anonymous or denied requests are attributable where possible through correlation id and actor context.

## Required Indexes and Constraints

- Doctor search support: active doctor/profile fields used for filtering, including specialization, experience, location, activity score, price, status, soft-delete state, and stable identifier.
- Campaign browsing: `(CompanyId, CreatedAtUtc)` for company list/detail ownership.
- Campaign target uniqueness: unique `(CampaignId, DoctorId)`.
- Campaign submission idempotency: unique `(CompanyId, IdempotencyKey)`.
- Queue uniqueness: unique `(CampaignId, DoctorId)` for non-deleted queue rows or equivalent duplicate prevention.
- Queue ordering: `(DoctorId, Status, QueuedAtUtc, Id)` with reads ordered by `QueuedAtUtc ASC, Id ASC`.
- Wallet owner uniqueness: active `(OwnerType, OwnerId)`.
- Wallet transaction idempotency: unique `(OperationType, IdempotencyKey)`.
- Wallet transaction browsing: `(WalletId, CreatedAtUtc)`.
- Wallet ledger browsing: `(WalletId, CreatedAtUtc)` and `(WalletTransactionId, CreatedAtUtc)`.

## Repository and Unit-of-Work Contracts

- `IProfileRepository`: add eligible doctor search/count methods with filters, pagination, and deterministic ordering.
- `ICampaignRepository`: add campaign create/list/detail methods, target snapshot batch creation, company-scoped idempotency lookup/write, status transition lookup, and campaign ownership checks.
- `IStoredFileRepository`: reuse or extend approved campaign asset readiness checks for one or more file ids.
- `IMessageQueueRepository`: add batch queue creation or get-or-create behavior with campaign/doctor duplicate prevention and per-doctor FIFO queries.
- `IWalletRepository`: add company wallet detail lookup, top-up balance staging, and active wallet creation/lookup as needed.
- `IWalletTransactionRepository`: reuse idempotency lookup and add transaction detail/page query support.
- `IWalletLedgerEntryRepository`: add immutable top-up ledger entry creation and query support.
- `IAuditEventRepository`: add append-only Phase 5 audit event creation.
- `IDomainUnitOfWork`: coordinate campaign submission atomicity, queue creation retry safety, and wallet top-up balance plus transaction plus ledger atomicity.

## State Transitions

### Campaign

```text
Submitted -> PendingReview
PendingReview -> Approved
PendingReview -> Rejected (Phase 6 primary path)
Approved -> Paused | Completed | Cancelled (later/admin paths)
```

Phase 5 implements submission into `PendingReview` and the approved-campaign queue side effect. Admin review UI remains Phase 6.

### DoctorMessageQueue

```text
NotQueued -> Queued (when campaign becomes Approved and target remains eligible)
Queued -> Activated (Phase 7)
Queued -> Cancelled (later/admin path)
```

Phase 5 creates `Queued` rows only. Delivery activation, daily limits, carry-over, and expiry are not implemented.

### Company Wallet Top-Up

```text
TopUp accepted -> Wallet AvailableBalance increases
TopUp accepted -> append-only WalletTransaction(TopUp)
TopUp accepted -> immutable WalletLedgerEntry crediting available balance
Duplicate retry -> existing result/no duplicate financial effect
```

Top-up does not affect reserved balance and does not reserve, charge, release, earn, refund, withdraw, or settle funds.
