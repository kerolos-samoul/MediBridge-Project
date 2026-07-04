# Phase 7 Reliability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct Phase 7 cancellation ownership, replay verification, interrupted-run accounting, and single-snapshot timing without changing business scope.

**Architecture:** Keep HTTP mapping in the existing middleware/Phase 7 exception boundary, orchestration in Services, and persisted state projections in Core/Repository. Introduce one internal job-progress value owned by each service invocation so normal and exceptional completion consume the same counters and captured time.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core, EF Core 8, SQL Server, xUnit

---

### Task 1: Storage cancellation ownership

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryAssetAccessTests.cs`
- Modify: `MediBridge.Services/Services/FileWorkflowService.cs`

- [ ] Add a provider-timeout test using `OperationCanceledException` while the request token is active; expect a safe 503 envelope.
- [ ] Add a caller-cancellation service test; expect cancellation propagation rather than storage translation.
- [ ] Run the focused tests and observe the provider-timeout case fail with 500.
- [ ] Replace exception-type filtering with caller-token-aware cancellation handling.
- [ ] Rerun the focused tests and require both cases to pass.

### Task 2: Strict expiry replay evidence

**Files:**
- Modify: `MediBridge.Core/Interfaces/Messaging/DeliveryProcessingReadModels.cs`
- Modify: `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`
- Modify: `MediBridge.Services/Services/DeliveryExpiryService.cs`
- Modify: `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7ExpiryFinancialTests.cs`

- [ ] Add replay tests that corrupt delivery state, ledger currency, and ledger idempotency identity; require retry classification to fail safely.
- [ ] Run the focused expiry tests and observe incorrect replay acceptance.
- [ ] Extend the replay projection with delivery and reservation status.
- [ ] Require Expired/Released plus exact transaction and two-ledger evidence for release replay; retain and strengthen Active/Reserved checks for reserve replay.
- [ ] Rerun expiry and injector replay tests.

### Task 3: Invocation-owned job progress and timing

**Files:**
- Modify: `MediBridge.Services/Services/DeliveryJobRunTracker.cs`
- Modify: `MediBridge.Services/Services/DeliveryExpiryService.cs`
- Modify: `MediBridge.Services/Services/DailyDeliveryInjectorService.cs`
- Modify: `tests/unit/MediBridge.UnitTests/DeliveryExpiryServiceTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryJobRunIntegrationTests.cs`

- [ ] Add tests proving one clock capture per failed/interrupted invocation and preservation of counters recorded before cancellation.
- [ ] Run focused tests and observe the second capture/zero-counter behavior fail.
- [ ] Add an internal sequential progress accumulator that produces immutable `DeliveryJobRunCounters` snapshots.
- [ ] Move stopwatch/progress ownership to `RunAsync`; pass them through normal and exceptional completion.
- [ ] Rerun job-run, expiry, and injector tests.

### Task 4: Regression and documentation gate

**Files:**
- Modify: `specs/008-delivery-expiry-jobs/tasks.md`

- [ ] Run all Phase 7 unit, contract, and integration tests.
- [ ] Run the full solution build and `git diff --check`.
- [ ] Inspect the final diff for layering, cancellation, security, and scope regressions.
- [ ] Record root causes, fixes, evidence, and remaining performance-profile risk in `tasks.md`.
