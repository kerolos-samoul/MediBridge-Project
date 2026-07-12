# Implementation Plan: Phase 9 Activity & Weekly Enforcement

**Branch**: `[011-activity-weekly-enforcement]` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/011-activity-weekly-enforcement/spec.md`

## Summary

Deliver the Phase 9 backend slice that recalculates doctor Activity Score daily from the last 30 completed `Africa/Cairo` calendar days, records immutable score history, evaluates weekly Accept/Reject minimums for the last completed Monday-to-Monday Egypt week, tracks rolling 8-week violations, and gives Admin users violation review plus manual warning/reduce/suspend/reactivate controls. Reuse the existing Phase 7 Hangfire hosting pattern, `IEgyptBusinessClock`, Repository + Unit of Work persistence, Admin JWT authorization, response envelope, and safe audit plumbing. Phase 9 reads committed delivery/interaction outcomes from Phase 8 and updates doctor status/limits and activity/enforcement history only; it does not mutate queue, delivery activation/expiry, settlement, wallet balances, campaign reporting, withdrawals, or payment-gateway behavior.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer authorization, ASP.NET Core rate limiting, Entity Framework Core 8.0.11 SQL Server, existing Hangfire 1.8.17 scheduling, existing API envelope/exception/correlation middleware, existing `IEgyptBusinessClock`, existing delivery interaction/read models, existing audit repository, existing admin controller/service patterns  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository`; all activity score history, weekly enforcement decisions, violation events, admin enforcement actions, doctor status/limit changes, and job-run records remain behind Repository + Unit of Work abstractions  
**Testing**: xUnit, FluentAssertions, ASP.NET Core test host, and SQL Server Testcontainers through existing unit, contract, and integration projects  
**Target Platform**: Always-running ASP.NET Core server process with Hangfire workers plus Admin-only HTTP APIs  
**Project Type**: Layered web service using the existing Onion Architecture  
**Performance Goals**: At least 95% of warmed Admin violation-list requests for a page of up to 100 doctors complete within 1 second; daily score and weekly enforcement retries/concurrent attempts produce exactly one per-doctor/date or per-doctor/week decision in 100% of tested cases; company doctor search always sees either the previous committed score or the newly committed score, never partial sub-scores  
**Constraints**: DST-aware `Africa/Cairo`; score window is the last 30 completed Cairo calendar days before the score date; score date deliveries are excluded; weekly window is the last completed Monday-to-Monday Cairo week; weekly requirement counts Accept plus Reject only; activity scoring processes approved, non-deleted doctors including suspended doctors; weekly enforcement skips any week overlapping suspension; temporary suspension requires future `SuspendedUntilUtc`; automatic reactivation occurs at or after that timestamp through a dedicated suspension-expiry operation plus opportunistic checks before Phase 9 jobs/Admin reads; Admin may reactivate earlier with a reason; warning stage is rolling violations 1-5 and manual reduce/suspend eligibility starts at 6; no raw EF Core outside Repository; no public job-control endpoint; no wallet, queue, settlement, expiry, activation, reporting, withdrawal, or gateway mutation  
**Scale/Scope**: Add Phase 9 entities/configurations/migration, repository contracts and implementations for activity score history, weekly enforcement decisions/violations, enforcement actions, and activity job runs; extend doctor profile state with suspension expiry; add services for daily score calculation, weekly enforcement, automatic suspension expiry, Admin violation review, Admin doctor enforcement actions, and Admin job/catch-up enqueue/status; add Admin controllers/contracts, validators, DI/recurring job registration, and unit/contract/integration/performance-oriented tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- **Layering gate: PASS** - Doctor status rules, activity/enforcement entities, repository contracts, read models, and clock-facing domain decisions belong in `MediBridge.Core`; SQL Server/EF Core persistence and row locking remain in `MediBridge.Repository`; score/enforcement/admin orchestration belongs in `MediBridge.Services`; Hangfire registration and HTTP controllers remain in `MediBridge.APIs`.
- **Controller gate: PASS** - Admin controllers will map requests, current admin identity, route/query/body values, response envelopes, and rate-limit/security attributes only. They will not compute scores, count weekly interactions, mutate doctor status directly, or access persistence.
- **Data gate: PASS** - Activity history, weekly decisions, violation events, enforcement actions, doctor profile updates, and job-run records use Repository + Unit of Work contracts. Services consume abstractions and do not depend on EF Core.
- **Security gate: PASS** - Violation review, enforcement actions, and manual catch-up job requests require Admin JWT authorization. Scheduled job execution has no public HTTP surface.
- **API contract gate: PASS** - Admin responses and job outcome responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`; failures flow through global exception middleware without raw stacks or internal job details.
- **Scope gate: PASS** - The plan explicitly excludes Phase 10 reporting analytics, withdrawal payout, payment gateway behavior, interaction settlement changes, queue activation, expiry release, and campaign moderation changes.
- **Queue gate: PASS** - Phase 9 reads committed deliveries/interactions only. It does not activate, expire, requeue, cancel, reprioritize, or carry over queue items.
- **Wallet gate: PASS** - Phase 9 creates no company charge, doctor earning, reservation, release, payout, wallet transaction, or ledger entry. It only reads settled delivery outcomes for score/enforcement calculations.

### Post-Phase 1 Design Re-check

- **Layering gate: PASS** - [data-model.md](./data-model.md), [contracts/activity-weekly-enforcement-api.yaml](./contracts/activity-weekly-enforcement-api.yaml), and [quickstart.md](./quickstart.md) keep EF Core in Repository, calculations and admin decisions in Services, and HTTP/Hangfire wiring in APIs.
- **Controller gate: PASS** - The contract requires only Admin transport mapping, envelope shaping, and request forwarding to services.
- **Data gate: PASS** - The model defines repository methods, unique per-doctor/date and per-doctor/week constraints, suspension overlap checks, safe job-run outcomes, and audit persistence without exposing `DbContext` to controllers or services.
- **Security gate: PASS** - Admin JWT, role policy, rate limiting, safe failure responses, and non-public scheduled execution are explicit and testable.
- **API contract gate: PASS** - Success, validation, authorization, conflict, and job-failure outcomes retain the standard envelope.
- **Scope gate: PASS** - Research and design preserve all queue/wallet/settlement/reporting/withdrawal/gateway exclusions.
- **Queue gate: PASS** - No Phase 9 artifact changes queue state, ordering, delivery activation, expiry, or carry-over behavior.
- **Wallet gate: PASS** - The data model and quickstart include verification that wallet balances, transactions, and ledgers remain unchanged by Phase 9 operations.

## Project Structure

### Documentation (this feature)

```text
specs/011-activity-weekly-enforcement/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── activity-weekly-enforcement-api.yaml
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks; not created here
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   ├── Messaging/
│   ├── Policies/
│   └── Profiles/
├── Enums/
└── Interfaces/
    ├── Identity/
    ├── Messaging/
    └── Policies/

