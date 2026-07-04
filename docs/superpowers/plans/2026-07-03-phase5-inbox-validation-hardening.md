# Phase 5 Inbox Validation Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct the Phase 5 benchmark model and close its declared functional proof gaps without changing production rate limiting.

**Architecture:** Keep the production request pipeline intact. Model aggregate load across four real user partitions, observe SQL through an EF Core command interceptor, and add focused API-level integration coverage using existing factories and seed helpers.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core SQL Server, xUnit, Testcontainers.MsSql.

---

### Task 1: Prove the benchmark partition defect

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxPerformanceTests.cs`

- [ ] Add a non-opt-in test that builds the planned warm-up/measured request allocation and asserts every user partition stays within the configured 60-request limit.
- [ ] Run `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase7TodayInboxPerformanceTests"` and verify the current single-user allocation fails with 220 requests.

### Task 2: Implement representative multi-user benchmark allocation

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxPerformanceTests.cs`

- [ ] Replace the single Doctor seed with four equivalent 100-item Doctor inboxes.
- [ ] Create one authenticated client per Doctor and distribute warm-up/measured requests round-robin.
- [ ] Rerun the allocation test and verify every partition has 55 requests.

### Task 3: Add bounded SQL query observation and reporting

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxPerformanceTests.cs`

- [ ] Add a thread-safe `DbCommandInterceptor` that counts `DoctorAdDeliveries` and `StoredFiles` SELECTs only while measurement is enabled.
- [ ] Register the interceptor through the existing `DbContextOptions<MediBridgeDbContext>` replacement pattern.
- [ ] Reset after warm-up, assert 200 delivery and 200 asset queries, assert zero signed grants, and write successful p50/p95/p99/query/failure metrics to xUnit output.

### Task 4: Close inbox date, pagination, and authorization coverage

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7TodayInboxIntegrationTests.cs`
- Modify: `tests/contract/MediBridge.ContractTests/DoctorTodayMessagesContractTests.cs`

- [ ] Add direct default-50 and maximum-100 page assertions.
- [ ] Add Cairo midnight/DST boundary visibility assertions with controllable `TimeProvider`.
- [ ] Add anonymous, Company/Admin, cross-Doctor, malformed, stale-date, and future-date assertions with standard envelopes.

### Task 5: Close delivery-asset state coverage

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Phase7DeliveryAssetAccessTests.cs`

- [ ] Add cross-Doctor and inactive-Doctor denial cases.
- [ ] Add Pending, Rejected, Quarantined, deleted, replaced, unrelated-campaign, and prior-day file/delivery cases.
- [ ] Assert every denial avoids storage-provider calls and returns a safe standard envelope.

### Task 6: Verify and document

**Files:**
- Modify: `specs/008-delivery-expiry-jobs/tasks.md`

- [ ] Run the focused contract and integration filters.
- [ ] Run `dotnet build .\MediBridge.slnx --no-restore`.
- [ ] Run `git diff --check`.
- [ ] Append exact pass/fail/skip evidence and remaining reference-profile risk to the Phase 5 review note.
