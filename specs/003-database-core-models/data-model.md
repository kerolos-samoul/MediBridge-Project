# Data Model: Database & Core Models (Phase 3)

## Persistence Direction

All Phase 3 entities are designed for SQL Server persistence through Entity Framework Core in `MediBridge.Repository`. `MediBridge.Core` defines pure domain entities, enums, and repository/unit-of-work contracts only. EF Core mappings, DbSet registration, SQL Server precision, indexes, constraints, migrations, and transaction implementation belong in `MediBridge.Repository`.

Implementation alignment: the completed Phase 3 model preserves the property names described in this document, so no business-meaning renames are required.

Soft-deleted records remain historically linked and are excluded from normal active-record repository queries by default. Audit, policy, review, activity, wallet transaction, and financial history records are append-only; corrections and reversals are represented by new linked or compensating records. Wallet ledger entries are immutable.

Phase 3 migrations must preserve Phase 2 Identity and approval data. Existing Doctor, Company, and Admin roles, account approval state, and `RefreshCredential` replacement tracing must remain intact after the Phase 3 schema is applied.

## Entities

### ApplicationUser

Existing Phase 2 identity domain record extended only as needed for Phase 3 relationships.

**Fields**:

- `Id`: unique user identifier
- `Role`: `Admin`, `Doctor`, or `Company`
- `AccountStatus`: approval and suspension state
- `CreatedAtUtc`: creation timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp

**Relationships**:

- One Doctor user has zero or one `DoctorProfile`.
- One Company user has zero or one `CompanyProfile`.
- One user may appear as actor/reviewer/admin in audit, review, pricing, fee, file review, and withdrawal decision records.

**Validation Rules**:

- Domain records that require a Doctor or Company owner must reference an active user with the matching role unless preserving historical relationships.
- Soft-deleted users remain linked to historical records.
- Existing `Doctor`, `Company`, and `Admin` roles and account approval state must not be dropped, renamed, or reset by Phase 3 migrations.

### RefreshCredential

Existing Phase 2 refresh-token rotation record preserved for authentication compatibility.

**Fields**:

- Existing Phase 2 refresh credential identifier
- User relationship
- Expiration timestamp
- Revocation state
- Creation timestamp
- Replacement credential tracing fields

**Validation Rules**:

- Phase 3 migrations must not drop, rename, or reset the refresh credential table/entity.
- Refresh token rotation tracing must remain queryable after Phase 3 migrations.

### DoctorProfile

Doctor profile extended from Phase 2 registration data to support marketplace targeting, delivery limits, pricing, wallet ownership, and activity history.

**Fields**:

- `Id`: unique profile identifier
- `UserId`: owning `ApplicationUser`
- `Specialization`: required
- `ExperienceYears`: non-negative number
- `Location`: required
- `DailyMessageLimit`: admin-controlled daily delivery limit
- `MinimumWeeklyRequirement`: admin-controlled weekly interaction requirement
- `RequestedDailyMessageLimit`: nullable doctor-requested limit for future admin review
- `RequestedMinimumWeeklyRequirement`: nullable doctor-requested weekly requirement for future admin review
- `ActivityScore`: decimal score from 0 to 100, default 95 until first delivery history exists
- `Status`: `Active`, `Warned`, or `Suspended`
- `PricePerMessage`: nullable EGP amount; null or 0 means the doctor cannot receive campaign messages
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp

**Relationships**:

- One Doctor has zero or one `Wallet`.
- One Doctor has many `CampaignTarget`, `DoctorMessageQueue`, `DoctorAdDelivery`, `WithdrawalRequest`, `DoctorPriceHistory`, and `ActivityScoreHistory` records.

**Validation Rules**:

- `ExperienceYears` must be zero or greater.
- `ActivityScore` must be between 0 and 100.
- `DailyMessageLimit` and `MinimumWeeklyRequirement` must be zero or greater.
- `PricePerMessage` must be null, zero, or a positive 2-decimal EGP value.
- Doctors with `PricePerMessage` null or 0 remain persistable but are excluded by later filtering, targeting, and queue injection workflows.

### CompanyProfile

Company profile extended from Phase 2 registration data to support campaigns, wallet ownership, and reporting derivation.

**Fields**:

- `Id`: unique profile identifier
- `UserId`: owning `ApplicationUser`
- `CompanyName`: required
- `LicenseNumber`: required, unique among active companies
- `ContactName`: required
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp

**Relationships**:

- One Company has zero or one `Wallet`.
- One Company has many `Campaign`, `DoctorAdDelivery`, and company-owned `StoredFile` records.

**Validation Rules**:

