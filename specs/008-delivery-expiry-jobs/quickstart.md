# Quickstart: Phase 7 Delivery & Expiry Jobs

## Prerequisites

- .NET 8 SDK
- SQL Server available through `ConnectionStrings:DefaultConnection`
- Existing migrations through Phase 6/Phase 5 wallet support applied
- Approved Doctor and Company accounts, positive doctor pricing, an effective platform-fee policy, approved campaign assets, Approved/Active campaigns, queued rows, and funded company wallets
- Private file storage configured for delivery-asset access testing

## Configuration Expectations

The API host uses the existing Hangfire 1.8.17 package references and SQL Server connection. Phase 7 registration must:

- configure Hangfire compatibility level 1.8 and SQL Server storage;
- start workers listening to the dedicated `delivery` queue;
- resolve DST-aware `Africa/Cairo` (with BCL Windows-id fallback) at startup;
- register `medibridge-expiry-cleaner` as scheduled and eligible at Cairo `00:00` without promising an exact worker-start instant;
- register `medibridge-daily-injector` as scheduled and eligible at Cairo `00:05`, and defer without mutation if invoked earlier;
- run startup/recovery reconciliation that creates one durable date/job dispatch claim and enqueues the same persisted expiry job first plus an eligible injector continuation without invoking either business service inline;
- keep the Hangfire dashboard and manual job HTTP controls unmapped;
- use bounded automatic retries and ordinary `CancellationToken` job parameters.

If Cairo time-zone data cannot be resolved, startup must fail with a safe configuration error instead of falling back to host time or fixed UTC+2.

## Build and Migrate

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
dotnet ef database update --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Verify the migration creates `DeliveryJobRuns` and `DeliveryRecoveryDispatches`, adds queue rowversion and delivery expiry time, backfills only Queued `CampaignSubmittedAtUtc` values from authentic `Campaign.SubmittedAtUtc`, rejects any unresolved Queued null, leaves terminal historical nulls allowed, never reads `QueuedAtUtc` as a backfill source, preserves existing unique delivery/transaction constraints, and adds the queue, overdue, inbox, job-run, and recovery-dispatch indexes described in [data-model.md](./data-model.md).

## Start the Host

```powershell
dotnet run --project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Startup logs should confirm Hangfire SQL storage/server startup and recurring registration without printing connection strings, credentials, storage keys, signed URLs, or idempotency keys.

## Expiry Smoke Scenario

1. Seed a company wallet with `AvailableBalance = 100.00` and `ReservedBalance = 50.00`.
2. Seed an Active/Reserved delivery dated before today's Cairo date with `ReservedAmount = 50.00`.
3. Invoke the expiry service through the test harness or trigger the registered Hangfire job.
4. Verify:
   - delivery is Expired/Released with `ExpiredAtUtc` set;
   - wallet is `AvailableBalance = 150.00`, `ReservedBalance = 0.00`;
   - one Release transaction uses `delivery:release:{deliveryId}`;
   - ledger contains Reserved Debit 50.00 and Available Credit 50.00;
   - doctor balance and earnings are unchanged.
5. Run the same job concurrently/repeatedly and verify every value and record count remains unchanged.

Also seed deliveries several days old to prove unbounded catch-up, and Accepted/Rejected/current-day deliveries to prove they remain unchanged.

## Injector Smoke Scenario

1. Seed a Doctor with `DailyMessageLimit = 2`, positive current price `50.00`, and no current-day deliveries.
2. Seed three Queued rows with distinct immutable `CampaignSubmittedAtUtc` values and deliberately different `QueuedAtUtc` values; expected FIFO follows `CampaignSubmittedAtUtc`, then queue id, never enqueue time.
3. Seed Approved/Active campaigns, Approved company accounts, approved active assets, and effective fee policy 20%.
4. Give the first company insufficient funds and later companies sufficient funds.
5. Invoke the injector.
6. Verify:
   - the insufficient row stays Queued;
   - the later two rows activate in FIFO scan order and fill the limit;
   - two unique current-day deliveries exist;
   - each delivery snapshots price 50.00, fee percent 20.00, fee 10.00, earnings 40.00, and reserved amount 50.00;
   - each funded company wallet moves 50.00 Available to Reserved;
   - each activation has one Reserve transaction plus Available Debit and Reserved Credit ledger entries;
   - no Doctor wallet credit or company Charge occurs.
7. Retry/overlap injection and verify no third delivery or duplicate financial record appears.

Repeat with terminal campaign/company/doctor states to verify cancellation, and with paused/suspended/unpriced states to verify rows remain Queued.

## Company-Scoped Expiry Gate Scenario

1. Seed overdue Active/Reserved deliveries for Company A and none for Company B.
2. Force Company A expiry to fail safely while Company B expiry succeeds or has no work.
3. Queue eligible candidates for both companies targeting doctors with capacity.
4. Run the injector.
5. Verify Company A rows stay Queued with no new delivery/reservation while Company B rows can activate.
6. Resolve and expire Company A's overdue delivery, retry injection, and verify Company A can activate exactly once.

## Missed-Schedule Startup Recovery

1. Start the host after 00:05 Cairo time with no current-date expiry/injector run and no recovery dispatch.
2. Verify startup performs no delivery, queue, wallet, transaction, or ledger mutation inline.
3. Verify one durable ExpiryCleaner dispatch claim is created and the existing expiry service-interface job is persisted first.
4. Verify one DailyInjector dispatch claim is created as a continuation of expiry and uses the existing injector service-interface job.
5. Start multiple hosts and rerun reconciliation; verify the unique date/job claims remain one each and no second recovery chain is intentionally dispatched.
6. Simulate interruption between claim persistence and scheduler acknowledgement; reconcile Pending work and verify at-least-once job delivery creates no duplicate business effect.
7. Repeat before 00:05; verify only expiry is claimed/enqueued and injection remains absent until eligible.

## Today Inbox Contract

With an approved Doctor bearer token:

```powershell
$headers = @{ Authorization = "Bearer $env:DOCTOR_ACCESS_TOKEN" }
Invoke-RestMethod -Method Get -Uri 'https://localhost:5001/api/doctor/messages/today' -Headers $headers
```

Expected envelope:

```json
{
  "Code": 200,
  "Message": "Today's messages retrieved.",
  "Data": {
    "BusinessDateEgypt": "2026-07-02",
    "Items": [],
    "NextCursor": null
  }
}
```

Seed more than 100 today deliveries plus yesterday and tomorrow deliveries for two doctors. Request `?PageSize=100`, follow each opaque `NextCursor`, and verify every authenticated-doctor current-date delivery appears exactly once in ascending persisted activation order by `DeliveredAtUtc`, then delivery id. Verify malformed, cross-doctor, and prior-date cursors receive safe 400 envelopes. Asset metadata must contain no storage key or signed URL.

## Delivery Asset Access Contract

Use the `AccessPath` returned for an Approved asset:

```powershell
Invoke-RestMethod -Method Get -Uri "https://localhost:5001$accessPath" -Headers $headers
```

Verify a 10-minute signed grant is returned and audited. Verify 404/403-safe envelopes for another doctor's delivery, a prior-day delivery, unrelated file, Pending/Rejected/Quarantined/deleted/replaced file, and a non-Doctor token. A provider failure returns a safe 503 envelope with no raw diagnostics.

## DST Boundary Checks

Use a fake `TimeProvider` in unit/integration tests to cover Cairo dates immediately before and after Egypt daylight-saving transitions. Assert that:

- the business date changes at Cairo midnight, not host midnight;
- recurring definitions retain Cairo `TimeZoneInfo` rather than a numeric offset;
- expiry and injector operate on the same captured date during a run;
- inbox visibility changes exactly at Cairo midnight.

## Verification Commands

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj
dotnet build .\MediBridge.slnx
```

