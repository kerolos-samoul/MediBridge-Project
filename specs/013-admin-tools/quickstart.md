# Quickstart: Admin Tools (Phase 11)

## Prerequisites

- .NET 8 SDK installed.
- SQL Server test database available through the project's existing integration-test configuration.
- Repository root: `D:\My Project\MediBridge Project\MediBridge`.
- Branch: `013-admin-tools`.

## Restore and Build

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
```

## Apply Migrations

Run the project's normal migration workflow after Phase 11 persistence changes are implemented. Migration scope should be limited to missing withdrawal/payout fields, pricing deactivation state/history fields, indexes, and any safe audit/read-model support required by the plan.

Expected checks:

- SQL persistence remains in `MediBridge.Repository`.
- `MediBridge.Core` and controllers do not reference EF Core infrastructure.
- Existing delivery, interaction, campaign reporting, and enforcement data are not rewritten.

## Manual Smoke Flow

### 1. Admin Work Queue

```http
GET /api/admin/work-queue?PageNumber=1&PageSize=20
Authorization: Bearer <admin-token>
```

Expected:

- Standard envelope.
- Items from pending accounts, files, campaigns, enforcement reviews, and withdrawals when present.
- Stable ordering by urgency, submitted/requested time, then id.
- No raw storage keys, provider credentials, raw idempotency material, payout destination data, private contact details beyond review need, or raw stack traces.

### 2. Doctor Withdrawal Request

```http
POST /api/doctor/withdrawals
Authorization: Bearer <approved-active-non-suspended-doctor-token>
Content-Type: application/json

{
  "amount": 250.00
}
```

Expected:

- Standard envelope with `Requested` withdrawal.
- Requested amount is held and no longer withdrawable.
- Repeating the same request through the supported idempotency/concurrency path does not create duplicate wallet effects.

Negative checks:

- Pending, rejected, inactive, suspended, non-owner, non-doctor, and unauthenticated callers are denied.
- Amounts with more than two decimals, zero, negative, or above withdrawable earnings are rejected without wallet mutation.
- No payout destination field is accepted or returned.

### 3. Admin Withdrawal Decisions

```http
GET /api/admin/withdrawals?status=Requested&PageNumber=1&PageSize=20
Authorization: Bearer <admin-token>
```

```http
PUT /api/admin/withdrawals/{withdrawalId}/approve
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "note": "Approved for out-of-system payout"
}
```

```http
PUT /api/admin/withdrawals/{withdrawalId}/mark-paid
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "payoutReference": "OPS-PAYOUT-20260713-001"
}
```

Expected:

- Approval preserves the hold.
- Paid finalizes the held amount exactly once.
- Later reject/fail/paid attempts are rejected safely.
- Audit evidence records actor, target, prior state, resulting state, time, and reason/reference when required.

### 4. Rejection and Failed Payout

```http
PUT /api/admin/withdrawals/{withdrawalId}/reject
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "reason": "Doctor requested cancellation"
}
```

```http
PUT /api/admin/withdrawals/{withdrawalId}/mark-failed
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "reason": "Out-of-system payout failed before funds left platform"
}
```

Expected:

- Rejection releases Requested hold once.
- Failed releases Approved hold once only when funds did not leave the platform.
- Paid withdrawals cannot be failed or released.

### 5. Pricing Deactivation

```http
PUT /api/admin/doctors/{doctorId}/price/deactivate
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "reason": "Doctor paused paid campaign participation"
}
```

Expected:

- Doctor becomes ineligible for future paid campaign activation until a new valid positive price is set.
- No null or zero inactive price marker is written.
- Existing delivery snapshots, settlements, reports, and ledgers remain unchanged.

### 6. Admin Statistics

```http
GET /api/admin/statistics?fromDateEgypt=2026-04-15&toDateEgypt=2026-07-13
Authorization: Bearer <admin-token>
```

Expected:

- Standard envelope with account, review, campaign, delivery, interaction, withdrawal, wallet, policy, and enforcement summaries.
- Date range is inclusive and capped at 90 days.
- Statistics are read-only.
- If wallet/ledger evidence is inconsistent, affected financial totals are withheld and flagged while non-financial totals are returned.

## Focused Validation Commands

Run the Phase 11 focused tests:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~PricingDeactivation"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~AdminStatistics"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~AdminStatistics"
```

