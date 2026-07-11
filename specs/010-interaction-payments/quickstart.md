# Quickstart: Phase 8 Interaction & Payments

## Prerequisites

- .NET 8 SDK
- SQL Server available through `ConnectionStrings:DefaultConnection`
- Existing migrations through Phase 7 applied
- Approved active Doctor and Company accounts
- Active current Egypt-day deliveries with `ReservationStatus = Reserved`
- Company wallet Reserved balance covering each delivery's `ReservedAmount`
- Doctor wallet available or repairable under existing wallet rules
- Valid stored delivery snapshots for price, platform fee, doctor earnings, and reserved amount

## Configuration Expectations

Phase 8 runs in the existing API host. Configuration must:

- keep JWT Doctor authorization on Doctor message actions;
- apply `RateLimitPolicyNames.DoctorInteraction` to both read tracking and Accept/Reject interaction;
- keep the standard envelope and global exception middleware authoritative;
- use the existing DST-aware Egypt business clock;
- avoid persisting, logging, auditing, returning, or writing task evidence with raw `Idempotency-Key` values, wallet balances, stack traces, or storage details; use only normalized hashes/fingerprints or non-sensitive conflict categories.

## Build and Migrate

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
dotnet ef database update --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Verify the Phase 8 migration creates interaction evidence and idempotency constraints, adds any delivery indexes required for current-day read/interact lookup, preserves Phase 7 delivery/queue/job tables, and does not mutate historical delivery, wallet, transaction, or ledger balances.

## Start the Host

