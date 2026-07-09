# Data Model: Phase 7 Delivery & Expiry Jobs

## Model Conventions

- All identifiers remain opaque string ids using the repository's current convention.
- All persisted instants are UTC `DateTime`; the business day is a `DateOnly` derived from DST-aware `Africa/Cairo`.
- Money is EGP `decimal(18,2)`. Platform fee percentages remain `decimal(5,2)`.
- SQL Server `rowversion` protects mutable financial and delivery state.
- Domain writes occur only through `IDomainUnitOfWork` repository contracts.
- Hangfire tables are scheduler infrastructure and are not part of this domain model.

## Existing Entity Changes

### DoctorMessageQueue

Represents one campaign candidate for one doctor.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key; stable FIFO tie-breaker after campaign submission time |
| `DoctorId` | string | Required FK to DoctorProfile |
| `CampaignId` | string | Required FK to Campaign |
| `QueuedAtUtc` | DateTime | Required operational enqueue timestamp; not a FIFO priority key |
| `CampaignSubmittedAtUtc` | DateTime? | Required immutable snapshot of authentic `Campaign.SubmittedAtUtc` whenever Status is Queued; terminal historical Activated/Cancelled rows may remain null and never participate in FIFO |
| `Status` | QueueItemStatus | `Queued`, `Activated`, or `Cancelled` |
| `CreatedAtUtc` | DateTime | Required |
| `UpdatedAtUtc` | DateTime? | Activation/cancellation time |
| `ConcurrencyToken` | byte[] | **New** SQL Server rowversion |

**Indexes and constraints**:

- Preserve unique `(CampaignId, DoctorId)`.
- Add/ensure scan index `(Status, DoctorId, CampaignSubmittedAtUtc, Id)`.
- Add conditional check constraint `Status <> Queued OR CampaignSubmittedAtUtc IS NOT NULL`.
- Backfill only existing Queued rows and only from the related campaign's authentic `SubmittedAtUtc`; never substitute `QueuedAtUtc`. Migration validation fails when any Queued row remains null. Historical Activated/Cancelled rows may remain null because both states are terminal and cannot return to Queued in Phase 7.
- Preserve the existing campaign-oriented status/count indexes where still useful.

**Transitions**:

```text
Queued ── successful atomic activation ──> Activated
Queued ── permanent campaign/company/doctor ineligibility ──> Cancelled
```

`Activated` and `Cancelled` are terminal in Phase 7. Paused campaigns, suspended or temporarily unapproved doctors, missing prices, insufficient company funds, unresolved company expiry, and isolated retryable failures leave the row `Queued`.

### DoctorAdDelivery

Represents one campaign delivery visible to one doctor for one Egypt business date.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key |
| `DoctorId` | string | Required FK |
| `CampaignId` | string | Required FK |
| `CompanyId` | string | Required FK; wallet owner/gating key |
| `DeliveryDateEgypt` | DateOnly | Required; derived from captured Cairo date |
| `DeliveredAtUtc` | DateTime | Required activation instant |
| `Status` | DeliveryStatus | Phase 7 writes `Active` and `Expired` |
| `ReservationStatus` | ReservationStatus | Phase 7 writes `Reserved` and `Released` |
| `PricePerMessageSnapshot` | decimal(18,2) | Positive current doctor price at activation |
| `PlatformFeePercentSnapshot` | decimal(5,2) | Exactly one effective policy at activation; greater than 0 and at most 100 |
| `PlatformFeeAmount` | decimal(18,2) | Rounded away from zero to two decimals; greater than 0 and less than price |
| `DoctorEarnings` | decimal(18,2) | Price minus rounded fee and greater than 0; not credited in Phase 7 |
| `ReservedAmount` | decimal(18,2) | Equals price snapshot |
| `ExpiredAtUtc` | DateTime? | **New**; set only on Active -> Expired |
| `ReadAtUtc`, `InteractedAtUtc`, feedback fields | existing | Not mutated by Phase 7 |
| `CreatedAtUtc`, `UpdatedAtUtc` | DateTime / DateTime? | Audit timing |
| `ConcurrencyToken` | byte[] | Existing SQL Server rowversion |

