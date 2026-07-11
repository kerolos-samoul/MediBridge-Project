# Data Model: Phase 8 Interaction & Payments

## Model Conventions

- All identifiers remain opaque string ids using the repository's current convention.
- All persisted instants are UTC `DateTime`; current-day checks use a `DateOnly` from DST-aware `Africa/Cairo`.
- Money is EGP `decimal(18,2)`. Platform fee percentages remain stored snapshots.
- SQL Server `rowversion` protects mutable delivery and wallet state.
- Domain writes occur only through `IDomainUnitOfWork` repository contracts.
- Raw idempotency material, wallet internals, and stack traces are never persisted, logged, written to public responses, or written to audit summaries.

## Existing Entity Changes

### DoctorAdDelivery

Represents one campaign delivery visible to one doctor for one Egypt business date.

| Field | Type | Phase 8 Rules |
|---|---|---|
| `Id` | string | Primary key and settlement aggregate id |
| `DoctorId` | string | Required owner; must match authenticated Doctor profile |
| `CampaignId` | string | Required campaign link |
| `CompanyId` | string | Required company wallet owner |
| `DeliveryDateEgypt` | DateOnly | Must equal captured Cairo date for read/interact |
| `DeliveredAtUtc` | DateTime | Existing activation instant |
| `ReadAtUtc` | DateTime? | Set once on read tracking; first-write-wins |
| `Status` | DeliveryStatus | Phase 8 writes `Accepted` or `Rejected` only from `Active` |
| `InteractedAtUtc` | DateTime? | Set once when Accept/Reject settles |
| `FeedbackText` | string? | Normalized optional plain-text feedback; max 2,000 chars after trimming; no markup delimiters or blocked URI/link patterns |
| `FeedbackCreatedAtUtc` | DateTime? | Set when normalized feedback is stored |
| `FeedbackQualityStatus` | FeedbackQualityStatus? | Marks later scoring eligibility; at least 15 non-whitespace chars qualifies |
| `PricePerMessageSnapshot` | decimal(18,2) | Existing activation snapshot; authoritative at settlement |
| `PlatformFeePercentSnapshot` | decimal(5,2) | Existing activation snapshot; authoritative at settlement |
| `PlatformFeeAmount` | decimal(18,2) | Existing rounded fee; must satisfy stored formula |
| `DoctorEarnings` | decimal(18,2) | Existing stored earning; credited on interaction |
| `ReservedAmount` | decimal(18,2) | Existing reserved amount; company Reserved is debited on interaction |
| `ReservationStatus` | ReservationStatus | Phase 8 changes `Reserved` to `Charged` |
| `ConcurrencyToken` | byte[] | SQL Server rowversion |

**Indexes and constraints**:

- Preserve unique `(DoctorId, DeliveryDateEgypt, CampaignId)`.
- Preserve Phase 7 inbox/count and expiry indexes.
- Add or ensure lookup index for `(DoctorId, Id, DeliveryDateEgypt, Status, ReservationStatus)` to support owner/current-day interaction lookup.
- Preserve monetary check constraints that make fee, earnings, and reserved amount positive and internally balanced.

**Transitions**:

```text
Active / Reserved ── read tracking ──> Active / Reserved
Active / Reserved ── Accept ──> Accepted / Charged
Active / Reserved ── Reject ──> Rejected / Charged
Expired / Released ── interaction attempt ──> no mutation
Accepted|Rejected / Charged ── replay ──> no mutation
```

Read tracking may occur before or after settlement on the owning doctor's current-day delivery but never changes outcome, reservation, wallet, transaction, ledger, platform-fee, or audit state.

### Wallet

Reused company and doctor wallets with `AvailableBalance`, `ReservedBalance`, EGP currency, soft-delete state, and rowversion.

**Interaction settlement**:

```text
Company ReservedBalance -= ReservedAmount
Doctor  AvailableBalance += DoctorEarnings
```

Company Available is unchanged; Doctor Reserved is unchanged. Wallet rows are locked before applying changes. Missing, deleted, wrong-currency, or insufficient-reserved wallets cause a safe anomaly and no partial mutation.

### WalletTransaction

One immutable transaction is created for each financial owner effect.

| Operation | Deterministic idempotency key | Amount | Owner |
|---|---|---|---|
| Charge | `delivery:charge:{deliveryId}` | stored reserved amount | Company wallet |
| Earn | `delivery:earn:{deliveryId}` | stored doctor earnings | Doctor wallet |

Preserve unique `(OperationType, IdempotencyKey)`. Existing Reserve and Release rows are reused for replay/anomaly checks but are not changed.

