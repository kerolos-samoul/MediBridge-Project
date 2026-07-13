# Data Model: Company Reporting & Analytics

## Overview

Phase 10 primarily introduces live reporting read models over existing campaign, delivery, interaction, wallet transaction, ledger, doctor profile, and audit data. It does not introduce stored reporting aggregates. The only write-path candidate is safe reconciliation discrepancy evidence when source delivery states and append-only financial evidence disagree.

## Existing Source Entities

### Campaign

- **Purpose**: Company-owned promotional campaign being reported.
- **Key fields used**: `Id`, `CompanyId`, `Title`, `Status`, `SubmittedAtUtc`, review timing/history, `IsDeleted`, `DeletedAtUtc`.
- **Relationships**:
  - Belongs to one company profile.
  - Has many campaign targets.
  - Has many doctor ad deliveries.
- **Rules**:
  - Every report query must scope by `CompanyId`.
  - Soft-deleted campaigns remain reportable only through authorized ownership paths.

### Campaign Target

- **Purpose**: Original target snapshot count and context for campaign summaries.
- **Key fields used**: `CampaignId`, `DoctorId`, specialization snapshot, experience snapshot, location snapshot, activity score snapshot, price snapshot.
- **Rules**:
  - Target count comes from target records, not delivery count.
  - Target snapshots may support historical doctor context when current doctor profile differs.

### Doctor Ad Delivery

- **Purpose**: Authoritative delivery state and snapshot source for company reports.
- **Key fields used**: `Id`, `CompanyId`, `CampaignId`, `DoctorId`, `DeliveryDateEgypt`, `DeliveredAtUtc`, `ReadAtUtc`, `Status`, `InteractedAtUtc`, `FeedbackText`, `FeedbackCreatedAtUtc`, `FeedbackQualityStatus`, `PricePerMessageSnapshot`, `ReservedAmount`, `PlatformFeeAmount`, `DoctorEarnings`, `ReservationStatus`.
- **Relationships**:
  - Belongs to one campaign and one company.
  - Belongs to one doctor profile.
  - May have one interaction evidence record.
  - Related wallet transactions and ledger entries provide financial evidence.
- **Rules**:
  - Date filters use `DeliveryDateEgypt` only.
  - `Active` deliveries contribute reserved amount only.
  - `Accepted` and `Rejected` deliveries are billable and contribute charged spend, doctor earnings, and platform fee.
  - `Expired` deliveries contribute no charged spend, doctor earnings, or platform fee.
  - Feedback report includes only non-empty normalized `FeedbackText`.

### Delivery Interaction

- **Purpose**: Existing interaction evidence for accepted/rejected deliveries.
- **Key fields used**: delivery id, outcome, interaction timestamp, normalized feedback, feedback score eligibility, safe idempotency hashes/fingerprints, audit event id.
- **Rules**:
  - Raw idempotency material is never exposed in reports.
  - Interaction evidence supports feedback eligibility and reconciliation.

### Wallet Transaction and Ledger Entries

- **Purpose**: Append-only financial evidence used to reconcile spend, earnings, platform fee, and reservation-related reporting.
- **Key fields used**: transaction id, wallet id, operation type, amount, related delivery id, created time, idempotency key/fingerprint, ledger debit/credit entries.
- **Rules**:
  - Company-visible reports expose totals only, not wallet internals or raw idempotency material.
  - Charge and Earn evidence must reconcile with accepted/rejected delivery snapshots.
  - Release evidence verifies expired deliveries do not become charged spend.

### Doctor Profile

- **Purpose**: Safe company-visible doctor context for delivery and feedback rows.
- **Key fields used**: stable public doctor identifier, specialization, experience years or experience band, location.
- **Rules**:
  - Reports must not expose doctor names, contact details, account-private data, verification data, or wallet data.
  - If a profile is soft-deleted, only the safe historical/public identifier and allowed non-private context may be exposed when ownership permits.

## Reporting Read Models

### Company Campaign Report Summary

- **Fields**:
  - `CampaignId`
  - `Title`
  - `Status`
  - `SubmittedAtUtc`
  - `ReviewedAtUtc` when available
  - `TargetCount`
  - `DeliveredCount`
  - `ActiveUnansweredCount`
  - `AcceptedCount`
  - `RejectedCount`
  - `ExpiredCount`
  - `FeedbackCount`
  - `ReservedAmount`
  - `ChargedSpend`
  - `DoctorEarnings`
  - `PlatformFee`
  - `CampaignActivityAtUtc`
- **Validation**:
  - Counts must satisfy `DeliveredCount = ActiveUnansweredCount + AcceptedCount + RejectedCount + ExpiredCount` for the selected scope.
  - Monetary totals must reconcile to financial source evidence.

### Campaign Delivery Report Row

- **Fields**:
  - `DeliveryId`
  - `CampaignId`
  - `PublicDoctorId`
  - `DoctorSpecialization`
  - `DoctorExperienceBand`
  - `DoctorLocation`
  - `DeliveryDateEgypt`
  - `DeliveredAtUtc`
  - `ReadAtUtc`
  - `Status`
  - `InteractedAtUtc`
  - `PriceSnapshot`
  - `ReservedAmount`
  - `ChargedAmount`
  - `DoctorEarnings`
  - `PlatformFee`
- **Ordering**: `DeliveryDateEgypt DESC`, `DeliveredAtUtc DESC`, stable delivery id.
- **Privacy**: No doctor name, contact data, wallet data, verification data, storage location, or idempotency material.

### Campaign Feedback Report Row