**Indexes and constraints**:

- Preserve unique `(DoctorId, DeliveryDateEgypt, CampaignId)`.
- Add expiry/gate index `(Status, ReservationStatus, DeliveryDateEgypt, CompanyId, Id)`.
- Add inbox/count index `(DoctorId, DeliveryDateEgypt, DeliveredAtUtc, Id)`.
- Preserve settlement checks: reservation equals price; fee plus earnings equals price; every snapshot is valid at two decimals.

**Transitions**:

```text
<none> ── activation ──> Active / Reserved
Active / Reserved ── no interaction after delivery date ──> Expired / Released
```

Accepted/Rejected and Charged transitions remain Phase 8 behavior.

### Wallet

Reused company wallet with `AvailableBalance`, `ReservedBalance`, EGP currency, soft-delete state, and rowversion.

**Activation**:

```text
AvailableBalance -= PricePerMessageSnapshot
ReservedBalance  += PricePerMessageSnapshot
```

**Expiry**:

```text
ReservedBalance  -= ReservedAmount
AvailableBalance += ReservedAmount
```

The wallet row is locked before rechecking funds or applying either movement. A missing/deleted wallet, currency mismatch, insufficient Available amount for activation, or insufficient Reserved amount for release creates no partial mutation.

### WalletTransaction

One immutable transaction groups each movement.

| Operation | Deterministic idempotency key | Amount | Link |
|---|---|---|---|
| Reserve | `delivery:reserve:{deliveryId}` | delivery price snapshot | `RelatedDeliveryId` |
| Release | `delivery:release:{deliveryId}` | stored reserved amount | `RelatedDeliveryId` |

Preserve unique `(OperationType, IdempotencyKey)`. Descriptions and metadata contain only safe identifiers/outcome context.

### WalletLedgerEntry

Two immutable entries balance each transaction:

| Operation | Entry 1 | Entry 2 |
|---|---|---|
| Reserve | Available / Debit / price | Reserved / Credit / price |
| Release | Reserved / Debit / reserved amount | Available / Credit / reserved amount |

Each entry references transaction, wallet, delivery, campaign, and company; currency is `EGP`. Because transaction plus both entries commit together, an existing idempotent transaction is treated as an already completed operation, not rebuilt piecemeal.

## New Entity

### DeliveryJobRun

Structured, safe operational summary for one expiry or injector invocation.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key |
| `JobType` | DeliveryJobType | `ExpiryCleaner` or `DailyInjector` |
| `BusinessDateEgypt` | DateOnly | Captured Cairo date for the run |
| `Status` | DeliveryJobRunStatus | `Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Deferred`, `Interrupted` |
| `StartedAtUtc` | DateTime | Required |
| `CompletedAtUtc` | DateTime? | Set for terminal outcome |
| `ExaminedCount` | int | Non-negative |
| `ActivatedCount` | int | Non-negative; injector only |
| `ExpiredCount` | int | Non-negative; expiry only |
| `CancelledCount` | int | Non-negative; injector only |
| `SkippedCount` | int | Non-negative |
| `FailedCount` | int | Non-negative |
| `SafeFailureSummary` | string? | Max 2000; no stacks, secrets, URLs, storage keys, wallet balances, or idempotency keys |
| `CreatedAtUtc`, `UpdatedAtUtc` | DateTime / DateTime? | Required/optional |
| `ConcurrencyToken` | byte[] | SQL Server rowversion |

**Indexes and behavior**:

- Index `(JobType, BusinessDateEgypt, StartedAtUtc)` for operational history.
- Index `(Status, StartedAtUtc)` for stale Running recovery.
- Multiple attempts per job/date are allowed and expected.
- Starting a new run marks stale prior `Running` rows for the same job type as `Interrupted`; it does not infer or repair financial state.
- `Deferred` means injector found no processable candidate because every encountered company was still expiry-gated. Mixed progress plus failures uses `PartiallySucceeded`.

### DeliveryRecoveryDispatch

Durable operational claim used only to coordinate missed-schedule enqueueing during startup/recovery.

