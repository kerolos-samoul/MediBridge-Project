# Implementation Plan: Backend Foundation and Setup (Phase 1)

**Branch**: `[001-backend-phase1]` | **Date**: 2026-04-23 | **Spec**: `specs/001-backend-phase1/spec.md`
**Input**: Feature specification from `/specs/001-backend-phase1/spec.md`

## Summary

Deliver Phase 1 backend foundations by standardizing all API responses to the `{ Code, Message, Data }` envelope, adding global safe exception handling, correlation-aware metadata-only request logging, development-only API documentation exposure, configuration scaffolding for SQL Server/JWT, and backend plan v1.3 security-readiness foundations for rate limiting, audit logging interfaces, current-user context, and ownership helper abstractions while preserving existing business behavior and HTTP status semantics.
The implementation approach is API-layer, cross-cutting pipeline work in `MediBridge.APIs` plus constitution-aligned solution scaffolding for `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` without introducing Phase 2+ domain workflows.

## Technical Context

**Language/Version**: C# / .NET 8
**Primary Dependencies**: ASP.NET Core Web API, middleware pipeline, ASP.NET Core rate limiting primitives, `Microsoft.AspNetCore.Authentication.JwtBearer` (8.0.*), `Swashbuckle.AspNetCore` (6.6.2), JWT Bearer configuration primitives
**Storage**: SQL Server configuration scaffolding via `MediBridge.Repository` abstractions (no Phase 1 schema changes)
**Testing**: `dotnet test` with Phase 1 contract and integration projects, plus HTTP smoke checks and performance harness `tests/performance/phase1-p95-regression.ps1`; unit test project is deferred beyond Phase 1.
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime (Kestrel)
**Project Type**: Layered web service (Onion Architecture)
**Performance Goals**: For `/weatherforecast`, execute 3 post-change runs of 5 minutes each at 10 concurrent users with at least 1000 requests per run under the same baseline environment; each run MUST keep p95 latency <= 110% of pre-Phase-1 baseline.
**Constraints**: Preserve existing business logic and HTTP status behavior; enforce envelope for all responses including framework-generated errors and applied rate-limit rejections; metadata-only logging; never log request/response bodies or auth tokens; expose Swagger only in Development; add rate-limit/audit/current-user/ownership readiness without implementing Phase 2+ flows
**Scale/Scope**: Phase 1 only; cross-cutting API foundation affecting all routes; no queue, wallet, settlement, or weekly enforcement implementation

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Plan maps changes to `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` with inward-only dependency intent.
- Controller gate: PASS - Response shaping, exception handling, and logging are middleware/pipeline concerns; controllers remain HTTP-only.
- Data gate: PASS - No persistence behavior is introduced in Phase 1; no controller data access is required.
- Security gate: PASS - JWT/role constraints remain constitutional defaults; this phase adds settings readiness, rate-limit policy scaffolding, audit contracts, current-user context, and ownership helper abstractions without altering secured flow decisions.
- API contract gate: PASS - Uniform envelope, safe error handling, correlation behavior, status preservation, and 429 envelope behavior for applied rate-limit policies are explicitly defined.
- Scope gate: PASS - Queue and wallet are explicitly out of scope and unchanged.
- Queue gate: PASS (N/A) - Deferred by scope with no remaining ambiguity for this phase.
- Wallet gate: PASS (N/A) - Deferred by scope with no remaining ambiguity for this phase.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/foundation-api.yaml`, and `quickstart.md` keep HTTP concerns in APIs and preserve layer boundaries.
- Controller gate: PASS - Contracts verify behavior without introducing controller business logic.
- Data gate: PASS - Phase 1 data model is operational and cross-cutting only; no direct controller persistence is introduced.
- Security gate: PASS - Metadata-only logging, safe error behavior, rate-limit scaffolding, audit abstractions, and current-user/ownership helpers align with security/privacy requirements while staying foundation-only.
- API contract gate: PASS - Contract artifact enforces envelope application across success and error paths while preserving HTTP status semantics.
- Scope gate: PASS - Design artifacts maintain strict Phase 1 scope and keep queue/wallet domains excluded.
- Queue gate: PASS (N/A) - Intentionally deferred beyond this phase.
- Wallet gate: PASS (N/A) - Intentionally deferred beyond this phase.

## Project Structure

### Documentation (this feature)

```text
specs/001-backend-phase1/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── foundation-api.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
MediBridge.slnx

