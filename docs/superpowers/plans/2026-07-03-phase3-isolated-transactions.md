# Phase 3 Candidate-Isolated Transactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Phase 3 expiry candidates an explicit transaction boundary that guarantees rollback-safe cleanup, bounded EF tracking, and transactional replay verification without changing unrelated Unit of Work behavior.

**Architecture:** Add an infrastructure-neutral isolated generic transaction operation to `IDomainUnitOfWork`, implement its EF behavior in `DomainUnitOfWork`, and route both expiry mutation and replay classification through it. Existing transaction methods remain semantically unchanged on success.

**Tech Stack:** C# 12, .NET 8, EF Core 8, SQL Server, xUnit

---

### Task 1: Capture lifecycle failures with tests

**Files:**
- Modify: `tests/unit/MediBridge.UnitTests/DeliveryExpiryServiceTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7ExpiryFinancialTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7ExpiryEligibilityTests.cs`
- Create: `tests/integration/MediBridge.IntegrationTests/Phase7IsolatedTransactionTests.cs`

- [X] Add a unit test whose fake Unit of Work tracks transaction depth and proves replay locking/evidence reads must occur inside a second transaction.
- [X] Add SQL-backed failed-first/success-second candidate coverage using a conditional database trigger, asserting the first candidate remains unchanged and the second commits exactly once.
- [X] Add SQL-backed equal-date/equal-created-time keyset continuation coverage ordered by id.
- [X] Add isolated transaction lifecycle tests for zero tracked entries after success and failure.
- [X] Add a rollback-failure test using an EF Core transaction interceptor, asserting cleanup and preservation of both exceptions.
- [X] Run the focused filters and confirm the new assertions fail for the current lifecycle behavior.

### Task 2: Add the isolated Core contract and repository implementation

**Files:**
- Modify: `MediBridge.Core/Interfaces/IDomainUnitOfWork.cs`
- Modify: `MediBridge.Repository/UnitOfWork/DomainUnitOfWork.cs`

- [X] Add `Task<T> ExecuteIsolatedInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)` with documentation restricting results to detached/scalar values.
- [X] Refactor transaction execution through one private generic executor while preserving both existing public methods' successful tracking semantics.
- [X] Attempt rollback with `CancellationToken.None` after operation/save/commit failure.
- [X] If rollback also fails, preserve both exceptions; retain caller cancellation semantics through an `OperationCanceledException` carrying the aggregate.
- [X] Clear tracking in `finally` after every isolated attempt and after every failed ordinary attempt.
- [X] Run lifecycle tests and confirm they pass.

### Task 3: Route expiry mutation and replay through isolation

**Files:**
- Modify: `MediBridge.Services/Services/DeliveryExpiryService.cs`
- Modify: `tests/unit/MediBridge.UnitTests/DeliveryExpiryServiceTests.cs`

- [X] Replace candidate mutation execution with `ExecuteIsolatedInTransactionAsync`.
- [X] Execute the complete replay lock/read/evidence classification callback through `ExecuteIsolatedInTransactionAsync`.
- [X] Update the test Unit of Work proxy for the new contract and assert two isolated boundaries for conflict replay.
- [X] Run `DeliveryExpiry` unit tests and confirm cancellation, counts, redaction, replay, and isolation pass.

### Task 4: Verify and document remediation

**Files:**
- Modify: `specs/008-delivery-expiry-jobs/tasks.md`

- [X] Run `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliveryExpiry"`.
- [X] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7Expiry|FullyQualifiedName~Phase7IsolatedTransaction"`.
- [X] Run `dotnet test .\MediBridge.slnx --no-restore`.
- [X] Run `dotnet build .\MediBridge.slnx --no-restore`.
- [X] Run `git diff --check` and inspect the scoped diff.
- [X] Append the Phase 3 remediation result and exact verification counts to `tasks.md`; do not alter Phase 4 tasks.
