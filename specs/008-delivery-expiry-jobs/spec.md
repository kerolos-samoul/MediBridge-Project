# Feature Specification: Delivery & Expiry Jobs (Phase 7)

**Feature Branch**: `[008-delivery-expiry-jobs]`  
**Created**: 2026-07-02  
**Status**: Draft  
**Input**: User description: "Phase 7: Delivery & Expiry Jobs in backend plan"

## Clarifications

### Session 2026-07-02

- Q: Should Egypt business time follow DST-aware `Africa/Cairo` rules or remain fixed at UTC+2 year-round? → A: Use DST-aware `Africa/Cairo` local time.
- Q: Should daily injection proceed when required overdue expiry is still running or has failed? → A: Defer injection until overdue expiry succeeds, then retry injection.
- Q: What should happen to queue rows blocked by permanent versus temporary eligibility conditions? → A: Cancel terminally undeliverable rows; keep temporarily blocked rows Queued.
- Q: Should catch-up expiry process only yesterday or every overdue Active delivery? → A: Process all Active deliveries dated before today.
- Q: If expiry has isolated failures, should the injection gate apply globally or only to affected companies? → A: Block only companies with unresolved expiry failures; continue for others.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Expire Unanswered Deliveries and Release Funds (Priority: P1)

As a pharmaceutical company user, I need reserved funds for messages that received no doctor interaction before the end of their delivery day to be returned automatically so that unused campaign funds become available again.

**Why this priority**: Expiry protects company balances and must finish before new daily reservations are attempted.

**Independent Test**: Create prior-day active deliveries with reserved company funds, run the expiry cycle more than once, and confirm each delivery expires and each reservation is released exactly once while interacted or current-day deliveries remain unchanged.

**Acceptance Scenarios**:

1. **Given** an Active delivery from the previous Egypt business day with a Reserved reservation and no interaction, **When** the expiry cycle runs at or after 00:00 Egypt time, **Then** the delivery becomes Expired, the reservation becomes Released, and the reserved amount returns to the company's available balance exactly once.
2. **Given** the same expired delivery, **When** the expiry cycle is retried or runs concurrently, **Then** no additional balance change, release transaction, or release ledger entry is created.
3. **Given** a prior-day delivery that was already Accepted or Rejected, **When** the expiry cycle runs, **Then** its delivery state and settled balances remain unchanged.
4. **Given** an expiry cycle was missed for one or more days, **When** the next cycle runs, **Then** all still-Active deliveries whose Egypt delivery day has ended are safely caught up before new delivery activation is attempted.

---

### User Story 2 - Activate the Daily Message Allocation (Priority: P1)

As a doctor, I need eligible queued campaign messages activated for the current Egypt day up to my daily limit so that I receive a fair, predictable daily inbox.

**Why this priority**: Daily activation is the core delivery workflow and the point at which company funds are reserved for potential interaction.

**Independent Test**: Prepare multiple doctors, approved and ineligible campaigns, FIFO queue rows, daily limits, and funded and underfunded company wallets; run the injector twice and confirm deterministic activation, correct limits, correct reservations, and no duplicate effects.

**Acceptance Scenarios**:

1. **Given** a doctor with eligible FIFO queue rows and unused daily capacity, **When** the daily injector runs at or after 00:05 Egypt time, **Then** it activates eligible messages in ascending immutable campaign-submission order by `CampaignSubmittedAtUtc`, then stable queue-row identifier, until the doctor's current daily limit is reached.
2. **Given** a candidate whose company has enough available funds, **When** activation succeeds, **Then** one current-day delivery is created, the queue row becomes Activated, the company's available balance decreases by the snapshotted doctor price, its reserved balance increases by the same amount, and the reservation records are created as one indivisible operation.
3. **Given** the next FIFO candidate belongs to a company without enough available funds, **When** reservation is attempted, **Then** that row remains Queued and the injector continues scanning later FIFO candidates so the doctor's remaining capacity can still be filled.
4. **Given** a queued candidate is permanently undeliverable because its campaign, company, or doctor is in a terminal state, **When** the injector evaluates the row, **Then** no delivery or reservation is created and the row becomes Cancelled; when the block is temporary, such as a paused campaign, suspended doctor, or temporarily missing positive price, the row remains Queued for a later cycle.
5. **Given** current-day deliveries already exist for the doctor, **When** the injector is retried, **Then** those deliveries count toward the daily limit and neither deliveries nor reservations are duplicated.