- `LicenseNumber` must not duplicate another active company.
- Soft-deleted companies remain linked to historical campaigns, wallets, deliveries, and audit records.

### Campaign

Promotional campaign or advertisement owned by a company.

**Fields**:

- `Id`: unique campaign identifier
- `CompanyId`: owning company
- `Title`: required
- `MediaFileId`: nullable campaign media file reference
- `VoiceNoteFileId`: nullable voice note file reference
- `ClinicalResearchInfo`: optional content/reference summary
- `Description`: required campaign description
- `Status`: `Draft`, `PendingReview`, `Approved`, `Rejected`, `Active`, `Paused`, `Completed`, or `Cancelled`
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp

**Relationships**:

- One Campaign has many `CampaignTarget`, `CampaignReviewHistory`, `DoctorMessageQueue`, `DoctorAdDelivery`, and campaign-owned `StoredFile` records.

**Validation Rules**:

- Campaign must belong to an active company for future workflow operations.
- Only approved/deliverable statuses are eligible for later queue injection; Phase 3 only stores status support.

### CampaignTarget

Snapshot of a doctor selected for a campaign.

**Fields**:

- `Id`: unique target identifier
- `CampaignId`: campaign
- `DoctorId`: targeted doctor
- `SpecializationSnapshot`: required
- `ExperienceYearsSnapshot`: non-negative number
- `LocationSnapshot`: required
- `ActivityScoreSnapshot`: decimal score from 0 to 100
- `PricePerMessageSnapshot`: 2-decimal EGP amount
- `CreatedAtUtc`: creation timestamp

**Relationships**:

- Many targets belong to one Campaign.
- Many targets refer to one Doctor.

**Validation Rules**:

- `CampaignId + DoctorId` should be unique for active target rows.
- Snapshot values are immutable after creation except by linked correction/history records if needed.

### CampaignReviewHistory

Append-only admin review trail for campaign moderation.

**Fields**:

- `Id`: unique history identifier
- `CampaignId`: reviewed campaign
- `AdminUserId`: reviewing admin
- `Decision`: `Approved`, `Rejected`, or `ChangesRequested`
- `Reason`: required for rejection or changes requested
- `Notes`: optional
- `CreatedAtUtc`: decision timestamp
- `CorrectsHistoryId`: nullable link to corrected review record

**Validation Rules**:

- Append-only; corrections create a new linked record.
- Reviewer must be an Admin user.

### DoctorMessageQueue

Per-doctor pending campaign item used by later daily delivery activation.

**Fields**:

- `Id`: stable tie-break identifier
- `DoctorId`: target doctor
- `CampaignId`: campaign
- `QueuedAtUtc`: persisted FIFO ordering key derived from campaign submission time or queue insertion time
- `CampaignSubmittedAtUtc`: nullable source timestamp retained when available for traceability
- `Status`: `Queued`, `Activated`, or `Cancelled`
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp

**Relationships**:

- Many queue items belong to one Doctor.
- Many queue items refer to one Campaign.

**Validation Rules**:

- Pending queue reads for one doctor/status order by `QueuedAtUtc ASC` then `Id ASC`.
- Phase 3 stores status and FIFO order support only; daily injection behavior is later scope.
- Phase 3 must not add priority queue behavior.
- Items that exceed a later daily limit remain eligible to appear on the next day in the same FIFO order.

### DoctorAdDelivery

Daily campaign message delivery for a doctor, including interaction and reservation facts needed by later workflows.

**Fields**:

- `Id`: unique delivery identifier
- `DoctorId`: receiving doctor
- `CampaignId`: source campaign
- `CompanyId`: denormalized owning company for reporting and ownership checks
- `DeliveryDateEgypt`: Egypt business date
- `DeliveredAtUtc`: activation timestamp
- `ReadAtUtc`: nullable read/open timestamp
- `Status`: `Active`, `Accepted`, `Rejected`, or `Expired`
- `InteractedAtUtc`: nullable interaction timestamp
- `FeedbackText`: nullable feedback
- `FeedbackCreatedAtUtc`: nullable feedback timestamp
- `FeedbackQualityStatus`: nullable `Pending`, `Accepted`, or `Flagged`
- `PricePerMessageSnapshot`: 2-decimal EGP amount
- `PlatformFeePercentSnapshot`: decimal percentage
- `PlatformFeeAmount`: 2-decimal EGP amount
- `DoctorEarnings`: 2-decimal EGP amount
- `ReservedAmount`: 2-decimal EGP amount
- `ReservationStatus`: `Reserved`, `Released`, or `Charged`
- `ConcurrencyToken`: optimistic concurrency token
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp

