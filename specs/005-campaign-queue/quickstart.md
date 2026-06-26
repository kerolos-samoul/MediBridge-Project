# Quickstart: Campaign & Queue (Phase 5)

This guide verifies Phase 5 after implementation. It assumes Phase 1-4 foundations are available and the current branch is `005-campaign-queue`.

## 1. Restore and build

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
```

Expected result: build succeeds with no layering violations.

## 2. Run focused contract tests

```powershell
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyDoctorSearch|FullyQualifiedName~CompanyCampaign|FullyQualifiedName~CompanyWallet"
```

Expected result:

- Company doctor search returns the standard response envelope.
- Campaign submission requires `Idempotency-Key`, required content, at least one approved asset, and 1-100 unique targets.
- Company campaign list/detail enforces ownership.
- Company wallet query and top-up use the standard response envelope.
- 401, 403, 400, 404, 409, and 429 responses use safe envelope semantics.

## 3. Run focused integration tests

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5"
```

Expected result:

- Doctor search excludes unapproved, suspended, soft-deleted, and zero-price doctors.
- Doctor search applies filters and orders by activity score descending, price ascending, then stable identifier ascending.
- Valid campaign submission creates a `PendingReview` campaign with target snapshots and no queue rows.
- Invalid campaign submission creates no campaign or target rows.
- Campaign submission retry with the same company and idempotency key creates no duplicate campaign or targets.
- Approved campaign queue creation creates one queue item per eligible target and remains retry-safe.
- Queue reads are FIFO by `QueuedAtUtc ASC, Id ASC`.
- Company wallet top-up credits available balance only, creates append-only transaction and immutable ledger entry, and is idempotent.
- Cross-company campaign and wallet access is denied.
- Scope guard tests confirm Phase 5 does not expose daily injector, expiry, doctor inbox/read/interact, settlement, reporting analytics, withdrawal, weekly enforcement, activity score job, or production payment gateway behavior.
- Layering boundary tests confirm Phase 5 controllers avoid EF Core and Phase 5 services avoid EF Core infrastructure.
- Audit safety tests confirm Phase 5 campaign and wallet audit metadata excludes raw gateway payloads, secrets, private file access tokens, storage keys, request bodies, response bodies, and stack traces.

## 4. Run focused unit tests

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5"
```

Expected result:

- Campaign target-list validation covers empty, duplicate, over-100, and ineligible targets.
- Campaign content validation covers missing title, description, clinical research information, and approved asset.
- Money validation rejects top-ups below 100 EGP and values with more than two decimal places.
- Doctor search ordering comparator remains deterministic.

## 5. Run full regression

```powershell
dotnet test .\MediBridge.slnx
```

Expected result: all tests pass, including earlier Phase 1-4 coverage.

## Manual verification checklist

- Inspect `MediBridge.APIs\Controllers` and confirm Phase 5 controllers are HTTP-only and delegate to services.
- Inspect `MediBridge.Services\Services` and confirm campaign, queue, and wallet orchestration use Core contracts and Unit of Work abstractions.
- Inspect project references and confirm `MediBridge.Core` has no EF Core, ASP.NET Core HTTP, storage-provider, or API dependencies.
- Inspect API DTOs and audit writes and confirm they do not expose raw gateway payloads, secrets, private file access tokens, storage keys, or stack traces.
- Confirm Phase 5 did not add daily injector jobs, expiry jobs, doctor inbox/read/interact endpoints, settlement, reporting analytics, withdrawals, weekly enforcement, activity score jobs, or production payment gateway integration.

## Endpoint paths and smoke filters

- `GET /api/company/doctors?PageNumber=1&PageSize=20&specialization=Cardiology&minExperienceYears=3&maxExperienceYears=20&location=Cairo&minActivityScore=70&minPrice=10&maxPrice=100`
- `POST /api/company/campaigns` with required `Idempotency-Key`, title, description, clinical research information, at least one approved `AssetIds` entry, and 1-100 unique `TargetDoctorIds`.
- `GET /api/company/campaigns?PageNumber=1&PageSize=20&status=PendingReview`
- `GET /api/company/campaigns/{campaignId}`
- `GET /api/company/wallet?PageNumber=1&PageSize=20`
- `POST /api/company/wallet/topup` with required `Idempotency-Key` and an EGP `Amount` of at least `100.00`.

All API-visible responses use the standard envelope:

```json
{
  "Code": 200,
  "Message": "Success",
  "Data": {}
}
```

Doctor search and campaign/wallet list responses place pagination under `Data.Page` or `Data.Transactions.Page`, with `PageNumber`, `PageSize`, and `TotalCount`. Campaign submission returns a `PendingReview` campaign detail. Wallet top-up returns `WalletId`, `TransactionId`, `AvailableBalance`, `ReservedBalance`, `Currency`, and `IdempotencyStatus`.

## Example API smoke flow

1. Authenticate as an approved Pharmaceutical Company user.
2. Upload and approve at least one campaign asset using Phase 4 workflows.
3. Request `GET /api/company/doctors?PageNumber=1&PageSize=20`.
4. Submit `POST /api/company/campaigns` with `Idempotency-Key`, required content, one approved asset id, and 1-100 eligible doctor ids.
5. Confirm the response status is `PendingReview` and no doctor delivery exists.
6. Trigger the trusted approved-campaign queue creation path used by admin review.
7. Confirm one queued item exists per still-eligible target doctor and queue reads preserve FIFO order.
8. Submit `POST /api/company/wallet/topup` with amount `100.00` or greater and an `Idempotency-Key`.
9. Request `GET /api/company/wallet` and confirm available balance, reserved balance, currency, and transaction history.

## Phase 5 validation notes

Recorded on 2026-06-19 after Phase 7 polish.

- `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~CompanyDoctorSearch|FullyQualifiedName~CompanyCampaign|FullyQualifiedName~CompanyWallet"`: passed, 32 tests, 0 failures.
- `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase5"`: passed, 60 tests, 0 failures.
- `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase5"`: passed, 26 tests, 0 failures.
- `dotnet build .\MediBridge.slnx`: succeeded, 0 warnings, 0 errors.
- `dotnet test .\MediBridge.slnx`: passed, 53 unit tests, 108 contract tests, and 246 integration tests, 0 failures.
- Manual controller inspection found Phase 5 controllers stay HTTP-only: they read auth/header/query/body data, delegate to `ICompanyDoctorSearchService`, `ICampaignWorkflowService`, or `ICompanyWalletService`, and do not reference EF Core, repositories, or Unit of Work.
- Manual service inspection found Phase 5 orchestration uses Core repository/unit-of-work abstractions and validators; no EF Core infrastructure or API controller/HTTP types are referenced by the Phase 5 services.
- Manual boundary inspection found Phase 5 service interfaces and DTOs expose no EF Core, ASP.NET Core HTTP, Cloudinary, storage-provider, private file token, storage key, raw gateway payload, request body, response body, or stack trace fields. Existing Phase 4 file-storage infrastructure remains outside the Phase 5 service contracts.
- Manual migration inspection found the Phase 5 migration adds `CampaignSubmissionRequests` and a unique `(CampaignId, DoctorId)` queue index; the `Up` migration does not drop Phase 1-4 tables or data.
