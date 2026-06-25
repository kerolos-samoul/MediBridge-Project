# Data Model: Wallet and Campaign Workflow

## Persistence Direction

This feature builds on Phase 3 persistence. Existing campaign, wallet, stored file, queue, policy, and audit records remain authoritative. New or expanded behavior must be exposed through service-owned use cases and persisted through Repository + Unit of Work abstractions; EF Core configuration and SQL Server migrations remain in `MediBridge.Repository`.

## Entities

### Company Wallet

Reuses existing `Wallet` with `OwnerType = Company`.

**Fields used**:

- `Id`
- `OwnerType`
- `OwnerUserId`
- `OwnerId`
- `AvailableBalance`
- `ReservedBalance`
- `Currency = EGP`
- `CreatedAtUtc`
- `UpdatedAtUtc`
- `IsDeleted`
- `DeletedAtUtc`
- `ConcurrencyToken`

**Validation rules**:

- Approved company wallet query returns one active wallet; if missing, first-use repair creates it.
- One active company wallet per company owner.
- Available and reserved balances cannot be negative.
- All user-supplied money values are positive EGP amounts with at most two decimals.

### Mock Payment Transaction

New internal payment record for demonstration-only wallet top-up.

**Fields**:

- `PaymentId`: unique payment identifier
- `CompanyId`: company profile identifier
- `WalletId`: credited company wallet
- `Amount`: positive two-decimal EGP amount
- `Currency`: `EGP`
- `Status`: `Succeeded`
- `CreatedAtUtc`: creation timestamp
- `TransactionReference`: internally generated unique reference
- `IdempotencyKey`: top-up idempotency key
- `WalletBalanceBefore`: company wallet available balance before credit
- `WalletBalanceAfter`: company wallet available balance after credit
- `WalletTransactionId`: related `WalletTransaction`
- `AuditEventId`: optional related audit event

**Relationships**:

- One mock payment belongs to one company.
- One mock payment credits one company wallet.
- One mock payment has one top-up wallet transaction and one or more wallet ledger entries.

**Validation rules**:

- Only `Succeeded` is produced by this feature; there is no pending, failed, callback, or provider status.
- `TransactionReference` is generated internally and unique.
- `CompanyId + IdempotencyKey` protects mock payment replay lookup for this top-up operation; wallet transactions still use existing operation-scoped idempotency rules.
- Conflicting replay by the same company with the same idempotency key and a different amount or currency is rejected without balance changes. A different company using the same idempotency key is independent.

### Wallet Transaction

Reuses existing append-only `WalletTransaction`.

**Workflow use**:

- Top-up creates `WalletTransactionType.TopUp`.
- Campaign submission creates `WalletTransactionType.Reserve`.
- Rejection, cancellation, or expiry before chargeable delivery creates `WalletTransactionType.Release` or refund-equivalent release behavior according to existing wallet semantics.

**Validation rules**:

- `OperationType + IdempotencyKey` remains unique.
- Transactions are append-only.
- Amount is positive two-decimal EGP.

### Wallet Ledger Entry

Reuses existing immutable `WalletLedgerEntry`.

**Workflow use**:

- Mock top-up credits company available balance.
- Campaign submission debits company available balance and credits company reserved balance.
- Rejection/cancellation/expiry releases reserved funds back to available balance.

**Validation rules**:

- Ledger entries are immutable.
- Ledger insertion is atomic with wallet balance and wallet transaction changes.

### Campaign

Reuses existing `Campaign`.

**Additional workflow facts**:

- Draft campaigns are company-owned and editable before submission.
- `PendingReview` campaigns have submitted target snapshots and reserved company funds.
- `Approved` campaigns have successful admin review and queue rows.
- `Rejected`, `Cancelled`, or expired campaigns release uncharged reserved funds and cancel pending queue rows.

**State transitions**:

```text
Draft -> PendingReview
PendingReview -> Approved
PendingReview -> Rejected
PendingReview -> Draft (returned for revision)
Approved -> Cancelled
Approved -> Completed
Draft -> Cancelled
```

### Campaign Asset

Reuses existing `StoredFile` with `OwnerType = Campaign` and `Purpose = CampaignMedia`.

**Fields used**:

- `Id`
- `OwnerType`
- `OwnerId` (campaign id)
- `Purpose`
- `OriginalFileName`
- `ContentType`
- `SizeBytes`
- `StorageKey`
- `Visibility`
- `ReviewStatus`
- `CreatedAtUtc`
- `ReviewedAtUtc`
- `ReviewedByAdminId`
- `ReviewReason`

**Validation rules**:

- Company can upload campaign assets only to owned `Draft` or revision-required campaigns.
- Company can replace only Pending or Rejected campaign assets on an owned `Draft` or revision-required campaign. Replacement creates a new stored-file row and preserves the prior row for audit, linked by lifecycle metadata.
- Company can delete only Pending or Rejected campaign assets on an owned `Draft` or revision-required campaign. Approved assets are immutable and retained.
- Authorized file reads return a short-lived signed access payload `{ FileId, Url, ExpiresAtUtc }`; clients never receive provider credentials.
- Submission requires at least one asset with `ReviewStatus = Approved`.
- Asset review records reviewer, decision time, and rejection reason when rejected.
- File scanning remains out of scope; third-party storage is accessed only through provider-neutral backend abstractions.

### Doctor Price Policy

Uses `DoctorProfile.PricePerMessage` plus append-only `DoctorPriceHistory`.

**Validation rules**:

- Admin update accepts only positive EGP amounts with at most two decimals.
- Null, zero, negative, and over-precise update values are rejected.
- Doctors without a valid positive price remain excluded from target previews and queue creation.
- Submitted campaign target snapshots retain the price used at submission.

### Campaign Target Snapshot

Reuses existing `CampaignTarget`.

**Workflow use**:

- Created at campaign submission for eligible doctors.
- Captures specialization, experience, location, activity score, and price per message.

**Validation rules**:

- One active target per campaign and doctor.
- Snapshot values are not recalculated during admin review.

### Campaign Review History

Reuses existing append-only `CampaignReviewHistory`.

**Validation rules**:

- Admin asset and campaign decisions are retained.
- Campaign rejection or return for revision requires a reason.
- Campaign approval retry must not duplicate review evidence or queue rows.

### Doctor Message Queue Row

Reuses existing `DoctorMessageQueue`.

**Workflow use**:

- Created after admin campaign approval, once per target doctor.
- Company sees aggregate queue counts only for owned campaigns.
- Admin can inspect doctor-level queue rows.

**Validation rules**:

- One active queued item per campaign and target doctor.
- Pending reads order by campaign submission time, then stable queue identifier.
- Delivery activation and doctor daily delivery-limit processing are outside this feature; this workflow creates ordered queued rows and leaves activation to an existing or future delivery workflow.
- Delivered-message expiry is outside this feature and belongs to the delivery activation and doctor interaction workflow.

### Campaign Queue Summary

Read model assembled by services from queue rows for one company-owned campaign.

**Fields**:

- `CampaignId`
- `QueuedCount`
- `ActivatedCount`
- `CancelledCount`
- `ExpiredCount`
- `GeneratedAtUtc`

**Validation rules**:

- Available only to the owning company or admins.
- Does not expose doctor identifiers or doctor-level queue details to companies.

### Audit Event

Reuses existing append-only `AuditEvent`.

**Workflow use**:

- Records mock payment success, top-up replay/conflict, admin price update, asset review, campaign review, queue creation, and authorization denials where applicable.

**Validation rules**:

- Audit metadata must not include secrets, raw request bodies, raw response bodies, payment credentials, or tokens.

## Required Indexes and Constraints

- Mock payment unique `TransactionReference`.
- Mock payment lookup by `CompanyId, CreatedAtUtc`.
- Mock payment duplicate protection by unique `(CompanyId, IdempotencyKey)` plus request payload comparison at the service layer.
- Existing active wallet uniqueness by `OwnerType + OwnerId`.
- Existing wallet transaction uniqueness by `OperationType + IdempotencyKey`.
- Existing campaign target uniqueness by `CampaignId + DoctorId`.
- Existing queue ordering by `DoctorId, Status, QueuedAtUtc, Id`.
- Queue creation must prevent duplicate active queue rows for the same `CampaignId + DoctorId`.

## Atomic Business Actions

### Mock Top-Up

```text
Ensure active company wallet
Create succeeded mock payment
Credit wallet available balance
Create TopUp wallet transaction
Create wallet ledger entry
Create audit event
Commit together
```

### Campaign Submission

```text
Validate owned draft campaign
Validate at least one approved asset
Resolve eligible priced doctors
Snapshot targets and prices
Validate wallet sufficiency
Reserve company funds
Move campaign to PendingReview
Commit together
```

### Campaign Approval

```text
Validate submitted campaign
Append campaign review decision
Create queue rows once per target doctor
Move campaign to Approved
Commit together
```

### Campaign Rejection, Cancellation, or Expiry

```text
Append decision or system event
Release uncharged reserved funds
Cancel pending queue rows
Move campaign to terminal or revision state
Commit together
```