| Field | Type | Rules |
|---|---|---|
| `Id` | string | Primary key |
| `BusinessDateEgypt` | DateOnly | Captured recovery business date |
| `JobType` | DeliveryJobType | `ExpiryCleaner` or `DailyInjector` |
| `Status` | RecoveryDispatchStatus | `Pending`, `Enqueued`, `Completed`, or `Failed` |
| `SchedulerJobId` | string? | Safe Hangfire job identifier after acknowledgement; never serialized arguments |
| `DependsOnDispatchId` | string? | Injector references the expiry dispatch when enqueued as a continuation |
| `ClaimedAtUtc`, `EnqueuedAtUtc`, `CompletedAtUtc` | DateTime / DateTime? | Operational timing |
| `SafeFailureSummary` | string? | Max 2000 and redacted |
| `ConcurrencyToken` | byte[] | SQL Server rowversion |

**Indexes and behavior**:

- Unique `(BusinessDateEgypt, JobType)` makes repeated/concurrent startup create one durable claim per required date/job pair.
- Normal transitions are `Pending -> Enqueued -> Completed`. Scheduler/coordination failure may set Failed with a safe summary, but Failed never counts as coverage and reconciliation reuses the unique claim through `Failed -> Pending`; Pending without scheduler acknowledgement is likewise retried.
- Recovery dispatch is at-least-once across a crash between claim persistence and scheduler acknowledgement. The persisted expiry/injector service methods remain idempotent and are the business-effect correctness boundary.
- The record contains operational coordination only and never delivery, queue, wallet, transaction, or ledger mutation data.

## Reused Eligibility Records

### DoctorProfile + ApplicationUser

Activation requires:

- user role Doctor, account Approved, and neither user nor profile deleted;
- doctor marketplace status Active;
- positive current `PricePerMessage` valid at two decimals;
- positive remaining `DailyMessageLimit` after counting every current-day delivery status.

Suspended/temporarily unapproved/unpriced stays Queued; deleted doctor is terminal and cancels the row. The doctor profile row is locked before recounting deliveries so concurrent injectors cannot exceed the daily limit.

### Campaign + CompanyProfile + ApplicationUser

Activation requires an undeleted campaign owned by an undeleted approved Company account and a campaign status currently deliverable (`Approved` or `Active`). `Paused` is temporary. `Rejected`, `Completed`, `Cancelled`, or deleted campaign/company is terminal for the queue row.

### PlatformFeePolicyHistory

Exactly one policy must be effective at the captured activation UTC instant. The percentage must satisfy `0 < Percent <= 100` at two-decimal precision. The rounded fee must satisfy `0 < Fee < Price`, and earnings must equal `Price - Fee` and remain greater than 0. Overlapping policies, no policy, an out-of-range percentage, or a rounded-invalid snapshot leaves the row Queued and fails that candidate safely without delivery or financial mutation. No implicit default is substituted during activation.

### StoredFile

Inbox assets must be related to the delivery campaign, stored, not deleted/replaced, and separately `Approved`. Asset metadata may appear in the inbox. A signed grant additionally requires a current-day delivery owned by the requesting doctor.

## Read Models and Repository Contracts

### Queue scan models

- `QueuedDoctorCursor`: stable doctor id for keyset paging doctors that still have Queued rows.
- `DeliveryQueueCandidateReadModel`: queue id, doctor id, campaign id, immutable `CampaignSubmittedAtUtc`, and stable id used only to select the next candidate; all mutable state is reloaded under lock.

Repository extensions provide:

- keyset pages of distinct doctors with Queued rows;
- keyset pages of a doctor's Queued rows ordered `CampaignSubmittedAtUtc`, then id;
- queue row retrieval for update with SQL Server update/row locks;
- terminal/activated transition methods that require the expected Queued state.

### Delivery processing models

Repository extensions provide:

- keyset pages of overdue Active/Reserved delivery ids ordered by date, creation time, then id;
- delivery retrieval for update with row lock and expected Active/Reserved state;
- existence of overdue Active/Reserved deliveries for a company/date gate;
- current-day delivery count for a doctor;
- unique-delivery existence/replay lookup;
- state transition to Expired/Released with `ExpiredAtUtc`.