**Relationships**:

- Many deliveries belong to one Doctor.
- Many deliveries belong to one Campaign.
- Many deliveries belong to one Company.
- One delivery may have many related `WalletTransaction` records.

**Validation Rules**:

- `DoctorId + DeliveryDateEgypt + CampaignId` must be unique.
- Money values must be two-decimal EGP values.
- `ConcurrencyToken` is required for retry-safe updates.
- Phase 3 stores fields and constraints only; expiry, activation, and settlement behavior are later scope.

### Wallet

Balance record for a Doctor, Company, or Platform owner.

**Fields**:

- `Id`: unique wallet identifier
- `OwnerType`: `Doctor`, `Company`, or `Platform`
- `OwnerUserId`: nullable user identifier for Doctor and Company wallets; null or system-owned for the Platform wallet
- `OwnerId`: owner profile identifier for Doctor and Company wallets; platform identifier for Platform wallet
- `AvailableBalance`: 2-decimal EGP amount
- `ReservedBalance`: 2-decimal EGP amount
- `Currency`: `EGP`
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp
- `ConcurrencyToken`: optimistic concurrency token

**Relationships**:

- One Wallet has many `WalletTransaction` records.
- One Wallet has many `WalletLedgerEntry` records.
- One active Doctor, Company, or Platform owner may have one active Wallet.

**Validation Rules**:

- `OwnerType + OwnerId` must be unique for active wallets.
- `AvailableBalance` is money usable immediately.
- `ReservedBalance` is money held for pending campaign/message delivery or withdrawal processing.
- Balances cannot be negative.
- Monetary inputs with more than two decimals are rejected.
- Wallet balance changes, wallet transaction records, and wallet ledger entries must commit in one unit of work.

### WalletTransaction

Append-only financial operation for every wallet balance mutation.

**Fields**:

- `Id`: unique transaction identifier
- `WalletId`: wallet
- `OperationType`: `TopUp`, `Reserve`, `Release`, `Charge`, `Earn`, `Refund`, `WithdrawRequest`, `WithdrawApproved`, `WithdrawRejected`, or `WithdrawPayout`
- `IdempotencyKey`: required operation key
- `Amount`: 2-decimal EGP amount
- `RelatedDeliveryId`: nullable delivery reference
- `Description`: optional safe text
- `Metadata`: optional safe structured metadata
- `CreatedAtUtc`: transaction timestamp
- `CorrectsTransactionId`: nullable link to correction/reversal transaction

**Relationships**:

- Many transactions belong to one Wallet.
- Many transactions may refer to one DoctorAdDelivery.
- One transaction has one or more `WalletLedgerEntry` records.

**Validation Rules**:

- `OperationType + IdempotencyKey` must be unique.
- Append-only; corrections/reversals create new linked transactions.
- Amount must be a positive 2-decimal EGP value unless a specific transaction type in later phases explicitly allows zero.
- No plaintext secrets, request bodies, or response bodies in metadata.
- Retriable money-moving operations require idempotency keys: campaign charging, doctor earning, refunds, top-ups, and withdrawal payout.

**Canonical transaction type enum**:

```csharp
public enum WalletTransactionType
{
    TopUp = 1,
    Reserve = 10,
    Release = 11,
    Charge = 20,
    Earn = 21,
    Refund = 22,
    WithdrawRequest = 30,
    WithdrawApproved = 31,
    WithdrawRejected = 32,
    WithdrawPayout = 33
}
```

**Required wallet behavior**:

- `TopUp`: Company `AvailableBalance` increases.
- `Reserve`: Company `AvailableBalance` decreases and `ReservedBalance` increases.
- Billable delivery example using doctor price 50 EGP, platform fee 10 EGP, and doctor earnings 40 EGP:
  - `Charge`: Company `ReservedBalance` decreases by 50.
  - `Earn`: Doctor `AvailableBalance` increases by 40.
  - Platform fee ledger entries increase Platform `AvailableBalance` by 10.
- Failure, cancellation, or non-delivery:
  - `Refund` returns money to the company.
  - If money is only reserved, reserved decreases and available increases.
  - If money is already charged, a compensating `Refund` transaction returns the amount.
- Withdraw flow:
  - `WithdrawRequest` optionally moves Doctor available balance to reserved balance.
  - `WithdrawApproved` records status/audit; usually no movement if already reserved.
  - `WithdrawRejected` returns reserved money to available balance.
  - `WithdrawPayout` decreases Doctor reserved balance and is the final ledger-impacting withdrawal transaction.

`Withdraw` is not a standalone transaction type in Phase 3.

### WalletLedgerEntry

