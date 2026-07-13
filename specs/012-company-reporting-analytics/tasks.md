# Tasks: Phase 10 Company Reporting & Analytics

**Input**: Design documents from `/specs/012-company-reporting-analytics/`  
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/company-reporting-api.yaml](./contracts/company-reporting-api.yaml), [quickstart.md](./quickstart.md)

**Tests**: Tests are required for this feature because the specification defines measurable success criteria, reconciliation correctness, authorization/privacy guarantees, pagination guarantees, and performance targets.

**Constitution Note**: Every implementation task must preserve Onion Architecture. Keep domain/read model contracts in `MediBridge.Core`, SQL Server/EF Core projections in `MediBridge.Repository`, business orchestration in `MediBridge.Services`, and HTTP-only routing/envelope behavior in `MediBridge.APIs`. Do not access EF Core from controllers or services.

**Critical Non-Negotiables For Executors**:

- All reporting date filters use `DeliveryDateEgypt`, not campaign submission date, read date, interaction date, settlement date, or UTC timestamp.
- Date ranges are inclusive and must reject ranges longer than 90 delivery Egypt business days.
- Reports compute live from delivery records and append-only financial evidence on each request.
- Do not create or maintain stored reporting aggregate tables, cached counters, or authoritative reporting read-model tables.
- Company-visible doctor data is limited to public doctor identifier, specialization, experience band, and location.
- Never expose doctor names, doctor contact details, wallet internals, raw idempotency material, private doctor account data, private file storage locations, storage credentials, stack traces, or admin-only notes.
- Reporting is read-only except safe discrepancy evidence when source reconciliation fails.
- Reconciliation discrepancies block only the affected report scope and must not repair or mutate source delivery/wallet/ledger records.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently after the foundational phase.

## Phase 1: Setup (Shared Preparatory Inspection)

**Purpose**: Confirm the existing project shape, generated contract, and test locations before implementation starts. These tasks are intentionally preparatory and support later requirement-bearing implementation tasks.

