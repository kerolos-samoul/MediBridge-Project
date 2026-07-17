# Data Model: Admin Tools (Phase 11)

## Overview

Phase 11 primarily adds admin-facing read models plus a deterministic withdrawal/payout workflow. Existing account decisions, file reviews, campaign reviews, pricing history, platform fee policy history, enforcement actions, wallet transactions, and ledger entries remain source evidence. New persistence is required only where current entities cannot represent pricing deactivation, withdrawal holds/finalization, payout status metadata, or efficient admin read paths.

## Entities and Read Models

### AdminWorkQueueItem

Safe read model representing one pending or action-relevant admin task.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `ItemId` | string | Yes | Stable source item id. |
| `Category` | enum | Yes | `Account`, `File`, `Campaign`, `Enforcement`, `Withdrawal`. |
| `Status` | string | Yes | Source workflow status. |
| `UrgencyRank` | integer | Yes | Lower number appears first. |
| `SubmittedAtUtc` | datetime | Yes | Requested/submitted/created time used for ordering. |
| `OwnerType` | enum | Yes | `Doctor`, `Company`, `Campaign`, `System`. |
| `OwnerId` | string | No | Safe owner/profile id where review needs it. |
| `OwnerDisplay` | string | No | Safe review summary only. |
| `Summary` | string | Yes | Short safe description. |
| `NextActions` | string array | Yes | Allowed actions such as `Approve`, `Reject`, `RequestCorrection`, `Review`, `MarkPaid`. |
| `SensitiveFlags` | string array | No | Safe flags such as `FinancialEvidenceInconsistent`; no raw details. |

Validation and privacy:

- Must exclude raw storage keys, provider credentials, raw idempotency material, payout destination data, private contact details beyond review need, and stack traces.
- Ordering is `UrgencyRank ASC`, `SubmittedAtUtc ASC`, `ItemId ASC`.
- Pagination uses `PageNumber >= 1`, `1 <= PageSize <= 100`.

### WithdrawalRequest

