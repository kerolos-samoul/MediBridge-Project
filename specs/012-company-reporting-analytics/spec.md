# Feature Specification: Company Reporting & Analytics (Phase 10)

**Feature Branch**: `[012-company-reporting-analytics]`  
**Created**: 2026-07-13  
**Status**: Draft  
**Input**: User description: "Phase 10: Company Reporting & Analytics"

## Clarifications

### Session 2026-07-13

- Q: What doctor identity should company reporting expose in delivery and feedback rows? → A: Public doctor identifier only; include specialization, experience band, and location, but no doctor names or contact details.
- Q: What date should company reporting filters use? → A: Delivery Egypt business date (`DeliveryDateEgypt`) for summaries, delivery lists, feedback lists, and analytics.
- Q: What should happen when delivery totals and financial evidence disagree? → A: Block the affected report scope and record a safe discrepancy for operational review.
- Q: How should Phase 10 produce reporting totals? → A: Compute reports live from delivery records and append-only financial evidence on each request; do not use stored reporting aggregates.
- Q: What maximum reporting date window should Phase 10 allow? → A: 90 delivery Egypt business days per request.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review Campaign Performance Summary (Priority: P1)

As a pharmaceutical company user, I need to view a reporting summary for my campaigns so that I can understand delivery outcomes, interaction results, feedback volume, and spend without opening each delivery one by one.

**Why this priority**: Campaign performance is the main business value of Phase 10 and lets companies judge whether paid campaigns are producing useful doctor engagement.

**Independent Test**: Sign in as an approved company user with campaigns containing delivered, accepted, rejected, expired, active, and feedback-bearing deliveries; request campaign reporting summaries and confirm only that company's campaigns are shown with correct counts and monetary totals.

**Acceptance Scenarios**:

1. **Given** an approved company has multiple campaigns with delivery outcomes, **When** the company views its campaign reports, **Then** each campaign summary includes campaign identity, current campaign status, submitted/review timing when available, target count, delivered count, active unanswered count, accepted count, rejected count, expired count, feedback count, total reserved amount, total charged amount, total doctor earnings, and total platform fee.
2. **Given** a campaign has active current-day deliveries and historical settled or expired deliveries, **When** the summary is calculated, **Then** delivered count includes all created deliveries while active, accepted, rejected, and expired counts remain separate and reconcile to the delivered total.
3. **Given** a company requests campaign reports with pagination and delivery Egypt business-date filters up to 90 days, **When** matching campaigns exist, **Then** the result is bounded, stable, and ordered consistently by the calculated campaign activity timestamp then stable campaign identifier.
4. **Given** a company has no matching campaigns for the selected filter, **When** it requests reports, **Then** the response succeeds with an empty result and zero aggregate totals.

---

### User Story 2 - Inspect Campaign Deliveries (Priority: P1)

As a pharmaceutical company user, I need to inspect the delivery-level results for one of my campaigns so that I can see which messages were delivered, expired, accepted, or rejected and when those outcomes happened.

**Why this priority**: Delivery-level visibility explains campaign performance and gives companies traceability for billing and follow-up decisions.

**Independent Test**: Create a campaign owned by one company with deliveries across multiple doctors and delivery dates, request the campaign delivery report as the owning company, and confirm every row is campaign-owned, paginated, filtered correctly, and free of unauthorized doctor private data.

**Acceptance Scenarios**:

1. **Given** an owning company requests deliveries for its campaign, **When** deliveries exist, **Then** each result includes safe campaign, delivery, public doctor identifier, specialization, experience band, location, delivery date, delivered time, read time when available, outcome status, interaction time when available, and the delivery's financial snapshot totals.
2. **Given** delivery filters include outcome status, Egypt delivery date range, and doctor specialization/location filters, **When** the company requests results, **Then** only matching deliveries for the owned campaign are returned.
3. **Given** more delivery rows exist than one page, **When** the company follows pagination controls, **Then** every matching delivery is reachable exactly once without duplicates or gaps.
4. **Given** a delivery belongs to another company's campaign, **When** the current company requests delivery reports, **Then** that delivery is never included or revealed.

