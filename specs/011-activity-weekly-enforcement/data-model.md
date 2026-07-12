# Data Model: Activity & Weekly Enforcement

## Existing Entities Reused

### DoctorProfile

Existing marketplace profile for a doctor.

**Existing relevant fields**:

- `Id`
- `UserId`
- `DailyMessageLimit`
- `MinimumWeeklyRequirement`
- `ActivityScore`
- `Status` (`Active`, `Warned`, `Suspended`)
- `PricePerMessage`
- `IsDeleted`
- `DeletedAtUtc`

**Phase 9 changes**:

- `SuspendedAtUtc` nullable UTC timestamp
- `SuspendedUntilUtc` nullable UTC timestamp
- `LastStatusChangedAtUtc` nullable UTC timestamp if not already supplied by profile/account state

**Validation rules**:

- `ActivityScore` is constrained to 0.0 through 100.0 and stored with one-decimal business precision.
- `SuspendedUntilUtc` is required and future when `Status = Suspended`.
- `SuspendedAtUtc` is required when `Status = Suspended`.
- `SuspendedUntilUtc` and `SuspendedAtUtc` are cleared or preserved only according to the chosen audit strategy when automatic/manual reactivation occurs; the enforcement action record remains authoritative history.
- Deleted doctors are excluded from Phase 9 job candidate sets.
- Unapproved doctors are excluded by joining to the approved identity/account status already used for marketplace eligibility.

### DoctorAdDelivery

Existing delivery record from Phase 7/8.

**Phase 9 usage**:

- Read-only source for delivered count, Accepted/Rejection interaction count, interaction timestamps, delivery timestamps, feedback text, delivery dates, and doctor id.
- Phase 9 MUST NOT change delivery status, read state, interaction state, reservation status, settlement fields, or wallet references.

## New Entities

### ActivityScoreHistory

Immutable daily score snapshot for one approved, non-deleted doctor and score date.

**Fields**:

- `Id`
- `DoctorId`
- `ScoreDateEgypt`
- `WindowStartDateEgypt`
- `WindowEndDateEgypt`
- `DeliveredCount`
- `InteractedCount`
- `FeedbackQualifiedCount`
- `ResponseSpeedScore`
- `EngagementScore`
- `FeedbackScore`
- `FinalScore`
- `CalculationMode` (`DefaultNoDeliveries`, `ZeroInteractions`, `Calculated`)
- `DoctorWasSuspended`
- `CalculatedAtUtc`
- `JobRunId` nullable
- `CreatedAtUtc`

**Relationships**:

- Many snapshots belong to one `DoctorProfile`.
- Optional reference to `ActivityEnforcementJobRun`.

**Validation and constraints**:

- Unique `(DoctorId, ScoreDateEgypt)`.
- `WindowEndDateEgypt` is the day before `ScoreDateEgypt`.
- `WindowStartDateEgypt` is 29 days before `WindowEndDateEgypt`.
- Counts are non-negative.
- Scores are 0.0 through 100.0.
- `FinalScore` is rounded to one decimal.
- `CalculationMode = DefaultNoDeliveries` requires `DeliveredCount = 0` and `FinalScore = 95.0`.
- `CalculationMode = ZeroInteractions` requires `DeliveredCount > 0`, `InteractedCount = 0`, and all sub-scores/final score are 0.0.

### WeeklyEnforcementDecision

One idempotent weekly participation decision for one approved, non-deleted doctor and one completed Cairo week.

**Fields**:

- `Id`
- `DoctorId`
- `WeekStartDateEgypt`
- `WeekEndDateEgypt`
- `MinimumWeeklyRequirement`
- `InteractionCount`
- `Decision` (`Compliant`, `SuspensionSkipped`, `Violation`)
- `SuspensionOverlapped`
- `RollingViolationCountAfterDecision`
- `CreatedAtUtc`
- `JobRunId` nullable

**Relationships**:

- Many decisions belong to one `DoctorProfile`.
- Optional reference to `ActivityEnforcementJobRun`.

**Validation and constraints**:

- Unique `(DoctorId, WeekStartDateEgypt)`.
- `WeekStartDateEgypt` is a Monday.
- `WeekEndDateEgypt` is the following Monday boundary date.
- `MinimumWeeklyRequirement` and `InteractionCount` are non-negative.
- `Decision = SuspensionSkipped` requires `SuspensionOverlapped = true`.
- `Decision = Violation` requires `SuspensionOverlapped = false` and `InteractionCount < MinimumWeeklyRequirement`.
- `Decision = Compliant` requires `SuspensionOverlapped = false` and `InteractionCount >= MinimumWeeklyRequirement`.

### DoctorWeeklyViolation

Audit-preserved violation event for a weekly decision that missed the minimum.

**Fields**:

