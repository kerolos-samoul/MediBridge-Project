# Quickstart: Database & Core Models (Phase 3)

This quickstart validates the Phase 3 planning assumptions and expected implementation surface.

## Prerequisites

- .NET 8 SDK installed.
- SQL Server available locally.
- `MediBridge.APIs/appsettings.Development.json` has `ConnectionStrings:DefaultConnection` pointing at the validation database.
- Current branch: `003-database-core-models`.
- If `dotnet ef` is not available, install or restore the EF Core CLI tool before running migration commands.

Example local SQL Server connection strings used for validation:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=MediBridge_Phase3;Trusted_Connection=True;TrustServerCertificate=True"
}
```

Use a disposable validation database when applying migrations locally.

## Validate Configuration

```powershell
dotnet build .\MediBridge.slnx
```

Expected result:

- Solution builds without Core depending on EF Core or ASP.NET HTTP types.
- Repository remains the EF Core/SQL Server persistence boundary.

## Apply Migrations

The final Phase 3 migration set is:

- `20260602022951_Phase3DatabaseCoreModels`
- `20260602025256_Phase3US2IntegrityConstraints`

Apply the migrations with:

```powershell
dotnet ef database update --project .\MediBridge.Repository --startup-project .\MediBridge.APIs
```

Expected result:

- Migration applies cleanly against SQL Server.
- Phase 2 identity tables remain intact, including Doctor/Company/Admin roles, account approval state, the `RefreshCredential` table/entity, and refresh-token replacement tracing.
- Phase 3 tables, indexes, uniqueness constraints, precision rules, and concurrency tokens are present.

## Validate Persistence Rules

Run the automated suite:

```powershell
dotnet test .\MediBridge.slnx
```

Focused Phase 3 validation commands:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase3"
```

Expected Phase 3 coverage:

- Core entity create/read through repository and unit-of-work boundaries.
- No controller or service direct dependency on EF Core infrastructure types.
- No Phase 3 public or diagnostic controller exists; repository, integration, migration, and service-boundary tests are the Phase 3 validation surface.
- Phase 3 intentionally has no public validation endpoint or diagnostic controller; architecture tests, repository tests, migration tests, and service-boundary tests are the only validation surface for this phase.
- Queue reads order by `QueuedAtUtc ASC, Id ASC` within doctor/status scope, including same-timestamp tie-breaks and later next-day carry-over support.
- Duplicate `(DoctorId, DeliveryDateEgypt, CampaignId)` deliveries are rejected.
- Duplicate wallet transactions for the same `OperationType + IdempotencyKey` are rejected for top-up, charge, earn, refund, and withdrawal payout retries.
- Monetary inputs with more than two decimal places are rejected.
- Wallet balance changes, wallet transactions, and immutable wallet ledger entries commit or roll back together.
- Wallet ledger flow tests cover `TopUp`, `Reserve`, `Charge`, `Earn`, Platform fee, `Refund`, `WithdrawRequest`, `WithdrawApproved`, `WithdrawRejected`, and `WithdrawPayout`.
- Soft-deleted records remain historically linked but are excluded from active-record queries.
- Audit/history corrections create linked records without modifying original history.

## Manual Database Spot Checks

Use SQL Server tools to confirm:

- Wallet money columns store EGP values at 2-decimal precision.
- Wallet transaction idempotency has a unique constraint on operation type plus key.
- Wallet ledger entries are immutable and reference wallet transactions.
- Delivery uniqueness exists for doctor, Egypt delivery date, and campaign.
- Queue ordering index exists and no priority queue ordering exists in Phase 3.
- Historical/audit tables do not require hard deletion for correction scenarios.

## Scope Guard

Do not validate Phase 4+ or Phase 5+ workflows during Phase 3:

- No file upload/retrieval behavior.
- No campaign creation workflow.
- No queue injection or expiry jobs.
- No interaction settlement.
- No wallet top-up or withdrawal endpoint behavior.
- No public validation or diagnostic controller.
- No persistent reporting read models.