---

### User Story 3 - View Only Today's Doctor Inbox (Priority: P1)

As a doctor, I need to view only my messages for the current Egypt business day so that expired or future messages do not appear actionable.

**Why this priority**: The delivery-day visibility boundary is a locked product rule and prevents doctors from acting on stale campaign messages.

**Independent Test**: Authenticate as a doctor with deliveries on yesterday, today, and tomorrow in Egypt time, request the today inbox around the day boundary, and confirm only the authenticated doctor's current-day messages and safe approved content are returned.

**Acceptance Scenarios**:

1. **Given** the authenticated doctor has deliveries from multiple dates, **When** the doctor requests the today inbox, **Then** only deliveries whose `DeliveryDateEgypt` equals the current Egypt business date are returned.
2. **Given** two doctors have current-day deliveries, **When** either doctor requests the today inbox, **Then** that doctor sees only their own deliveries.
3. **Given** the Egypt business date changes at midnight, **When** the inbox is requested after the boundary, **Then** the prior day's messages no longer appear.
4. **Given** a current-day delivery contains campaign content and approved assets, **When** it is returned in the inbox, **Then** the doctor receives usable content and authorized asset access without storage credentials, raw storage locations, or internal financial details.
5. **Given** the authenticated doctor has more current-day deliveries than one response page, **When** the doctor follows successive inbox cursors, **Then** every current-day delivery is reachable exactly once in deterministic order without gaps, duplicates, or cross-doctor cursor reuse.

---

### User Story 4 - Operate and Retry Daily Cycles Safely (Priority: P2)

As an operator, I need expiry and injection cycles to run in the required order and tolerate retries or overlapping execution so that temporary failures do not corrupt deliveries or wallet balances.

**Why this priority**: Automated financial workflows must be recoverable and observable without requiring manual balance repair.

**Independent Test**: Trigger scheduled, repeated, and overlapping expiry and injector executions, including an injected failure partway through a candidate operation, and confirm ordering, atomic rollback, deterministic retry, and safe operational records.

**Acceptance Scenarios**:

1. **Given** a normal Egypt day boundary, **When** recurring processing is registered, **Then** expiry is scheduled and eligible at 00:00 Egypt time and daily injection is scheduled and eligible at 00:05; worker start may be later because of availability or infrastructure latency, but injection never activates before 00:05 and a candidate is processed only after required overdue expiry for its owning company has succeeded.
2. **Given** two workers attempt the same expiry or activation concurrently, **When** both complete, **Then** each eligible business event has one final state and at most one financial effect.
3. **Given** an unexpected failure occurs while changing a delivery, queue row, wallet, transaction, or ledger record, **When** the operation ends, **Then** all related changes for that candidate are committed together or none are committed.
4. **Given** an authorized operator reviews or retries a failed or completed cycle, **When** the operator uses infrastructure-managed read-only SQL access for `DeliveryJobRun` review or protected Hangfire administration tooling for requeue, **Then** platform IAM and database permissions enforce access, the requeued persisted service-interface job uses the same idempotent operations as scheduled execution, and timing, outcome, counts, and safe error summaries remain traceable without exposing secrets or raw stack traces. No public or application-mapped dashboard or manual job-control HTTP endpoint is provided.
5. **Given** the service starts or recovers after one or both required schedules for the current Egypt business date were missed, **When** startup recovery runs repeatedly or concurrently on multiple hosts, **Then** it durably claims the missing date/job dispatch, enqueues the same persisted expiry service job first, enqueues the same persisted injector service job only after expiry completion when injection is already eligible, and performs no delivery, queue, wallet, transaction, or ledger mutation directly at startup.

### Edge Cases