Doctor-owned payout workflow record.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Id` | string | Yes | Stable request id. |
| `DoctorId` | string | Yes | Doctor profile id. |
| `Amount` | decimal(18,2) | Yes | Positive EGP amount. |
| `Status` | enum | Yes | See state transitions. |
| `RequestedAtUtc` | datetime | Yes | Creation time. |
| `ReviewedByAdminUserId` | string | No | Set on approval/rejection. |
| `ReviewedAtUtc` | datetime | No | Set on approval/rejection. |
| `DecisionReason` | string | No | Required for rejection; optional note for approval. |
| `PayoutReference` | string | No | Required when marked Paid; max 200 characters. |
| `PayoutStatusChangedByAdminUserId` | string | No | Set on Paid/Failed. |
| `PayoutStatusChangedAtUtc` | datetime | No | Set on Paid/Failed. |
| `PayoutFailureReason` | string | No | Required when marked Failed. |
| `ConcurrencyToken` | rowversion | Yes | Required for safe concurrent transitions. |

Validation:

- New request requires authenticated approved, active, non-suspended Doctor owner.
- Amount must be positive, use two or fewer decimal places, and not exceed withdrawable settled earnings.
- No payout destination fields are collected, stored, displayed, or validated.
- Payout reference is operational evidence only and must not be treated as provider data.

### WithdrawalStatus

| Status | Meaning | Allowed Next States |
|--------|---------|---------------------|
| `Requested` | Doctor request created and amount is held. | `Approved`, `Rejected` |
| `Approved` | Admin approved request; hold remains pending payout. | `Paid`, `Failed` |
| `Rejected` | Admin rejected request; hold released. | None |
| `Paid` | Payout stub marked paid; hold finalized. | None |
| `Failed` | Payout failed before funds left platform; hold released. | None |

Invalid transitions are rejected without wallet mutation or duplicate audit history.

### WithdrawalHoldLedgerEvidence

Financial evidence tying wallet movement to a withdrawal request.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `WalletTransactionId` | string | Yes | Transaction evidence id. |
| `WalletLedgerEntryId` | string | Yes | Ledger evidence id. |
| `WithdrawalRequestId` | string | Yes | References the withdrawal request. |
| `Direction` | enum | Yes | Debit/Credit semantics from wallet perspective. |
| `BalanceType` | enum | Yes | `Available` or `Reserved`/hold bucket as modeled by existing wallet balance rules. |
| `Amount` | decimal(18,2) | Yes | Positive EGP amount. |
| `Operation` | enum | Yes | `Hold`, `Release`, `FinalizePayout`. |
| `CreatedAtUtc` | datetime | Yes | Transaction time. |

Rules:

- Request creates exactly one hold effect for the amount.
- Reject and eligible failure create exactly one release effect.
- Paid creates exactly one finalization effect.
- Retries return or preserve the existing effect rather than duplicating it.

### DoctorPriceHistoryEntry

Extends current price history semantics to represent separate deactivation.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `Id` | string | Yes | Stable history id. |
| `DoctorId` | string | Yes | Doctor profile id. |
| `PreviousPricePerMessage` | decimal(18,2) | No | Existing active price when known. |
| `NewPricePerMessage` | decimal(18,2) | No | Required for set-price action; absent for deactivation. |
| `PricingIsActive` | boolean | Yes | Resulting pricing-active state. |
| `ChangedByAdminUserId` | string | Yes | Admin actor. |
| `Reason` | string | Yes | Required/recorded reason. |
| `CreatedAtUtc` | datetime | Yes | Effective time for future eligibility. |

Rules:

- Positive monetary price is required for set-price.
- Deactivation is a separate action and must not write null/zero as an inactive price marker.
- Existing delivery snapshots and historical reports do not change.

### PlatformFeePolicyHistoryEntry

Existing immutable policy history used for current/future activation snapshots.

Rules:

- Fee percent must be greater than 0 and less than or equal to 100 using supported precision.
- New policy affects future activation snapshots only.
- Existing delivery snapshots, settlement evidence, company reporting, and ledgers do not change.

### AdminStatisticsSnapshot

Read-only calculated response for one bounded date range.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `FromDateEgypt` | date | Yes | Inclusive lower bound. |
| `ToDateEgypt` | date | Yes | Inclusive upper bound. |
| `AccountCounts` | object | Yes | Counts by role/status. |
| `ReviewCounts` | object | Yes | Pending account/file/campaign/withdrawal counts. |
| `CampaignCounts` | object | Yes | Campaign counts by status. |
| `DeliveryOutcomeCounts` | object | Yes | Active/accepted/rejected/expired counts. |
| `InteractionOutcomeCounts` | object | Yes | Accepted/rejected/feedback counts. |
| `WithdrawalStatusCounts` | object | Yes | Requested/approved/rejected/paid/failed counts. |
| `WalletMovementSummary` | object | No | Withheld when financial evidence inconsistent. |
| `PricingPolicySummary` | object | Yes | Current policy and change counts. |
| `EnforcementActionCounts` | object | Yes | Warning/reduce/suspend/reactivate counts. |
| `WithheldFinancialScopes` | string array | Yes | Empty when all financial evidence reconciles. |

Rules:

- Date range is inclusive and may not exceed 90 days.
- Statistics are read-only and do not create repair records.
- Inconsistent financial evidence withholds affected financial totals and returns non-financial totals.

### AdminDecisionRecord

Audit-preserved evidence for admin decisions. Existing audit tables may already represent this; add only missing fields where necessary.

Required evidence:

- Actor admin user id
- Target type and target id
- Prior state and resulting state
- Public reason when owner-visible
- Internal note when admin-only
- Decision timestamp
- Correlation evidence
- No raw stack traces, storage keys, provider payloads, raw idempotency material, or payout destination data

## Relationships

- `WithdrawalRequest.DoctorId` references `DoctorProfile.Id`.
- `WithdrawalRequest.ReviewedByAdminUserId` references Admin user identity when reviewed.
- `WithdrawalHoldLedgerEvidence.WithdrawalRequestId` references `WithdrawalRequest.Id`.
- Wallet transaction and ledger evidence may reference `WithdrawalRequestId` for hold, release, and finalization operations.
- `DoctorPriceHistoryEntry.DoctorId` references `DoctorProfile.Id`.
- `AdminWorkQueueItem` is a projection over source entities and does not own source state.
- `AdminStatisticsSnapshot` is a response/read model and does not persist aggregate totals.

## State Transitions

### Withdrawal Request

```text
Requested -> Approved -> Paid
Requested -> Approved -> Failed
Requested -> Rejected
```

Transition effects:

- `Requested`: create request and hold amount atomically.
- `Approved`: record admin decision, preserve hold.
- `Rejected`: record admin decision, release hold.
- `Paid`: record payout reference, finalize held amount as paid.
- `Failed`: record failure reason, release hold if funds did not leave platform.

### Doctor Pricing

```text
ActivePrice -> ActivePrice       (set new positive price)
ActivePrice -> PricingInactive   (deactivate pricing)
PricingInactive -> ActivePrice   (set new positive price)
```

Transition effects:

- New valid positive price affects future campaign activation eligibility and snapshots only.
- PricingInactive blocks future paid campaign activation for that doctor.
- Existing delivery snapshots and settled evidence are immutable.

## Index Guidance

Add or verify indexes for:

- `WithdrawalRequests(Status, RequestedAtUtc, Id)`
- `WithdrawalRequests(DoctorId, RequestedAtUtc, Id)`
- `WithdrawalRequests(ReviewedAtUtc, ReviewedByAdminUserId)`
- `WithdrawalRequests(PayoutReference)` where supported and useful
- Wallet transactions/ledger entries by `WithdrawalRequestId`
- Existing source tables used by work queue filters: pending account status/date, pending file review status/date, pending campaign review status/date, enforcement status/date
- Statistics read paths over 90-day windows: delivery date/status, interaction date/status, wallet transaction date/type, withdrawal requested/reviewed date/status, policy history created/effective dates, enforcement action date/type

## Validation Rules

- Admin-only endpoints require Admin JWT authorization.
- Doctor withdrawal submission requires Doctor JWT, owner profile, approved account, active state, non-suspended status, and sufficient withdrawable settled earnings.
- Monetary amounts use EGP and two decimal places.
- Payout reference is required for Paid and must not exceed 200 characters.
- Rejection and payout failure require a reason.
- Incompatible transitions return safe conflict errors and do not mutate state.
- Statistics date ranges reject malformed dates, start-after-end, and more than 90 inclusive days.
- Work queue and list endpoints reject page number below 1 and page size outside 1-100.

## Privacy and Security Rules

- No response exposes payout destination data because none is collected.
- Admin queue/statistics responses exclude raw storage keys, provider credentials, raw idempotency material, private contact details beyond authorized review need, wallet internals beyond authorized summaries, and raw stack traces.
- Doctor withdrawal lists expose only the authenticated doctor's own requests.
- Cross-role and unauthenticated attempts are denied using standard safe envelopes.

## Non-Mutating Reads

The following are read-only:

- Admin work queue
- Admin withdrawal list
- Doctor withdrawal list
- Admin statistics
- Current policy/price views

They must not mutate accounts, files, campaigns, deliveries, wallets, transactions, ledgers, pricing policy, payout status, enforcement state, or audit evidence.