- `Id`
- `DoctorId`
- `WeeklyEnforcementDecisionId`
- `WeekStartDateEgypt`
- `WeekEndDateEgypt`
- `MinimumWeeklyRequirement`
- `InteractionCount`
- `RollingViolationCount`
- `CreatedAtUtc`
- `AuditEventId` nullable

**Relationships**:

- One violation belongs to one `WeeklyEnforcementDecision`.
- Many violations belong to one `DoctorProfile`.
- Optional reference to `AuditEvent`.

**Validation and constraints**:

- Unique `(DoctorId, WeekStartDateEgypt)`.
- `InteractionCount < MinimumWeeklyRequirement`.
- Historical rows are never modified or deleted to alter violation history.
- Rolling count is evidence captured at creation time; current Admin summaries recalculate rolling count from decisions/violations.

### DoctorEnforcementAction

Structured, append-only Admin enforcement decision.

**Fields**:

- `Id`
- `DoctorId`
- `ActorAdminUserId`
- `ActionType` (`Warn`, `ReduceDailyLimit`, `Suspend`, `Reactivate`, `AutomaticReactivate`)
- `Reason`
- `PreviousStatus`
- `NewStatus`
- `PreviousDailyMessageLimit`
- `NewDailyMessageLimit` nullable
- `SuspendedAtUtc` nullable
- `SuspendedUntilUtc` nullable
- `EffectiveAtUtc`
- `CorrelationId` nullable
- `AuditEventId` nullable
- `CreatedAtUtc`

**Relationships**:

- Many actions belong to one `DoctorProfile`.
- Actor id references Admin identity user.
- Optional reference to `AuditEvent`.

**Validation and constraints**:

- `Reason` is required for Admin-initiated actions.
- `ActionType = Suspend` requires `NewStatus = Suspended`, `SuspendedAtUtc`, and future `SuspendedUntilUtc`.
- `ActionType = Reactivate` requires `PreviousStatus = Suspended`, `NewStatus = Active`, and a required reason.
- `ActionType = AutomaticReactivate` requires `PreviousStatus = Suspended`, `NewStatus = Active`, and `EffectiveAtUtc >= SuspendedUntilUtc`.
- `ActionType = ReduceDailyLimit` requires a non-negative `NewDailyMessageLimit` that satisfies configured platform delivery-setting constraints.
- Actions never delete or rewrite score history, violation history, delivery history, wallet history, or campaign history.

### ActivityEnforcementJobRun

Operational evidence for Phase 9 scheduled or manual catch-up runs.

**Fields**:

- `Id`
- `JobType` (`DailyActivityScore`, `WeeklyEnforcement`, `SuspensionExpiry`)
- `TargetScoreDateEgypt` nullable
- `TargetWeekStartDateEgypt` nullable
- `StartedAtUtc`
- `CompletedAtUtc` nullable
- `Status` (`Running`, `Succeeded`, `PartiallySucceeded`, `Failed`, `Deferred`, `Interrupted`)
- `ProcessedCount`
- `SkippedCount`
- `CreatedCount`
- `UpdatedCount`
- `FailedCount`
- `SafeFailureSummary` nullable
- `RequestedByAdminUserId` nullable
- `CreatedAtUtc`

**Validation and constraints**:

- Daily score runs require `TargetScoreDateEgypt`.
- Weekly enforcement runs require `TargetWeekStartDateEgypt`.
- No stack traces, credentials, raw exception dumps, or sensitive account metadata in `SafeFailureSummary`.
- Running rows older than a configured threshold may be marked Interrupted before a new run starts.

## Read Models and DTO Shapes

### Violation Summary

Admin-facing projection.

**Fields**:

- `DoctorId`
- `DoctorDisplayName` or safe identifier available from profile/account
- `Status`
- `DailyMessageLimit`
- `MinimumWeeklyRequirement`
- `ActivityScore`
- `SuspendedUntilUtc`
- `RollingViolationCount`
- `Eligibility` (`None`, `Warning`, `ActionEligible`)
- `RecentViolationWeeks`
- `LastEnforcementAction`

### Enforcement Action Result

Admin-facing result after a status/limit action.

**Fields**:

- `DoctorId`
- `Status`
- `DailyMessageLimit`
- `SuspendedUntilUtc`
- `ActionType`
- `EffectiveAtUtc`
- `AuditEventId`

### Activity Job Outcome

Admin-facing job status/catch-up result.

**Fields**:

- `JobRunId`
- `JobType`
- `TargetDateOrWeek`
- `Status`
- `ProcessedCount`
- `SkippedCount`
- `CreatedCount`
- `UpdatedCount`
- `FailedCount`
- `SafeFailureSummary`

## Repository Contracts

### Profile Repository Extensions