MediBridge.Repository/
├── Configurations/
│   ├── Identity/
│   ├── Messaging/
│   └── Policies/
├── Data/
├── Migrations/
├── Repositories/
│   ├── Identity/
│   ├── Messaging/
│   └── Policies/
└── UnitOfWork/

MediBridge.Services/
├── DTOs/
│   ├── Admin/
│   └── Messaging/
├── Interfaces/
├── Services/
└── Validators/
    └── Admin/

MediBridge.APIs/
├── Config/
├── Controllers/
├── Extensions/
├── Security/
└── Program.cs

tests/
├── contract/MediBridge.ContractTests/
├── integration/MediBridge.IntegrationTests/
└── unit/MediBridge.UnitTests/
```

**Structure Decision**: Extend the existing four projects and Admin API surface. Add no worker project: Phase 9 recurring jobs run through the existing Hangfire-in-API-host pattern, while business behavior stays in Services and all persistence stays behind Core repository contracts implemented in Repository.

## Resolved Implementation Decisions

- Add daily Activity Score job service scheduled at `00:30 Africa/Cairo` using the existing Hangfire registration pattern. The job accepts an optional score date for Admin catch-up, captures Cairo time through `IEgyptBusinessClock`, and evaluates the last 30 completed Cairo calendar days before that score date.
- Process approved, non-deleted doctors for activity scoring, including suspended doctors. Exclude deleted and unapproved doctors from score processing. Preserve a default score of `95.0` when no deliveries exist in the score window.
- Compute response speed from Accept/Reject interactions only, clamp each contribution to 0-100, then average. Compute engagement as interacted deliveries divided by delivered deliveries. Compute feedback score from interactions whose feedback has at least 15 non-whitespace characters. Final score is `0.4 * response + 0.3 * engagement + 0.3 * feedback`, rounded to one decimal and clamped to 0.0-100.0.
- Add immutable `ActivityScoreHistory` with a unique `(DoctorId, ScoreDateEgypt)` key. Repeated/concurrent runs for the same doctor/date create or preserve one snapshot and update `DoctorProfile.ActivityScore` atomically with that snapshot.
- Add weekly enforcement job service scheduled at Monday `00:00 Africa/Cairo`. It evaluates the last completed Monday-to-Monday Cairo week, counts only Accepted and Rejected deliveries/interactions, and skips deleted, unapproved, and any doctor whose suspension overlaps any part of the evaluated week.
- Add a dedicated suspension-expiry job service scheduled every 5 minutes using the existing Hangfire registration pattern. It finds approved, non-deleted suspended doctors with `SuspendedUntilUtc <= nowUtc`, reactivates each eligible doctor exactly once, records an `AutomaticReactivate` action, and is also callable by an Admin-only catch-up endpoint.
- Add a weekly decision record with a unique `(DoctorId, WeekStartDateEgypt)` key and decision type `Compliant`, `SuspensionSkipped`, or `Violation`. Violation decisions also contribute to an immutable violation event/read model used for rolling 8-week summaries.
- Compute rolling warning/action status from violation decisions in the last 8 completed weekly windows. Rolling counts 1-5 are warning-stage; rolling counts greater than 5 make the doctor eligible for manual daily-limit reduction or temporary suspension.
- Extend `DoctorProfile` with `SuspendedUntilUtc`, `SuspendedAtUtc`, and status-change metadata sufficient to enforce temporary suspension expiry and preserve audit context. `SuspendedUntilUtc` must be future UTC when status becomes Suspended.
- Automatic reactivation occurs at or after `SuspendedUntilUtc`. Implement it as a service-owned operation that runs through a dedicated recurring Hangfire job, through an Admin-only catch-up endpoint, at the start of Phase 9 jobs, and before Admin violation reads. Every automatic reactivation writes one `AutomaticReactivate` enforcement action plus safe audit/history evidence. Admin may manually reactivate earlier with a required reason.
- Add Admin violation review at `GET /api/admin/violations` with standard pagination and filters for doctor id, status, eligibility, week range, and rolling count bucket. It returns warning/action eligibility, current status, daily limit, minimum weekly requirement, `SuspendedUntilUtc`, rolling count, and recent violation weeks.
- Add Admin doctor enforcement action endpoint at `PUT /api/admin/doctors/{doctorId}/status`. It supports `Warn`, `ReduceDailyLimit`, `Suspend`, and `Reactivate`; all require a reason. `ReduceDailyLimit` requires a valid non-negative/new configured limit. `Suspend` requires future `SuspendedUntilUtc`. `Reactivate` clears active suspension state.
- Add Admin activity job endpoints under `api/admin/activity-jobs` for status, daily score catch-up enqueue/run request, weekly enforcement catch-up enqueue/run request, and suspension-expiry catch-up request. Keep them Admin-only and rate-limited; scheduled execution remains non-public.
- Record every Admin enforcement action in `DoctorEnforcementAction` plus safe `AuditEvent` metadata with actor, target doctor, previous/new status, previous/new daily limit, `SuspendedUntilUtc` when applicable, reason, correlation id, and timestamp.
- Add `ActivityEnforcementJobRun` or equivalent Phase 9 job-run record rather than overloading Phase 7 `DeliveryJobRun`. Store target score date, week start, or null target for suspension expiry, processed/skipped/created/updated/failed counts, terminal status, and safe failure summary only.
- Add repository methods for approved/non-deleted doctor pages for scoring/enforcement, delivery aggregate counts by doctor/date window, weekly interaction counts, suspension overlap checks, rolling violation summaries, score snapshot upsert/lookup, weekly decision insert-or-replay, enforcement action persistence, and automatic reactivation candidates.
- Use candidate-sized transactions for score snapshot/current-score updates, weekly decision creation, and admin enforcement actions. Job-level failures must not leave partial per-doctor mutations; retried jobs converge through unique keys and state rechecks.
- Unit tests cover Cairo score/week window calculations, score formula and clamping, zero-delivery and zero-interaction defaults, feedback eligibility, suspension overlap logic, rolling 8-week counts, automatic expiry of suspension, admin action validation, and idempotency classifiers.
- Contract tests cover Admin authorization, envelopes, pagination/filter parameters, action payload validation, required reasons, `SuspendedUntilUtc` validation, and safe forbidden/unauthorized behavior.
- SQL Server integration tests cover unique snapshot/weekly decision constraints, concurrent job retries, admin enforcement audit persistence, automatic reactivation, company doctor search reading the latest committed score, and no mutation to wallet/queue/settlement tables.

## Performance Test Profile

- Run Release build on .NET 8 with Server GC, SQL Server 2022 Testcontainers on the same host, at least 4 dedicated vCPUs, 8 GB available RAM, SSD-backed storage, `BatchSize = 100`, and no debugger, coverage collector, or parallel non-test workload.
- Admin violation-list profile: seed at least 1,000 approved doctors with 8 weeks of mixed compliant/violation/suspension-skip decisions; issue 20 sequential warm-up requests followed by 200 measured requests at concurrency 10 for pages of 100; require at least 190 requests within 1 second, zero failed valid requests, bounded query count, and report p50/p95/p99.
- Job profile: seed 1,000 approved doctors, 30 completed Cairo days of representative deliveries/interactions, and 8 weeks of enforcement history; run one warm-up daily score and weekly enforcement cycle, then three clean measured cycles; require no duplicate snapshots/decisions, no wallet/queue mutation, bounded batches/memory/query behavior, and safe per-doctor failure accounting.
- Performance tests are opt-in outside the designated CI profile. A skipped run must report unmet prerequisites and is not passing performance evidence.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.

## Phase 0 Research Summary

[research.md](./research.md) resolves the score window, scoring formula details, weekly enforcement window, suspension overlap policy, job idempotency, automatic suspension expiry, Admin enforcement action model, violation summary design, safe job-run observability, security posture, and no-wallet/no-queue boundaries. No unresolved clarification markers remain.

## Phase 1 Design Summary

- [data-model.md](./data-model.md) defines new and changed entities, validation rules, state transitions, unique constraints, repository methods, and candidate transaction boundaries.
- [contracts/activity-weekly-enforcement-api.yaml](./contracts/activity-weekly-enforcement-api.yaml) defines Admin violation review, doctor enforcement actions, and Admin job-status/catch-up contracts.
- [quickstart.md](./quickstart.md) provides migration, scheduled/manual job, admin action, suspension expiry, authorization, idempotency, score-window, no-wallet/no-queue, performance, and verification steps.
