# Internal Persistence Contracts: Database & Core Models (Phase 3)

Phase 3 does not add public API endpoints. These contracts define the service-facing persistence boundary that `MediBridge.Services` can consume without depending on EF Core or SQL Server types.

## Contract Rules

- Contracts live in `MediBridge.Core`.
- Implementations live in `MediBridge.Repository`.
- Services may depend on these contracts.
- API controllers must not depend on EF Core, `MediBridgeDbContext`, or repository implementations.
- Phase 3 must not add public validation endpoints or diagnostic controllers.
- All methods that mutate wallet balances, wallet transactions, and wallet ledger entries must be usable inside one unit-of-work transaction.
- Repository query methods return active records by default when the entity supports soft delete.
- History and ledger creation methods are append-only.
- Wallet ledger entries are immutable; corrections use compensating transactions such as `Refund` or `Release`.

## IDomainUnitOfWork

**Purpose**: Coordinate Phase 3 repository access and atomic persistence.

**Required members**:

- `Campaigns`: campaign/target/review repository
- `MessageQueues`: doctor message queue repository
- `Deliveries`: doctor delivery repository
- `Wallets`: wallet repository
- `WalletTransactions`: wallet transaction repository
- `WalletLedgerEntries`: wallet ledger entry repository
- `StoredFiles`: stored file metadata repository
- `PolicyHistory`: price, fee, and activity history repository
- `AuditEvents`: audit event repository
- `SaveChangesAsync(cancellationToken)`
- `ExecuteInTransactionAsync(operation, cancellationToken)`
- `ExecuteInTransactionAsync<T>(operation, cancellationToken)`

**Acceptance expectations**:

- Wallet balance updates, wallet transaction inserts, and wallet ledger entry inserts can commit together or roll back together.
- Services do not need direct `DbContext` access.

## Wallet Ledger Semantics

**Wallet owners**: `Doctor`, `Company`, and `Platform`

**Required transaction types**:

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

**Required behavior**:

- `TopUp`: Company available balance increases.
- `Reserve`: Company available balance decreases and reserved balance increases.
- Billable delivery: `Charge` decreases Company reserved balance, `Earn` increases Doctor available balance, and Platform fee increases Platform available balance.
- `Refund`: returns money to Company. If money is still reserved, reserved decreases and available increases. If money was already charged, a compensating `Refund` transaction returns the amount.
- `WithdrawRequest`: optionally moves Doctor available balance to reserved balance.
- `WithdrawApproved`: status/audit record, usually no movement if money is already reserved.
- `WithdrawRejected`: returns reserved money to Doctor available balance.
- `WithdrawPayout`: decreases Doctor reserved balance and is the final ledger-impacting withdrawal transaction.

`Withdraw` is represented by the workflow `WithdrawRequest -> WithdrawApproved / WithdrawRejected -> WithdrawPayout`; do not add a standalone `Withdraw` type.

## Wallet Transaction Idempotency

**Uniqueness**: `OperationType + IdempotencyKey`

**Required behavior**:

- Creating a transaction with a duplicate operation type and idempotency key is rejected.
- Different operation types may reuse the same idempotency key.
- The repository exposes a duplicate check or creation result that services can use for retry-safe workflows.
- Idempotency keys are required for retriable money-moving operations: campaign charging, doctor earning, refunds, top-ups, and withdrawal payout.

## Soft Delete Query Contract

**Default behavior**:

- Repository methods for active workflows exclude soft-deleted records.
- Explicit historical/audit query methods may include soft-deleted records when needed.
- Soft-deleted records keep relationships intact.

## Money Precision Contract

**Required behavior**:

- Monetary input values with more than two decimal places are rejected before persistence.
- Stored EGP values use two-decimal precision.
- Future calculated fee values may use the backend plan's explicit settlement formula, but user/input amounts must not be silently rounded or truncated.

## Append-Only History Contract

**Applies to**:

- WalletTransaction
- WalletLedgerEntry
- CampaignReviewHistory
- DoctorPriceHistory
- PlatformFeePolicyHistory
- ActivityScoreHistory
- AuditEvent

**Required behavior**:

- Existing history rows are not modified or deleted to represent corrections.
- Corrections, reversals, or invalidations create new linked records.
- Metadata must not include passwords, plaintext tokens, request bodies, response bodies, or secrets.

## Queue and Delivery Persistence Contract

**Queue ordering**:

- `QueuedAtUtc` is the persisted FIFO ordering key derived from campaign submission time or queue insertion time.
- Pending queue reads are ordered by `QueuedAtUtc ASC` then `Id ASC` within doctor/status scope.
- `Id` is the deterministic tie-breaker.
- Phase 3 has no priority queue behavior.

**Delivery uniqueness**:

- Duplicate deliveries for the same `(DoctorId, DeliveryDateEgypt, CampaignId)` are rejected.
- Delivery and withdrawal mutable records include optimistic concurrency protection.

## Scope Boundary

The persistence contracts must not imply implementation of:

- Campaign creation workflow
- Queue injection job
- Delivery expiry job
- Doctor interaction settlement
- Wallet top-up endpoint
- Withdrawal endpoint
- Campaign moderation endpoint
- File upload/retrieval endpoint
- Reporting workflow or persistent reporting read model
- Public validation endpoint or diagnostic controller