- [X] T001 Verify the Phase 10 OpenAPI contract parses and keep it as the source checklist in `specs/012-company-reporting-analytics/contracts/company-reporting-api.yaml`.
- [X] T002 [P] Prepare a working notes section for Phase 10 test evidence in `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T003 [P] Inspect existing company campaign controller routes and record endpoint names to extend in implementation notes inside `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T004 [P] Inspect existing campaign DTO naming and record chosen Phase 10 DTO class names in implementation notes inside `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T005 [P] Inspect existing repository projection patterns and record reporting projection conventions in implementation notes inside `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T006 [P] Inspect existing wallet transaction and ledger query support and record financial evidence query conventions in implementation notes inside `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T007 [P] Inspect existing audit metadata safety rules and record discrepancy evidence conventions in implementation notes inside `specs/012-company-reporting-analytics/quickstart.md`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared contracts, DTOs, validation helpers, and safe evidence primitives that every user story depends on.

**Critical**: No user story implementation should begin until every task in this phase is complete.

- [X] T008 Add Phase 10 request/query value objects for date range, pagination, delivery filters, feedback filters, and analytics scope in `MediBridge.Core/Interfaces/Campaigns/CompanyReportingQueries.cs`.
- [X] T009 Add Phase 10 campaign summary, delivery row, feedback row, analytics, rate metric, and reconciliation read models in `MediBridge.Core/Interfaces/Campaigns/CompanyReportingReadModels.cs`.
- [X] T010 Add public doctor summary read model with only `PublicDoctorId`, `Specialization`, `ExperienceBand`, and `Location` in `MediBridge.Core/Interfaces/Campaigns/CompanyReportingReadModels.cs`.
- [X] T011 Add reporting discrepancy category constants or enum values for count mismatch, charge mismatch, earn mismatch, fee mismatch, and missing evidence in `MediBridge.Core/Enums/ReportingDiscrepancyCategory.cs`.
- [X] T012 Add service-facing Phase 10 DTOs mirroring the OpenAPI schemas in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtos.cs`.
- [X] T013 Add a mapper from Core reporting read models to service DTOs in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtoMapper.cs`.
- [X] T014 Add a date range validator that enforces inclusive `DeliveryDateEgypt` range, `from <= to`, maximum 90 days, both-omitted default to latest 90 days ending current Egypt business date, only-`toDateEgypt` default to `toDateEgypt - 89 days`, and only-`fromDateEgypt` default to the earlier of `fromDateEgypt + 89 days` or current Egypt business date in `MediBridge.Services/Validators/Campaigns/CompanyReportingDateRangeValidator.cs`.
- [X] T015 Add a pagination validator that enforces `PageNumber >= 1`, `1 <= PageSize <= 100`, default page number 1, and default page size 20 in `MediBridge.Services/Validators/Campaigns/CompanyReportingPaginationValidator.cs`.
- [X] T016 Add filter validators for delivery status, read/interacted state, feedback outcome, feedback eligibility, specialization, and location in `MediBridge.Services/Validators/Campaigns/CompanyReportingFilterValidator.cs`.
- [X] T017 Add `ICompanyReportingService` with methods for summaries, deliveries, feedback, and analytics in `MediBridge.Services/Interfaces/ICompanyReportingService.cs`.
- [X] T018 Extend `ICampaignRepository` with company-owned report summary and report-visible ownership methods in `MediBridge.Core/Interfaces/Campaigns/ICampaignRepository.cs`.
- [X] T019 Extend `IDeliveryRepository` with company delivery page, feedback page, analytics source, and delivery id scope methods in `MediBridge.Core/Interfaces/Messaging/IDeliveryRepository.cs`.
- [X] T020 Extend `IWalletTransactionRepository` with read-only financial evidence projection methods by delivery ids and operation type in `MediBridge.Core/Interfaces/Wallets/IWalletTransactionRepository.cs`.
- [X] T021 Extend `IAuditEventRepository` with a safe reporting discrepancy write helper or document reuse of existing `AddAuditEventAsync` for discrepancy evidence in `MediBridge.Core/Interfaces/Policies/IAuditEventRepository.cs`.
- [X] T022 Add company reporting service registration in `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`.
- [X] T023 Add or confirm a company reporting authorization policy alias that still requires approved Company access in `MediBridge.APIs/Security/AuthorizationPolicies.cs`.
- [X] T024 Wire the reporting authorization policy alias, if created, in `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`.
- [X] T025 Add database index configuration for delivery reporting lookups in `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`.
- [X] T026 Add database index configuration for feedback reporting lookups in `MediBridge.Repository/Configurations/Messaging/MessagingConfigurations.cs`.
- [X] T027 Add database index configuration for financial evidence lookup support if existing indexes are insufficient in `MediBridge.Repository/Configurations/Wallets/WalletConfigurations.cs`.
- [X] T028 Add EF Core migration for Phase 10 reporting indexes and optional discrepancy evidence only if model/index changes require it in `MediBridge.Repository/Migrations/`.
- [X] T029 Confirm the migration does not add stored reporting aggregate tables, cached counters, or historical data rewrites in `MediBridge.Repository/Migrations/`.
- [X] T030 Add unit tests for date range validation including both-omitted default, only-`fromDateEgypt` default, only-`toDateEgypt` default, 90-day inclusive acceptance, 91-day rejection, malformed dates, and `from > to` in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingDateRangeValidatorTests.cs`.
- [X] T031 Add unit tests for pagination and filter validation in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingQueryValidatorTests.cs`.
- [X] T032 Add unit tests for rate metric zero-denominator behavior in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingRateMetricTests.cs`.
- [X] T033 Add unit tests proving public doctor summary mapping excludes names, contact details, wallet data, verification data, and private account fields in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingPrivacyMapperTests.cs`.
- [X] T034 Add unit tests for delivery-state money interpretation for Active, Accepted, Rejected, and Expired deliveries in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingMoneyRulesTests.cs`.
- [X] T035 Add unit tests for reconciliation discrepancy classification without writing source mutations in `tests/unit/MediBridge.UnitTests/Campaigns/CompanyReportingReconciliationTests.cs`.

**Checkpoint**: Foundation ready. The repository now has all shared contracts, DTOs, validators, DI hooks, index/migration decisions, and unit tests required for user story work.

---

## Phase 3: User Story 1 - Review Campaign Performance Summary (Priority: P1) MVP

**Goal**: Approved company users can request their campaign list with reporting summary fields for campaigns owned by their company only.

**Independent Test**: Authenticate as an approved company with campaigns containing Active, Accepted, Rejected, Expired, and feedback-bearing deliveries; call `GET /api/company/campaigns` with a valid `DeliveryDateEgypt` range and confirm summary counts/money totals are correct, paginated, and company-owned.