### WalletLedgerEntry

Ledger entries remain append-only.

| Operation | Entry |
|---|---|
| Charge | Company Reserved / Debit / stored reserved amount |
| Earn | Doctor Available / Credit / stored doctor earnings |

Each entry references transaction, wallet, delivery, campaign, owner, currency, and safe metadata. Transaction plus ledger entries commit with the delivery state change.

### AuditEvent

Existing policy/audit persistence is reused for safe interaction evidence.

| Event | Required Safe Data |
|---|---|
| Settlement succeeded | Delivery id, actor user id/profile id, outcome, amount categories, timestamp, correlation id |
| Idempotency conflict | Delivery id if known, actor user id/profile id, conflict category, timestamp, correlation id |
| Reservation/snapshot anomaly | Delivery id, anomaly category, timestamp, correlation id |

Audit records must not include raw idempotency keys, stack traces, wallet balances, storage locations, or full sensitive financial internals.

## New Entity

### DeliveryInteraction

Represents one accepted interaction request and its replay/conflict evidence.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key |
| `DeliveryId` | string | Required; one successful interaction per delivery |
| `DoctorId` | string | Required owner profile id |
| `ActorUserId` | string | Required authenticated user id |
| `Outcome` | DeliveryInteractionOutcome | `Accept` or `Reject` |
| `IdempotencyKeyHash` | string | Required; stores only the normalized Doctor-scoped hash/fingerprint, never the raw key |
| `RequestFingerprint` | string | Required; hash of delivery id, outcome, and normalized feedback |
| `FeedbackText` | string? | Normalized optional feedback copy or null |
| `FeedbackQualifiesForScore` | bool | True when feedback has at least 15 non-whitespace characters |
| `ChargeTransactionId` | string | Required after settlement |
| `EarnTransactionId` | string | Required after settlement |
| `AuditEventId` | string? | Safe settlement audit evidence |
| `CreatedAtUtc` | DateTime | Required |
| `ConcurrencyToken` | byte[] | SQL Server rowversion |

**Indexes and constraints**:

- Unique `DeliveryId` for successful interactions.
- Unique `IdempotencyKeyHash` within the Doctor interaction scope.
- Check outcome is Accept or Reject.
- Check feedback length <= 2,000 when non-null.
- FK to `DoctorAdDelivery`, Charge and Earn `WalletTransaction`, and optional `AuditEvent`.

**Replay classification**:

- Existing matching `IdempotencyKeyHash` + matching `RequestFingerprint`: return original result.
- Existing matching `IdempotencyKeyHash` + different fingerprint: conflict, no mutation.
- Existing delivery interaction with different key but matching outcome/feedback: return settled result without new financial effects.
- Existing delivery interaction with different key and conflicting outcome/feedback: conflict, no mutation.

## Read Models and DTOs

### ReadTrackingResultDto

| Field | Type | Rules |
|---|---|---|
| `DeliveryId` | string | Delivery id |
| `Status` | DeliveryStatus | Current delivery status |
| `ReadAtUtc` | DateTime | First read timestamp |
| `AlreadyRead` | bool | True when replay preserved an existing value |

No wallet, storage, or administrative fields are returned.

### DoctorInteractionRequestDto

| Field | Type | Rules |
|---|---|---|
| `Outcome` | string | Required; Accept or Reject only |
| `Feedback` | string? | Optional plain text; trim, allow empty, reject blocked markup patterns or >2,000 chars |

The `Idempotency-Key` is a required request header, not a body field. It is accepted only as transient input and is immediately normalized into non-sensitive hashes/fingerprints before persistence, audit, logging, or diagnostics.

### Feedback Plain-Text Validation

After trimming, omitted, empty, and whitespace-only feedback are allowed. Non-empty feedback may contain ordinary letters, numbers, whitespace, line breaks, and punctuation except markup delimiters. Reject before any mutation when feedback contains any of these case-sensitive or case-insensitive blocked patterns:

- literal `<` or `>` characters;
- encoded angle brackets `&lt;` or `&gt;` in any casing;
- Markdown links or images matching `[text](url)` or `![alt](url)` forms;
- URI schemes `javascript:` or `data:` in any casing.

### DoctorInteractionResultDto

