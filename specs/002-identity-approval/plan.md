# Implementation Plan: Identity and Approval (Phase 2)

**Branch**: `[002-identity-approval]` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/002-identity-approval/spec.md`

## Summary

Deliver Phase 2 backend identity by adding account lifecycle, Doctor and Company registration, Admin approval decisions, JWT login gating, refresh-token rotation/reuse detection, logout revocation, password reset scaffolding, email/phone verification scaffolding, and token-based rejected-account resubmission. The implementation approach keeps HTTP controllers thin in `MediBridge.APIs`, places use-case decisions in `MediBridge.Services`, defines pure domain identity contracts in `MediBridge.Core`, keeps ASP.NET Identity framework types in `MediBridge.Repository` infrastructure, and stores identity, profile, token, verification, resubmission, and audit records through SQL Server-oriented EF Core infrastructure in `MediBridge.Repository` with unit-of-work boundaries.

## Temporary Architecture Build Mode

The current implementation pass is intentionally infrastructure-first. Build temporary but correctly layered domain models, repository contracts, EF Core SQL Server mappings, DbContext wiring, and service/API contracts so later database tasks can attach real migrations and behavior cleanly. Do not optimize for running the full test suite in this pass. Keep test tasks in the plan for traceability, but defer test execution until the SQL schema and behavior are ready enough for meaningful verification.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, ASP.NET Core Identity, Entity Framework Core SQL Server, JWT Bearer authentication, FluentValidation, Swagger/OpenAPI, Phase 1 envelope/middleware/rate-limit/current-user/audit foundations  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; Identity persistence plus Phase 2 tables for profiles, refresh credentials, account decisions, reset flows, verification flows, resubmissions, and auth audit events  
**Testing**: `dotnet test` across contract and integration projects, with new identity-focused contract/integration/unit coverage. For the temporary architecture build, test files and commands may remain planned but execution is deferred until the SQL-ready infrastructure compiles and database behavior is implemented.  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: 95% of valid registration, login, refresh, logout, password reset, verification, and approval requests complete with user-visible results in under 2 seconds in QA; approved-user login flow completes in under 2 minutes from user start to token receipt  
**Constraints**: Preserve Phase 1 response envelope, safe exception handling, metadata-only request logging, correlation behavior, rate-limit categories, current-user context, and audit abstraction; require account status `Approved` for token issuance; contact verification is not a Phase 2 token gate; rejected resubmission uses a time-limited single-use token rather than JWT; capture verification metadata only, not file contents; keep `MediBridge.Core` free of ASP.NET Identity framework types  
**Scale/Scope**: Phase 2 identity and approval only; Auth and Admin account-approval endpoints plus persistence and tests; excludes campaign, queue, delivery, wallet, settlement, payout, file content upload/storage/review, pricing, and platform-fee workflows

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Changes map to pure `MediBridge.Core` entities/contracts, `MediBridge.Repository` persistence and ASP.NET Identity infrastructure mapping, `MediBridge.Services` use cases, and `MediBridge.APIs` HTTP wiring with inward-only dependency intent.
- Controller gate: PASS - Auth and Admin controllers expose requests/responses only; registration, approval, token lifecycle, revocation, and audit decisions belong to services.
- Data gate: PASS - Identity and Phase 2 persistence target SQL Server through EF Core in `MediBridge.Repository`, behind repository/unit-of-work discipline; controllers do not access persistence directly.
- Security gate: PASS - Secured routes require JWT and role-aware authorization; token issuance is gated by account status `Approved` and soft-delete state.
- API contract gate: PASS - Identity responses preserve `{ Code, Message, Data }`, HTTP status semantics, correlation behavior, and global safe errors.
- Scope gate: PASS - Spec explicitly excludes queue, wallet, campaign, delivery, file content storage/review, settlement, payout, pricing, and platform-fee behavior.
- Queue gate: PASS (N/A) - Queue behavior is not in Phase 2 scope and no queue ambiguity remains.
- Wallet gate: PASS (N/A) - Wallet behavior is not in Phase 2 scope and no wallet ambiguity remains.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/identity-approval-api.yaml`, and `quickstart.md` keep domain contracts in Core, data access in Repository, use-case orchestration in Services, and HTTP-only concerns in APIs.
- Controller gate: PASS - Contracts define endpoint surfaces only; approval/token/revocation rules remain service-owned.
- Data gate: PASS - Data model identifies entities, relationships, validation, SQL Server table intent, EF Core mapping boundaries, and state transitions for repository/unit-of-work implementation.
- Security gate: PASS - Contracts and quickstart cover JWT, role gates, account-status token gate, refresh reuse detection, logout/password/suspension revocation, rate-limit categories, and non-enumerating recovery responses.
- API contract gate: PASS - OpenAPI artifact keeps all responses inside the standard envelope and preserves appropriate 2xx/4xx semantics.
- Scope gate: PASS - Design artifacts continue to exclude campaign, queue, delivery, wallet, file-content storage/review, settlement, payout, pricing, and platform-fee workflows.
- Queue gate: PASS (N/A) - No queue ordering, daily limit, carry-over, or expiry logic is introduced.
- Wallet gate: PASS (N/A) - No debit/credit, fee, ledger, or transaction atomicity rules are introduced.

## Project Structure

### Documentation (this feature)

```text
specs/002-identity-approval/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── identity-approval-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks, not created by /speckit.plan
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Identity/
│   └── Profiles/
├── Enums/
└── Interfaces/
    ├── Identity/
    └── IAuditLogger.cs

MediBridge.Repository/
├── Data/
│   └── Identity/
├── Configurations/
│   └── Identity/
├── Repositories/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   ├── Auth/
│   └── Admin/
├── Interfaces/
├── Services/
└── Validators/

MediBridge.APIs/
├── Controllers/
│   ├── AuthController.cs
│   └── AdminAccountsController.cs
├── Config/
├── Security/
└── Program.cs

tests/
├── contract/
│   └── MediBridge.ContractTests/
├── integration/
│   └── MediBridge.IntegrationTests/
└── unit/
    └── MediBridge.UnitTests/
```

**Structure Decision**: Use the existing four-project Onion structure. Add Phase 2 domain models and repository contracts under Core, persistence under Repository, use-case orchestration and validators under Services, HTTP endpoints and JWT/Identity wiring under APIs, and identity-specific tests under the existing contract/integration test roots plus a unit test project if not already present.

## Complexity Tracking

No constitution violations were identified, so no complexity exceptions are recorded.

## Final Phase 8 Constitution Review

Final review completed during Phase 8. Layering, thin-controller, SQL Server/EF Core repository boundary, JWT/role authorization, API envelope, safe error handling, and Phase 2 scope gates remain compliant. No deviations were identified.