Immutable debit/credit line item created by a wallet transaction.

**Fields**:

- `Id`: unique ledger entry identifier
- `WalletTransactionId`: owning wallet transaction
- `WalletId`: affected wallet
- `Direction`: `Debit` or `Credit`
- `Amount`: positive 2-decimal EGP amount
- `BalanceType`: `Available` or `Reserved`
- `Currency`: `EGP`
- `CampaignId`: nullable campaign reference
- `MessageDeliveryId`: nullable delivery reference
- `DoctorId`: nullable doctor reference
- `CompanyId`: nullable company reference
- `WithdrawalRequestId`: nullable withdrawal request reference
- `IdempotencyKey`: nullable key copied for retriable money-moving operations
- `CreatedAtUtc`: ledger entry timestamp

**Relationships**:

- Many ledger entries belong to one Wallet.
- Many ledger entries belong to one WalletTransaction.

**Validation Rules**:

- Ledger entries are immutable after creation.
- Corrections use compensating transactions such as `Refund` or `Release`.
- Amount must be positive, two-decimal EGP.
- Ledger entry insertion must be atomic with the wallet balance update and wallet transaction.

### WithdrawalRequest

Doctor payout request and admin decision record foundation.

**Fields**:

- `Id`: unique request identifier
- `DoctorId`: requesting doctor
- `Amount`: 2-decimal EGP amount
- `Status`: `Requested`, `Approved`, `Rejected`, `Paid`, or `Failed`
- `RequestedAtUtc`: request timestamp
- `ReviewedByAdminUserId`: nullable admin reviewer
- `ReviewedAtUtc`: nullable review timestamp
- `DecisionReason`: nullable decision reason
- `PayoutReference`: nullable payout/stub reference
- `ConcurrencyToken`: optimistic concurrency token

**Validation Rules**:

- Amount must be a positive 2-decimal EGP value.
- Concurrency token prevents lost review decisions.
- Endpoint workflows and cooldown validation are later scope.

### StoredFile

Backend-controlled file metadata for verification documents, campaign media, voice notes, and clinical research attachments.

**Fields**:

- `Id`: unique file identifier
- `OwnerType`: `Doctor`, `Company`, `Admin`, or `Campaign`
- `OwnerId`: owner identifier
- `Purpose`: `VerificationDocument`, `CampaignMedia`, `VoiceNote`, or `ClinicalResearchAttachment`
- `OriginalFileName`: required
- `ContentType`: required
- `SizeBytes`: positive number
- `StorageKey`: required internal storage locator
- `Visibility`: `Private`, `Protected`, or `Public`
- `ReviewStatus`: `Pending`, `Approved`, or `Rejected`
- `CreatedAtUtc`: creation timestamp
- `ReviewedAtUtc`: nullable review timestamp
- `ReviewedByAdminId`: nullable admin reviewer
- `ReviewReason`: nullable review reason

**Validation Rules**:

- Verification documents default to private visibility.
- Phase 3 stores metadata only; upload streams, storage provider behavior, malware scanning, and retrieval endpoints are Phase 4+ scope.

### DoctorPriceHistory

Append-only history of admin-controlled doctor price changes.

**Fields**:

- `Id`: unique history identifier
- `DoctorId`: affected doctor
- `PreviousPricePerMessage`: nullable 2-decimal EGP amount
- `NewPricePerMessage`: nullable 2-decimal EGP amount
- `ChangedByAdminUserId`: admin actor
- `Reason`: optional
- `CreatedAtUtc`: change timestamp
- `CorrectsHistoryId`: nullable correction link

**Validation Rules**:

- Append-only; corrections create linked records.
- Values with more than two decimals are rejected.

### PlatformFeePolicyHistory

Append-only platform fee policy history.

**Fields**:

- `Id`: unique policy identifier
- `FeePercent`: decimal percentage
- `EffectiveFromUtc`: policy start timestamp
- `EffectiveToUtc`: nullable policy end timestamp
- `ChangedByAdminUserId`: admin actor
- `Reason`: optional
- `CreatedAtUtc`: record timestamp
- `CorrectsHistoryId`: nullable correction link

**Validation Rules**:

- Fee percent must be zero or greater.
- Later activation workflows snapshot the active fee policy; Phase 3 stores history only.

### ActivityScoreHistory

Append-only snapshot of computed doctor activity score.

**Fields**:

- `Id`: unique snapshot identifier
- `DoctorId`: doctor
- `ActivityScore`: decimal score from 0 to 100
- `ResponseSpeedScore`: nullable score
- `EngagementScore`: nullable score
- `FeedbackScore`: nullable score
- `WindowStartDateEgypt`: Egypt date
- `WindowEndDateEgypt`: Egypt date
- `CreatedAtUtc`: snapshot timestamp
- `CorrectsHistoryId`: nullable correction link