- **Fields**:
  - `DeliveryId`
  - `CampaignId`
  - `Outcome`
  - `FeedbackText`
  - `FeedbackCreatedAtUtc`
  - `FeedbackQualifiesForScore`
  - `PublicDoctorId`
  - `DoctorSpecialization`
  - `DoctorExperienceBand`
  - `DoctorLocation`
- **Ordering**: `FeedbackCreatedAtUtc DESC`, stable delivery id.
- **Rules**:
  - Include only rows where normalized feedback is non-empty.
  - Short feedback is visible but marked as not score-eligible.

### Campaign Analytics Snapshot

- **Fields**:
  - Scope: `CampaignId`, `FromDateEgypt`, `ToDateEgypt`
  - Counts: delivered, active unanswered, accepted, rejected, expired, feedback
  - Denominators: delivered denominator, interacted denominator, feedback denominator
  - Rates: interaction, acceptance, rejection, expiry, feedback among interacted deliveries
  - Money: reserved amount, charged spend, doctor earnings, platform fee
  - Reconciliation status
- **Rules**:
  - Rates with zero denominator return `0` and mark the denominator as having no eligible records.
  - Analytics block when reconciliation fails.

## New or Reused Evidence Entity

### Reporting Reconciliation Discrepancy

- **Preferred implementation**: Reuse existing `AuditEvent` if its safe metadata and target fields can capture the required evidence.
- **Add new entity only if needed**:
  - `Id`
  - `CompanyId`
  - `CampaignId`
  - `FromDateEgypt`
  - `ToDateEgypt`
  - `ReportKind`
  - `Category`
  - `DetectedAtUtc`
  - `SafeMetadata`
  - `Outcome`
- **Rules**:
  - Created only when delivery/source financial evidence does not reconcile.
  - Must not include raw idempotency material, wallet internals, request/response bodies, stack traces, private doctor data, or storage credentials.
  - Does not repair or mutate financial source records.

## Query Objects and Validation Rules

### Reporting Date Range

- `FromDateEgypt`: optional date.
- `ToDateEgypt`: optional date.
- When both present, `FromDateEgypt <= ToDateEgypt`.
- Inclusive range must not exceed 90 delivery Egypt business days.
- When both values are omitted, resolve to the latest 90 inclusive delivery Egypt business days ending on the current Egypt business date.
- When only `ToDateEgypt` is provided, resolve `FromDateEgypt` to 89 days before `ToDateEgypt`.
- When only `FromDateEgypt` is provided, resolve `ToDateEgypt` to the earlier of 89 days after `FromDateEgypt` or the current Egypt business date.
- Date defaults are resolved in the service layer before repository queries are executed.

### Campaign Activity Timestamp

- `CampaignActivityAtUtc` is used only for campaign summary ordering.
- It is the greatest non-null timestamp among:
  - latest delivery `DeliveredAtUtc`
  - latest delivery `ReadAtUtc`
  - latest delivery `InteractedAtUtc`
  - latest delivery `FeedbackCreatedAtUtc`
  - latest campaign review timestamp
  - campaign `SubmittedAtUtc`
  - campaign creation timestamp
- Missing values are ignored.
- Campaign summary ordering is `CampaignActivityAtUtc DESC`, then stable campaign identifier.

### Pagination

- `PageNumber`: default 1, minimum 1.
- `PageSize`: default 20, minimum 1, maximum 100.
- Applies to campaign summaries, deliveries, and feedback.

### Filters

- Delivery status: Active, Accepted, Rejected, Expired.
- Read/interacted state: Read, Unread, Interacted, Uninteracted.
- Feedback eligibility: Eligible, Ineligible.
- Outcome: Accepted, Rejected.
- Doctor specialization/location: exact or normalized matching following existing doctor search conventions.

## State and Money Interpretation

| Delivery Status | Reporting Count | Reserved Amount | Charged Spend | Doctor Earnings | Platform Fee |
|-----------------|-----------------|-----------------|---------------|-----------------|--------------|
| Active | Delivered, active unanswered | Stored reserved amount | 0 | 0 | 0 |
| Accepted | Delivered, accepted | 0 | Stored charged amount | Stored doctor earnings | Stored platform fee |
| Rejected | Delivered, rejected | 0 | Stored charged amount | Stored doctor earnings | Stored platform fee |
| Expired | Delivered, expired | 0 | 0 | 0 | 0 |

## Repository Projections

- `ICampaignRepository` adds company-owned reporting summary projections and ownership checks that include soft-delete behavior.
- `IDeliveryRepository` adds company campaign delivery/feedback page projections and analytics source projections scoped by company id, campaign id, and `DeliveryDateEgypt`.
- `IWalletTransactionRepository` or a financial evidence repository adds read-only transaction/ledger projections by related delivery ids or campaign scope for Charge/Earn/platform-fee reconciliation.
- `IAuditEventRepository` or a new discrepancy repository records safe reporting discrepancy evidence.

## Index Guidance

- Deliveries: `(CompanyId, CampaignId, DeliveryDateEgypt, Status, DeliveredAtUtc, Id)`.
- Feedback: `(CompanyId, CampaignId, FeedbackCreatedAtUtc, Id)` filtered or optimized for non-null feedback where supported.
- Deliveries by interaction: `(CompanyId, CampaignId, InteractedAtUtc, Status)`.
- Wallet transactions/evidence: related delivery id plus operation type, created time, and wallet id if existing indexes do not support reconciliation efficiently.
- Campaigns: existing `(CompanyId, CreatedAtUtc)` may remain; add or reuse submitted/activity ordering support as needed for `CampaignActivityAtUtc`.

## Migration Notes

- A migration is needed only if indexes are missing or if a dedicated reporting discrepancy entity is introduced.
- Do not introduce stored reporting aggregate tables in Phase 10.
- Do not rewrite historical delivery, wallet, transaction, or ledger data.
