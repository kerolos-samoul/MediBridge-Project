# Feature Specification: Activity & Weekly Enforcement (Phase 9)

**Feature Branch**: `[011-activity-weekly-enforcement]`  
**Created**: 2026-07-12  
**Status**: Draft  
**Input**: User description: "Phase 9: Activity & Weekly Enforcement in backend plan"

## Clarifications

### Session 2026-07-12

- Q: Should temporary suspension require a defined duration? → A: Temporary suspension requires a future UTC `SuspendedUntilUtc` date/time and reason; the doctor automatically becomes active at or after that time through a dedicated suspension-expiry operation, and admin may manually reactivate earlier.
- Q: Which doctors are eligible for daily activity scoring and weekly enforcement? → A: Process approved, non-deleted doctors; include suspended doctors in activity scoring but skip new weekly violations during suspension.
- Q: How should weekly enforcement handle a suspension that overlaps only part of the evaluated week? → A: If suspension overlaps any part of the evaluated week, record no new weekly violation for that week.
- Q: Which 30-day window should daily activity scoring use? → A: Use the last 30 completed Africa/Cairo calendar days before the score date; exclude the score date itself.
- Q: What happens when a temporary suspension reaches `SuspendedUntilUtc`? → A: Doctor automatically becomes active at or after `SuspendedUntilUtc` through a recurring suspension-expiry operation and may also be reactivated by the same operation before Phase 9 jobs or Admin violation reads; admin may manually reactivate earlier with a reason.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recalculate Doctor Activity Scores Daily (Priority: P1)

As a pharmaceutical company user, I need doctor activity scores to reflect recent doctor responsiveness so that doctor search and campaign targeting favor doctors who interact reliably with delivered campaign messages.

**Why this priority**: Activity score is already used in company-side doctor filtering and target snapshots. If it is stale or manually maintained, campaign targeting becomes misleading.

**Independent Test**: Prepare doctors with delivered messages across the last 30 completed Africa/Cairo calendar days before the score date, run the daily activity calculation, and confirm each doctor's current score and score history match the locked formula, including doctors with no deliveries and doctors with deliveries but no interactions.

**Acceptance Scenarios**:

1. **Given** a doctor has no delivered messages in the last 30 completed Africa/Cairo calendar days before the score date, **When** the daily activity score calculation runs, **Then** the doctor's current Activity Score becomes 95.0 and a history snapshot records the defaulted result.
2. **Given** a doctor has delivered messages with Accept or Reject interactions in the last 30 completed Africa/Cairo calendar days before the score date, **When** the daily calculation runs, **Then** the system computes response speed, engagement, and feedback sub-scores, combines them using the weighted formula, rounds the final score to one decimal place, and stores both the current score and an immutable history snapshot.
3. **Given** a doctor has delivered messages in the last 30 completed Africa/Cairo calendar days before the score date but no Accept or Reject interactions, **When** the daily calculation runs, **Then** all sub-scores are 0 and the current Activity Score becomes 0.0 with a history snapshot explaining the zero-interaction basis.
4. **Given** feedback exists on an interaction but has fewer than 15 non-whitespace characters, **When** feedback score is calculated, **Then** that interaction is not counted as feedback-qualified even though the interaction remains counted for response speed and engagement.
5. **Given** a company searches doctors after the daily calculation completes, **When** the company filters or sorts by activity score, **Then** the company sees the latest stored score and no stale value from before the calculation.
6. **Given** an approved, non-deleted suspended doctor has delivery activity in the activity score window, **When** the daily activity score calculation runs, **Then** the doctor's Activity Score and history snapshot are still updated for audit and future eligibility review.

---

### User Story 2 - Enforce Weekly Interaction Minimums (Priority: P1)

As a platform operator, I need the system to check each doctor's weekly Accept and Reject interactions against the doctor's minimum weekly requirement so that low participation is detected consistently and can be reviewed before campaign quality declines.

**Why this priority**: The business rule defines weekly participation using billable interactions only. Enforcement must be automatic, time-bound, and auditable before admin action is meaningful.

**Independent Test**: Prepare approved, non-deleted doctors with different minimum weekly requirements, interaction counts, and suspension states in the prior Egypt week, run weekly enforcement, and confirm only eligible non-suspended below-threshold doctors receive one violation for that week while compliant and suspended doctors receive none.

**Acceptance Scenarios**:

1. **Given** the week starts Monday 00:00 Africa/Cairo and an approved, non-deleted doctor whose suspension does not overlap the completed week has fewer Accept plus Reject interactions than the doctor's minimum weekly requirement, **When** weekly enforcement runs, **Then** one violation event is recorded for that doctor and week.
2. **Given** an approved, non-deleted doctor whose suspension does not overlap the completed week meets or exceeds the minimum weekly requirement, **When** weekly enforcement runs, **Then** no violation event is recorded for that doctor and week.
3. **Given** a doctor opened or read messages but did not Accept or Reject enough of them, **When** weekly enforcement counts participation, **Then** reads and views do not satisfy the weekly requirement.
4. **Given** a doctor's suspension overlaps any part of the completed week being evaluated, **When** weekly enforcement runs, **Then** no new weekly violation is recorded for that doctor/week.
5. **Given** weekly enforcement is rerun for the same completed week after a retry or service restart, **When** the run completes, **Then** duplicate violation events are not created.
6. **Given** a doctor has violation events outside the rolling 8-week window, **When** the current warning count is calculated, **Then** old violations are excluded from the rolling count while historical audit records remain preserved.

---

### User Story 3 - Review Violations and Apply Admin Actions (Priority: P2)

As an admin, I need to review doctors with recent weekly violations and apply manual enforcement actions so that warnings, daily-limit reductions, suspensions, and reactivations are controlled, justified, and auditable.

**Why this priority**: The locked decision says the system tracks warnings automatically, while admin applies reduce-limit or suspension decisions manually after repeated violations.

**Independent Test**: Authenticate as an admin, list doctors with rolling 8-week violations, inspect warning/action eligibility, apply a reasoned daily-limit reduction or temporary suspension, and confirm the doctor's status/limit changes and audit trail are visible.

**Acceptance Scenarios**:

1. **Given** a doctor has 1 to 5 violations in the rolling 8-week window, **When** an admin views violations, **Then** the doctor is shown with warning status and the current rolling count.
2. **Given** a doctor has more than 5 violations in the rolling 8-week window, **When** an admin views violations, **Then** the doctor is shown as eligible for manual daily-limit reduction or temporary suspension.
3. **Given** an admin applies a daily-limit reduction with a required reason, **When** the action succeeds, **Then** the doctor's enforced daily limit changes, the doctor remains traceable to the action, and an audit record captures the actor, reason, prior value, new value, and time.
4. **Given** an admin temporarily suspends a doctor with a required reason and future UTC `SuspendedUntilUtc` date/time, **When** the action succeeds, **Then** the doctor's status changes, the suspension end time is recorded, and the action is auditable without deleting violation history.
5. **Given** a doctor's temporary suspension has reached `SuspendedUntilUtc` or an admin reactivates the doctor earlier with a required reason, **When** reactivation occurs, **Then** the doctor's active status is restored and prior violation and suspension history remains auditable.
6. **Given** a non-admin, unauthenticated caller, or deleted account attempts to review violations or change enforcement state, **When** the request is processed, **Then** access is denied without exposing protected doctor details.

---

### User Story 4 - Protect Scheduled Job Safety and Observability (Priority: P2)

As a platform operator, I need activity and weekly enforcement jobs to be safe under retries, overlapping attempts, and missed schedules so that doctors are not penalized twice and company-facing score data remains trustworthy.

**Why this priority**: Both jobs are scheduled operational workflows. Repeated runs and missed windows are normal failure modes and must not corrupt scores, warnings, or admin decisions.

**Independent Test**: Run each job repeatedly and concurrently for the same target date/week, including catch-up dates, and confirm results converge on one daily score snapshot per eligible doctor per score date and one weekly compliant, suspension-skip, or violation decision per eligible doctor per week.

**Acceptance Scenarios**:

1. **Given** the activity score calculation runs more than once for the same doctor and score date, **When** all runs complete, **Then** the doctor's current score reflects the same deterministic calculation and no duplicate history snapshots exist for that doctor/date.
2. **Given** weekly enforcement runs more than once for the same completed week, **When** all runs complete, **Then** each eligible doctor has at most one compliant, suspension-skip, or violation decision for that week.
3. **Given** a scheduled run is missed, **When** a catch-up run is executed for the missed score date or completed week, **Then** it uses the intended Egypt date/week boundaries rather than the host machine's local clock.
4. **Given** an unexpected failure occurs during either job, **When** the failure is reported, **Then** partial updates are avoided and safe operational evidence is available without raw stack traces or sensitive account details in public responses.
5. **Given** a doctor's `SuspendedUntilUtc` has passed, **When** the recurring suspension-expiry operation or Admin suspension-expiry catch-up runs, **Then** the doctor is reactivated once, an `AutomaticReactivate` action is recorded, and repeated runs do not create duplicate reactivation evidence.

### Edge Cases

