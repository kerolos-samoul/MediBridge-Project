# Research: Phase 7 Delivery & Expiry Jobs

## Decision: Reuse Hangfire 1.8.17 With SQL Server Storage

**Rationale**: `MediBridge.APIs.csproj` already references `Hangfire.AspNetCore` and `Hangfire.SqlServer` 1.8.17. Configure those packages in the API host with compatibility level 1.8, recommended serializers, SQL Server storage, and `AddHangfireServer`. Use the existing application database connection by default while keeping Hangfire's internal objects in its own schema; MediBridge domain records remain EF Core-managed. Official Hangfire guidance supports ASP.NET Core DI/server registration and SQL Server storage.

**Alternatives considered**:

- Add Quartz.NET or a custom hosted timer: rejected because Hangfire is already selected and referenced, and durable retry/storage would otherwise be rebuilt.
- Add a separate worker project now: rejected because the current always-running backend can host the server and service-interface jobs preserve later extraction.
- Store domain delivery state in Hangfire data: rejected because scheduler persistence is not the domain source of truth.

**Sources**: [Hangfire ASP.NET Core applications](https://docs.hangfire.io/en/latest/getting-started/aspnet-core-applications.html), [Hangfire overview](https://docs.hangfire.io/en/latest/)

## Decision: Register Stable Cairo-Time Recurring Jobs

**Rationale**: Register `medibridge-expiry-cleaner` at `0 0 * * *` and `medibridge-daily-injector` at `5 0 * * *`, both with the Cairo `TimeZoneInfo` and the dedicated `delivery` queue. These are scheduling and eligibility instants, not guaranteed worker starts. Stable recurring identifiers make startup registration an idempotent add-or-update operation. Hangfire's recurring scheduler checks persistent definitions on a minute interval, so the jobs themselves must handle delayed execution and catch-up; the injector returns Deferred without mutation if invoked before 00:05 Egypt time.

**Alternatives considered**:

- Schedule by UTC offsets: rejected because Cairo switches daylight-saving offsets.
- Chain injector as an unconditional continuation: rejected because free Hangfire does not provide batch semantics and company-scoped gating belongs in business logic.
- Enqueue one job per candidate from the scheduler: rejected because doctor daily-limit ordering and company gating require coordinated scanning.

**Sources**: [Performing recurrent tasks](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html), [Configuring job queues](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html)

## Decision: Reconcile Missed Runs Through Durable Startup Dispatch

**Rationale**: Recurring registration does not itself guarantee catch-up after the host missed a schedule. On startup/recovery, a Services-owned coordinator captures one Cairo snapshot and compares required current-date job types against `DeliveryJobRun` plus a unique `(BusinessDateEgypt, JobType)` `DeliveryRecoveryDispatch`. It atomically claims missing work through Repository + Unit of Work and calls an infrastructure-neutral Core enqueue contract. An APIs-layer Hangfire adapter enqueues the existing `IDeliveryExpiryService.RunAsync` job first and, when injection is eligible, `IDailyDeliveryInjectorService.RunAsync` as its continuation; it enqueues injection directly only when expiry already completed. Startup never calls either business service or mutates delivery, queue, wallet, transaction, or ledger state. Reconciliation is at-least-once across a crash between claim persistence and Hangfire acknowledgement, while the same idempotent job services protect business effects.

**Alternatives considered**:

- Execute expiry/injection inline at startup: rejected because startup must remain orchestration-only and must not hold business transactions.
- Rely on the next recurring schedule: rejected because current-date work could remain missing for nearly a day.
- Enqueue without a durable date/job claim: rejected because concurrent multi-host startup would create avoidable duplicate recovery chains.

## Decision: Resolve `Africa/Cairo` With BCL Cross-Platform Fallback

**Rationale**: Use `TimeProvider.GetUtcNow()` and a single validated Cairo `TimeZoneInfo`. First resolve `Africa/Cairo`; if unavailable on a Windows/NLS host, convert the IANA ID to its Windows equivalent and resolve that. Startup validation fails if neither provides a DST-capable Cairo zone. This avoids a new time-zone package while honoring the clarified DST-aware rule.

**Alternatives considered**:

- Fixed UTC+2: rejected by clarification and incorrect during Egyptian daylight saving.
- Host local time: rejected because deployments may run in any zone.
- Add TimeZoneConverter: rejected because .NET 8 exposes the required IANA/Windows conversion APIs and no extra dependency is needed.

**Sources**: [Obtaining a TimeZoneInfo object](https://learn.microsoft.com/en-us/dotnet/standard/datetime/instantiate-time-zone-info), [TimeZoneInfo API](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo)

## Decision: Database Invariants, Not Job Exclusivity, Guarantee Exactly-Once Effects

**Rationale**: Hangfire supports multiple servers and automatic retries, so overlap is normal. Candidate transactions re-read locked state and rely on SQL Server `rowversion`, row locks, delivery uniqueness, and wallet-transaction idempotency uniqueness. A concurrency conflict or duplicate insert causes a fresh-state no-op/retry, never a second balance mutation. Hangfire distributed coordination reduces duplicate scheduling but is not trusted as the financial correctness boundary.

**Alternatives considered**:

- Rely only on a non-concurrent job attribute: rejected because timeouts, lock loss, operator retries, and multiple servers still require idempotent domain operations.
- One global application lock: rejected because it harms availability and conflicts with company-scoped failure isolation.
- Serializable transaction over an entire job: rejected because it creates long locks and prevents independent candidate progress.

**Sources**: [Hangfire multiple server instances](https://docs.hangfire.io/en/latest/background-processing/running-multiple-server-instances.html), [EF Core concurrency handling](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)

## Decision: Use Bounded Automatic Retry and Cooperative Cancellation

**Rationale**: Job service methods accept ordinary `CancellationToken`; Hangfire replaces it at execution and requeues interrupted work after restart. Configure a bounded retry count for cycle-level transient exceptions. Candidate-level validation/conflict failures are counted and isolated rather than thrown through the entire cycle. This gives safe restart behavior without retry storms.

**Alternatives considered**:

- Disable all retries: rejected because transient database and process failures are expected.
- Keep Hangfire's default ten retries without an explicit policy: rejected because financial job retry behavior should be visible and bounded in configuration.
- Swallow every exception: rejected because cycle-level infrastructure failures must remain visible as Failed jobs and job-run outcomes.

**Sources**: [Using cancellation tokens](https://docs.hangfire.io/en/latest/background-methods/using-cancellation-tokens.html), [Dealing with exceptions](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)

## Decision: Derive Company-Scoped Injection Gates From Overdue Active Deliveries

**Rationale**: Before reserving for a candidate, query whether its company still owns any Active/Reserved delivery dated before today. If yes, leave the row Queued and skip it. This naturally blocks a company whose expiry work is running, not reached, or failed, while unrelated companies proceed. It also survives process crashes without a separate checkpoint that could disagree with delivery state.

**Alternatives considered**:

- Global run-success flag: rejected because one corrupt company's delivery would block the whole marketplace.
- Per-company checkpoint only: rejected because a stale checkpoint could say success while an overdue Active delivery remains.
- Proceed after merely attempting expiry: rejected because unreleased reservations may incorrectly deny or permit activation.

## Decision: Lock and Commit One Financial Candidate at a Time

**Rationale**: Expiry locks the delivery and then company wallet. Activation locks the doctor profile, queue row, campaign eligibility, and company wallet before recounting capacity and applying the candidate. Keep lock order consistent and transactions short. Each action changes state, wallet, one transaction, and two ledger entries in one Unit of Work transaction. Candidate isolation satisfies rollback and allows other work to continue.

**Alternatives considered**:

- One transaction per whole job or doctor: rejected because one failure would roll back unrelated candidates and hold locks too long.
- Balance updates outside the delivery transaction: rejected because it permits state/ledger divergence.
- Optimistic checks without wallet row locks: rejected because concurrent doctors can reserve from the same company balance.

## Decision: Use Two Balanced Ledger Entries Per Reserve or Release

**Rationale**: Reserve is Available Debit plus Reserved Credit on one company wallet; Release is Reserved Debit plus Available Credit. One append-only `WalletTransaction` groups the movement and uses `delivery:reserve:{deliveryId}` or `delivery:release:{deliveryId}`. Transaction uniqueness plus atomic persistence makes ledger replay safe.

**Alternatives considered**:

- One ledger entry for a balance transfer: rejected because it does not explain both stored balance changes.
- Two wallet transactions for one transfer: rejected because one business operation should have one idempotency identity and audit grouping.
- Update balances without ledger evidence: rejected by constitution and existing wallet invariants.

## Decision: Round Calculated Fees Away From Zero

**Rationale**: Require exactly one effective policy with `0 < Percent <= 100` at two-decimal precision. Compute `decimal.Round(price * percent / 100m, 2, MidpointRounding.AwayFromZero)` and derive earnings from the rounded fee. Require `0 < Fee < Price` and `DoctorEarnings > 0`. This is deterministic for midpoint cases and matches persisted delivery money constraints. Missing, overlapping, out-of-range, zero-result, or otherwise rounded-invalid active policy snapshots leave the row Queued and fail the candidate without delivery, financial mutation, or fallback.

**Alternatives considered**:

- `MidpointRounding.ToEven`: rejected because midpoint fees can alternate direction and are harder to explain in transaction fixtures.
- Truncate: rejected because the backend plan requires rounding.
- Silently substitute the default fee for invalid policy data: rejected because policy corruption must be visible and must not create invented financial terms.

## Decision: Preserve Temporary Queue Rows and Cancel Terminal Rows

**Rationale**: After locking and rechecking the candidate, cancel rows tied to deleted doctors/companies or cancelled, rejected, completed, or deleted campaigns. Leave rows Queued for paused campaigns, suspended or temporarily unapproved doctors, missing temporary price, insufficient funds, or unresolved company expiry. None consumes daily capacity; scanning continues in FIFO order.

**Alternatives considered**:

- Keep every row forever: rejected because terminal rows create endless rescans.
- Cancel every ineligible row: rejected because recoverable business states would lose approved work.
- Stop at the first blocked FIFO row: rejected because the locked backend rule requires skip-and-continue to fill capacity.

## Decision: Separate Inbox Projection From Signed Asset Grant Creation

**Rationale**: `GET /api/doctor/messages/today` returns keyset pages ordered by persisted activation instant `DeliveredAtUtc`, then id, using `PageSize` 1-100 (default 50), an opaque Doctor/date-scoped cursor, and nullable `NextCursor`. Querying `PageSize + 1` keeps every current-day delivery reachable without offset drift or silent truncation. It returns approved active asset metadata and a delivery-scoped access path. `GET /api/doctor/messages/{deliveryId}/assets/{fileId}/access` performs current Doctor, current Egypt date, delivery ownership, campaign relationship, and Approved file checks before issuing an audited 10-minute signed grant. This prevents N signed-provider calls and N audit writes on every inbox read.

**Alternatives considered**:

- Generate every signed URL in the inbox query: rejected because it harms the one-second target and creates unused grants/audit noise.
- Return raw storage keys: rejected because it leaks provider internals.
- Let any authenticated doctor use the generic owner-only file endpoint: rejected because company-owned campaign assets require delivery-derived authorization.

## Decision: Persist Safe Job-Run Summaries

**Rationale**: Add `DeliveryJobRun` with job type, Egypt business date, UTC timing, outcome, counters, and bounded safe summary. It supplements Hangfire operational state with domain-relevant examined/expired/activated/cancelled/skipped/failed counts. A new run marks stale Running records interrupted. Never persist exception stacks, signed URLs, storage keys, credentials, or financial idempotency keys in summaries.

**Alternatives considered**:

- Use logs only: rejected because structured cycle history and counts are required and logs may have shorter retention.
- Copy Hangfire job payload/state into domain tables: rejected because it couples domain persistence to scheduler internals and may expose serialized arguments.
- Persist one row per successful candidate: rejected because delivery, transaction, and ledger records already provide that evidence.

## Decision: Preserve Campaign-Submission FIFO Explicitly

**Rationale**: Use the existing queue-row `CampaignSubmittedAtUtc` as an immutable snapshot copied from the campaign's authentic `SubmittedAtUtc` at queue creation. Injection keyset order is `CampaignSubmittedAtUtc`, then queue id. `QueuedAtUtc` remains operational enqueue timing and never substitutes for campaign priority. Require the snapshot conditionally whenever status is Queued. Existing Queued rows may be backfilled only from related `Campaign.SubmittedAtUtc`; missing authentic submission data is a migration/data-quality failure rather than permission to invent priority. Historical Activated/Cancelled rows may remain null because those states are terminal and cannot participate in FIFO or return to Queued in Phase 7.

**Alternatives considered**:

- Order by `QueuedAtUtc`: rejected because the constitution mandates campaign-submission FIFO.
- Fall back to `QueuedAtUtc` for legacy nulls: rejected because it silently changes business priority.
- Join mutable campaign timing on every scan: rejected because the queue requires an immutable ordering snapshot and bounded keyset reads.

## Decision: Keep Operator Control Outside the Application HTTP Surface

**Rationale**: Authorized operators inspect `DeliveryJobRun` through infrastructure-managed read-only SQL access and requeue failed persisted jobs through protected Hangfire administration tooling. Platform IAM and database permissions enforce access. Requeue invokes the same persisted service-interface job used by recurring execution. The application maps neither Hangfire Dashboard nor manual job-control HTTP endpoints.

**Alternatives considered**:

- Public or Admin application endpoints: rejected as unnecessary Phase 7 scope and additional attack surface.
- No operator retry path: rejected because recoverability and explicit retry equivalence are required.
- Direct domain-table mutation: rejected because it bypasses service idempotency and Unit of Work invariants.

## Decision: Measure Performance Against One Reproducible Profile

**Rationale**: Performance evidence uses a Release build with Server GC, SQL Server 2022 Testcontainers on the same SSD-backed host, at least 4 dedicated vCPUs and 8 GB available RAM, and no debugger, coverage collector, or parallel workload. Inbox evidence uses 20 warm-ups plus 200 requests at concurrency 10 against a 100-item page and reports p50/p95/p99/query/failure counts. Daily-cycle evidence uses one warm-up plus three clean-data runs over a fixed 1,000-doctor/10,000-candidate distribution. Missing prerequisites cause an explicit skip, not a passing result.

**Alternatives considered**:

- An unspecified “agreed environment”: rejected because results would not be reproducible or comparable.
- Enforce performance tests in every developer run: rejected because ordinary machines may not satisfy the controlled profile.
- Treat skipped tests as evidence: rejected because absence of measurement cannot establish the success criteria.
