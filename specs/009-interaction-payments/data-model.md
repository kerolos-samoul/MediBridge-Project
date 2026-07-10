# Data Model: Phase 8 Interaction & Payments

## Model Conventions

- All identifiers remain opaque string ids using the repository's current convention.
- All persisted instants are UTC `DateTime`; the current business date is a `DateOnly` derived from DST-aware `Africa/Cairo`.
- Money is EGP `decimal(18,2)`. Platform fee percentages remain the already stored activation snapshot.
- SQL Server `rowversion` protects mutable delivery and wallet state.
- Domain writes occur only through `IDomainUnitOfWork` repository contracts.
- Read tracking and interaction settlement are Doctor-owned delivery operations; company and doctor wallet mutations are side effects of successful interaction only.

## Existing Entity Changes

### DoctorAdDelivery

Represents one campaign delivery visible to one doctor for one Egypt business date.

| Field | Type | Phase 8 Rules |
|---|---|---|
| `Id` | string | Primary key and settlement id |
| `DoctorId` | string | Must match authenticated approved Doctor profile |
| `CampaignId` | string | Required existing campaign relationship |
| `CompanyId` | string | Required company wallet owner for Charge |
| `DeliveryDateEgypt` | DateOnly | Must equal captured current Egypt date for read or interaction |
| `DeliveredAtUtc` | DateTime | Existing activation instant; not changed |
| `ReadAtUtc` | DateTime? | Set only by read endpoint, first successful read wins, never changed by interaction |
| `Status` | DeliveryStatus | Active can become Accepted or Rejected; Expired/Accepted/Rejected are not interactable |
| `InteractedAtUtc` | DateTime? | Set once on successful Accept/Reject |
| `FeedbackText` | string? | Optional trimmed feedback; null when absent or empty after trim; max 1,000 characters |
| `FeedbackCreatedAtUtc` | DateTime? | Set when non-empty feedback is stored |
| `FeedbackQualityStatus` | FeedbackQualityStatus? | Left null unless existing enum conventions require a pending value; review is later scope |
| `PricePerMessageSnapshot` | decimal(18,2) | Positive stored activation price; must equal ReservedAmount |
| `PlatformFeePercentSnapshot` | decimal | Existing activation snapshot; not recalculated |
| `PlatformFeeAmount` | decimal(18,2) | Stored rounded fee; must be positive and combine with DoctorEarnings to equal price |
| `DoctorEarnings` | decimal(18,2) | Stored amount credited to Doctor wallet |
| `ReservedAmount` | decimal(18,2) | Amount removed from company Reserved balance |
| `ReservationStatus` | ReservationStatus | Reserved becomes Charged on successful interaction |
| `ExpiredAtUtc` | DateTime? | Not changed by Phase 8 |
| `ConcurrencyToken` | byte[] | Existing rowversion for financial state |
| `CreatedAtUtc`, `UpdatedAtUtc` | DateTime / DateTime? | Updated only by first read or settlement |

**Validation and constraints**:

- Read requires authenticated Doctor ownership, current Egypt date, and visible content; it does not require Active/Reserved state if already Accepted or Rejected for the current day remains visible, but it must not operate on Expired/prior-day deliveries.
- Interaction requires Active/Reserved, current Egypt date, no prior `InteractedAtUtc`, positive monetary snapshots, `ReservedAmount == PricePerMessageSnapshot`, and `PlatformFeeAmount + DoctorEarnings == PricePerMessageSnapshot`.
- Interaction does not require `ReadAtUtc` and does not backfill it.
- If persistence currently allows feedback up to 4,000 characters, the service must enforce 1,000 characters and the repository configuration or migration should narrow the database boundary where safe.

**Transitions**:

```text
Active / Reserved ── Accept ──> Accepted / Charged
Active / Reserved ── Reject ──> Rejected / Charged
```

`Accepted`, `Rejected`, and `Expired` are terminal for Phase 8 settlement.

### Company Wallet

The company wallet is locked and rechecked during settlement.

**Charge**:

```text
ReservedBalance -= DoctorAdDelivery.ReservedAmount
```

Rules:

- Wallet must belong to `CompanyId`, be active/not deleted, and use EGP.
- `ReservedBalance` must cover exactly the delivery reserved amount.
- Available balance does not change during Charge.

### Doctor Wallet

The doctor wallet is locked and rechecked during settlement.

**Earn**:

```text
AvailableBalance += DoctorAdDelivery.DoctorEarnings
```

Rules:

- Wallet must belong to `DoctorId`, be active/not deleted, and use EGP.
- Reserved balance does not change during Earn.
- Missing doctor wallet is a consistency failure and creates no partial mutation.

### WalletTransaction

One immutable transaction is written for each financial side.

