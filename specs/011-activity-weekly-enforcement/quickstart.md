# Quickstart: Activity & Weekly Enforcement

This quickstart verifies the Phase 9 design after implementation. Paths assume the repository root is `D:\My Project\MediBridge Project\MediBridge`.

## 1) Apply Phase 9 Migration

```powershell
dotnet ef database update --project .\MediBridge.Repository\MediBridge.Repository.csproj --startup-project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Expected:

- Activity score history table exists.
- Weekly enforcement decision and violation tables exist.
- Doctor enforcement action table exists.
- Phase 9 job-run table exists.
- `DoctorProfiles` includes suspension expiry fields.
- Unique constraints exist for one score snapshot per doctor/date and one weekly decision per doctor/week.

## 2) Run Focused Unit Tests

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase9|FullyQualifiedName~ActivityScore|FullyQualifiedName~WeeklyEnforcement|FullyQualifiedName~DoctorEnforcement"
```

Expected coverage:

- Last 30 completed Africa/Cairo dates exclude the score date.
- Monday-to-Monday Cairo week boundaries are correct.
- Activity score formula, rounding, and clamping are correct.
- No-delivery doctors receive score 95.0.
- Delivered-with-zero-interaction doctors receive score 0.0.
- Feedback under 15 non-whitespace characters does not qualify for feedback score.
- Any suspension overlap skips weekly violation creation.
- `SuspendedUntilUtc` automatic reactivation behavior is deterministic.

## 3) Run Contract Tests

```powershell
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~Violation|FullyQualifiedName~ActivityJob|FullyQualifiedName~DoctorEnforcement"
```

Expected:

- `GET /api/admin/violations` requires Admin JWT and returns the standard envelope.
- `PUT /api/admin/doctors/{doctorId}/status` requires Admin JWT, a reason, and valid action-specific fields.
- `Suspend` requires future `SuspendedUntilUtc`.
- `ReduceDailyLimit` rejects invalid limits.
- Admin job endpoints require Admin JWT and return safe job-run envelopes.
- Non-admin and unauthenticated callers receive safe unauthorized/forbidden envelopes.

