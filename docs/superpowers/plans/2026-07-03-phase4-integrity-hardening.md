# Phase 4 Integrity Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the Phase 4 eligibility, replay-validation, concurrency-test, synchronization-test, and ambient-time integrity gaps found during Senior review.

**Architecture:** Keep business orchestration in `MediBridge.Services`, persistence and SQL Server locking in `MediBridge.Repository`, and infrastructure-neutral contracts/read models in `MediBridge.Core`. Eligibility reads will lock the complete aggregate snapshot, replay classification will require a fully consistent committed event, and all mutation timestamps will be supplied by callers rather than generated in repositories.

**Tech Stack:** C# 12, .NET 8, EF Core 8, SQL Server LocalDB integration tests, xUnit.

---

### Task 1: Prove complete eligibility locking

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7InjectorCompanyGateAndPolicyTests.cs`
- Modify: `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs`
- Modify: `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`

- [ ] Add SQL-backed tests that begin an eligibility-read transaction and assert a second connection times out when updating the Doctor user, Company profile, or Company user:

```csharp
await using var transaction = await lockingContext.Database.BeginTransactionAsync();
await repository.FindDoctorDeliveryEligibilityForUpdateAsync(doctorId);
await Assert.ThrowsAsync<SqlException>(() => competingContext.Database.ExecuteSqlRawAsync(
    "SET LOCK_TIMEOUT 250; UPDATE [Users] SET [AccountStatus] = [AccountStatus] WHERE [Id] = {0}", userId));
```

- [ ] Run the focused tests and confirm they fail because user/company rows are currently read with `AsNoTracking` and no update lock.
- [ ] Replace those reads with parameterized `UPDLOCK, ROWLOCK, HOLDLOCK` reads in a stable profile/campaign → owner profile → user order.
- [ ] Rerun the tests and confirm the competing updates time out while the eligibility transaction is open.

### Task 2: Require complete replay evidence

**Files:**
- Modify: `MediBridge.Core/Interfaces/Messaging/DeliveryProcessingReadModels.cs`
- Modify: `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`
- Modify: `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7InjectorFinancialTests.cs`

- [ ] Add a failing integration test that seeds a Queued row plus an existing delivery without complete Reserve evidence and expects `FailedCount == 1`, not replay/skip.
- [ ] Add a repository projection containing delivery identity, owner references, date, and reserved amount for replay verification.
- [ ] Treat any existing delivery or orphan Reserve record encountered while the queue row is still locked as Queued as an inconsistent failure.
- [ ] In fresh-state replay classification, require the queue row to be Activated (or terminally Cancelled), require the deterministic delivery id, and validate transaction amount/wallet/idempotency plus exactly two fully referenced balanced ledger entries.
- [ ] Rerun focused financial tests.

### Task 3: Make mutation timestamps explicit

**Files:**
- Modify: `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`
- Modify: `MediBridge.Core/Interfaces/Wallets/IWalletRepository.cs`
- Modify: `MediBridge.Repository/Repositories/Wallets/WalletRepository.cs`
- Modify: all production/test call sites returned by `rg "AddDeliveryAsync|StageAvailableBalanceChangeAsync|StageReservedBalanceChangeAsync"`

- [ ] Add failing tests proving local/unspecified mutation timestamps are rejected.
- [ ] Make `deliveredAtUtc` and wallet `updatedAtUtc` required parameters and validate `DateTimeKind.Utc` at Repository boundaries.
- [ ] Pass the already captured operation timestamp from injector/expiry/company-wallet services and explicit test timestamps from persistence tests.
- [ ] Verify no activation or expiry persistence path reads `DateTime.Now`/`DateTime.UtcNow` inside Repository mutation methods.

### Task 4: Strengthen concurrency tests

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7InjectorTestHarness.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7InjectorFinancialTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7InjectorCompanyGateAndPolicyTests.cs`

- [ ] Extend the harness to seed a second approved Doctor and target one Company wallet from two Doctor queues.
- [ ] Replace the same-Doctor wallet test with a different-Doctor/same-Company race and assert exactly one Reserve effect at a 50 EGP balance.
- [ ] Add a command interceptor with a `TaskCompletionSource` signal for the company expiry-gate query.
- [ ] Replace `Task.Delay(250)` as the proof of blocking: await the interceptor signal, assert the injector remains incomplete while the update transaction is open, then commit and verify activation.

### Task 5: Verify and document

**Files:**
- Modify: `specs/008-delivery-expiry-jobs/tasks.md`

- [ ] Run:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~DeliverySettlementSnapshot|FullyQualifiedName~DeliveryCandidateEligibility|FullyQualifiedName~DeliveryExpiry"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7Injector|FullyQualifiedName~Phase7Expiry|FullyQualifiedName~Phase7IsolatedTransaction|FullyQualifiedName~Phase3Wallet"
dotnet build .\MediBridge.slnx --no-restore
```

- [ ] Run `git diff --check`, inspect the Phase 4 diff for forbidden EF/Hangfire dependencies, and append remediation evidence to `tasks.md`.