| Operation | Wallet owner | Idempotency key | Amount | Balance effect | Link |
|---|---|---|---|---|---|
| Charge | Company | `delivery:charge:{deliveryId}` | `ReservedAmount` | Reserved debit | `RelatedDeliveryId` |
| Earn | Doctor | `delivery:earn:{deliveryId}` | `DoctorEarnings` | Available credit | `RelatedDeliveryId` |

Preserve unique `(OperationType, IdempotencyKey)`. Descriptions and metadata contain only safe ids and outcome labels. Raw client idempotency keys should not appear in transaction descriptions, logs, or ledger metadata.

### WalletLedgerEntry

Each transaction has one immutable ledger entry in Phase 8:

| Operation | Entry |
|---|---|
| Charge | Company wallet / Reserved / Debit / reserved amount |
| Earn | Doctor wallet / Available / Credit / doctor earnings |

Each entry references transaction, wallet, delivery, campaign, company or doctor owner context, and EGP currency according to the existing ledger model. Charge and Earn entries are committed in the same Unit of Work transaction as the delivery transition.

## New or Extended Evidence

### Interaction Operation Evidence

Required for Phase 8. `DeliveryInteractionOperation` is the authoritative client replay evidence for Doctor + delivery + `Idempotency-Key` + normalized payload classification; wallet transactions and audit rows are supporting evidence only and MUST NOT be used alone as the client replay authority.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key |
| `DoctorId` | string | Authenticated Doctor profile id |
| `DeliveryId` | string | Target delivery |
| `IdempotencyKey` | string | Normalized required header, 8-128 characters |
| `Decision` | DeliveryStatus or interaction enum | Accepted or Rejected only |
| `FeedbackText` | string? | Normalized text used for replay comparison, or a deterministic fingerprint if text is stored only on delivery |
| `Status` | string/enum | Created, Replayed, or Conflict classification as needed |
| `ChargeTransactionId` | string? | Linked successful Charge transaction |
| `EarnTransactionId` | string? | Linked successful Earn transaction |
| `CreatedAtUtc`, `CompletedAtUtc` | DateTime / DateTime? | UTC operation timing |
| `SafeFailureSummary` | string? | Optional redacted failure category; no raw stacks, balances, or idempotency material |
| `ConcurrencyToken` | byte[] | SQL Server rowversion if mutable |

**Indexes and constraints**:

- Unique `(DoctorId, DeliveryId, IdempotencyKey)`.
- Index `(DeliveryId, Decision)` for replay lookup.
- Length constraint on normalized `IdempotencyKey` is 8-128.
- Feedback stored here, if present, follows the same 1,000-character cap.

Do not replace this model with ad hoc fields on `DoctorAdDelivery`. The implementation must create and persist `DeliveryInteractionOperation` so same-key replay, same-key conflict, and different-key already-settled behavior can be classified without duplicate financial effects.

## DTOs and Read Models

### MarkReadResultDto

| Field | Type | Rules |
|---|---|---|
| `DeliveryId` | string | Target delivery |
| `ReadAtUtc` | DateTime | First stored read timestamp |
| `ReadStatus` | string | `Created` for first read or `Replayed` when already read |

### InteractDeliveryRequestDto

| Field | Type | Rules |
|---|---|---|
| `Decision` | string | Required; `Accept` or `Reject` |
| `FeedbackText` | string? | Optional; trim; empty means absent; max 1,000 after trim |

### InteractDeliveryResultDto

| Field | Type | Rules |
|---|---|---|
| `DeliveryId` | string | Target delivery |
| `Status` | string | `Accepted` or `Rejected` |
| `InteractedAtUtc` | DateTime | First successful interaction timestamp |
| `FeedbackText` | string? | Normalized stored feedback when present |
| `IdempotencyStatus` | string | `Created` or `Replayed` |

Wallet balances, platform fee internals, raw idempotency keys, and storage details are not returned.

## Repository Contracts

### Delivery repository extensions

- Find a Doctor-owned current-date delivery for read with update lock.
- Mark first read timestamp and return whether it was newly created or replayed.
- Find an Active/Reserved Doctor-owned current-date delivery for interaction with update lock.
- Find already settled Doctor-owned delivery by id for same-result replay classification.
- Persist Accepted/Rejected state, Charged reservation state, interaction timestamp, and normalized feedback atomically with the Unit of Work.

### Wallet repository extensions

- Find and lock company wallet by owner for charge.
- Find and lock doctor wallet by owner for earn.
- Apply reserved debit and available credit mutations without allowing negative balances.

### Wallet transaction and ledger repositories

- Find Charge/Earn transaction by deterministic key.
- Add Charge/Earn transactions and ledger entries within the same Unit of Work.
- Verify exact replay evidence when deterministic transactions already exist.

### Interaction operation repository