**Validation Rules**:

- Scores must be between 0 and 100.
- Job computation is later scope; Phase 3 stores history only.

### AuditEvent

Append-only trace record for admin, authentication-sensitive, financial, document review, and system events.

**Fields**:

- `Id`: unique audit identifier
- `EventType`: required event category
- `ActorUserId`: nullable actor
- `ActorRole`: nullable role
- `TargetType`: nullable target category
- `TargetId`: nullable target identifier
- `Outcome`: success, denial, or informational category
- `Reason`: nullable safe reason
- `CorrelationId`: nullable request trace identifier
- `Metadata`: optional safe structured metadata
- `CreatedAtUtc`: event timestamp
- `CorrectsAuditEventId`: nullable correction link

**Validation Rules**:

- Append-only; corrections create linked records.
- Actor may be null for anonymous or system events, but `EventType` and `CreatedAtUtc` are required.
- Audit metadata must not store passwords, plaintext tokens, request bodies, response bodies, or other secrets.

## Required Indexes and Constraints

- Queue ordering: `(DoctorId, Status, QueuedAtUtc, Id)` with reads ordered by `QueuedAtUtc ASC, Id ASC`; no priority column or priority ordering.
- Delivery uniqueness: unique `(DoctorId, DeliveryDateEgypt, CampaignId)`.
- Campaign browsing: `(CompanyId, CreatedAtUtc)`.
- Wallet owner uniqueness: unique active `(OwnerType, OwnerId)`.
- Wallet ledger browsing: `(WalletId, CreatedAtUtc)`.
- Wallet transaction idempotency: unique `(OperationType, IdempotencyKey)`.
- Wallet ledger transaction browsing: `(WalletTransactionId, CreatedAtUtc)`.
- Company license uniqueness: unique active `LicenseNumber`.
- Profile ownership: unique `UserId` for doctor and company profiles.

## State Transitions

### DoctorMessageQueue

```text
Queued -> Activated
Queued -> Cancelled
Activated -> terminal for queue item
Cancelled -> terminal for queue item
```

Phase 3 stores states and ordering only. Daily injection and carry-over behavior are later scope.

### DoctorAdDelivery

```text
Active -> Accepted
Active -> Rejected
Active -> Expired
```

Phase 3 stores states, uniqueness, reservation fields, and concurrency support only. Expiry and interaction settlement behavior are later scope.

### ReservationStatus

```text
Reserved -> Released
Reserved -> Charged
```

### Wallet Money Flows

```text
TopUp: Company Available +
Reserve: Company Available - -> Company Reserved +
Charge: Company Reserved -
Earn: Doctor Available +
Platform fee: Platform Available +
Refund while reserved: Company Reserved - -> Company Available +
Refund after charge: compensating Refund transaction credits Company Available
WithdrawRequest: Doctor Available - -> Doctor Reserved + (when reservation is used)
WithdrawApproved: status/audit only unless future workflow explicitly requires movement
WithdrawRejected: Doctor Reserved - -> Doctor Available +
WithdrawPayout: Doctor Reserved -
```

### WithdrawalRequest

```text
Requested -> Approved
Requested -> Rejected
Approved -> Paid
Approved -> Failed
Failed -> Approved (retry after admin review)
```

### Soft Delete

```text
Active record -> Soft-deleted
Soft-deleted -> historical access only by default
```

## Repository and Unit-of-Work Contracts

- `ICampaignRepository`: campaign, target, and review-history persistence methods.
- `IMessageQueueRepository`: per-doctor queue add/query/update methods ordered by `QueuedAtUtc ASC, Id ASC` within doctor/status scope.
- `IDeliveryRepository`: delivery add/query/update methods with duplicate prevention and concurrency handling.
- `IWalletRepository`: wallet lookup and balance mutation staging methods.
- `IWalletTransactionRepository`: append-only wallet transaction creation/query methods with `(OperationType, IdempotencyKey)` duplicate detection.
- `IWalletLedgerEntryRepository`: immutable ledger entry creation/query methods by wallet, transaction, reference, and created time.
- `IStoredFileRepository`: metadata persistence and review-state query methods.
- `IPolicyHistoryRepository`: doctor price, platform fee, and activity score history methods.
- `IAuditEventRepository`: append-only audit creation/query methods.
- `IDomainUnitOfWork`: aggregate repository access plus `SaveChangesAsync` and transaction helpers for wallet balance plus ledger atomicity.