### Tests for User Story 1

- [X] T036 [P] [US1] Add contract tests for `GET /api/company/campaigns` reporting fields, envelope shape, pagination metadata, 90-day date range validation, omitted-date default behavior, and `409` reporting-discrepancy response in `tests/contract/MediBridge.ContractTests/CompanyCampaignReportingContractTests.cs`.
- [X] T037 [P] [US1] Add integration test for company-owned campaign report summaries with mixed delivery states and calculated campaign activity timestamp ordering in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs`.
- [X] T038 [P] [US1] Add integration test proving another company cannot see campaign report summaries for campaigns it does not own in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs`.
- [X] T039 [P] [US1] Add integration test for empty matching campaign filters returning an empty page with zero totals in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs`.
- [X] T040 [P] [US1] Add integration test proving missing/stale aggregate data is ignored when source evidence is consistent and proving summary reconciliation discrepancies return no partial monetary totals with safe discrepancy evidence in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs`.

### Implementation for User Story 1

- [X] T041 [US1] Implement campaign summary projection query scoped by company id and resolved `DeliveryDateEgypt` range in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`.
- [X] T042 [US1] Implement delivery outcome aggregate source projection for campaign summaries in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T043 [US1] Implement financial evidence projection for campaign summary Charge, Earn, platform fee, Reserve, and Release totals in `MediBridge.Repository/Repositories/Wallets/WalletTransactionRepository.cs`.
- [X] T044 [US1] Implement summary reconciliation checks and calculated campaign activity timestamp as greatest non-null latest delivered, read, interacted, feedback-created, review, submitted, or campaign-created timestamp in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T045 [US1] Implement company actor resolution and approved company ownership scoping for summaries in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T046 [US1] Implement `GetCompanyCampaignReportsAsync` in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T047 [US1] Map campaign summary read models to `CampaignReportSummaryDto` and `CampaignReportPageDto` in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtoMapper.cs`.
- [X] T048 [US1] Update `GetCampaigns` query handling to accept `fromDateEgypt` and `toDateEgypt` while preserving existing `status`, `PageNumber`, and `PageSize`, and delegate omitted-date defaulting to the service in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T049 [US1] Ensure the controller returns the OpenAPI-defined `ApiEnvelope<CampaignReportPageDto>` success shape and the OpenAPI-defined `409` reporting-discrepancy failure shape in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T050 [US1] Ensure summary endpoint failures use global safe exception handling and never return raw stack traces or financial internals in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T051 [US1] Run and pass US1 unit, contract, and integration tests for summaries in `tests/unit/MediBridge.UnitTests/`, `tests/contract/MediBridge.ContractTests/`, and `tests/integration/MediBridge.IntegrationTests/`.

**Checkpoint**: User Story 1 is independently functional and demoable as the MVP.

---

## Phase 4: User Story 2 - Inspect Campaign Deliveries (Priority: P1)

**Goal**: Approved company users can inspect paginated delivery-level reporting rows for one owned campaign, including safe doctor context and delivery financial snapshots.

**Independent Test**: Authenticate as the owning company, call `GET /api/company/campaigns/{campaignId}/deliveries`, and confirm rows are campaign-owned, ordered by `DeliveryDateEgypt DESC`, `DeliveredAtUtc DESC`, then delivery id, paginated without gaps/duplicates, filterable, and privacy-safe.

### Tests for User Story 2

- [X] T052 [P] [US2] Add contract tests for `GET /api/company/campaigns/{campaignId}/deliveries` query parameters, response schema, envelope shape, and privacy field absence in `tests/contract/MediBridge.ContractTests/CompanyCampaignDeliveriesContractTests.cs`.
- [X] T053 [P] [US2] Add integration test for delivery report ordering and full pagination traversal without duplicates or gaps in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesIntegrationTests.cs`.
- [X] T054 [P] [US2] Add integration test for delivery filters by status, read state, interacted state, specialization, location, and `DeliveryDateEgypt` range in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesIntegrationTests.cs`.
- [X] T055 [P] [US2] Add integration test proving cross-company delivery report access is denied without revealing protected delivery data in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs`.
- [X] T056 [P] [US2] Add integration test proving delivery report rows never include doctor names, contact details, wallet data, verification data, raw storage paths, or raw idempotency material in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesPrivacyTests.cs`.

