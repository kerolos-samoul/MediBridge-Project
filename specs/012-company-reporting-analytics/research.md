# Phase 0 Research: Company Reporting & Analytics

## Decision: Compute Reports Live From Source Evidence

**Rationale**: The clarified specification makes delivery records and append-only financial evidence authoritative for every request. Live projections avoid a new aggregate maintenance workflow, avoid stale counters, and keep Phase 10 read-mostly with one narrow discrepancy write path.

**Alternatives considered**:

- Stored reporting aggregates with read-through fallback: better steady-state speed, but conflicts with the selected clarification and adds cache invalidation tasks.
- Stored aggregates only: fastest reads, but stale/missing aggregates would block otherwise valid reports.
- Derived read model maintained by background jobs: useful later, but outside Phase 10 and risks duplicate reporting truth.

## Decision: Use `DeliveryDateEgypt` for All Reporting Date Filters

**Rationale**: Delivery, expiry, interaction, and company reporting all need the same business event boundary. `DeliveryDateEgypt` is already persisted on deliveries and matches the company-visible delivery day.

**Alternatives considered**:

- Campaign submission date: useful for campaign lifecycle reporting but not for delivery/spend analytics.
- Interaction or settlement date: misaligns active and expired delivery reporting and creates unclear partial campaign views.
- UTC timestamps: would reintroduce time-zone ambiguity already resolved in earlier phases.

## Decision: Limit Date Ranges to 90 Inclusive Delivery Business Days

**Rationale**: Phase 10 computes reports live. A 90-day limit gives companies meaningful campaign windows while bounding source projection size and making the 10,000-delivery performance profile realistic.

**Alternatives considered**:

- 180 or 365 days: useful for annual reporting, but higher live-query risk without stored aggregates.
- No maximum: conflicts with bounded performance and could create expensive reports.
- 30 days: safer operationally, but too narrow for campaigns that span several months.

## Decision: Scope Every Query by Approved Company Ownership

**Rationale**: Company reporting contains campaign performance, feedback, and financial data. Services must resolve the authenticated user to an approved active company profile and every repository projection must include company ownership filtering.

**Alternatives considered**:

- Controller-only ownership checks: rejected because controllers must remain HTTP-only.
- Campaign id lookup followed by separate delivery queries without company scoping: rejected because it risks cross-company data leakage if an intermediate check is bypassed.
- Admin access through company paths: rejected by the specification; admin statistics belong to Phase 11.

## Decision: Expose Public Doctor Identifier Only

**Rationale**: Reporting needs row-level traceability while preserving doctor privacy. A stable public doctor identifier plus specialization, experience band, and location is enough for business analysis without exposing names, contact details, verification, wallet, or private account data.

**Alternatives considered**:

- Display names: more convenient for companies, but higher privacy risk.
- Fully anonymous rows: strongest privacy, but reduces delivery/feedback traceability and campaign follow-up value.

## Decision: Fail Closed on Source Reconciliation Discrepancies

**Rationale**: Campaign analytics include financial totals. If delivery states and append-only financial evidence disagree, returning partial or invented totals would erode trust. Blocking only the affected report scope preserves unrelated reports while surfacing safe operational evidence.

**Alternatives considered**:

- Return delivery totals with warning: risks companies acting on unreliable money data.
- Treat ledger totals as authoritative: hides delivery-state defects.
- Treat delivery totals as authoritative: hides financial evidence defects.

## Decision: Use Existing Audit Infrastructure Unless It Cannot Represent Discrepancy Scope Safely

**Rationale**: Existing `AuditEvent` infrastructure already supports safe metadata and target ids. Reusing it avoids a new persistence surface unless reporting discrepancies need stricter shape, uniqueness, or query behavior than audit events provide.

**Alternatives considered**:

- Always add a new `ReportingReconciliationDiscrepancy` table: clearer domain shape, but extra migration and repository work.
- Log-only discrepancies: rejected because operational evidence must be durable and reviewable without depending on logs.

## Decision: Standard Pagination With Deterministic Ordering

**Rationale**: The project standard uses `PageNumber` and `PageSize` with max page size 100. Deterministic ordering by delivery date/time/id or feedback time/id makes traversal testable within the requested ordering.

**Alternatives considered**:

- Keyset pagination: stronger under concurrent writes, but conflicts with current standard pagination assumptions.
- Unbounded exports: outside Phase 10 and risky for live source queries.

## Decision: Feedback Rows Include Only Non-Empty Stored Feedback

**Rationale**: Omitted, empty, and whitespace-only feedback should not clutter feedback reports, but their interactions still count in analytics. Short feedback remains visible when present and marked as not score-eligible.

**Alternatives considered**:

- Include empty feedback rows: makes feedback reports noisy and duplicates interaction reporting.
- Hide short feedback: would lose valid company insight and conflict with Phase 8 storage rules.

## Decision: Rates Return Zero With Explicit Denominator Status

**Rationale**: Zero-denominator cases are expected for new campaigns or empty filters. Returning `0` plus denominator/eligibility metadata keeps responses stable and prevents divide-by-zero behavior.

**Alternatives considered**:

- Null rates: forces clients to special-case every metric.
- Omit unavailable rates: complicates response contracts and comparisons.
