# Quickstart: Phase 8 Interaction & Payments

## Prerequisites

- .NET 8 SDK
- SQL Server available through `ConnectionStrings:DefaultConnection`
- Existing migrations through Phase 7 delivery and wallet support applied
- Approved active Doctor and Company accounts
- Active current Egypt-day deliveries with `ReservationStatus = Reserved`
- Company wallets with Reserved balance matching each delivery `ReservedAmount`
- Doctor wallets available for earnings
- Existing API envelope, global exception middleware, Doctor JWT authorization, Egypt business clock, audit logging, and rate-limit configuration

## Configuration Expectations

Phase 8 uses existing application hosting and does not add new background jobs. The API host must:

- expose Doctor read and interaction endpoints under `api/doctor/messages`;
- require Doctor JWT authorization and owner scoping;
- use the current DST-aware `Africa/Cairo` business date for read and interaction eligibility;
- keep read tracking on the doctor-message read rate-limit category or equivalent;
- attach the `doctor-interaction` rate-limit policy to the interaction endpoint;
- document `Idempotency-Key` as required on interaction;
- keep all settlement writes behind Repository + Unit of Work abstractions;
- avoid logging raw idempotency keys, wallet balances, full feedback text, storage keys, signed URLs, or raw exception stacks.

## Build and Migrate

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
dotnet ef database update --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

If Phase 8 adds interaction operation evidence or narrows feedback persistence, verify the migration:

- creates a unique `(DoctorId, DeliveryId, IdempotencyKey)` replay boundary if a dedicated evidence table is used;
- preserves `WalletTransactions` unique `(OperationType, IdempotencyKey)`;
- preserves existing delivery uniqueness and Phase 7 indexes;
- enforces or supports the 1,000-character feedback limit without truncating data silently;
- adds only Phase 8 read/interact persistence changes.

## Start the Host

```powershell
dotnet run --project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Startup logs should not print credentials, storage keys, signed URLs, wallet balances, raw idempotency keys, or raw exception stacks.

## Read Tracking Smoke Scenario

With an approved Doctor bearer token and an Active current-day delivery:

```powershell
$headers = @{ Authorization = "Bearer $env:DOCTOR_ACCESS_TOKEN" }
Invoke-RestMethod -Method Put -Uri "https://localhost:5001/api/doctor/messages/$deliveryId/read" -Headers $headers
```

Expected envelope shape:

```json
{
  "Code": 200,
  "Message": "Message read recorded.",
  "Data": {
    "DeliveryId": "delivery-123",
    "ReadAtUtc": "2026-07-10T10:15:00Z",
    "ReadStatus": "Created"
  }
}
```

Run the same request again and verify:

- `ReadStatus` is `Replayed`;
- `ReadAtUtc` is unchanged;
- delivery status remains Active;
- `ReservationStatus` remains Reserved;
- company and doctor wallet balances are unchanged;
- no Charge, Earn, Reserve, or Release transaction is created.

## Interaction Settlement Smoke Scenario

Seed:

- delivery `Status = Active`, `ReservationStatus = Reserved`, current `DeliveryDateEgypt`;
- `ReservedAmount = 50.00`, `PricePerMessageSnapshot = 50.00`;
- `PlatformFeeAmount = 10.00`, `DoctorEarnings = 40.00`;
- company wallet `ReservedBalance >= 50.00`;
- doctor wallet available for earning.

Submit Accept:

```powershell
$headers = @{
  Authorization = "Bearer $env:DOCTOR_ACCESS_TOKEN"
  "Idempotency-Key" = "interact-demo-0001"
}
$body = @{ Decision = "Accept"; FeedbackText = " Useful campaign details. " } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "https://localhost:5001/api/doctor/messages/$deliveryId/interact" -Headers $headers -Body $body -ContentType "application/json"
```

Expected result:

- delivery `Status = Accepted`;
- `ReservationStatus = Charged`;
- `InteractedAtUtc` is set;
- `ReadAtUtc` remains unchanged if it was null;
- feedback is stored as `Useful campaign details.`;
- company `ReservedBalance` decreases by 50.00;
- doctor `AvailableBalance` increases by 40.00;
- one Charge transaction uses `delivery:charge:{deliveryId}`;
- one Earn transaction uses `delivery:earn:{deliveryId}`;
- company ledger has Reserved Debit 50.00;
- doctor ledger has Available Credit 40.00.

Repeat the same request with the same `Idempotency-Key` and verify `IdempotencyStatus = Replayed` with no additional delivery, wallet, transaction, or ledger mutation.

## Reject Settlement Scenario

Repeat the smoke scenario with `Decision = "Reject"` and no feedback. Verify the same company Charge and doctor Earn behavior as Accept, with final delivery status Rejected and no stored feedback.

## Feedback Validation

Verify:

- omitted feedback settles successfully;
- whitespace-only feedback settles successfully and is stored as absent;
- feedback shorter than 15 characters settles successfully and is left for later scoring/reporting interpretation;
- feedback longer than 1,000 characters after trimming returns a safe 400 envelope and creates no delivery, wallet, transaction, ledger, or audit settlement effect;
- same key with different normalized feedback after a successful settlement returns conflict and preserves the original result.

## Idempotency and Conflict Checks

Run these with the same Doctor and delivery:

1. Same key, same decision, same normalized feedback: returns replay with no mutation.
2. Same key, different decision: returns conflict with no mutation.
3. Same key, same decision, different normalized feedback: returns conflict with no mutation.
4. Different key after already Accepted, same decision: returns the existing settled result, does not change stored feedback, and never mutates delivery, wallet, transaction, ledger, or audit settlement state.
5. Different key after already Accepted, different decision: returns conflict with no mutation.
6. Concurrent same-key Accept requests: exactly one Created result and the rest Replayed.
7. Concurrent Accept and Reject requests: exactly one final status wins, no duplicate Charge/Earn effects.

## Negative Authorization and State Checks

Verify safe envelopes and no mutation for:

- unauthenticated caller;
- Company or Admin token;
- another Doctor's delivery;
- malformed delivery id;
- prior-day or future-day delivery;
- Expired/Released delivery;
- already Accepted or Rejected delivery with conflicting decision;
- missing company wallet;
- missing doctor wallet;
- company Reserved balance below `ReservedAmount`;
- invalid stored monetary snapshots;
- missing or short/long `Idempotency-Key`.

## Current-Day Boundary Check

Use a fake or controlled `IEgyptBusinessClock` in tests:

- Active/Reserved delivery on today's Cairo date can be read and interacted with.
- Active/Reserved delivery from yesterday after Cairo midnight cannot be read or interacted with, even if expiry has not yet processed it.
- Interaction without prior read succeeds and does not backfill `ReadAtUtc`.

## Verification Commands

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj
dotnet build .\MediBridge.slnx
```