### Implementation for User Story 2

- [X] T057 [US2] Implement company campaign ownership check for delivery report requests in `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`.
- [X] T058 [US2] Implement delivery page projection with safe doctor public identifier, specialization, experience band, and location in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T059 [US2] Implement delivery report filters for status, `DeliveryDateEgypt` range, specialization, location, read state, and interacted state in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T060 [US2] Implement deterministic delivery report ordering by `DeliveryDateEgypt DESC`, `DeliveredAtUtc DESC`, then stable delivery id in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T061 [US2] Implement delivery report page count projection using the same ownership/filter predicates as the page query in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T062 [US2] Implement `GetCampaignDeliveryReportsAsync` in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T063 [US2] Map delivery report read models to `CampaignDeliveryReportRowDto` and `DeliveryReportPageDto` in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtoMapper.cs`.
- [X] T064 [US2] Add `GET /api/company/campaigns/{campaignId}/deliveries` action with HTTP-only query binding and envelope response in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T065 [US2] Ensure delivery report validation rejects invalid pagination, invalid filters, malformed date values, `fromDateEgypt > toDateEgypt`, and ranges longer than 90 days in `MediBridge.Services/Validators/Campaigns/CompanyReportingFilterValidator.cs`.
- [X] T066 [US2] Run and pass US2 unit, contract, and integration tests for delivery reports in `tests/unit/MediBridge.UnitTests/`, `tests/contract/MediBridge.ContractTests/`, and `tests/integration/MediBridge.IntegrationTests/`.

**Checkpoint**: User Story 2 is independently functional and does not depend on feedback or analytics endpoints.

---

## Phase 5: User Story 3 - Read Doctor Feedback for a Campaign (Priority: P1)

**Goal**: Approved company users can review paginated non-empty feedback rows for one owned campaign with outcome, feedback eligibility, and safe doctor context.

**Independent Test**: Authenticate as the owning company, call `GET /api/company/campaigns/{campaignId}/feedback`, and confirm only non-empty feedback rows for the owned campaign are returned, short feedback is visible but marked ineligible, and empty feedback is excluded while still counted by analytics.

### Tests for User Story 3

- [X] T067 [P] [US3] Add contract tests for `GET /api/company/campaigns/{campaignId}/feedback` query parameters, schema, envelope shape, and feedback eligibility field in `tests/contract/MediBridge.ContractTests/CompanyCampaignFeedbackContractTests.cs`.
- [X] T068 [P] [US3] Add integration test for feedback rows excluding omitted, empty, and whitespace-only feedback in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs`.
- [X] T069 [P] [US3] Add integration test proving short non-empty feedback is returned and marked not score-eligible in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs`.
- [X] T070 [P] [US3] Add integration test for feedback filters by outcome, feedback eligibility, specialization, location, and `DeliveryDateEgypt` range in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs`.
- [X] T071 [P] [US3] Add integration test proving cross-company feedback access is denied without revealing whether feedback exists in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs`.
- [X] T072 [P] [US3] Add integration test proving feedback rows never include doctor names, contact details, wallet data, verification data, or raw idempotency material in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackPrivacyTests.cs`.

### Implementation for User Story 3