- The service starts after 00:05 Egypt time or recovers after downtime with no required current-date run or recovery dispatch recorded; the startup/recovery coordinator durably claims the missing work, enqueues expiry first, and enqueues injection as its continuation. Repeated or concurrent startup may deliver the dispatch at least once but creates one durable date/job claim and relies on the same idempotent services for zero duplicate business effects.
- Expiry is still running or has unresolved failures for a company when the scheduled injection time arrives; candidates owned by that company are deferred with no new delivery or reservation, while candidates owned by companies whose overdue expiry succeeded may continue.
- A doctor's daily limit is zero, reduced below the number of already activated current-day deliveries, or changes while injection is running; no existing delivery is removed and no additional delivery is activated beyond the limit observed for a candidate decision.
- Several queue rows share the same immutable campaign-submission time; stable queue-row identifier ordering resolves the tie. `QueuedAtUtc` does not determine campaign priority.
- A terminal historical queue row has no authentic campaign submission timestamp; because Activated and Cancelled rows can never return to Queued in Phase 7, it remains nullable and is never eligible for FIFO injection. Every unresolved Queued row must have an authentic immutable submission timestamp before Phase 7 can run.
- The same campaign has duplicate or legacy pending rows for one doctor; current-day uniqueness prevents more than one delivery for the doctor, date, and campaign.
- A campaign becomes cancelled, rejected, completed, or deleted after queueing, or its company is deleted; the row becomes Cancelled without a delivery or financial effect.
- A campaign is paused, a doctor is suspended or temporarily unapproved, or a positive doctor price is temporarily missing; the row remains Queued without consuming capacity and can be reconsidered in a later cycle.
- A doctor is deleted or otherwise permanently unable to receive messages; affected pending rows become Cancelled without a delivery or financial effect.
- A company's balance equals the required reservation exactly; activation may reserve the full amount and leave zero available balance.
- Wallet balance or delivery state changes concurrently; stale operations are rejected and safely retried without a partial financial effect.
- A delivery has an inconsistent reservation state or missing wallet relationship; the candidate is left unchanged, the issue is recorded for operations, and processing continues where isolation is safe.
- Daylight-saving or host-time differences occur; all business-date decisions use the project's authoritative Egypt business-time rule rather than the host machine's local date.
- The inbox contains no current-day deliveries; the doctor receives a successful empty result.
- The inbox contains more than one page, a cursor is malformed, belongs to another doctor, or was issued for a prior Egypt business date; valid pages remain gap-free and duplicate-free, while invalid, cross-doctor, and stale-date cursors are rejected safely.
- An Admin, Company user, unauthenticated caller, or one doctor attempts to access another doctor's today inbox; access is denied without disclosing whether the deliveries exist.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST derive one authoritative Egypt business date and local clock from the DST-aware `Africa/Cairo` time zone for scheduling, delivery visibility, daily-limit counting, activation uniqueness, and expiry eligibility; it MUST NOT treat Egypt business time as a fixed UTC+2 offset year-round.
- **FR-002**: The system MUST register the expiry cycle with a 00:00 Egypt-time schedule and the daily injection cycle with a 00:05 Egypt-time schedule. These times define scheduling and eligibility rather than guaranteed worker start instants. Delayed execution caused by worker availability, restart, or infrastructure latency MUST retain the captured Egypt business-date rules and perform safe catch-up. Injection MUST NOT activate candidates before 00:05 Egypt time, and successful required overdue expiry remains a mandatory company-scoped gate before a candidate owned by that company can activate for the business date.
- **FR-002A**: On host startup or recovery, an idempotent coordinator MUST capture the Egypt business date and determine whether the required expiry and, when local time is at or after 00:05, injection runs are absent and have no active or completed recovery dispatch. It MUST create or reuse one durable recovery-dispatch claim per Egypt business date and job type, retry a Failed/Pending claim rather than allowing it to count as coverage, enqueue the same persisted expiry service job first, and enqueue the same persisted injector service job as a continuation after expiry completion when injection is eligible. If expiry already completed for the date and injection alone is missing, it MAY enqueue injection directly. The coordinator and startup path MUST NOT invoke either business service directly or mutate deliveries, queues, wallets, transactions, or ledgers. Dispatch is at-least-once across a crash between durable claim and scheduler acknowledgement; repeated delivery MUST remain safe through the same idempotent job services.
- **FR-003**: The system MUST identify every still-Active delivery with a `DeliveryDateEgypt` before the current Egypt business date and no completed Accept or Reject interaction as eligible for expiry, without limiting catch-up to yesterday or to a bounded lookback window.
- **FR-004**: The system MUST change each eligible delivery from Active to Expired and its reservation from Reserved to Released.
- **FR-005**: For each expiry, the system MUST move exactly the delivery's reserved amount from the owning company's reserved balance back to its available balance without charging the company or crediting the doctor.
- **FR-006**: Each expiry release MUST create append-only financial transaction and ledger evidence linked to the delivery and protected by a deterministic operation-level idempotency key.
- **FR-007**: The delivery state change, reservation state change, company balance change, financial transaction, and ledger entries for one expiry MUST complete atomically.
- **FR-008**: Repeated, overlapping, or catch-up expiry execution MUST produce no duplicate release and no second transition for an already expired or settled delivery.
- **FR-009**: Before activation, the injector MUST count the doctor's existing current-day deliveries toward the doctor's current global daily message limit across all companies.
- **FR-010**: The injector MUST evaluate pending queue rows per doctor in FIFO order by immutable `CampaignSubmittedAtUtc`, then stable queue-row identifier. `CampaignSubmittedAtUtc` MUST be copied from the campaign's actual `SubmittedAtUtc` when the queue row is created and MUST never be changed afterward. `QueuedAtUtc` MUST NOT replace campaign-submission order.
- **FR-010A**: `CampaignSubmittedAtUtc` MUST be non-null for every unresolved Queued row and enforced by a conditional persistence constraint. Existing Queued rows MUST be backfilled only from authentic `Campaign.SubmittedAtUtc`; migration MUST fail if any Queued row remains unresolved. Activated or Cancelled historical rows MAY retain null because those states are terminal in Phase 7 and MUST never participate in FIFO injection or return to Queued. `QueuedAtUtc` MUST NOT be used as a backfill source for any state.
- **FR-011**: The injector MUST activate candidates until the doctor's remaining daily capacity is filled or no further eligible queue candidate can be activated in the current scan.
- **FR-012**: A queue candidate MUST be activatable only when the doctor is currently approved, active, not deleted or suspended, has a positive current message price, and the campaign is currently approved, owned by an eligible company, and deliverable rather than paused, cancelled, rejected, completed, or deleted.
- **FR-013**: The injector MUST prevent more than one delivery for the same doctor, Egypt delivery date, and campaign, including under retry and concurrency.
- **FR-014**: At activation, exactly one platform-fee policy MUST be effective. The system MUST snapshot the doctor's current positive price and a fee percentage greater than 0 and at most 100 with two-decimal precision. The fee MUST be rounded to two decimals using midpoint rounding away from zero; the rounded fee MUST be greater than 0 and strictly less than the price, and doctor earnings MUST equal price minus the rounded fee and remain greater than 0. Missing, overlapping, out-of-range, or rounded-invalid policy results MUST leave the candidate Queued, record a safe isolated failure, and create no delivery or financial mutation.
- **FR-015**: A candidate MUST activate only when the owning company's available balance is at least the price snapshot.
- **FR-016**: On successful activation, the system MUST create one Active current-day delivery with a Reserved reservation, mark its queue row Activated, move the price snapshot from the company's available balance to its reserved balance, and create append-only reservation transaction and ledger evidence linked to the delivery.
- **FR-017**: The delivery, queue transition, wallet balance changes, reservation transaction, and ledger entries for one activation MUST complete atomically and use a deterministic operation-level idempotency key.
- **FR-018**: If a company lacks sufficient available funds, the injector MUST leave that queue row Queued, create no delivery or financial record for it, and continue scanning later FIFO rows for that doctor during the same cycle.
- **FR-019**: The injector MUST change a queue row to Cancelled when its campaign, owning company, or target doctor is permanently undeliverable, including a cancelled, rejected, completed, or deleted campaign or a deleted company or doctor; this transition MUST create no delivery or financial record.
- **FR-019A**: The injector MUST leave a temporarily blocked row Queued, including for a paused campaign, suspended or temporarily unapproved doctor, or temporarily missing positive doctor price, so it can be reconsidered in a later cycle.
- **FR-019B**: A cancelled, temporarily blocked, insufficient-fund, or isolated failed candidate MUST NOT consume the doctor's daily capacity; processing MUST continue with later candidates when doing so cannot compromise consistency.
- **FR-020**: Repeated, overlapping, or resumed injector execution MUST count existing current-day deliveries and create no duplicate delivery, reservation, transaction, ledger entry, or queue transition.
- **FR-021**: The system MUST defer candidates owned by a company and create no new delivery or reservation for that company while its required overdue expiry is running or has unresolved failures; after that company's expiry succeeds, its candidates MUST be retried idempotently whether the delay arose from normal scheduling, a missed schedule, failure, or service restart. Candidates owned by companies with successful overdue expiry MUST remain eligible in the same cycle.
- **FR-022**: The system MUST expose an authenticated today-inbox operation for Doctor users.
- **FR-023**: The today inbox MUST return only deliveries owned by the authenticated doctor whose `DeliveryDateEgypt` equals the current Egypt business date; it MUST return a successful empty result when none exist, and every current-day delivery MUST remain reachable when more than one response page exists.
- **FR-023A**: The today inbox MUST use keyset pagination. `PageSize` defaults to 50 and MUST be between 1 and 100. An optional opaque cursor identifies the last returned ordering pair and captured Egypt business date. Responses MUST include nullable `NextCursor`. A malformed cursor or a cursor issued for another doctor or Egypt business date MUST be rejected safely without exposing protected delivery existence.
- **FR-024**: The today inbox MUST use deterministic ascending ordering by the persisted activation instant `DeliveredAtUtc`, then stable delivery identifier. `CreatedAtUtc` is audit metadata and MUST NOT be used as the inbox ordering key.
- **FR-025**: Today-inbox items MUST include the delivery identifier, delivery status, campaign message content needed for review, delivery-day context, and authorized access to currently usable approved campaign assets, while excluding wallet internals, private administrative data, storage credentials, and raw storage locations.
- **FR-026**: The system MUST deny unauthenticated users, non-Doctor roles, and one doctor access to another doctor's deliveries without disclosing protected delivery existence.
- **FR-027**: Scheduled execution and explicitly authorized requeue through infrastructure-managed Hangfire administration tooling MUST invoke the same persisted service-interface jobs, use the same idempotent business operations, and produce equivalent outcomes. Phase 7 MUST expose no public or application-mapped Hangfire dashboard or manual job-control HTTP endpoint.
- **FR-028**: Each job cycle MUST record its business date, start and finish times, outcome, examined/activated/expired/skipped/failed counts as applicable, and a safe failure summary suitable for operational diagnosis. Startup recovery MUST separately record its durable date/job dispatch claim, scheduler job identifiers when acknowledged, state, timing, and safe failure summary without storing job arguments, secrets, or financial idempotency material.
- **FR-029**: One malformed or conflicting expiry candidate MUST be recorded and isolate injection only for its owning company until resolved; other companies may continue. Other malformed or conflicting candidates MUST be isolated so independent doctors and candidates can continue whenever financial and state consistency is preserved.
- **FR-030**: Workflow failures MUST be handled through the standard error path without exposing raw stack traces, credentials, idempotency material, or sensitive wallet and storage details.
- **FR-031**: Public workflow responses MUST use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **FR-032**: Phase 7 MUST NOT settle Accept or Reject interactions, charge reserved funds, credit doctor earnings, enforce weekly activity, calculate activity scores, send notifications, or expose company/admin analytics; those workflows remain outside this feature.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-021` and `FR-027` to `FR-030` target `MediBridge.Core`, `MediBridge.Repository`, and `MediBridge.Services` for business-time rules, job orchestration, queue/delivery state, idempotent wallet mutation, persistence, and operational outcomes; scheduler and host registration target `MediBridge.APIs` without placing business logic there.
  - `FR-022` to `FR-026`, `FR-031`, and `FR-032` target `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, and `MediBridge.APIs` for authorized today-inbox queries, response contracts, and scope boundaries.