MediBridge.Core/                    # Planned in Phase 1
├── Entities/
└── Interfaces/                     # Audit/current-user/ownership abstractions

MediBridge.Repository/              # Planned in Phase 1
├── Data/
├── Repositories/
└── UnitOfWork/

MediBridge.Services/                # Planned in Phase 1
├── Interfaces/
├── Services/
└── DTOs/

MediBridge.APIs/                    # Existing project, extended in Phase 1
├── Controllers/
├── Middleware/                     # Planned in Phase 1
├── Config/                         # Planned in Phase 1
├── Security/                       # Planned in Phase 1 rate-limit/current-user adapters
└── Program.cs

tests/                              # Planned test projects and scripts
├── contract/
├── integration/
├── performance/
└── unit/                           # Deferred beyond Phase 1
```

**Structure Decision**: Adopt the constitution-mandated four-layer Onion structure and implement Phase 1 behavior via API cross-cutting components, preserving current endpoint business outcomes and preparing clean layer boundaries for later phases.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Final Phase 7 Constitution Gate Evidence

Recorded on 2026-05-31 after Phase 7 validation.

- Layering gate: PASS - `dotnet test "MediBridge.slnx"` passed contract and integration checks, including layer-boundary and security-readiness tests.
- Controller gate: PASS - route-surface and layering tests confirm production controllers remain HTTP-oriented and no diagnostic/test route is exposed by the production app.
- Data gate: PASS - Phase 1 still introduces no queue, wallet, settlement, identity persistence, or audit-persistence tables.
- Security gate: PASS - rate-limit policy scaffolding, safe error handling, metadata-only logging, current-user context, ownership helper, and Core-owned audit abstractions are present without adding Phase 2+ secured workflows.
- API contract gate: PASS - implemented `/weatherforecast`, validation, safe-error, and applied rate-limit rejection behavior match `contracts/foundation-api.yaml`; no contract example update was required.
- Scope gate: PASS - validation against `docs/backend-plan.md` v1.3 confirms Phase 1 includes response envelope, global exception handling, Swagger development-only policy, SQL/JWT config readiness, rate-limit policy scaffolding, audit/current-user/ownership readiness, and excludes queue, wallet, settlement, identity flows, and audit persistence.
- Performance evidence: FAIL/INCOMPLETE - the p95 regression harness exists and was invoked, but could not run because the required pre-Phase-1 `BaselineP95Ms` value is not recorded in the repository.
- Queue gate: PASS (N/A) - queue behavior remains deferred beyond Phase 1.
- Wallet gate: PASS (N/A) - wallet behavior remains deferred beyond Phase 1.

## Backend Plan v1.3 Phase 1 Scope Validation

Validation source: `docs/backend-plan.md` v1.3.

Confirmed included in Phase 1:

- Standard response envelope `{ Code, Message, Data }`.
- Global exception middleware with safe error wrapping.
- Request logging and correlation ID plumbing.
- Development-only Swagger policy.
- SQL Server connection and JWT configuration readiness.
- Rate-limit policies for login, registration, refresh, company top-up, doctor withdrawal, and doctor interaction categories.
- Core-owned audit logging interface, current-user context, and ownership helper abstractions.

Confirmed excluded from Phase 1 implementation:

- Identity/JWT issuance and approval flows.
- Queue delivery workflows, daily injector, expiry cleaner, and Hangfire job scheduling.
- Wallet balances, ledger transactions, reservation, release, charge, earn, withdrawal, or settlement behavior.
- Campaign review/moderation, delivery, interaction, weekly enforcement, activity score, reporting, and admin tooling workflows.
- Audit persistence tables.

Code-scope scan note: searched source/test trees for queue, wallet, delivery, identity, Hangfire job, and later-phase endpoint markers. The only matches were existing Hangfire package references in `MediBridge.APIs.csproj` and a test-owned `OwnershipRequirement("Wallet", ...)` value used to verify ownership abstractions; no later-phase business workflow implementation was found.