- [X] T073 [US3] Implement feedback page projection with only non-empty normalized feedback in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T074 [US3] Implement feedback filters for accepted/rejected outcome, feedback eligibility, specialization, location, and `DeliveryDateEgypt` range in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T075 [US3] Implement deterministic feedback report ordering by `FeedbackCreatedAtUtc DESC`, then stable delivery id in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T076 [US3] Implement feedback report page count projection using the same ownership/filter predicates as the page query in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T077 [US3] Implement `GetCampaignFeedbackReportsAsync` in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T078 [US3] Map feedback report read models to `CampaignFeedbackReportRowDto` and `FeedbackReportPageDto` in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtoMapper.cs`.
- [X] T079 [US3] Add `GET /api/company/campaigns/{campaignId}/feedback` action with HTTP-only query binding and envelope response in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T080 [US3] Ensure feedback report validation rejects invalid pagination, invalid filters, malformed date values, `fromDateEgypt > toDateEgypt`, and ranges longer than 90 days in `MediBridge.Services/Validators/Campaigns/CompanyReportingFilterValidator.cs`.
- [X] T081 [US3] Run and pass US3 unit, contract, and integration tests for feedback reports in `tests/unit/MediBridge.UnitTests/`, `tests/contract/MediBridge.ContractTests/`, and `tests/integration/MediBridge.IntegrationTests/`.

**Checkpoint**: User Story 3 is independently functional and does not require analytics endpoint completion.

---

## Phase 6: User Story 4 - Reconcile Spend and Engagement Analytics (Priority: P1)

**Goal**: Approved company users can request one-campaign analytics that reconcile live delivery states with append-only financial evidence and fail closed when source evidence disagrees.

**Independent Test**: Authenticate as the owning company, call `GET /api/company/campaigns/{campaignId}/analytics`, and compare counts, rates, reserved amount, charged spend, doctor earnings, and platform fee against delivery and financial source evidence. Force a mismatch and confirm only the affected report scope is blocked with safe discrepancy evidence.

### Tests for User Story 4

- [X] T082 [P] [US4] Add contract tests for `GET /api/company/campaigns/{campaignId}/analytics` schema, envelope shape, rate metrics, date validation, and discrepancy failure response in `tests/contract/MediBridge.ContractTests/CompanyCampaignAnalyticsContractTests.cs`.
- [X] T083 [P] [US4] Add integration test for analytics counts and rates across Active, Accepted, Rejected, and Expired deliveries in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.
- [X] T084 [P] [US4] Add integration test for Active deliveries contributing reserved amount only and no charged spend, doctor earnings, or platform fee in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.
- [X] T085 [P] [US4] Add integration test for Accepted and Rejected deliveries contributing charged spend, doctor earnings, platform fee, and interaction rate in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.
- [X] T086 [P] [US4] Add integration test for Expired deliveries contributing delivered and expired counts but zero spend, earnings, and fee in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.
- [X] T087 [P] [US4] Add integration test for zero-denominator rates returning value 0 with denominator metadata rather than null or errors in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.
- [X] T088 [P] [US4] Add integration test for reconciliation discrepancy blocking only the affected report scope and creating safe discrepancy evidence in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReconciliationIntegrationTests.cs`.
- [X] T089 [P] [US4] Add integration test proving analytics requests are read-only except discrepancy evidence creation in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingReadOnlyTests.cs`.
- [X] T090 [P] [US4] Add integration test proving analytics ignores missing, stale, or inconsistent stored aggregate/counter data when source evidence is internally consistent in `tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs`.

### Implementation for User Story 4

- [X] T091 [US4] Implement analytics delivery source projection scoped by company id, campaign id, and `DeliveryDateEgypt` range in `MediBridge.Repository/Repositories/Messaging/DeliveryRepository.cs`.
- [X] T092 [US4] Implement financial evidence source projection for Charge, Earn, platform fee, Reserve, and Release evidence by selected delivery ids in `MediBridge.Repository/Repositories/Wallets/WalletTransactionRepository.cs`.
- [X] T093 [US4] Implement live analytics aggregation for delivered, active unanswered, accepted, rejected, expired, and feedback counts in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T094 [US4] Implement live monetary aggregation rules for reserved amount, charged spend, doctor earnings, and platform fee in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T095 [US4] Implement rate calculations for interaction, acceptance, rejection, expiry, and feedback among interacted deliveries with explicit zero-denominator metadata in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T096 [US4] Implement reconciliation checks comparing delivery state/snapshot totals against append-only financial evidence in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T097 [US4] Implement safe discrepancy evidence creation through `IAuditEventRepository` without raw idempotency material, wallet internals, stack traces, private doctor data, or request/response bodies in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T098 [US4] Ensure discrepancy evidence metadata is accepted by `AuditMetadataRules.EnsureSafe` in `MediBridge.Core/Entities/Policies/AuditMetadataRules.cs`.
- [X] T099 [US4] Implement `GetCampaignAnalyticsAsync` in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T100 [US4] Map analytics read models to `CampaignAnalyticsDto` and `RateMetricDto` in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtoMapper.cs`.
- [X] T101 [US4] Add `GET /api/company/campaigns/{campaignId}/analytics` action with HTTP-only query binding and envelope response in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T102 [US4] Ensure analytics endpoint validation rejects malformed dates, `fromDateEgypt > toDateEgypt`, ranges longer than 90 days, and unauthorized/cross-company campaign scopes in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T103 [US4] Ensure analytics failure response for reconciliation discrepancy returns no partial or invented monetary totals in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T104 [US4] Run and pass US4 unit, contract, and integration tests for analytics and reconciliation in `tests/unit/MediBridge.UnitTests/`, `tests/contract/MediBridge.ContractTests/`, and `tests/integration/MediBridge.IntegrationTests/`.