### Today inbox models

`TodayDeliveryReadModel` contains delivery/campaign message fields only. The repository reads `PageSize + 1` rows in ascending persisted activation order by `DeliveredAtUtc`, then id, where `PageSize` defaults to 50 and is bounded from 1 through 100. The service returns at most `PageSize` items and emits nullable `NextCursor` when another row exists. `CreatedAtUtc` is audit metadata and is never the inbox ordering key. A second bounded query loads Approved active file metadata for the distinct campaign ids in the returned page, then the service groups files without N+1 calls.

`TodayInboxCursor` is opaque to clients and carries the captured Egypt business date plus the last `DeliveredAtUtc, Id` ordering pair. Cursor validation is scoped to the authenticated Doctor; malformed, cross-doctor, and stale-date cursors are rejected without exposing delivery existence.

`DeliveryAssetAuthorizationReadModel` joins delivery, doctor, campaign, and file identity for a current Cairo date without returning raw storage credentials to the API layer.

### Job-run repository

`IDeliveryJobRunRepository` supports begin, stale-run interruption, and terminal update. It is exposed on `IDomainUnitOfWork` and implemented only in `MediBridge.Repository`.

### Recovery-dispatch repository

`IDeliveryRecoveryDispatchRepository` atomically finds or creates one `(BusinessDateEgypt, JobType)` claim, determines whether a current-date run or dispatch already covers required work, records scheduler acknowledgement/job id and dependency without serialized arguments, reconciles Pending claims after interruption, and marks completion from observed job-run outcomes. It is exposed on `IDomainUnitOfWork` and implemented only in `MediBridge.Repository`.

## Atomic Business Actions

### Expire one delivery

1. Begin Unit of Work transaction.
2. Lock and re-read the delivery; no-op if no longer Active/Reserved or no longer overdue.
3. Check deterministic Release transaction; an exact existing completed operation is a replay no-op, while inconsistent state is a failure.
4. Lock the active EGP company wallet and verify Reserved balance covers `ReservedAmount`.
5. Stage delivery Expired/Released and timestamps.
6. Stage wallet Reserved debit and Available credit.
7. Add one Release transaction and two ledger entries.
8. Commit once. Concurrency/unique conflicts roll back all steps and are reclassified from fresh state.

### Activate one queue row

1. Begin Unit of Work transaction.
2. Lock the doctor profile and revalidate Doctor user/profile eligibility.
3. Lock and re-read the expected Queued row.
4. Recount all current-date deliveries; stop if limit is filled.
5. Lock/revalidate campaign and owning Company eligibility.
6. Cancel terminal rows; preserve temporary rows.
7. Check for any overdue Active/Reserved delivery owned by the company; if present, preserve Queued.
8. Resolve current doctor price and exactly one active fee policy; compute deterministic snapshots.
9. Derive a candidate delivery id, then check unique delivery and deterministic Reserve replay state.
10. Lock the EGP company wallet and verify Available balance.
11. Stage Active/Reserved delivery, queue Activated, wallet transfer, one Reserve transaction, and two ledger entries.
12. Commit once. Conflicts roll back and are reclassified from fresh state.

## Migration Impact

One Phase 7 migration must:

- add `DoctorMessageQueues.ConcurrencyToken` rowversion;
- backfill null `CampaignSubmittedAtUtc` only for Queued rows and only from related `Campaign.SubmittedAtUtc`; abort if a Queued row remains null; add `Status <> Queued OR CampaignSubmittedAtUtc IS NOT NULL`; leave terminal historical nulls unchanged and never use `QueuedAtUtc`;
- add `DoctorAdDeliveries.ExpiredAtUtc` nullable datetime;
- create `DeliveryJobRuns` with checks for non-negative counters and rowversion;
- create `DeliveryRecoveryDispatches` with unique date/job claim, safe metadata, and rowversion;
- add exact queue, expiry/gate, inbox/count, job-run, and recovery-dispatch indexes;
- preserve existing delivery uniqueness, wallet checks, transaction idempotency, and foreign keys;
- contain no data rewrite that activates, expires, reserves, or releases historical records.