## Phase 11 Verification Evidence

Recorded after Phase 8 hardening:

- Build: `dotnet build .\MediBridge.slnx` passed with 0 warnings and 0 errors.
- Unit tests: `dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~PricingDeactivation|FullyQualifiedName~AdminStatistics"` passed 40/40.
- Contract tests: `dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~AdminStatistics|FullyQualifiedName~SwaggerPhase11"` passed 40/40.
- Integration tests: `dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~AdminTools|FullyQualifiedName~Withdrawal|FullyQualifiedName~AdminStatistics|FullyQualifiedName~AdminPricing"` passed 33/33.
- OpenAPI hardening: Phase 11 route metadata assertions pass, and the previously failing admin pricing Swagger description regression now passes.

## Performance Profile

```powershell
dotnet test .\tests\performance --filter "FullyQualifiedName~Phase11AdminTools"
```

Expected profile:

- Release build, no debugger or coverage collector.
- SQL Server-backed test database.
- At least 10,000 operational records across a 90-day period.
- 200 measured requests per endpoint at concurrency 10 after warmup.
- p95 below 2 seconds for work queue, withdrawal list, and statistics endpoints.

Local performance execution status:

- `tests/performance` currently has no executable `.csproj`; it contains static performance profiles and seed helpers.
- Phase 11 profile files were added for work queue, withdrawal list, and statistics p95/query-count evaluation.
- Phase 11 seed helpers cover accounts, files, campaigns, deliveries, interactions, wallet transactions, withdrawals, policy histories, and enforcement actions.
- Unmet prerequisite for an actual p95 run: attach these profiles to an executable SQL Server-backed performance harness with Release build, warmup, concurrency, query-count capture, and a 10,000-record 90-day dataset.

## Definition of Done Checks

- Admin work queue returns stable, safe, paginated items.
- Existing admin workflows remain compatible and audit-preserved.
- Pricing deactivation is separate from numeric price values.
- Withdrawal hold, approval, rejection, paid, and failed transitions are atomic and idempotent.
- Payout stub stores only payout reference/status metadata, never payout destination data.
- Admin statistics are read-only and financially conservative.
- All secured routes enforce role-aware authorization.
- All responses use the standard envelope.
- No raw stack traces or sensitive operational details leak.

## Final Constitution Compliance Review

- Layering: controllers bind HTTP inputs and return envelopes only; business rules remain in `MediBridge.Services`; EF Core access remains in `MediBridge.Repository`.
- SQL persistence boundary: review found no EF Core or DbContext references in `MediBridge.Core`, Phase 11 services, or Phase 11 controllers.
- JWT/role security: Phase 11 admin endpoints use `AdminOnly`; doctor withdrawal endpoints use `DoctorOnly`; unauthenticated and wrong-role scenarios are covered by contract/integration tests.
- Response envelope: Phase 11 controller success and validation responses use the standard `ApiEnvelope` shape, with global exception handling preserving envelope errors.
- Privacy: Phase 11 DTOs exclude payout destination data, raw idempotency values, raw storage references, provider credentials, private contact fields, and stack traces.
- Queue determinism: work queue ordering remains urgency, submitted/requested time, then id; pagination stability is covered by tests.
- Wallet determinism: withdrawal request, release, and payout transitions use isolated transactions, locked wallet reads, deterministic internal idempotency keys, wallet transactions, and ledger evidence; unrelated company top-up, reservation, expiry release, interaction charge, doctor earnings, platform fee, and reporting behavior were not changed.
- OpenAPI determinism: Swagger schema IDs are namespace-qualified to avoid short-name collisions as DTOs expand.

## Phase 11 Setup Inspection Notes

### Contract Parse Check

- `contracts/admin-tools-api.yaml` parses as OpenAPI `3.0.3`.
- Parsed contract currently defines 9 route paths and remains the Phase 11 route/schema checklist.

### Existing Admin Route Comparison