- Page approved, non-deleted doctor ids for activity scoring.
- Page approved, non-deleted doctor ids for weekly enforcement.
- Find doctor profile for enforcement update by id with update lock.
- Find suspension-expired doctors for automatic reactivation.
- Apply current activity score to doctor profile.
- Apply warning, daily-limit reduction, suspension, and reactivation state changes.
- Check whether a doctor suspension overlaps a Cairo week.

### Delivery Repository Extensions

- Aggregate delivered/interacted/feedback-qualified counts for one doctor and score window.
- Aggregate response time contributions for one doctor and score window.
- Count Accepted/Rejection interactions for one doctor and weekly window.
- Provide batch aggregates by doctor where practical to avoid N+1 query behavior.

### Phase 9 Repository Additions

- Add/find `ActivityScoreHistory` by `(DoctorId, ScoreDateEgypt)`.
- Add/find `WeeklyEnforcementDecision` by `(DoctorId, WeekStartDateEgypt)`.
- Add/find `DoctorWeeklyViolation` by `(DoctorId, WeekStartDateEgypt)`.
- Query rolling 8-week violation summaries with pagination/filtering.
- Add `DoctorEnforcementAction`.
- Add/update/list `ActivityEnforcementJobRun`.

## State Transitions

### Doctor Marketplace Status

```text
Active -> Warned       (Admin warning action)
Active -> Suspended    (Admin suspension action with future SuspendedUntilUtc)
Warned -> Suspended    (Admin suspension action with future SuspendedUntilUtc)
Warned -> Active       (Admin reactivation/clear warning when policy allows)
Suspended -> Active    (automatic at or after SuspendedUntilUtc, or manual earlier by Admin reason)
Suspended -> Suspended (Admin extends suspension with new future SuspendedUntilUtc and reason)
```

Daily-limit reduction may occur while status is Active or Warned when policy conditions allow. It changes `DailyMessageLimit` and creates an enforcement action but does not change delivery, queue, wallet, or settlement history.

### Weekly Enforcement Decision

```text
Missing -> Compliant          (eligible doctor meets/exceeds requirement)
Missing -> SuspensionSkipped  (suspension overlaps evaluated week)
Missing -> Violation          (eligible non-suspended doctor misses requirement)
Existing -> Existing          (retry/replay; no duplicate decision)
```

### Activity Score Snapshot

```text
Missing -> Created
Existing -> Existing/Replayed
```

The doctor profile current `ActivityScore` is updated with the snapshot's `FinalScore` in the same candidate transaction when the snapshot is created or confirmed.

## Indexes and Constraints

- `ActivityScoreHistories`: unique `(DoctorId, ScoreDateEgypt)`, index `(ScoreDateEgypt, DoctorId)`.
- `WeeklyEnforcementDecisions`: unique `(DoctorId, WeekStartDateEgypt)`, index `(WeekStartDateEgypt, Decision, DoctorId)`.
- `DoctorWeeklyViolations`: unique `(DoctorId, WeekStartDateEgypt)`, index `(WeekStartDateEgypt, DoctorId)`.
- `DoctorEnforcementActions`: index `(DoctorId, CreatedAtUtc DESC)`, index `(ActorAdminUserId, CreatedAtUtc DESC)`.
- `ActivityEnforcementJobRuns`: index `(JobType, StartedAtUtc DESC)`, index `(JobType, TargetScoreDateEgypt)`, index `(JobType, TargetWeekStartDateEgypt)`.
- `DoctorProfiles`: index `(Status, SuspendedUntilUtc)` for automatic reactivation candidates; check constraint requiring future `SuspendedUntilUtc` is enforced by service validation because "future" is time-relative.
- Existing `DoctorAdDeliveries` indexes may need additions for `(DoctorId, DeliveryDateEgypt, Status, InteractedAtUtc)` and `(DoctorId, DeliveryDateEgypt)` aggregate windows if current indexes are not sufficient.

## Candidate Transaction Boundaries

- Daily activity candidate: read aggregate inputs, create/replay score snapshot, update doctor current score, save atomically.
- Weekly enforcement candidate: lock/recheck doctor eligibility and suspension overlap, read weekly interaction count, create/replay weekly decision and violation if needed, save atomically.
- Admin enforcement action: lock doctor profile, validate current state and request, update doctor status/limit/suspension fields, add enforcement action, add audit event, save atomically.
- Automatic reactivation candidate: lock expired suspended doctor, set Active, add automatic enforcement action and audit event, save atomically.

## Explicit Non-Mutations

Phase 9 must not update:

- `DoctorMessageQueues`
- `DoctorAdDeliveries` status/reservation/interaction/read fields
- `Wallets`
- `WalletTransactions`
- `WalletLedgerEntries`
- Campaign status or review history
- Payment transaction state
- Withdrawal state