**Checkpoint**: User Story 4 is independently functional and source-data reconciliation is enforced.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify the feature end-to-end, harden performance/privacy, and update generated evidence.

- [X] T105 [P] Add or update OpenAPI generation assertions for the four Phase 10 routes in `MediBridge.APIs/OpenApi/`.
- [X] T106 [P] Add performance-profile seed helpers for one company, one campaign, 10,000 deliveries, and financial evidence inside 90 days in `tests/performance/CompanyReportingPerformanceSeed.cs`.
- [X] T107 [P] Add performance test for campaign summaries p95 under 2 seconds in `tests/performance/CompanyReportingPerformanceTests.cs`.
- [X] T108 [P] Add performance test for delivery report pages p95 under 2 seconds and bounded query count in `tests/performance/CompanyReportingPerformanceTests.cs`.
- [X] T109 [P] Add performance test for feedback report pages p95 under 2 seconds and bounded query count in `tests/performance/CompanyReportingPerformanceTests.cs`.
- [X] T110 [P] Add performance test for analytics p95 under 2 seconds and zero false reconciliation discrepancies in `tests/performance/CompanyReportingPerformanceTests.cs`.
- [X] T111 Review all Phase 10 DTOs to confirm they expose no prohibited doctor, wallet, idempotency, storage, stack trace, or admin-only fields in `MediBridge.Services/DTOs/Campaigns/CampaignReportingDtos.cs`.
- [X] T112 Review all Phase 10 repository queries to confirm every query is scoped by authenticated company id before campaign/delivery/financial rows are returned in `MediBridge.Repository/Repositories/`.
- [X] T113 Review all Phase 10 service methods to confirm no EF Core types are referenced in `MediBridge.Services/Services/CompanyReportingService.cs`.
- [X] T114 Review `CompanyCampaignsController` to confirm controllers contain only HTTP query binding, actor extraction, service calls, and envelope creation in `MediBridge.APIs/Controllers/CompanyCampaignsController.cs`.
- [X] T115 Run `dotnet format` or the repository's formatting equivalent for changed C# files in `D:/My Project/MediBridge Project/MediBridge`.
- [X] T116 Run `dotnet build .\\MediBridge.slnx --configuration Release` from `D:/My Project/MediBridge Project/MediBridge`.
- [X] T117 Run `dotnet test .\\MediBridge.slnx --configuration Release` from `D:/My Project/MediBridge Project/MediBridge`.
- [X] T118 Run the quickstart smoke and validation checklist and record results in `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T119 Update task completion evidence or notes for any skipped performance prerequisites in `specs/012-company-reporting-analytics/quickstart.md`.
- [X] T120 Perform final constitution compliance review for layering, thin controllers, repository/unit-of-work usage, JWT/company authorization, standard envelopes, safe errors, queue non-mutation, and wallet non-mutation in `specs/012-company-reporting-analytics/tasks.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup**: No dependencies; can begin immediately.
- **Phase 2 Foundational**: Depends on Phase 1; blocks all user stories.
- **Phase 3 US1 Summary MVP**: Depends on Phase 2.
- **Phase 4 US2 Delivery Reports**: Depends on Phase 2; can run in parallel with US1 after shared service/repository skeleton exists, but safest order is after US1.
- **Phase 5 US3 Feedback Reports**: Depends on Phase 2; can run in parallel with US2 after delivery projection patterns are established.
- **Phase 6 US4 Analytics/Reconciliation**: Depends on Phase 2 and benefits from US1 source summary logic; can be implemented independently but should reuse shared reconciliation helpers.
- **Phase 7 Polish**: Depends on all desired user stories.

### User Story Dependencies

- **US1 Review Campaign Performance Summary**: MVP. No dependency on US2, US3, or US4 after foundation.
- **US2 Inspect Campaign Deliveries**: Independent endpoint. Reuses shared validators, DTO mapping conventions, and ownership service from foundation/US1.
- **US3 Read Doctor Feedback**: Independent endpoint. Reuses delivery repository filtering conventions and privacy mapping.
- **US4 Reconcile Spend and Engagement Analytics**: Independent endpoint. Reuses source projection and reconciliation helpers; must not require stored aggregates.

