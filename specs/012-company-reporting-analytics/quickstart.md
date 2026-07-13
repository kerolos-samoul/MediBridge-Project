# Quickstart: Company Reporting & Analytics

## Prerequisites

- Current branch: `012-company-reporting-analytics`.
- SQL Server connection configured for the API and tests.
- Earlier phases provide approved companies, campaigns, deliveries, interactions, feedback, wallet transactions, ledger evidence, and standard envelope/error middleware.
- Run from repository root: `D:\My Project\MediBridge Project\MediBridge`.

## Build and Test Baseline

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx --configuration Release
dotnet test .\MediBridge.slnx --configuration Release
```

## Apply Migration When Needed

Create a migration only if implementation adds supporting indexes or a dedicated reporting discrepancy entity.

```powershell
dotnet ef migrations add AddCompanyReportingAnalytics `
  --project .\MediBridge.Repository `
  --startup-project .\MediBridge.APIs

dotnet ef database update `
  --project .\MediBridge.Repository `
  --startup-project .\MediBridge.APIs
```

## Manual Smoke Flow

1. Create or seed an approved Pharmaceutical Company user and obtain a JWT.
2. Seed one owned campaign with targets and deliveries inside an inclusive 90-day `DeliveryDateEgypt` range.
3. Include Active, Accepted, Rejected, and Expired deliveries.
4. Include non-empty feedback, omitted feedback, empty feedback, and short ineligible feedback.
5. Include Charge, Earn, platform-fee, Reserve, and Release financial evidence matching delivery states.
6. Request:

```http
GET /api/company/campaigns?fromDateEgypt=2026-05-01&toDateEgypt=2026-07-29&PageNumber=1&PageSize=20
GET /api/company/campaigns/{campaignId}/deliveries?fromDateEgypt=2026-05-01&toDateEgypt=2026-07-29&PageNumber=1&PageSize=20
GET /api/company/campaigns/{campaignId}/feedback?fromDateEgypt=2026-05-01&toDateEgypt=2026-07-29&PageNumber=1&PageSize=20
GET /api/company/campaigns/{campaignId}/analytics?fromDateEgypt=2026-05-01&toDateEgypt=2026-07-29
```

7. Confirm all responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
8. Confirm doctor rows contain only public doctor identifier, specialization, experience band, and location.

## Validation Checks

- Date range longer than 90 delivery Egypt business days returns validation failure with no partial report.
- `fromDateEgypt > toDateEgypt` returns validation failure.
- Cross-company campaign id returns a non-disclosing authorization/not-found style failure.
- Doctor, Admin, pending company, rejected company, and unauthenticated callers cannot access company reporting paths.
- Accepted and Rejected deliveries both contribute to charged spend, doctor earnings, platform fee, and interaction rate.
- Active deliveries contribute reserved amount only.
- Expired deliveries contribute delivered/expired counts only.
- Empty feedback is excluded from feedback rows but still counted in interaction analytics.
- Short non-empty feedback appears and is marked not score-eligible.
- Missing/stale stored aggregate data does not affect reports when delivery and financial evidence are internally consistent.

## Reconciliation Check

1. Create a controlled mismatch between a settled delivery and append-only financial evidence in a test database.
2. Request analytics for the affected campaign/date scope.
3. Confirm the affected report is blocked, no partial or invented monetary totals are returned, and safe discrepancy evidence is persisted.
4. Confirm unrelated campaigns with consistent evidence still report successfully.

## Read-Only Audit Check

After successful reporting requests, verify no changes occurred to:

- Campaign status or campaign targets.
- Delivery status, feedback, read time, interaction time, reservation status, or snapshots.
- Wallet balances.
- Wallet transactions and ledger entries.
- Queue rows.
- Job records.
- Activity scores.
- Stored reporting aggregates.

Safe discrepancy evidence is the only allowed write and only when reconciliation fails.

## Performance Profile

Seed one owned campaign with 10,000 deliveries inside an inclusive 90-day `DeliveryDateEgypt` window and representative source financial evidence. Run Release build without debugger or coverage collector.

Measure:

- Campaign summary page.
- Delivery page.
- Feedback page.
- Campaign analytics.

Pass criteria:

- p95 under 2 seconds for warmed valid requests.
- Zero failed valid requests.
- Zero duplicate/missing rows during full delivery and feedback pagination traversal.
- Zero false reconciliation discrepancies.
- Query counts remain bounded and documented.

## Phase 10 Implementation Notes

### Setup Evidence

- OpenAPI contract parse check: `company-reporting-api.yaml` parsed as OpenAPI `3.0.3` with four paths.
- Existing company campaign route group: `CompanyCampaignsController` under `api/company/campaigns`.
- Existing company campaign endpoints to extend:
  - `GET /api/company/campaigns` handled by `GetCampaigns`.
  - `GET /api/company/campaigns/{campaignId}` handled by `GetCampaignDetail`.
  - `GET /api/company/campaigns/{campaignId}/target-preview` handled by `GetTargetPreview`.
  - `GET /api/company/campaigns/{campaignId}/queue-summary` handled by `GetQueueSummary`.
  - `PUT /api/company/campaigns/{campaignId}` handled by `UpdateCampaign`.
  - `POST /api/company/campaigns/{campaignId}/submit` handled by `SubmitExistingCampaign`.
  - `GET /api/company/campaigns/{campaignId}/review-outcome` handled by `GetReviewOutcome`.
- Phase 10 DTO class names:
  - `CampaignReportPageDto`
  - `CampaignReportSummaryDto`
  - `DeliveryReportPageDto`
  - `CampaignDeliveryReportRowDto`
  - `FeedbackReportPageDto`
  - `CampaignFeedbackReportRowDto`
  - `CampaignAnalyticsDto`
  - `RateMetricDto`
  - `PublicDoctorSummaryDto`
- Repository projection conventions:
  - Core read models live under `MediBridge.Core/Interfaces/...`.
  - EF Core projection implementations live only in `MediBridge.Repository`.
  - Services consume repository interfaces and never reference EF Core infrastructure.
  - Company reporting queries must include `CompanyId` predicates before returning campaign, delivery, feedback, or financial rows.
- Financial evidence conventions:
  - Delivery snapshots come from `DoctorAdDelivery` stored values.
  - Append-only financial evidence comes from `WalletTransaction.RelatedDeliveryId` plus `WalletTransactionType`.
  - Raw idempotency keys, wallet ids, and ledger internals are not exposed to DTOs.
- Discrepancy evidence conventions:
  - Reuse `IAuditEventRepository.AddPhase5AuditEventAsync` for safe reporting discrepancy evidence.
  - Metadata must pass `AuditMetadataRules.EnsureSafe`.
  - Metadata may include company id, campaign id, report kind, date scope, category, detected counts, expected/actual amount totals, and outcome.
  - Metadata must exclude request/response bodies, stack traces, raw idempotency material, wallet internals, doctor private data, storage keys, and secrets.

### Final Verification Evidence

- `dotnet format .\MediBridge.slnx`: completed successfully.
- `dotnet build .\MediBridge.slnx --configuration Release`: passed with 0 warnings and 0 errors.
- `dotnet test .\MediBridge.slnx --configuration Release`: passed.
  - Unit: 185 passed, 0 skipped.
  - Contract: 147 passed, 0 skipped.
  - Integration: 458 passed, 4 skipped.
- Phase 10 OpenAPI generation assertion added in `Swagger_DocumentsPhase10CompanyReportingRoutes`.
- Senior review completed without unresolved findings:
  - DTOs expose only approved reporting fields and no prohibited doctor, wallet, idempotency, storage, stack trace, or admin-only data.
  - Repository reporting queries are scoped by authenticated company id before campaign/delivery rows are returned; financial evidence is read by already-scoped delivery ids.
  - `CompanyReportingService` has no EF Core or API infrastructure references.
  - `CompanyCampaignsController` remains HTTP-only query binding, actor extraction, service calls, and envelope creation.
  - Phase 10 migration is limited to reporting-support indexes and does not add aggregate tables, cached counters, or historical data rewrites.
- Performance prerequisite note: `tests/performance/CompanyReportingPerformanceSeed.cs` and `tests/performance/CompanyReportingPerformanceTests.cs` provide the 10,000-delivery seed/profile gates for p95 under 2 seconds, bounded query count, and zero false reconciliation discrepancies. The reference profile is opt-in because it requires a warmed local SQL Server/API workload; it is not part of the default solution test run.
- Optional Spec Kit `after_implement` git commit hook was not run because it is optional and no commit was requested.