- **CA-002 Controller Boundary**: Controllers and job entry points remain transport or trigger adapters only and delegate Egypt-date calculation, eligibility, state transitions, wallet rules, and inbox ownership to service use cases.
- **CA-003 SQL Persistence Boundary**: Delivery, queue, wallet, transaction, ledger, job-run, and recovery-dispatch persistence uses SQL Server through EF Core-backed Repository + Unit of Work abstractions in `MediBridge.Repository`; controllers, scheduler/startup entry points, and services do not depend on EF Core infrastructure types directly.
- **CA-004 Response Contract**: The today inbox and any exposed operational trigger response use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-005 Error Handling**: Validation, authorization, stale-state, recovery-dispatch, scheduler-acknowledgement, and unexpected failures use centralized error handling and safe operational records; raw stack traces are never returned.
- **CA-006 Security**: The today inbox requires JWT Doctor authorization plus authenticated-owner scoping. Phase 7 exposes no application dashboard or manual job-control HTTP surface. Authorized operators review `DeliveryJobRun` records through infrastructure-managed read-only SQL access and requeue persisted jobs through protected Hangfire administration tooling governed by platform IAM and database permissions.
- **CA-007 Ambiguity Control**: No unresolved queue or wallet ambiguity remains. Activation reserves the current doctor price from the campaign owner's company wallet; expiry releases exactly the stored reservation; both use candidate-level atomicity and deterministic idempotency.
- **CA-008 Queue Determinism**: Queue order is the immutable campaign-submission snapshot `CampaignSubmittedAtUtc`, then stable queue-row identifier. Every Queued row requires a non-null authentic snapshot; terminal historical Activated/Cancelled rows may remain null because they never participate in FIFO or return to Queued. `QueuedAtUtc` is operational enqueue timing only and is never a priority or backfill source. Existing current-day deliveries count toward the global doctor limit. Insufficient-fund and temporarily blocked rows remain Queued; terminally undeliverable rows become Cancelled. None consume capacity, and the scan continues. Unanswered deliveries expire after their delivery day, and retries/catch-up create no duplicate delivery or reservation.
- **CA-009 Wallet Determinism**: Activation debits company Available and credits company Reserved for the price snapshot using a Reserve transaction. Expiry debits company Reserved and credits company Available for the delivery's stored reserved amount using a Release transaction. The fee and doctor-earnings snapshots are calculated at activation but no company Charge or doctor Earn occurs until the interaction phase. Every mutation and its ledger evidence commit atomically.