- Existing admin account routes: `GET /api/admin/pending-accounts`, `PUT /api/admin/accounts/{id}/decision`.
- Existing admin file routes: `GET /api/admin/files/pending`, `PUT /api/admin/files/{fileId}/review`, `POST /api/admin/campaign-assets/{assetId}/review`, `GET /api/admin/files/{fileId}/reviews`.
- Existing admin campaign routes: `GET /api/admin/campaigns/pending-review`, `GET /api/admin/campaigns/{campaignId}/review-detail`, `POST /api/admin/campaigns/{campaignId}/review`, `GET /api/admin/campaigns/{campaignId}/queue`.
- Existing admin pricing routes: `PUT /api/admin/doctors/{doctorId}/price`, `GET /api/admin/doctors/{doctorId}/delivery-settings`, `PUT /api/admin/doctors/{doctorId}/delivery-settings`.
- Existing platform fee routes: `GET /api/admin/platform-fee-policy/current`, `PUT /api/admin/platform-fee-policy`.
- Existing enforcement routes: `GET /api/admin/violations`, `PUT /api/admin/doctors/{doctorId}/status`.
- Phase 11 contract adds new routes for `GET /api/admin/work-queue`, doctor withdrawal create/list, admin withdrawal list/decisions/payout status, `PUT /api/admin/doctors/{doctorId}/price/deactivate`, and `GET /api/admin/statistics`.
- Route naming deviation to preserve intentionally: existing campaign asset review uses `POST /api/admin/campaign-assets/{assetId}/review`, while Phase 11 does not replace it with a generic decision endpoint.

### Existing Controller Reuse Decisions

- Reuse `AdminAccountsController`, `AdminFilesController`, `AdminCampaignsController`, `AdminPricingController`, `AdminPlatformFeePolicyController`, and `AdminActivityEnforcementController` for domain-specific decisions.
- Add new Phase 11 controllers only for work queue, doctor withdrawals, admin withdrawals, pricing deactivation, and statistics where routes are absent.
- Keep controllers HTTP-only: actor extraction, route/query/body binding, service call, and standard envelope response.

### Wallet And Withdrawal Persistence Gaps

- `WithdrawalRequest` exists with doctor, amount, status, requested/reviewed fields, decision reason, payout reference, and rowversion.
- Missing withdrawal persistence fields for Phase 11: payout status actor user id, payout status changed time, and payout failure reason.
- `WalletLedgerEntry` already has `WithdrawalRequestId`, but `WalletTransaction` does not; withdrawal evidence should either add a typed reference or use idempotency/ledger evidence consistently.
- Existing transaction types include `WithdrawRequest`, `WithdrawApproved`, `WithdrawRejected`, and `WithdrawPayout`; Phase 11 needs explicit hold/release/finalize semantics mapped without changing unrelated wallet operations.
- `Wallet` already has available/reserved balances and rowversion-backed repository methods for locked balance staging.

### Audit And Privacy Conventions

- `AuditMetadataRules.EnsureSafe` rejects metadata containing password, plaintext token, token, request/response body, secret, payload, storage key, and stack trace terms.
- Phase 11 audit metadata should continue using safe structured metadata with actor, target, prior/resulting state, reason/reference, and correlation evidence only.
- Add payout-specific audit evidence without bank account, card, mobile wallet, payout destination, provider payload, raw idempotency material, storage key, private contact, or stack trace data.

### Contract Test Naming Conventions

- Existing contract tests are feature-named files in `tests/contract/MediBridge.ContractTests`, for example `AdminAccountDecisionContractTests.cs`, `AdminDoctorPricingContractTests.cs`, `CampaignReviewModerationContractTests.cs`, and `ResponseEnvelopeValidationContractTests.cs`.
- Phase 11 contract test names should follow the task list: `AdminToolsWorkQueueContractTests`, `DoctorWithdrawalContractTests`, `AdminWithdrawalContractTests`, `WithdrawalPrivacyContractTests`, `AdminStatisticsContractTests`, and `SwaggerPhase11AdminToolsContractTests`.
- Route helpers should be centralized in `AdminToolsRoutes.cs` for Phase 11 paths.

### Performance Profile Assumptions

- Existing performance files live under `tests/performance` and include a seed helper plus endpoint-specific tests for company reporting.
- Phase 11 performance tests should follow that split with `Phase11AdminToolsPerformanceSeed.cs` and `Phase11AdminToolsPerformanceTests.cs`.
- Performance prerequisites remain SQL Server-backed test data, Release build, warmed requests, no debugger/coverage collector, and explicit skipped evidence when the profile cannot run locally.