---

### User Story 3 - Read Doctor Feedback for a Campaign (Priority: P1)

As a pharmaceutical company user, I need to review feedback submitted with accepted or rejected interactions so that I can learn why doctors responded the way they did.

**Why this priority**: Feedback is explicitly part of Phase 10 and turns interaction outcomes into actionable business insight.

**Independent Test**: Prepare campaign deliveries with omitted feedback, empty feedback, short feedback, qualifying feedback, accepted outcomes, and rejected outcomes; request the campaign feedback report and confirm only feedback-bearing rows for the owning company are returned with correct eligibility markers and safe doctor context.

**Acceptance Scenarios**:

1. **Given** a company owns a campaign with interacted deliveries containing valid feedback, **When** it requests the feedback report, **Then** each feedback item includes the delivery, outcome, feedback text, feedback creation time, feedback quality eligibility, public doctor identifier, specialization, experience band, and location.
2. **Given** interacted deliveries have omitted, empty, or whitespace-only feedback, **When** feedback is listed, **Then** those deliveries are excluded from feedback rows while still counted in interaction analytics.
3. **Given** feedback filters include outcome, delivery Egypt business-date range, feedback eligibility, and doctor attributes, **When** the company applies filters, **Then** only matching feedback for the owned campaign is returned.
4. **Given** another company requests the same campaign feedback, **When** authorization is evaluated, **Then** access is denied without exposing whether feedback exists.

---

### User Story 4 - Reconcile Spend and Engagement Analytics (Priority: P1)

As a pharmaceutical company user, I need analytics totals for one campaign to match wallet and delivery evidence so that reported spend, fees, and engagement rates are trustworthy.

**Why this priority**: Company reporting handles money and must match the ledger-backed settlement rules from earlier phases.

**Independent Test**: For a campaign with reserved active deliveries, charged interactions, expired releases, and platform fees, request analytics and compare every reported monetary total and rate against the underlying delivery outcomes and append-only financial evidence.

**Acceptance Scenarios**:

1. **Given** a campaign has accepted, rejected, expired, and active deliveries, **When** analytics are requested, **Then** the report includes delivered, active unanswered, accepted, rejected, expired, feedback, and interaction-rate metrics.
2. **Given** accepted and rejected deliveries have final charges, doctor earnings, and platform fees, **When** analytics are requested, **Then** charged spend, doctor earnings, and platform fee totals equal the settled financial evidence for those deliveries.
3. **Given** active deliveries still have reserved funds, **When** analytics are requested, **Then** reserved amount is reported separately from charged spend and does not inflate final spend.
4. **Given** a selected delivery Egypt business-date range covers only part of a campaign's delivery history, **When** analytics are requested, **Then** counts, rates, and monetary totals are calculated only for deliveries inside that range while campaign-level identity remains visible.
5. **Given** delivery states and append-only financial evidence disagree for the selected campaign or reporting scope, **When** analytics are requested, **Then** the affected report is blocked and a safe discrepancy record is created for operational review.
6. **Given** reporting totals are requested for a campaign, **When** the report is generated, **Then** counts, rates, and monetary totals are computed from current delivery records and append-only financial evidence rather than from stored reporting aggregates.

### Edge Cases