### Key Entities *(include if feature involves data)*

- **Doctor Message Queue Row**: One campaign candidate for one doctor, with an immutable authentic submission-time snapshot required while Queued and used as the primary FIFO key, a stable identifier used as the tie-breaker, separate operational enqueue timing, campaign and company relationships, and Queued, Activated, or Cancelled state. Terminal historical rows may retain a null snapshot because they never re-enter Queued; terminal ineligibility causes cancellation and temporary ineligibility preserves Queued state.
- **Doctor Ad Delivery**: A doctor-visible campaign delivery for one Egypt business date, with unique doctor/date/campaign identity, Active or Expired state in this phase, reservation state, price and fee snapshots, created time, and concurrency state.
- **Company Wallet**: The campaign owner's available and reserved EGP balances changed by activation reservation and unanswered-delivery release.
- **Wallet Transaction and Ledger Entries**: Append-only evidence for one Reserve or Release operation, linked to its delivery and deterministic idempotency key, recording the balanced movement between available and reserved company funds.
- **Delivery Job Run**: Operational record for an expiry or injector cycle, including Egypt business date, timing, outcome, safe counts, and redacted failure summary.
- **Delivery Recovery Dispatch**: Operational startup/recovery claim unique by Egypt business date and job type, recording pending/enqueued/completed state, scheduler identifiers and safe timing/error metadata. It coordinates enqueueing only and never contains or mutates delivery or financial business state.
- **Today Inbox Page**: Doctor-facing keyset page containing current-day delivery items and approved campaign content, an optional opaque continuation cursor scoped to the authenticated doctor and captured Egypt business date, and no internal wallet, administrative, or storage-provider details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of eligible unanswered deliveries whose Egypt delivery day has ended are marked expired and have their full reserved amount returned exactly once in repeated, concurrent, and catch-up test runs.
- **SC-002**: 100% of tested activation candidates begin only after required overdue expiry succeeds for their owning company; a company with running or unresolved expiry is deferred with zero new deliveries or reservations until a later idempotent retry, while eligible candidates for unaffected companies continue.
- **SC-003**: 100% of activated messages respect each doctor's global daily limit and deterministic FIFO ordering by immutable campaign-submission time, then stable queue-row identifier, across all companies.
- **SC-004**: 100% of activation retries and overlapping executions create at most one delivery and one reservation effect for each doctor, Egypt date, and campaign.
- **SC-005**: In all tested insufficient-fund cases, the unfunded row remains queued and later eligible funded rows can fill the doctor's remaining daily capacity.
- **SC-006**: 100% of sampled activation and expiry operations either preserve all related delivery, queue, wallet, transaction, and ledger changes or preserve none of them after a forced failure.
- **SC-007**: 100% of today-inbox results contain only the authenticated doctor's current Egypt-date deliveries, and prior-day messages disappear immediately after the business-day boundary.
- **SC-008**: Under the reproducible Phase 7 performance profile, at least 190 of 200 measured warmed today-inbox requests for a 100-item page complete within 1 second, with no failed requests or unbounded content queries.
- **SC-009**: Under the reproducible Phase 7 performance profile, each of three measured clean-data daily cycles covering 1,000 doctors and 10,000 queued candidates completes within 5 minutes without duplicate deliveries or financial effects.
- **SC-010**: 100% of sampled job runs expose enough safe timing, outcome, and count information to distinguish success, partial candidate failures, and cycle failure without exposing secrets or raw stack traces.
- **SC-011**: 100% of unauthorized and cross-doctor inbox attempts are denied without returning protected campaign or delivery data.
- **SC-012**: In repeated, concurrent, and crash-recovery startup tests after missed schedules, each required Egypt-date/job pair has one durable recovery-dispatch claim, expiry is enqueued before an eligible injector continuation, startup performs zero direct delivery or financial mutations, and repeated persisted job delivery creates no duplicate business effect.

