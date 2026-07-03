# Phase 3 Candidate-Isolated Transactions Design

## Scope

Remediate only Phase 3 of `008-delivery-expiry-jobs`. The change covers delivery-expiry candidate transactions, replay verification, EF Core tracking cleanup, rollback-failure handling, and the directly related automated tests. It does not add scheduling, injection, inbox, asset access, or any Phase 4 behavior.

## Root Cause

`DeliveryExpiryService` performs an unbounded candidate loop through one scoped `IDomainUnitOfWork`. The existing transaction methods are request-oriented: they intentionally retain successfully tracked entities and expose no isolated-operation semantic. Reusing that contract for a background candidate loop creates three coupled failures:

1. replay verification runs after the original transaction and calls an update-lock repository method without a surrounding transaction;
2. failure cleanup occurs after `RollbackAsync`, so a rollback exception prevents `ChangeTracker.Clear()` and may leave staged mutations available to a later candidate;
3. successful candidate graphs remain tracked for the lifetime of the job scope, causing unbounded memory and repeated EF change-detection cost.

The root cause is therefore the missing candidate-isolated transaction boundary, rather than three independent service bugs.

## Chosen Architecture

Add `ExecuteIsolatedInTransactionAsync<T>` to `IDomainUnitOfWork`. The method is explicitly for operations that return detached/scalar results and must not retain EF tracking between calls. Existing `ExecuteInTransactionAsync` methods retain their current behavior for all unrelated callers.

`DomainUnitOfWork` will share a private transaction executor that:

- begins one SQL transaction;
- executes the callback, saves once, and commits once;
- attempts rollback with a non-cancelled cleanup token after any operation/save/commit failure;
- preserves ordinary caller cancellation when rollback succeeds;
- reports both the original and rollback failures when rollback also fails;
- clears EF tracking in `finally` for isolated transactions on success or failure;
- continues clearing on failure for existing transaction methods without clearing successful unrelated transactions.

`DeliveryExpiryService` will use the isolated executor for candidate mutation and for replay verification. The locking delivery read and the transaction/ledger evidence queries will therefore observe one explicit SQL transaction and leave no tracked state behind.

## Alternatives Rejected

- **Create a DI scope per candidate**: adds service-locator coupling, repeated scope construction, and orchestration overhead.
- **Clear tracking after every existing Unit of Work transaction**: changes global semantics for unrelated request workflows and may detach results those workflows intentionally retain.
- **Use a no-lock replay query**: avoids the immediate contract violation but does not provide a coherent state/evidence snapshot and leaves the tracking lifecycle unresolved.

## Error and Cancellation Semantics

The original exception remains primary when rollback succeeds. If rollback also fails, the executor throws an aggregate containing both failures; for caller-requested cancellation it throws an `OperationCanceledException` carrying the aggregate as its inner exception and the original cancellation token. Tracking cleanup runs regardless of either failure.

The expiry service continues to redact candidate failures into its existing safe summary. Cancellation requested by the caller still exits the run rather than being counted as a candidate failure.

## Verification

Add tests proving:

- a failed first SQL-backed candidate does not contaminate a later successful candidate;
- isolated success and failure both leave zero tracked EF entries;
- replay locking/evidence verification executes inside the isolated transaction;
- tracker cleanup occurs even when rollback fails and both failures remain observable;
- keyset continuation handles equal delivery dates and equal creation timestamps by id;
- focused Phase 3 tests, full solution tests, build, and `git diff --check` pass.