## 4) Run Integration Tests

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9"
```

Expected:

- Daily score job creates exactly one snapshot per approved, non-deleted doctor/date.
- Concurrent score runs converge without duplicate snapshots.
- Weekly enforcement creates exactly one decision per approved, non-deleted eligible doctor/week.
- Suspended-overlap weeks create `SuspensionSkipped` decisions and no violation.
- Rolling 8-week count excludes older violations.
- Warning/action eligibility is 1-5 and greater than 5 respectively.
- Admin enforcement actions update doctor state and write audit/action records atomically.
- Automatic reactivation occurs at or after `SuspendedUntilUtc`.
- Phase 9 operations do not mutate wallet, wallet transaction, ledger, queue, delivery activation, expiry, or settlement records.

## 5) Verify Scheduled Job Registration

Start the API in Development or test configuration with Hangfire enabled.

```powershell
dotnet run --project .\MediBridge.APIs\MediBridge.APIs.csproj
```

Expected:

- Daily Activity Score recurring job is registered for `00:30 Africa/Cairo`.
- Weekly Enforcement recurring job is registered for Monday `00:00 Africa/Cairo`.
- Suspension Expiry recurring job is registered every 5 minutes in `Africa/Cairo`.
- No public Hangfire dashboard or unauthenticated job-control endpoint is introduced by Phase 9.

## 6) Verify Manual Daily Score Catch-Up

Use an Admin JWT and a completed score date.

```http
POST /api/admin/activity-jobs/run-score
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ScoreDateEgypt": "2026-07-11"
}
```

Expected:

- Response envelope has `Code = 200`.
- `Data.JobType = "DailyActivityScore"`.
- Counts include processed, skipped, created, updated, and failed.
- Repeating the same request creates no duplicate score snapshots.

## 7) Verify Weekly Enforcement Catch-Up

Use an Admin JWT and a completed Monday week start.

```http
POST /api/admin/activity-jobs/run-weekly-enforcement
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "WeekStartDateEgypt": "2026-06-29"
}
```

Expected:

- Response envelope has `Code = 200`.
- `Data.JobType = "WeeklyEnforcement"`.
- Below-threshold eligible doctors receive one violation decision.
- Compliant doctors receive no violation.
- Doctors whose suspension overlaps the week receive no violation.
- Repeating the same request creates no duplicate weekly decisions or violations.

## 8) Verify Manual Suspension Expiry Catch-Up

Use an Admin JWT after seeding at least one approved, non-deleted suspended doctor whose `SuspendedUntilUtc` is in the past.

```http
POST /api/admin/activity-jobs/run-suspension-expiry
Authorization: Bearer <admin-token>
```

Expected:

- Response envelope has `Code = 200`.
- `Data.JobType = "SuspensionExpiry"`.
- `Data.TargetScoreDateEgypt` and `Data.TargetWeekStartDateEgypt` are null.
- Expired eligible suspended doctors become `Active`.
- Each reactivated doctor receives exactly one `AutomaticReactivate` enforcement action and safe audit evidence.
- Repeating the same request does not create duplicate reactivation evidence.

## 9) Verify Admin Violation Review

```http
GET /api/admin/violations?PageNumber=1&PageSize=20&eligibility=ActionEligible
Authorization: Bearer <admin-token>
```

Expected:

- Response envelope has `Code = 200`.
- Results include rolling 8-week violation count, status, daily limit, minimum weekly requirement, Activity Score, suspension expiry when applicable, and eligibility.
- Counts 1-5 are warning-stage.
- Counts greater than 5 are action-eligible.
- Page size above 100 is rejected or capped according to existing pagination rules.

## 10) Verify Admin Enforcement Actions

### Warn

```http
PUT /api/admin/doctors/{doctorId}/status
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ActionType": "Warn",
  "Reason": "Rolling weekly violations require warning review."
}
```

Expected: doctor status becomes `Warned` and an action/audit record is created.

### Reduce Daily Limit

```http
PUT /api/admin/doctors/{doctorId}/status
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ActionType": "ReduceDailyLimit",
  "NewDailyMessageLimit": 3,
  "Reason": "Repeated weekly violations require temporary delivery reduction."
}
```

Expected: daily limit changes atomically and an action/audit record captures old and new values.

### Suspend

```http
PUT /api/admin/doctors/{doctorId}/status
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ActionType": "Suspend",
  "SuspendedUntilUtc": "2026-07-20T00:00:00Z",
  "Reason": "Repeated weekly violations require temporary suspension."
}
```

Expected: doctor status becomes `Suspended`, `SuspendedUntilUtc` is stored, and the action/audit record is created.

### Reactivate

```http
PUT /api/admin/doctors/{doctorId}/status
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ActionType": "Reactivate",
  "Reason": "Manual early reactivation approved after review."
}
```

Expected: doctor status becomes `Active`, prior violation/suspension history remains visible, and the action/audit record is created.

## 11) Verify Automatic Suspension Expiry

Seed a suspended approved doctor whose `SuspendedUntilUtc` is in the past, then run daily score, weekly enforcement, Admin violation review, or the dedicated suspension-expiry operation.

Expected:

- Doctor status becomes `Active`.
- An `AutomaticReactivate` enforcement action and safe audit evidence are created.
- Previous violation and suspension action history remains unchanged.

## 12) Verify Company Doctor Search Uses Latest Score

After running daily score:

```http
GET /api/company/doctors?MinActivityScore=80&PageNumber=1&PageSize=20
Authorization: Bearer <company-token>
```

Expected:

- Results use the latest committed `DoctorProfile.ActivityScore`.
- A concurrent score run never exposes partial sub-scores.
- Suspended or otherwise ineligible doctors remain governed by existing company-search eligibility rules.

## 13) Verify Scope Discipline

Compare row counts and balances before and after Phase 9 jobs/actions:

- `DoctorMessageQueues`
- `DoctorAdDeliveries`
- `Wallets`
- `WalletTransactions`
- `WalletLedgerEntries`
- Campaign status/review tables

Expected:

- Phase 9 creates no queue, delivery activation, expiry, settlement, wallet, ledger, campaign moderation, withdrawal, or payment-gateway mutations.

## 14) Performance Profile

Run the designated Phase 9 performance profile only on a prepared machine:

```powershell
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase9Performance"
```

Expected:

- At least 95% of warmed Admin violation-list requests for a page of up to 100 doctors complete within 1 second.
- Daily score and weekly enforcement reference cycles create no duplicate snapshots, decisions, or violations.
- Performance evidence reports p50, p95, p99, query count, failure count, and skip count.

## 15) Final Verification

```powershell
dotnet test .\MediBridge.slnx
```

Expected:

- Existing Phase 1-8 tests still pass.
- Phase 9 tests pass.
- No layering boundary tests fail.