- A campaign has targets but no activated deliveries; reporting succeeds with zero delivery, interaction, feedback, spend, earnings, and fee totals.
- A campaign has only active current-day deliveries; reserved amounts are visible separately while charged spend, doctor earnings, platform fee, and expired counts remain zero.
- A campaign has expired deliveries with released reservations; expired deliveries count as delivered and expired but do not count as charged spend, doctor earnings, or platform fee.
- Accepted and rejected deliveries are both billable interactions and both contribute to charged spend, doctor earnings, platform fee, and interaction rate.
- Feedback exists on an interaction but is shorter than the feedback-score eligibility threshold; the feedback is visible and counted as feedback, but its eligibility marker remains distinguishable.
- A delivery Egypt business-date range begins after it ends, exceeds 90 days, or uses malformed dates; the request is rejected safely without returning partial results.
- One or both delivery Egypt business-date filters are omitted; the system applies a bounded default range that never exceeds 90 inclusive delivery Egypt business days.
- Pagination inputs below 1 or above the maximum page size are handled through standard pagination validation and bounds.
- Campaign, delivery, feedback, or wallet evidence is soft-deleted or belongs to a soft-deleted owner; audit-preserving records remain available only when ownership and authorization rules allow them.
- A company user attempts to access another company's campaign id, delivery id, feedback, analytics, or financial totals; access is denied without exposing protected data.
- Reporting data changes while the company pages through results because new deliveries settle or expire; each page remains internally consistent and avoids duplicate rows within its requested ordering.
- Monetary totals include many small rounded fees; reporting uses the stored per-delivery amounts rather than recalculating fees from current policy.
- Delivery state totals and append-only financial evidence disagree for one campaign or date scope; the affected report is blocked, safe discrepancy evidence is recorded, and unrelated campaign reports remain available when their evidence is consistent.
- Stored reporting aggregate rows or cached counters are absent, stale, or inconsistent; Phase 10 reporting still derives results from delivery records and append-only financial evidence rather than trusting stored aggregates.
- Unexpected failures are returned through the standard safe error path without exposing stack traces, wallet internals, storage details, or private doctor information.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow approved Pharmaceutical Company users to view reporting summaries only for campaigns owned by their company account.
- **FR-002**: Campaign reporting summaries MUST include campaign identity, current campaign status, target count, submitted/review timing when available, delivered count, active unanswered count, accepted count, rejected count, expired count, feedback count, total reserved amount, total charged amount, total doctor earnings, and total platform fee.
- **FR-003**: Delivered count MUST represent all delivery records created for the campaign within the requested reporting scope, and active unanswered, accepted, rejected, and expired counts MUST reconcile to that delivered count.
- **FR-004**: Accepted and rejected outcomes MUST both count as billable interactions for interaction rate, charged spend, doctor earnings, and platform fee reporting.
- **FR-005**: Expired deliveries MUST count as delivered and expired but MUST NOT contribute to charged spend, doctor earnings, or platform fee totals.
- **FR-006**: Active unanswered deliveries MUST report reserved amount separately from charged spend and MUST NOT contribute to accepted, rejected, expired, feedback, doctor earnings, or platform fee totals.
- **FR-007**: All Phase 10 reporting date filters MUST use the delivery's Egypt business date (`DeliveryDateEgypt`) as interpreted by the project's authoritative DST-aware Egypt business-date rules; campaign submission dates, read dates, interaction dates, settlement dates, and UTC timestamps MUST NOT redefine the reporting date range.
- **FR-008**: Phase 10 reporting requests that accept a date range MUST reject ranges longer than 90 delivery Egypt business days.
- **FR-009**: When both `fromDateEgypt` and `toDateEgypt` are omitted, the system MUST default to the latest 90 inclusive delivery Egypt business days ending on the current Egypt business date. When only `toDateEgypt` is provided, `fromDateEgypt` MUST default to 89 days before `toDateEgypt`. When only `fromDateEgypt` is provided, `toDateEgypt` MUST default to the earlier of 89 days after `fromDateEgypt` or the current Egypt business date.
- **FR-009A**: Campaign reporting summaries MUST support standard pagination with stable ordering by calculated campaign activity timestamp descending, then stable campaign identifier. The calculated campaign activity timestamp MUST be the greatest non-null timestamp among the latest delivery delivered time, latest delivery read time, latest interaction time, latest feedback creation time, latest campaign review time, submitted time, and campaign creation time for that campaign; absent values MUST be ignored.
- **FR-010**: The system MUST allow an approved Pharmaceutical Company user to view delivery-level reporting for one owned campaign.
- **FR-011**: Delivery-level reporting MUST include delivery identifier, campaign identifier, public doctor identifier, doctor specialization, doctor experience band, doctor location, Egypt delivery date, delivered time, read time when available, outcome status, interaction time when available, price snapshot, reserved amount, charged amount, doctor earnings, and platform fee amount.
- **FR-012**: Delivery-level reporting MUST support filters for outcome status, delivery Egypt business-date range, doctor specialization, doctor location, and read/interacted state.
- **FR-013**: Delivery-level reporting MUST use standard pagination and deterministic ordering by Egypt delivery date descending, delivered time descending, then stable delivery identifier.
- **FR-014**: The system MUST allow an approved Pharmaceutical Company user to view feedback reporting for one owned campaign.
- **FR-015**: Feedback reporting MUST include only deliveries for the owned campaign that contain non-empty stored feedback text after normalization.
- **FR-016**: Feedback reporting MUST include delivery identifier, campaign identifier, outcome, feedback text, feedback creation time, feedback quality eligibility marker, public doctor identifier, doctor specialization, doctor experience band, and doctor location.
- **FR-017**: Feedback reporting MUST support filters for outcome, delivery Egypt business-date range, feedback quality eligibility, doctor specialization, and doctor location.
- **FR-018**: Feedback reporting MUST use standard pagination and deterministic ordering by feedback creation time descending, then stable delivery identifier.
- **FR-019**: The system MUST allow an approved Pharmaceutical Company user to view analytics for one owned campaign.
- **FR-020**: Campaign analytics MUST include delivered count, active unanswered count, accepted count, rejected count, expired count, feedback count, interaction rate, acceptance rate, rejection rate, expiry rate, feedback rate among interacted deliveries, reserved amount, charged spend, doctor earnings, and platform fee.
- **FR-021**: Rates MUST have explicit zero-denominator behavior: when the denominator is zero, the rate is reported as zero and marked as based on no eligible records.
- **FR-022**: Analytics monetary totals MUST use stored delivery and financial evidence amounts created by reservation, release, charge, earn, and platform-fee settlement; reports MUST NOT recalculate historical fees from current doctor price or current platform-fee policy.
- **FR-023**: Charged spend, doctor earnings, and platform fee totals MUST reconcile to append-only financial evidence for the selected campaign and reporting scope.
- **FR-024**: If delivery state totals and append-only financial evidence disagree for a selected campaign or reporting scope, the system MUST block the affected report result, record a safe discrepancy for operational review, and avoid returning partial or invented monetary totals.
- **FR-025**: A reconciliation discrepancy in one campaign or reporting scope MUST NOT block unrelated company reports whose delivery and financial evidence are internally consistent.
- **FR-026**: Phase 10 reporting totals MUST be computed live from delivery records and append-only financial evidence for each request; stored reporting aggregates, cached counters, and derived read models MUST NOT be treated as authoritative for counts, rates, or monetary totals.
- **FR-027**: Missing, stale, or inconsistent stored reporting aggregates MUST NOT prevent report generation when delivery records and append-only financial evidence are internally consistent.
- **FR-028**: Reporting MUST exclude doctor names, doctor contact details, wallet internals, raw idempotency material, private doctor account data, private file storage locations, storage credentials, and administrative-only notes from company-visible results.
- **FR-029**: The system MUST deny unauthenticated callers, Doctor users, Admin users using company-only reporting paths, pending or rejected company accounts, and company users accessing another company's campaign reporting.
- **FR-030**: Authorization failures for cross-company reporting MUST NOT reveal whether the requested campaign, delivery, feedback, or financial evidence exists.
- **FR-031**: All successful company reporting, delivery, feedback, and analytics responses MUST use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-032**: Validation, authorization, missing-resource, stale-reporting, reconciliation-discrepancy, and unexpected failures MUST be handled by the standard safe error path with no raw stack traces or sensitive financial details exposed.
- **FR-033**: Reporting MUST remain read-only and MUST NOT mutate campaign status, delivery status, feedback, wallet balances, wallet transactions, ledger records, queue rows, job records, activity scores, or audit-sensitive source evidence except for creating safe discrepancy evidence when reconciliation fails.
- **FR-034**: Reporting MUST preserve soft-delete and audit-trail rules by reading historical records only through authorized ownership paths and never hard-deleting or repairing evidence.
- **FR-035**: The feature MUST NOT implement admin platform-wide statistics, admin settlement correction, campaign moderation decisions, withdrawal payout workflows, doctor activity scoring, weekly enforcement, notification dispatch, stored reporting aggregate maintenance, or new settlement behavior.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-009A` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for company-owned campaign reporting summaries, ownership checks, pagination, response shape, date-window validation, activity ordering, and persistence-backed reads.
  - `FR-010` to `FR-018` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for delivery and feedback reporting queries, filtering, safe doctor summaries, and company-only access.
  - `FR-019` to `FR-027` target `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` for analytics calculations, Egypt business-date scoping, live source-data reporting, monetary reconciliation, discrepancy handling, and privacy-safe report shaping, with `MediBridge.APIs` acting only as the HTTP boundary.
  - `FR-028` to `FR-035` target all layers for security, response contracts, safe errors, read-only scope, audit preservation, and explicit exclusions.
- **CA-002 Controller Boundary**: Controllers remain HTTP-only and delegate ownership checks, report filtering, count/rate calculation, monetary reconciliation, pagination, and privacy shaping to service use cases.
- **CA-003 SQL Persistence Boundary**: Campaign, delivery, feedback, wallet transaction, ledger, and reporting reads use SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers and services do not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: All company reporting responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation, authorization, missing campaign, cross-company access, stale data, reconciliation discrepancies, and unexpected failures use global exception handling and safe messages with no raw stack traces.
- **CA-006 Security**: Company reporting requires JWT authorization for approved Pharmaceutical Company users and strict ownership scoping for every campaign, delivery, feedback, and financial total. Doctor and Admin roles are not granted access through company reporting paths.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Reporting is read-only, active deliveries show reserved funds separately, expired deliveries have no spend, and accepted/rejected deliveries are the only charged outcomes.
- **CA-008 Queue Determinism**: Queue ordering, daily activation, expiry, and carry-over behavior remain owned by earlier delivery phases. Phase 10 reads resulting delivery states only and does not requeue, reprioritize, activate, expire, or retry messages.
- **CA-009 Wallet Determinism**: Reporting does not debit, credit, reserve, release, charge, earn, refund, withdraw, or correct funds. Reserved, charged, doctor earning, and platform fee totals are computed live from stored delivery records and append-only financial evidence produced by earlier wallet workflows. Stored reporting aggregates are not authoritative. When source evidence disagrees for a selected report scope, the affected report is blocked and safe discrepancy evidence is recorded instead of repairing or inventing financial totals.

### Key Entities *(include if feature involves data)*

- **Campaign Report Summary**: Company-facing summary for one owned campaign, combining campaign identity, lifecycle status, target count, delivery outcome counts, feedback count, and monetary totals.
- **Campaign Delivery Report Row**: One company-visible delivery result for an owned campaign, including safe doctor summary, delivery timing, status, interaction timing, and stored financial snapshot amounts.
- **Campaign Feedback Report Row**: One feedback-bearing interacted delivery for an owned campaign, including feedback text, outcome, feedback timing, feedback eligibility marker, and safe doctor context.
- **Campaign Analytics Snapshot**: Live calculated reporting result for one owned campaign and optional delivery Egypt business-date scope, including outcome counts, rates, reserved amount, charged spend, doctor earnings, and platform fee.
- **Doctor Ad Delivery**: Source delivery evidence with delivery date, read time, status, interaction time, feedback, price snapshot, reserved amount, charged amount, doctor earnings, and platform fee.
- **Wallet Transaction and Ledger Evidence**: Append-only financial evidence used to reconcile company-visible reserved, charged, doctor earning, and platform fee totals without exposing internal wallet details.
- **Safe Doctor Profile Summary**: Minimal doctor context suitable for company reporting: stable public doctor identifier, specialization, experience band, and location, excluding doctor names, contact details, private account data, verification data, and wallet data.
- **Reporting Reconciliation Discrepancy**: Safe operational evidence that a selected campaign or reporting scope could not be reported because delivery states and append-only financial evidence did not reconcile; it excludes raw idempotency material, wallet internals, stack traces, and private doctor data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of company reporting requests return only campaigns owned by the authenticated approved company account.
- **SC-002**: 100% of sampled campaign summaries reconcile delivered count as active unanswered plus accepted plus rejected plus expired within the selected reporting scope.
- **SC-003**: 100% of sampled accepted and rejected delivery totals match charged spend, doctor earnings, and platform fee evidence stored for those deliveries.
- **SC-004**: 100% of sampled expired deliveries contribute zero charged spend, zero doctor earnings, and zero platform fee while still counting as delivered and expired.
- **SC-005**: 100% of sampled active unanswered deliveries report reserved amount separately and contribute zero charged spend, zero doctor earnings, and zero platform fee.
- **SC-006**: At least 95% of measured warmed campaign summary, delivery list, feedback list, and analytics requests for a campaign with 10,000 deliveries inside a 90-day delivery Egypt business-date window complete within 2 seconds under the agreed Phase 10 performance profile.
- **SC-007**: 100% of paginated delivery and feedback report tests return every matching row exactly once with no gaps or duplicates when traversing all pages.
- **SC-008**: 100% of cross-company, unauthenticated, Doctor-role, Admin-role-on-company-path, pending-company, and rejected-company access attempts are denied without returning protected campaign, delivery, feedback, or financial data.
- **SC-009**: 100% of company-visible reporting responses use the standard response envelope and expose no raw stack traces, raw idempotency material, wallet internals, private doctor account data, storage credentials, or administrative-only notes.
- **SC-010**: 100% of reporting operations are read-only in mutation audits, with no changes to campaign status, delivery status, feedback, wallet balances, wallet transactions, ledger records, queue rows, job records, or activity scores.
- **SC-011**: 100% of forced reconciliation-discrepancy tests block only the affected report scope, create safe discrepancy evidence, and return no partial or invented monetary totals.
- **SC-012**: 100% of reporting tests with missing, stale, or inconsistent stored aggregate data still produce correct reports from delivery records and append-only financial evidence when source evidence is internally consistent.

## Assumptions

- Earlier phases already provide approved company accounts, owned campaigns, campaign targets, activated deliveries, read tracking, accepted/rejected interactions, expired deliveries, feedback storage, wallet transactions, ledger evidence, and standard ownership helpers.
- Basic company campaign list/detail behavior may already exist from Phase 5; Phase 10 extends reporting value with delivery, feedback, analytics, and financial summaries rather than changing campaign creation or review workflows.
- Reporting date filters always use `DeliveryDateEgypt` and follow the same DST-aware `Africa/Cairo` rules established for delivery and interaction phases.
- EGP is the only reporting currency for Phase 10 monetary totals.
- Standard pagination uses `PageNumber` and `PageSize`, with default `PageSize` 20 and maximum `PageSize` 100 unless a prior platform-wide rule changes those bounds.
- Phase 10 reporting date ranges are inclusive and cannot exceed 90 delivery Egypt business days per request.
- Omitted reporting date bounds use the defaulting rules in FR-009 and are resolved before authorization-scoped repository queries run.
- Feedback shorter than the feedback-score threshold remains visible when present but is marked separately from feedback that qualifies for activity-score credit.
- Reporting reads stored historical prices, reserved amounts, charged amounts, earnings, and platform fees; current doctor prices and current fee policy do not change historical reporting totals.
- Phase 10 does not introduce stored reporting aggregate maintenance; any future reporting cache or read model must remain non-authoritative unless a later feature explicitly changes this rule.
- Admin platform-wide statistics remain a Phase 11 concern; Phase 10 is company-owned reporting only.