## Assumptions

- Egypt business time follows the DST-aware `Africa/Cairo` time zone and is shared by job scheduling, repositories, services, and tests; any fixed UTC+2 wording in the backend plan is superseded for Phase 7 date and clock calculations.
- Earlier phases already provide approved doctors and companies, positive doctor pricing, approved campaigns, pending queue rows, wallets, append-only financial records, private approved campaign assets, and optimistic concurrency fields.
- Existing unresolved Queued rows either have authentic `CampaignSubmittedAtUtc` or an authentic related `Campaign.SubmittedAtUtc` available for migration backfill. Terminal historical rows may lack both because they remain permanently outside Phase 7 FIFO processing.
- The active platform fee policy is available at activation time; absence of a valid active policy makes the individual candidate ineligible and records a safe operational failure rather than applying an invented fee.
- Existing current-day deliveries count toward the daily limit regardless of whether a later phase changes them from Active to Accepted or Rejected.
- Catch-up expiry has no date lookback cutoff and includes every still-Active delivery before the current Egypt date, so missed schedules cannot leave funds reserved indefinitely.
- Authorized operational review uses infrastructure-managed read-only SQL access to `DeliveryJobRun`; explicit retry uses protected Hangfire administration tooling to requeue the persisted service-interface job. Authorization is supplied by platform IAM and database permissions, and no public or application-mapped dashboard or manual job-control HTTP endpoint is introduced.
- Message read tracking, Accept/Reject interaction, final Charge/Earn settlement, weekly enforcement, activity scoring, notifications, and analytics are delivered by later phases.
