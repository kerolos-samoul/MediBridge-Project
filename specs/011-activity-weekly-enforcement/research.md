# Phase 0 Research: Activity & Weekly Enforcement

## Decision: Use the existing Hangfire-in-API-host pattern for Phase 9 jobs

**Rationale**: Phase 7 already introduced Hangfire 1.8.17 in the always-running API host, with recurring registration, job abstractions, and SQL-backed durability. Phase 9 needs two recurring jobs and Admin catch-up controls, but not a separate worker process. Reusing the existing pattern keeps scheduling infrastructure consistent while leaving calculations and business decisions in `MediBridge.Services`.

**Alternatives considered**:

- Separate worker project: deferred because no current scaling requirement justifies a new deployable.
- Plain hosted service/timer: rejected because Phase 7 already chose Hangfire for durable recurring execution and retries.

## Decision: Activity score uses last 30 completed Africa/Cairo calendar days before score date

**Rationale**: The clarified spec excludes the score date itself, preventing messages activated on the current day from lowering a score before doctors have a fair chance to respond. Calendar-day windows align with existing Egypt business-day semantics and company-facing campaign targeting.

**Alternatives considered**:

- Last rolling 30 x 24 hours: rejected because results depend on exact job run time and make catch-up harder to reason about.
- Score date plus prior 29 days: rejected because newly activated score-date deliveries can unfairly hurt doctors.

## Decision: Store one immutable score snapshot per doctor per score date

**Rationale**: A unique `(DoctorId, ScoreDateEgypt)` record makes daily recalculation idempotent under retries and concurrent workers. Updating `DoctorProfile.ActivityScore` in the same candidate transaction keeps company search on a committed whole score.

**Alternatives considered**:

- Append a new row for every recalculation: rejected because retries would inflate history and complicate reporting.
- Only update the doctor profile: rejected because the spec requires historical logs and auditability.

## Decision: Weekly enforcement evaluates the last completed Monday-to-Monday Africa/Cairo week

**Rationale**: The backend plan locks the week start at Monday 00:00 Egypt time. Using the last completed week keeps enforcement stable whether the scheduled run starts exactly at midnight or is retried later.

**Alternatives considered**:

- Rolling last 7 days from job start: rejected because it conflicts with the locked weekly boundary.
- Calendar week in UTC: rejected because the project uses Cairo business time.

## Decision: Weekly participation counts only Accepted and Rejected deliveries

**Rationale**: The business rule states the weekly requirement means Accept + Reject interactions, not delivered/opened/read messages. This reuses Phase 8 final delivery statuses and interaction timestamps without consulting wallet events.

**Alternatives considered**:

- Count reads/views: rejected by the spec and business rule.
- Count wallet Charge/Earn records: rejected because Phase 9 must not depend on wallet mutation for enforcement and must not create wallet ambiguity.

## Decision: Skip weekly violations for any week overlapping doctor suspension

**Rationale**: Clarification selected conservative treatment: if the platform restricted the doctor during any part of the evaluated week, no new violation should be recorded. This avoids prorating disputes and keeps tests deterministic.

**Alternatives considered**:

- Skip only full-week suspensions: rejected because partial platform restriction could still prevent compliance.
- Prorate requirements: rejected as more complex and not specified by business rules.

## Decision: Process approved, non-deleted doctors; include suspended doctors in scoring

**Rationale**: Activity scoring remains useful for audit and future eligibility review while suspension is active. Weekly enforcement should not create new violations for suspended periods, and deleted/unapproved doctors should not participate in marketplace enforcement.

**Alternatives considered**:

- Process all doctors: rejected because deleted/unapproved doctors are outside active marketplace enforcement.
- Process only active non-suspended doctors for scoring: rejected because suspended doctors would lose current historical score evidence.

## Decision: Automatic reactivation occurs at or after `SuspendedUntilUtc`

**Rationale**: A temporary suspension should end without relying on manual cleanup. A dedicated recurring suspension-expiry operation provides the primary automatic path, while running the same check before Phase 9 jobs and Admin violation reads keeps status current if a recurring run is missed. Admins can still reactivate earlier with a required reason.

**Alternatives considered**:

- Manual-only reactivation after expiry: rejected by clarification.
- Expiry affects weekly enforcement only: rejected because doctor status would remain stale.

## Decision: Use a dedicated Phase 9 job-run record

**Rationale**: Existing `DeliveryJobRun` is scoped to delivery jobs and `DeliveryJobType`. Activity scoring and weekly enforcement have different counters, target dates/weeks, and semantics. A dedicated `ActivityEnforcementJobRun` keeps observability clear without overloading delivery concepts.

**Alternatives considered**:

- Extend `DeliveryJobType`: rejected because it mixes delivery activation/expiry with marketplace enforcement.
- No job-run persistence: rejected because the spec requires safe operational outcomes and failure counts.

## Decision: Model weekly decisions separately from rolling summaries

**Rationale**: A unique weekly decision (`Compliant`, `SuspensionSkipped`, `Violation`) per doctor/week gives idempotent job behavior. Rolling 8-week warning/action status can be computed from violation decisions, avoiding denormalized counters that can drift.

**Alternatives considered**:

- Store only violation events: rejected because compliant and skipped outcomes are useful for idempotency and operations.
- Store rolling counters only: rejected because historical audit and window changes would be hard to verify.

## Decision: Admin enforcement action is append-only and also updates doctor profile state

**Rationale**: Admin warning, limit reduction, suspension, and reactivation must be auditable while taking effect on the doctor marketplace profile. The action record plus safe audit event preserves actor, reason, prior/new values, and correlation evidence.

**Alternatives considered**:

- Audit event only: rejected because action-specific querying and validation need structured fields.
- Profile update only: rejected because reasons and previous values would be lost.

## Decision: Admin APIs are protected by existing Admin policy and envelope/rate-limit patterns

**Rationale**: AdminPricing and AdminDeliveryJobs controllers already establish role policy, envelope shape, actor extraction, and rate limiting. Phase 9 should follow that pattern and expose no public job controls.

**Alternatives considered**:

- Infrastructure-only job controls: rejected because the spec requires safe manual catch-up outcomes.
- Public operational endpoints: rejected by security requirements.

## Decision: Phase 9 creates no wallet, queue, delivery activation, expiry, or settlement mutations

**Rationale**: The constitution requires deterministic queue/wallet boundaries. Phase 9 reads delivery/interaction outcomes and updates profile/enforcement history only. Tests must prove no balances, wallet transactions, ledgers, queue rows, activation, expiry, or settlement states change.

**Alternatives considered**:

- Base enforcement on wallet Charge/Earn records: rejected to avoid wallet coupling.
- Apply automatic delivery throttling beyond Admin actions: rejected as outside scope.