### Phase 7 Validation Evidence (2026-07-04)

- Validation ran on clean local SQL Server databases with the authoritative DST-aware
  `Africa/Cairo` zone. Deterministic scenario clocks covered Cairo business dates from
  2026-07-02 through 2026-07-04 plus the Egyptian DST transition boundaries.
- The focused Cairo/expiry/injector unit scenarios passed 37/37, the Doctor HTTP contract
  scenarios passed 5/5, and the SQL-backed Phase 7 integration scenarios passed 75/75.
- Multi-page inbox traversal, malformed/cross-Doctor/stale-date cursor rejection, delivery-scoped
  asset access, startup recovery, Pending reconciliation, and protected Hangfire storage requeue
  were exercised. `/hangfire` and manual job-control routes remained absent.
- The two opt-in reference performance tests were skipped because this host was not declared as
  the documented dedicated Release/Server-GC/SSD profile; those skips are not performance evidence.

## Operator Review and Retry

- Review `DeliveryJobRun` rows only through infrastructure-managed read-only SQL access governed by platform IAM and database permissions.
- Requeue a failed persisted Hangfire job only through protected infrastructure Hangfire administration tooling; confirm it invokes the same service-interface method and produces no duplicate delivery or financial effect.
- Do not expose `/hangfire` or any application manual job-control route. Never place credentials, connection strings, job payload secrets, raw exceptions, wallet balances, or financial idempotency keys in the runbook or captured evidence.

## Performance Profile

Run the opt-in Release-profile tests only on a host with SQL Server 2022 Testcontainers, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, Server GC, `BatchSize=100`, and no debugger, coverage collector, or parallel workload. For inbox measurement, perform 20 sequential warm-ups and 200 requests at concurrency 10 against a 100-item page; at least 190 must finish within 1 second with zero failures. For the daily cycle, perform one warm-up and three clean-data measured runs over 1,000 doctors and 10,000 candidates; each must finish within 5 minutes with zero duplicate delivery or financial effects. Record p50/p95/p99, query count, failures, and any unmet prerequisite; a skipped test is not passing evidence.

## Done Criteria

- Cairo schedules and date calculations are DST-aware and startup-validated.
- Every overdue unanswered Active/Reserved delivery releases exactly once.
- Injector respects global doctor limits, FIFO, skip-and-continue, terminal/temporary distinctions, and company-scoped expiry gates.
- Reserve/Release operations are atomic, balanced, and idempotent under retry/concurrency.
- Today inbox and asset access enforce Doctor ownership/current-day/Approved-file boundaries and standard envelopes.
- Job-run records and logs are diagnostically useful but contain no sensitive material.
- Phase 8+ interaction settlement, enforcement, scoring, notifications, and analytics remain absent.
