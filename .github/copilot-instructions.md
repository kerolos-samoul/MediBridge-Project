# Copilot Instructions for MediBridge

## Build, test, and lint commands

Run from repository root (`D:\My Project\MediBridge Project\MediBridge`):

- Restore: `dotnet restore .\MediBridge.slnx`
- Build (full solution): `dotnet build .\MediBridge.slnx`
- Run API locally: `dotnet run --project .\MediBridge.APIs\MediBridge.APIs.csproj`
- Run with hot reload: `dotnet watch --project .\MediBridge.APIs\MediBridge.APIs.csproj run`
- Test suite: `dotnet test .\MediBridge.slnx`
- Single test (when test projects exist): `dotnet test .\<TestProject>.csproj --filter "FullyQualifiedName~<Namespace.Class.TestMethod>"`

Current repository state: there are no `*Test*.csproj` projects yet, so `dotnet test` currently discovers no tests.

Linting: no dedicated lint command/tooling is configured in this repo. Treat `dotnet build` warnings/errors as the active quality gate.

## High-level architecture

- The constitution defines a **target Onion Architecture** with these layers:
  `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and
  `MediBridge.APIs`.
- The current solution snapshot is still transitional: `MediBridge.slnx` presently
  contains only `MediBridge.APIs`.
- `MediBridge.APIs\Program.cs` wires the app pipeline and service registration:
  - `AddControllers()` + `MapControllers()` (controller-based API style, not minimal API routing)
  - OpenAPI support via `AddEndpointsApiExplorer()` and `AddSwaggerGen()`
  - Swagger UI is enabled only in Development
  - `UseHttpsRedirection()` and `UseAuthorization()` are part of the default request pipeline
- API endpoints currently live under `MediBridge.APIs\Controllers\`. The sample `WeatherForecastController` returns `WeatherForecast` models from `MediBridge.APIs\WeatherForecast.cs`.
- Runtime config is split by environment in `appsettings.json` and `appsettings.Development.json`; launch URLs/profiles are in `Properties\launchSettings.json` (notably `http://localhost:5246` and `https://localhost:7230`).
- `MediBridge.APIs\MediBridge.APIs.http` is the local HTTP scratch file and uses `http://localhost:5246` by default.

## Key repository conventions

- Keep API endpoints in **controller classes** under `MediBridge.APIs\Controllers` with attribute routing (current pattern uses `[Route("[controller]")]`).
- Keep controllers HTTP-focused only; move business logic and orchestration to
  service classes.
- Keep root service/middleware wiring centralized in `Program.cs`; add new services to `builder.Services` and middleware to the app pipeline in order.
- Use repository and unit-of-work abstractions for persistence concerns; avoid direct
  data access in controllers.
- Persistence is SQL Server through Entity Framework Core implementations in
  `MediBridge.Repository`; keep EF Core types out of `MediBridge.Core` and out of
  controller/service public contracts.
- Use the standard API response envelope
  `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` and global
  exception handling middleware for API errors.
- For queue-related features, explicitly define and preserve FIFO ordering, daily limit
  enforcement, and day-end expiry semantics.
- For wallet-related features, implement deterministic debit/credit triggers,
  config-driven platform fee handling, and transactional atomicity.
- Keep Swagger availability development-only unless there is an explicit requirement to expose it elsewhere.
- Follow the existing namespace layout (`MediBridge.APIs` and `MediBridge.APIs.Controllers`) when adding new types.
- This repo includes **SpecKit workflow integration** (`.specify`, `.github/agents`, `.github/prompts`). Feature work is expected to use `specs\<feature-branch>\` artifacts (`spec.md`, `plan.md`, `tasks.md`) and feature branch naming like `001-short-name` (or timestamp-based `YYYYMMDD-HHMMSS-short-name`).

## Active Technologies
- C# / .NET 8 + ASP.NET Core Web API, DI, JWT Bearer authorization, ASP.NET Core rate limiting, Entity Framework Core 8.0.11 SQL Server, existing API envelope/exception/correlation middleware, existing Egypt business clock, existing delivery, wallet, audit, and doctor message abstractions (010-interaction-payments)
- SQL Server through EF Core implementations in `MediBridge.Repository`; all delivery, interaction, wallet, ledger, and audit persistence remains behind Repository + Unit of Work abstractions (010-interaction-payments)
- C# / .NET 8 + ASP.NET Core Web API, DI, JWT Bearer authorization, existing standard API envelope/exception/correlation middleware, Entity Framework Core 8 SQL Server, existing Egypt business clock/date handling, existing campaign, delivery, wallet transaction, ledger, audit, and identity/profile abstractions (012-company-reporting-analytics)
- SQL Server through EF Core implementations in `MediBridge.Repository`; all reporting reads and discrepancy writes remain behind Repository + Unit of Work abstractions (012-company-reporting-analytics)

- C# / .NET 8 + ASP.NET Core Web API, DI, JWT Bearer authorization, Entity Framework Core 8.0.11 SQL Server, Hangfire.AspNetCore 1.8.17, Hangfire.SqlServer 1.8.17, BCL `TimeProvider`/`TimeZoneInfo`, existing API envelope/exception/correlation middleware, existing campaign/file/queue/delivery/wallet/audit abstractions (008-delivery-expiry-jobs)
- SQL Server through EF Core implementations in `MediBridge.Repository`; Hangfire uses its own `HangFire` scheduler schema in the configured SQL Server database, while all MediBridge delivery, wallet, ledger, and job-run records remain behind Repository + Unit of Work abstractions (008-delivery-expiry-jobs)

## Recent Changes

- 008-delivery-expiry-jobs: Added C# / .NET 8 + ASP.NET Core Web API, EF Core SQL Server, Hangfire 1.8.17, and DST-aware `TimeProvider`/`TimeZoneInfo` planning context