Focused filters should cover:

- read tracking idempotency and non-financial behavior;
- interaction request validation and feedback normalization;
- required `Idempotency-Key` contract and OpenAPI documentation;
- Accept/Reject financial settlement;
- same-key replay and conflict behavior;
- concurrent settlement races;
- wallet atomic rollback on forced failure;
- owner/role authorization and safe error envelopes;
- Phase 8 scope guard excluding reporting, scoring, notifications, and payouts.

## Performance Profile

Run the opt-in Release-profile tests only on a host with SQL Server 2022 Testcontainers or local SQL Server, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, Server GC, and no debugger, coverage collector, or parallel workload. Seed 1,000 current-day Active/Reserved deliveries with matching company/doctor wallets. Warm up 20 reads and 20 interactions, then measure 200 eligible read requests and 200 eligible interaction requests at concurrency 10. At least 95% must finish within 1 second with zero duplicate financial effects. Record p50, p95, p99, query count, failures, and any unmet prerequisite; a skipped run is not passing performance evidence.

## Done Criteria

- Read tracking sets `ReadAtUtc` once and never settles payment.
- Accept and Reject both settle exactly one company Charge and one doctor Earn.
- Interaction requires a valid `Idempotency-Key` header and handles replay/conflict deterministically.
- Feedback is optional, trimmed, empty-after-trim as absent, and capped at 1,000 characters.
- Settlement does not require prior read and never mutates `ReadAtUtc`.
- Stale, expired, cross-owner, inconsistent, and missing-wallet deliveries produce no partial effects.
- All financial state, delivery state, wallet transactions, ledger entries, and replay evidence commit atomically.
- Doctor endpoints use JWT, role/owner checks, rate limiting, standard envelopes, and safe error handling.
- Phase 9+ enforcement, activity scoring, reporting, notifications, withdrawals, payouts, and admin tooling remain absent.