- A doctor has interactions exactly at Monday 00:00 Africa/Cairo; the interaction belongs to the new week, not the completed week being enforced.
- A doctor has deliveries on the boundary of the 30-day score window; only deliveries inside the last 30 completed Africa/Cairo calendar days before the score date contribute to sub-scores, and deliveries on the score date itself are excluded.
- A response time is greater than 24 hours due to delayed settlement or data correction; the response speed contribution is clamped to 0 instead of becoming negative.
- An interaction appears before the delivery time because of bad historical data; that contribution is clamped within the 0 to 100 response-speed range and the anomaly is auditable.
- A doctor's minimum weekly requirement is 0; the doctor is considered compliant for the week and no violation is recorded.
- A doctor is suspended, deleted, unapproved, or lacks an active price; activity scoring processes approved, non-deleted doctors including suspended doctors, while weekly enforcement skips new violations for any week that overlaps suspension and does not process deleted or unapproved doctors.
- An admin tries to reduce a daily limit below zero, suspend without a reason, suspend without a future UTC `SuspendedUntilUtc` date/time, or reactivate a doctor whose account is not otherwise eligible; the action is rejected safely.
- Company doctor search runs while activity scoring is in progress; companies see either the previously committed score or the newly committed score, never a partial sub-score.
- Repeated weekly enforcement after an admin has already reduced or suspended a doctor does not overwrite the manual admin action.
- Public and admin responses expose no raw job exception traces, storage details, or sensitive internal identifiers beyond the minimum needed for authorized review.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST use the DST-aware `Africa/Cairo` business clock for all activity-score dates, weekly enforcement windows, and scheduled/catch-up job decisions; it MUST NOT use host-local dates or a fixed UTC offset.
- **FR-002**: The activity score calculation MUST run for each approved, non-deleted doctor, including suspended doctors, on a daily score date and use only delivered messages inside the last 30 completed Africa/Cairo calendar days before the score date; deliveries on the score date itself MUST be excluded.
- **FR-003**: When a doctor has no delivered messages in the activity score window, the system MUST set the current Activity Score to 95.0 and record a history snapshot marked as defaulted.
- **FR-004**: When a doctor has delivered messages but no Accept or Reject interactions in the activity score window, the system MUST set response speed, engagement, feedback, and final Activity Score to 0.0.
- **FR-005**: The response speed sub-score MUST average `(1 - ResponseTime / 24 hours) * 100` for Accept and Reject interactions in the activity score window and MUST clamp each contribution to the 0 to 100 range before averaging.
- **FR-006**: The engagement sub-score MUST equal `(Accept plus Reject interaction count / delivered message count) * 100` for the activity score window.
- **FR-007**: The feedback sub-score MUST equal `(interactions with at least 15 non-whitespace feedback characters / Accept plus Reject interaction count) * 100`; if there are no interactions, the feedback sub-score MUST be 0.0.
- **FR-008**: The final Activity Score MUST equal `0.4 * ResponseSpeedScore + 0.3 * EngagementScore + 0.3 * FeedbackScore`, rounded to one decimal place and constrained to the 0.0 to 100.0 range.
- **FR-009**: Each completed daily activity calculation MUST update the doctor's current Activity Score and create or preserve exactly one immutable score history snapshot for that doctor and score date.
- **FR-010**: Company-side doctor discovery MUST use the latest committed current Activity Score for filtering, ordering, and target snapshots.
- **FR-011**: Weekly enforcement MUST evaluate the completed Monday-to-Monday Africa/Cairo week for approved, non-deleted doctors whose suspension does not overlap any part of the evaluated week, and count only Accept plus Reject interactions toward the doctor's minimum weekly requirement.
- **FR-012**: Weekly enforcement MUST NOT count reads, views, expired deliveries, queued messages, or active uninteracted deliveries toward the weekly requirement.
- **FR-013**: If a doctor's weekly interaction count is below the doctor's configured minimum weekly requirement, the system MUST record exactly one violation event for that doctor and completed week.
- **FR-014**: If a doctor's weekly interaction count meets or exceeds the configured minimum weekly requirement, the system MUST record no violation for that doctor and week and MUST preserve any earlier history.
- **FR-015**: Weekly enforcement MUST be idempotent so repeated, concurrent, or retried runs for the same doctor/week create at most one weekly decision, suspension skip, or violation event.
- **FR-016**: The system MUST calculate each doctor's warning count from violation events in the rolling 8-week window while preserving older violation history for audit.
- **FR-017**: Doctors with 1 to 5 violations in the rolling 8-week window MUST be presented as warning-stage doctors for admin review.
- **FR-018**: Doctors with more than 5 violations in the rolling 8-week window MUST be presented as eligible for manual admin daily-limit reduction or temporary suspension.
- **FR-019**: The system MUST expose an authenticated Admin violations review capability with pagination and filters for doctor identity, warning/action eligibility, status, week, and rolling violation count.
- **FR-020**: The system MUST expose authenticated Admin enforcement actions to warn, reduce a doctor's enforced daily limit, temporarily suspend a doctor until a defined future UTC `SuspendedUntilUtc` date/time, or reactivate a doctor when policy conditions allow.
- **FR-021**: Admin enforcement actions MUST require a reason and MUST record actor, target doctor, previous state, new state, effective time, and correlation evidence in an audit trail.
- **FR-022**: Admin daily-limit reduction MUST reject invalid limits, including negative values and values that would violate configured platform constraints.
- **FR-023**: Admin suspension MUST require a future UTC `SuspendedUntilUtc` date/time, MUST distinguish temporary suspension from ordinary warning status, and MUST NOT delete activity score, violation, interaction, wallet, delivery, or campaign history.
- **FR-024**: Reactivation MUST occur automatically at or after `SuspendedUntilUtc` through a dedicated recurring suspension-expiry operation, MUST also be available through an authenticated Admin suspension-expiry catch-up operation, MUST still run opportunistically before Phase 9 jobs and Admin violation reads, MUST allow manual earlier reactivation by admin action with a required reason, and MUST preserve all prior violation and enforcement audit history.
- **FR-025**: Non-admin roles and unauthenticated callers MUST be denied access to violation review and enforcement actions without exposing protected doctor details.
- **FR-026**: Scheduled and manual catch-up runs for activity scoring, weekly enforcement, and suspension expiry MUST expose safe operational outcomes that show processed counts, skipped counts, created/updated counts, and failure counts.
- **FR-027**: Failures in activity scoring, weekly enforcement, violation review, or enforcement actions MUST use the standard safe error path and MUST NOT expose raw stack traces, storage details, sensitive account metadata, or internal job infrastructure details.
- **FR-028**: The feature MUST NOT change interaction settlement, company wallet charge/earn behavior, delivery activation, expiry release, campaign reporting analytics, withdrawal payout, external payment-gateway behavior, or campaign moderation decisions.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-018` and `FR-026` to `FR-028` target `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` for Egypt-time windows, score formulas, weekly-count rules, idempotent job decisions, score/violation persistence, audit evidence, and explicit scope boundaries.
  - `FR-019` to `FR-025` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for Admin-only review/actions, response contracts, authorization, validation, and HTTP-only adapters.
- **CA-002 Controller Boundary**: Controllers remain transport adapters only and delegate activity calculations, weekly enforcement, violation eligibility, admin action validation, and audit recording to service use cases.
- **CA-003 SQL Persistence Boundary**: Activity score history, weekly violation events, doctor status/limit changes, and admin audit records use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: Admin violation review, admin enforcement actions, and job outcome responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation, authorization, stale-state, duplicate-run, and unexpected failures use centralized safe error handling; raw stack traces and sensitive account/job details are never returned.
- **CA-006 Security**: Violation review and enforcement actions require JWT Admin authorization. Activity and weekly job execution paths must not expose secured data through public endpoints.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Weekly requirement counts Accept plus Reject interactions only. Reads, views, active deliveries, expired deliveries, queued messages, and wallet events do not satisfy weekly participation.
- **CA-008 Queue Determinism**: Queue ordering, daily injection, expiry, retry, and carry-over rules remain owned by earlier phases. Phase 9 reads committed delivery and interaction outcomes but does not activate, expire, requeue, cancel, reprioritize, or carry over queue items.
- **CA-009 Wallet Determinism**: Phase 9 creates no company charge, doctor earning, wallet reservation, release, payout, or ledger mutation. It only reads settled interaction outcomes and committed delivery data for scoring and enforcement.

### Key Entities *(include if feature involves data)*

- **Doctor Activity Score**: The doctor's current 0.0 to 100.0 score used for company doctor discovery, based on the last 30 completed Africa/Cairo calendar days before the score date and updated by the daily calculation.
- **Activity Score History Snapshot**: Immutable daily evidence for one approved, non-deleted doctor and score date, including delivered count, interacted count, feedback-qualified count, response speed sub-score, engagement sub-score, feedback sub-score, final score, default/zero-interaction markers, suspension status when applicable, and calculation time.
- **Weekly Enforcement Window**: The completed Monday-to-Monday Africa/Cairo week evaluated against each approved, non-deleted doctor's minimum weekly interaction requirement, except where any suspension overlap prevents a new violation for that week.
- **Weekly Violation Event**: Audit-preserved evidence that an approved, non-deleted, non-suspended doctor missed the weekly minimum for a specific completed week, including interaction count, requirement, week boundaries, rolling-count contribution, and creation time.
- **Rolling Violation Summary**: Admin-facing read model that presents each doctor's last 8 weeks of violations, warning/action eligibility, current status, current daily limit, and minimum weekly requirement.
- **Admin Enforcement Action**: Auditable admin decision to warn, reduce daily limit, temporarily suspend until a future UTC `SuspendedUntilUtc` date/time, or reactivate a doctor, including reason, actor, previous state, new state, suspension end time when applicable, and effective time.
- **Job Run Outcome**: Operational record or response summarizing a scheduled or catch-up activity/enforcement run, including target date/week, processed doctors, skipped doctors, created/updated records, duplicate decisions avoided, and failures.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sampled daily activity calculations match the locked formula and final one-decimal score for doctors with no deliveries, zero interactions, partial interactions, and feedback-qualified interactions.
- **SC-002**: 100% of daily activity score runs create or preserve exactly one score history snapshot per approved, non-deleted doctor per score date, even when the same date is processed repeatedly or concurrently.
- **SC-003**: 100% of company doctor-search results after a completed score run use the latest committed current Activity Score for filtering, ordering, and target snapshots.
- **SC-004**: 100% of weekly enforcement tests count only Accept plus Reject interactions inside the completed Monday-to-Monday Africa/Cairo week.
- **SC-005**: 100% of eligible non-suspended below-threshold doctors receive exactly one violation event for the evaluated week, and 100% of compliant or suspension-skipped doctors receive no violation event for that week.
- **SC-006**: 100% of weekly enforcement retries, overlapping attempts, and catch-up runs avoid duplicate weekly decisions or violation events for the same eligible doctor/week.
- **SC-007**: 100% of rolling violation summaries include only the last 8 completed weeks in the warning/action count while preserving older audit history.
- **SC-008**: 100% of doctors with 1 to 5 rolling violations are shown as warning-stage; 100% of doctors with more than 5 rolling violations are shown as eligible for manual reduce-limit or suspension review.
- **SC-009**: 100% of admin enforcement actions require a reason and produce audit evidence with actor, target doctor, previous state, new state, and time.
- **SC-010**: 100% of unauthenticated, non-admin, and cross-role attempts to review violations or apply enforcement are denied without protected doctor detail disclosure.
- **SC-011**: At least 95% of warmed admin violation-list requests for a page of up to 100 doctors complete within 1 second under the agreed Phase 9 performance profile.
- **SC-012**: 100% of public and admin-facing responses use the standard envelope and expose no raw stack traces, sensitive account metadata, storage details, or internal job infrastructure details.
- **SC-013**: 100% of Phase 9 operations leave wallet balances, wallet transactions, ledger entries, delivery activation, expiry release, and interaction settlement unchanged.
- **SC-014**: 100% of expired suspensions processed by the recurring or Admin catch-up suspension-expiry operation restore eligible doctors to active status exactly once and preserve prior violation and enforcement history.

## Assumptions

- Earlier phases already provide approved doctors, delivered messages, read tracking, Accept/Reject interaction outcomes, optional feedback, company doctor search, current doctor daily limits, minimum weekly requirements, standard response envelopes, role-aware authorization, and safe audit plumbing.
- The weekly requirement is measured against the last completed Monday-to-Monday Egypt week when weekly enforcement runs at Monday 00:00 Africa/Cairo or during catch-up.
- The activity score window is measured as the last 30 completed Africa/Cairo calendar days before the score date, excluding deliveries on the score date itself.
- The phrase "Violations 1-5: Warning; after repeated violations admin may reduce daily limit and/or suspend temporarily" is treated as warnings for rolling counts 1 through 5 and manual reduce/suspend eligibility beginning at 6 rolling violations.
- Doctors with a minimum weekly requirement of 0 are considered compliant for weekly enforcement.
- Activity scoring processes approved, non-deleted doctors, including suspended doctors, so their recent history remains auditable and available for future eligibility review.
- Weekly enforcement processes approved, non-deleted doctors, but records no new violation for any completed week that overlaps a suspension period.
- Admin warning/reduce/suspend actions are manual decisions and are not automatically applied by the weekly job.
- Temporary suspension requires a future UTC `SuspendedUntilUtc` date/time; the doctor automatically becomes active at or after that time through the recurring suspension-expiry operation, and admin may manually reactivate earlier with a required reason.
- Existing company-facing doctor filters already include Activity Score; this feature refreshes the value and its history rather than redesigning company search.