If added, repository supports:

- Find by `(DoctorId, DeliveryId, IdempotencyKey)` for update.
- Add pending/success evidence.
- Link Charge/Earn transaction ids after successful settlement.
- Classify same-payload replay vs conflict.

## Atomic Business Actions

### Mark delivery read

1. Resolve authenticated approved active Doctor profile.
2. Capture Egypt business-clock snapshot once.
3. Begin Unit of Work transaction.
4. Lock/re-read delivery by id, Doctor id, and current Egypt date.
5. Reject absent, prior-day, expired, cross-owner, or invisible delivery safely.
6. If `ReadAtUtc` is null, set it to captured UTC and update `UpdatedAtUtc`; otherwise leave it unchanged.
7. Save changes and add safe audit evidence.
8. Return first read timestamp with `Created` or `Replayed`.

No wallet, transaction, ledger, reservation, or interaction fields are mutated.

### Interact with delivery

1. Validate required `Idempotency-Key` and request body before starting settlement.
2. Normalize decision and feedback; reject invalid decision or feedback over 1,000 characters.
3. Resolve authenticated approved active Doctor profile.
4. Capture Egypt business-clock snapshot once.
5. Begin isolated Unit of Work transaction.
6. Lock/re-read delivery by id, Doctor id, current Egypt date, Active status, and Reserved reservation.
7. If no Active/Reserved row exists, check existing interaction/evidence for same-result replay or conflict; otherwise return safe not-found/conflict without mutation.
8. Lock or create interaction operation evidence for Doctor + delivery + key and classify same-payload replay vs conflict.
9. Validate stored monetary snapshots and current wallet relationships.
10. Lock company wallet, then doctor wallet, and recheck balances.
11. Check deterministic Charge and Earn transactions. Exact existing completed pair means replay; inconsistent partial evidence is a consistency failure.
12. Mark delivery Accepted or Rejected, set `InteractedAtUtc`, set `ReservationStatus = Charged`, store normalized feedback if present, and leave `ReadAtUtc` unchanged.
13. Debit company Reserved by `ReservedAmount`.
14. Credit doctor Available by `DoctorEarnings`.
15. Add Charge transaction and company Reserved Debit ledger entry.
16. Add Earn transaction and doctor Available Credit ledger entry.
17. Link interaction operation evidence and add safe audit event.
18. Commit. On any failure, all delivery, wallet, transaction, ledger, and evidence changes roll back.

## State and Replay Matrix

| Existing state | Request | Result |
|---|---|---|
| Active/Reserved current-day, no interaction | Valid Accept/Reject with new key | Settle once, return Created |
| Active/Reserved current-day, no read | Valid Accept/Reject | Settle once, leave `ReadAtUtc` null |
| Already Accepted with same Doctor/delivery/key/decision/feedback | Replay | Return Replayed, no mutation |
| Already Rejected with same Doctor/delivery/key/decision/feedback | Replay | Return Replayed, no mutation |
| Same Doctor/delivery/key but different decision or feedback | Conflict | Preserve original outcome, no mutation |
| Different key after already settled with same decision | Already settled | Return existing settled result, do not mutate delivery state, feedback, wallets, transactions, ledgers, or audit settlement evidence |
| Different key after already settled with different decision | Conflict | Preserve original outcome, no mutation |
| Expired, Released, prior-day, future-day, cross-owner, missing wallet, inconsistent balance | Any interaction | Safe rejection, no mutation |

## Indexes and Migration Considerations

- Existing unique `(DoctorId, DeliveryDateEgypt, CampaignId)` remains authoritative for delivery uniqueness.
- Add/ensure point lookup index `(DoctorId, DeliveryDateEgypt, Id)` for read and interact ownership checks if existing indexes do not cover it.
- Add/ensure settlement scan/support index `(Status, ReservationStatus, DeliveryDateEgypt, DoctorId, Id)` if current Phase 7 expiry/inbox indexes do not cover Active/Reserved point locks efficiently.
- Add unique interaction operation index `(DoctorId, DeliveryId, IdempotencyKey)` if a dedicated evidence table is introduced.
- Preserve wallet transaction unique `(OperationType, IdempotencyKey)`.
- Narrow or constrain feedback text to 1,000 characters where practical; otherwise enforce in service and add tests proving database writes never exceed the clarified limit.

## Audit and Redaction

- Audit event types include read created/replayed, interaction settled, interaction replayed, interaction conflict, invalid delivery state, missing wallet, inconsistent monetary snapshot, and settlement consistency failure.
- Audit metadata may include actor id, Doctor id, delivery id, campaign id, company id, decision, outcome category, and safe timestamps.
- Audit metadata must not include raw idempotency keys, wallet balances, storage keys, signed URLs, raw exception stacks, or full feedback text.