```powershell
dotnet run --project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Startup logs must not print connection strings, raw idempotency keys, wallet balances, or stack traces.

## Read Tracking Smoke Scenario

With an approved Doctor bearer token:

```powershell
$headers = @{ Authorization = "Bearer $env:DOCTOR_ACCESS_TOKEN" }
Invoke-RestMethod -Method Put -Uri "https://localhost:5001/api/doctor/messages/$env:DELIVERY_ID/read" -Headers $headers
```

Expected envelope:

```json
{
  "Code": 200,
  "Message": "Message read recorded.",
  "Data": {
    "DeliveryId": "delivery-123",
    "Status": "Active",
    "ReadAtUtc": "2026-07-11T12:00:00Z",
    "AlreadyRead": false
  }
}
```

Run the request again and verify:

- `AlreadyRead` is true or equivalent replay metadata is returned;
- the original `ReadAtUtc` is preserved;
- delivery status and reservation status do not change;
- company wallet, doctor wallet, wallet transactions, ledger entries, and financial audit records do not change.

After settling the same delivery through Accept or Reject, run the read request again and verify:

- `ReadAtUtc` is recorded or replayed;
- delivery status remains Accepted or Rejected;
- reservation status remains Charged;
- company wallet, doctor wallet, wallet transactions, ledger entries, platform-fee evidence, and settlement audit records do not change.

## Interaction Settlement Smoke Scenario

```powershell
$headers = @{
  Authorization = "Bearer $env:DOCTOR_ACCESS_TOKEN"
  "Idempotency-Key" = "interaction-demo-001"
}
$body = @{ Outcome = "Accept"; Feedback = "Useful clinical reminder for my patients." } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "https://localhost:5001/api/doctor/messages/$env:DELIVERY_ID/interact" -Headers $headers -ContentType "application/json" -Body $body
```

Expected envelope:

```json
{
  "Code": 200,
  "Message": "Message interaction recorded.",
  "Data": {
    "DeliveryId": "delivery-123",
    "Status": "Accepted",
    "ReservationStatus": "Charged",
    "InteractedAtUtc": "2026-07-11T12:01:00Z",
    "ReadAtUtc": "2026-07-11T12:00:00Z",
    "FeedbackAccepted": true,
    "FeedbackQualifiesForScore": true,
    "ChargeAmount": 100.00,
    "DoctorEarnings": 87.65,
    "PlatformFeeAmount": 12.35,
    "Replayed": false
  }
}
```

Verify:

- delivery status is Accepted or Rejected and reservation status is Charged;
- company Reserved decreased by `ReservedAmount`;
- company Available did not change;
- doctor Available increased by `DoctorEarnings`;
- Charge transaction uses `delivery:charge:{deliveryId}`;
- Earn transaction uses `delivery:earn:{deliveryId}`;
- ledger contains company Reserved Debit and doctor Available Credit entries;
- platform-fee evidence equals stored `PlatformFeeAmount`;
- safe audit evidence exists for the successful settlement.

## Idempotency and Conflict Checks

Repeat the exact same request with the same `Idempotency-Key` and verify the original result is returned with no additional delivery, wallet, transaction, ledger, feedback, or audit mutation beyond safe replay handling.

Verify the database, logs, audit records, diagnostics, response bodies, and test output contain only normalized idempotency hashes/fingerprints or non-sensitive conflict categories, never the raw `Idempotency-Key`.

Then reuse the same key with `Outcome = "Reject"` or materially different feedback and verify a safe 409 envelope with no mutation.

Finally, submit a different key against the already settled delivery:

- matching outcome and feedback returns the settled result without financial effects;
- conflicting outcome or materially different feedback returns a safe conflict with no mutation.

## Feedback Validation Checks

Test these cases before settlement:

- omitted feedback: settlement succeeds;
- empty string: settlement succeeds;
- whitespace-only feedback: settlement succeeds after normalization;
- plain text with letters, numbers, normal punctuation, quotes, slashes, ampersands that are not encoded angle brackets, and line breaks: settlement succeeds;
- 14 non-whitespace characters: settlement succeeds but is not eligible for later feedback-score credit;
- 15 non-whitespace characters: settlement succeeds and is eligible for later feedback-score credit;
- 2,000 characters after trimming: settlement succeeds;
- 2,001 characters after trimming: safe validation failure and no mutation;
- literal `<` or `>`: safe validation failure and no mutation;
- encoded angle brackets such as `&lt;script&gt;` in any casing: safe validation failure and no mutation;
- Markdown links/images such as `[label](https://example.test)` or `![alt](https://example.test/a.png)`: safe validation failure and no mutation;
- `javascript:` or `data:` URI schemes in any casing: safe validation failure and no mutation.

## Current-Day and Authorization Checks

Use test data for yesterday, today, and tomorrow in `Africa/Cairo`.

Verify:

- only current Egypt-day Active/Reserved deliveries can settle;
- Active prior-day deliveries fail even if expiry has not yet run;
- expired deliveries fail;
- another Doctor's delivery fails without protected existence disclosure;
- Company/Admin/unauthenticated callers fail;
- all failure envelopes avoid stack traces, wallet internals, and idempotency material.

## Rate-Limit Checks

Send read and interaction requests above the configured Doctor interaction threshold.

Verify throttled requests:

- return the standard safe rate-limit envelope;
- do not change read state, delivery state, feedback, reservation status, wallets, transactions, ledger entries, or financial audit state;
- leave later valid requests able to replay or settle according to the same idempotency rules when the limit window permits.

## Anomaly and Audit Checks

Force these settlement blockers in SQL-backed tests:

- company Reserved balance lower than `ReservedAmount`;
- missing Charge or Earn replay evidence with a settled delivery;
- invalid stored fee/earning formula;
- missing or deleted wallet;
- same idempotency key with different request content.

Verify each records safe operational/audit evidence where required, returns a safe error, and commits no partial settlement.

## Verification Commands

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "ReadTracking"
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "Idempotency|InteractionSettlement"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessageInteractionAuthorization|DoctorMessageInteraction"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8ReadTracking|RateLimit"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionSettlement|Phase8InteractionIdempotency|Phase8InteractionConcurrency|Phase8InteractionAuditSafety|Phase8InteractionRateLimit"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionIneligible|Phase8InteractionAuthorization|Phase8InteractionBusinessDate"
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "Interaction|Read|Settlement|Feedback|Idempotency"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "DoctorMessage|Interaction"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Interaction|Settlement|RateLimit|Phase8"
dotnet build .\MediBridge.slnx
```

Full Phase 8 SQL Server migration constraint evidence is opt-in because it requires a working Docker/Testcontainers SQL Server 2022 endpoint:

```powershell
$env:MEDIBRIDGE_PHASE8_SQLSERVER_MIGRATION = "1"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "Phase8InteractionMigration"
```

## Performance Profile

Run the opt-in Release-profile tests only on a host with SQL Server 2022 Testcontainers, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, Server GC, and no debugger, coverage collector, or parallel workload.

```powershell
$env:MEDIBRIDGE_PHASE8_PERFORMANCE = "1"
$env:MEDIBRIDGE_PHASE8_DEDICATED = "1"
$env:MEDIBRIDGE_PHASE8_SSD = "1"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj -c Release --filter "Phase8InteractionPerformance"
```

Warm with 20 sequential read and interaction/replay requests. Measure 200 read requests and 200 interaction/replay requests at concurrency 10. At least 95% must finish within 1 second, with zero duplicate financial effects and no failed valid requests. Record p50/p95/p99, query count, conflicts, throttles, failures, and any unmet prerequisite; a skipped test is not passing evidence.

## Done Criteria

- Read tracking records first-read time exactly once and never settles money.
- Accept and Reject settle from Reserved to Charged/Earned exactly once.
- Client idempotency keys replay or conflict deterministically.
- Feedback validation is normalized, bounded, and mutation-safe.
- Safe audit evidence exists for successful settlement, conflicts, and settlement-blocking anomalies.
- Doctor ownership, current-day visibility, rate limiting, standard envelopes, and safe errors are enforced.
- Phase 7 delivery/expiry and Phase 9+ enforcement/reporting/admin/payment-gateway behavior remain unchanged.