| Field | Type | Rules |
|---|---|---|
| `DeliveryId` | string | Delivery id |
| `Status` | DeliveryStatus | Accepted or Rejected |
| `ReservationStatus` | ReservationStatus | Charged |
| `InteractedAtUtc` | DateTime | Original settlement timestamp |
| `ReadAtUtc` | DateTime? | First read timestamp if present |
| `FeedbackAccepted` | bool | True when normalized non-empty feedback was stored |
| `FeedbackQualifiesForScore` | bool | True when stored feedback has at least 15 non-whitespace characters |
| `ChargeAmount` | decimal | Stored reserved amount charged from company Reserved |
| `DoctorEarnings` | decimal | Stored earning credited to doctor |
| `PlatformFeeAmount` | decimal | Stored platform fee evidence |
| `Replayed` | bool | True for idempotent replay |

No wallet balances, raw idempotency material, or audit internals are returned.

## Repository Contract Additions

### IDeliveryRepository

- `FindOwnedCurrentDayForReadAsync(doctorId, deliveryId, businessDateEgypt)` returns an owned current-day Active, Accepted, or Rejected delivery for update or null.
- `TryMarkReadAsync(deliveryId, readAtUtc)` sets `ReadAtUtc` when null and preserves existing value.
- `FindOwnedActiveReservedForInteractionAsync(doctorId, deliveryId, businessDateEgypt)` locks and returns an Active/Reserved delivery or null.
- `FindSettledInteractionReplayAsync(doctorId, deliveryId)` returns accepted/rejected replay state.
- `TryMarkInteractedAndChargedAsync(deliveryId, outcome, interactedAtUtc, feedbackText, feedbackQualityStatus)` transitions Active/Reserved to Accepted/Charged or Rejected/Charged.

### IDeliveryInteractionRepository

- `FindByIdempotencyHashAsync(doctorId, idempotencyKeyHash)`.
- `FindByDeliveryIdAsync(deliveryId)`.
- `AddInteractionAsync(DeliveryInteraction interaction)`.
- All methods are exposed through `IDomainUnitOfWork` and implemented only in `MediBridge.Repository`.

### Wallet Repositories

Existing wallet repositories are reused for wallet lock/update, transaction idempotency lookup, transaction creation, and ledger entry creation. Phase 8 may add helper methods for Charge/Earn replay classification, but they must remain repository abstractions.

## Atomic Business Actions

### Mark one delivery as read

1. Resolve approved active Doctor profile from actor user id.
2. Capture Cairo business-date snapshot.
3. Enforce `RateLimitPolicyNames.DoctorInteraction` before mutation.
4. Begin Unit of Work transaction only if a write is required.
5. Lock/re-read the owned current-day delivery; Active, Accepted, and Rejected statuses are eligible for read tracking.
6. If `ReadAtUtc` is null, set it to captured UTC time; otherwise preserve original.
7. Commit. Outcome, reservation status, wallet balances, wallet transactions, ledger entries, platform-fee evidence, and financial audit records are not changed.

### Settle one interaction

1. Resolve approved active Doctor profile from actor user id.
2. Validate required `Idempotency-Key`, normalize it into non-sensitive hashes/fingerprints, and discard the raw key from all persistence/logging/audit paths.
3. Normalize feedback; reject blocked markup patterns or >2,000 characters before mutation.
4. Capture Cairo business-date snapshot.
5. Begin Unit of Work transaction.
6. Lock/re-read idempotency evidence by Doctor/key and classify replay or conflict.
7. Lock/re-read owned Active/Reserved delivery for current Cairo date.
8. Validate stored snapshot formula and Reserved reservation state.
9. Lock company wallet and verify Reserved balance covers `ReservedAmount`.
10. Lock or create eligible Doctor wallet according to existing wallet rules.
11. Check Charge and Earn deterministic transaction keys for exact replay/conflict.
12. Stage delivery Accepted/Charged or Rejected/Charged, interaction timestamp, feedback, and quality marker.
13. Stage company Reserved debit, Doctor Available credit, Charge transaction, Earn transaction, ledger entries, interaction evidence, and required audit evidence.
14. Commit once. Concurrency, unique, or stale-state failures roll back all changes and are reclassified from fresh state.

## Migration Impact

One Phase 8 migration should:

- create `DeliveryInteractions` or equivalent interaction evidence with unique delivery and Doctor/idempotency constraints;
- add any needed delivery current-day interaction lookup indexes;
- add Charge/Earn foreign-key relationships from interaction evidence to wallet transactions;
- add check constraints for feedback length and valid outcome where applicable;
- preserve existing delivery uniqueness, reservation money checks, wallet balance checks, transaction idempotency, and Phase 7 job tables;
- contain no data rewrite that reads, interacts, charges, earns, releases, activates, expires, or repairs historical business rows.