### Within Each User Story

- Write and run tests first; they should fail before implementation.
- Implement repository read models before service orchestration.
- Implement service validation, ownership, privacy shaping, and reconciliation before controller actions.
- Implement controller actions last.
- Run story-specific tests before moving to the next story.

---

## Parallel Opportunities

- Setup inspections T002 to T007 can run in parallel.
- Foundational tests T030 to T035 can run in parallel after foundational DTOs/read models exist.
- US1 tests T036 to T040 can run in parallel.
- US2 tests T052 to T056 can run in parallel.
- US3 tests T067 to T072 can run in parallel.
- US4 tests T082 to T090 can run in parallel.
- Performance tests T106 to T110 can be prepared in parallel after all endpoints exist.

---

## Parallel Example: User Story 1

```text
Task: "T036 [US1] Add contract tests for GET /api/company/campaigns in tests/contract/MediBridge.ContractTests/CompanyCampaignReportingContractTests.cs"
Task: "T037 [US1] Add integration test for company-owned campaign report summaries in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs"
Task: "T038 [US1] Add integration test proving another company cannot see campaign summaries in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs"
Task: "T039 [US1] Add empty filter integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs"
Task: "T040 [US1] Add stale aggregate ignored integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportSummaryIntegrationTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T052 [US2] Add delivery contract tests in tests/contract/MediBridge.ContractTests/CompanyCampaignDeliveriesContractTests.cs"
Task: "T053 [US2] Add delivery ordering/pagination integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesIntegrationTests.cs"
Task: "T054 [US2] Add delivery filter integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesIntegrationTests.cs"
Task: "T055 [US2] Add cross-company delivery authorization test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs"
Task: "T056 [US2] Add delivery privacy test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignDeliveriesPrivacyTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T067 [US3] Add feedback contract tests in tests/contract/MediBridge.ContractTests/CompanyCampaignFeedbackContractTests.cs"
Task: "T068 [US3] Add empty feedback exclusion test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs"
Task: "T069 [US3] Add short feedback eligibility test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs"
Task: "T070 [US3] Add feedback filters test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackIntegrationTests.cs"
Task: "T071 [US3] Add cross-company feedback authorization test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingAuthorizationTests.cs"
Task: "T072 [US3] Add feedback privacy test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignFeedbackPrivacyTests.cs"
```

## Parallel Example: User Story 4

```text
Task: "T082 [US4] Add analytics contract tests in tests/contract/MediBridge.ContractTests/CompanyCampaignAnalyticsContractTests.cs"
Task: "T083 [US4] Add analytics counts/rates integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs"
Task: "T088 [US4] Add reconciliation discrepancy integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReconciliationIntegrationTests.cs"
Task: "T089 [US4] Add read-only mutation audit integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignReportingReadOnlyTests.cs"
Task: "T090 [US4] Add stale aggregate ignored integration test in tests/integration/MediBridge.IntegrationTests/Campaigns/CompanyCampaignAnalyticsIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 only.
3. Validate `GET /api/company/campaigns` returns owned campaign report summaries with correct counts and money totals.
4. Stop and demo before adding delivery, feedback, and analytics endpoints.

### Incremental Delivery

1. **US1**: Campaign report summaries.
2. **US2**: Delivery report rows.
3. **US3**: Feedback report rows.
4. **US4**: Analytics and reconciliation discrepancy behavior.
5. **Polish**: Performance, privacy review, full test suite, quickstart evidence.

### Smaller Model Execution Notes

- Do not skip tests unless explicitly instructed by the user.
- Do not implement stored reporting aggregates.
- Do not add admin endpoints in Phase 10.
- Do not expose doctor names or contact fields even if they are easy to query.
- Do not repair financial discrepancies; only block the affected report and record safe discrepancy evidence.
- Do not modify delivery status, wallet balances, wallet transactions, ledger entries, queue rows, job records, activity scores, or settlement code except where read-only projections require access.
- If an existing type name differs from this task list, prefer the existing codebase naming and update only the minimum necessary references.
- If a migration is needed, keep it limited to indexes or safe discrepancy evidence. Never rewrite historical financial data.
