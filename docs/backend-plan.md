# MediBridge Backend Execution Plan (v1.3)

This plan is backend-only and is derived from the agreed MediBridge business rules and
the project constitution (Onion architecture, thin controllers, repository + unit of
work, JWT security, standard API envelope, and global exception handling).

## Locked Decisions (Rules)

### Time Boundary (Egypt)

- Business time is Egypt timezone (UTC+2).
- Day starts at 00:00 and ends at 23:59:59 Egypt time.
- Messages are visible only within their delivery day and expire at midnight.
- Week starts Monday 00:00 Egypt time.

### Payment + Reservation Model

- Billing model is pay-on-interaction. This is the official payment rule for MVP.
- A billable interaction is Accept or Reject.
- Funds are reserved when a message becomes Active (daily delivery), not when a company creates the campaign or queues the message.
- If a message expires without interaction, the reservation is released back to the company.
- If a message expires without interaction: no company charge and no doctor earnings.
- The earlier product idea of immediate deduction on message send is intentionally replaced by the safer reservation model.

### Snapshotting + Rounding

- `PricePerMessage` and `PlatformFeePercent` are snapshotted when delivery becomes Active.
- Platform fee is a configurable percentage set by admin (policy history supported).
- Rounding rules:
  - `Fee = round(Price * FeePercent, 2)` (EGP, 2 decimals)
  - `DoctorEarnings = Price - Fee`
  - Store monetary values as 2-decimal precision EGP.

### Queue + Delivery Semantics

- Queue is per-doctor FIFO (by queued time; tie-break by id) across all campaigns/companies.
- A doctor's daily message limit applies across all companies combined, not per-company.
- Daily delivery enforces `DailyMessageLimit` per doctor.
- If reservation fails during daily injector due to insufficient company funds:
  - Skip that queued item and continue scanning next FIFO item to fill the doctor’s daily limit.
- **Doctor Default Price**: Doctors with no `PricePerMessage` set (null or 0) are excluded from company filtering, campaign targeting, and queue injection. Admin must set the price before the doctor can receive any messages.

### Weekly Enforcement + Activity Score

- Doctors must meet a minimum weekly interaction threshold. "Weekly requirement" means Accept + Reject interactions, not merely delivered or opened messages.
- Violations tracked in a rolling window (last 8 weeks).
- Violations 1–5: Warning.
- After repeated violations: admin may reduce daily limit and/or suspend temporarily.
- **Automated Activity Score**: The Activity Score (0–100) is auto-computed by the system daily using a rolling 30-day window:
  - `ActivityScore = 0.4 * ResponseSpeedScore + 0.3 * EngagementScore + 0.3 * FeedbackScore`
  - *ResponseSpeedScore*: Average of `(1 - ResponseTime / 24 hours) * 100` for all interacted messages (Accept/Reject) within the window.
  - *EngagementScore*: `(Interacted Messages / Total Delivered Messages) * 100` within the window.
  - *FeedbackScore*: `(Interactions with Feedback / Total Interacted Messages) * 100` within the window. Feedback must be >= 15 characters to be counted.
  - *Defaults*: New doctors start with a default `ActivityScore = 95` until they receive their first delivery. If they receive messages but ignore them (0 interactions), all sub-scores for that window are `0` and the score decays.

### Refresh Token Strategy

- Refresh tokens are stored in the database with an expiration (default 7 days).
- Refresh tokens are single-use and rotated on each refresh request (rotated token is revoked, new token issued).
- Refresh tokens are fully revoked on user logout, password change, or admin suspension.

### Wallet & Transaction Validations

- **Company Top-up Limit**: Minimum top-up amount is 100 EGP (configurable).
- **Doctor Withdrawal Limit**: Minimum withdrawal amount is 50 EGP (configurable).
- **Withdrawal Cooldown**: Doctor cannot submit a new withdrawal request while a previous request is in `Requested` or `Approved` status.
- **Balance Integrity**: Withdrawal amount cannot exceed the doctor's `AvailableBalance`.
- **Ledger Requirement**: Wallet balances are stored for fast reads, but every balance mutation must also create an append-only `WalletTransaction` record in the same database transaction.
- **Idempotency Keys**: Top-up, withdrawal, reservation, release, charge, earn, and payout operations must use operation-level idempotency keys to prevent duplicate financial effects during retries.

### Global Application Rules

- **Standard Pagination**: All collection/list endpoints support standard pagination query parameters (`PageNumber` and `PageSize`). Default `PageSize` is 20; maximum `PageSize` is 100.
- **Soft-Delete Strategy**: All entities containing financial history or audit trails (Doctor, Company, Campaign, Wallet) must utilize soft-delete (`IsDeleted` and `DeletedAtUtc`). Hard-deletion is strictly forbidden for audit-trail preservation.
- **Ownership Authorization**: Doctor endpoints must only expose the authenticated doctor's own deliveries, wallet, and settings. Company endpoints must only expose the authenticated company's own campaigns, wallet, analytics, and feedback. Admin endpoints may access all records according to role policy.
- **Pharmaceutical Compliance Placeholder**: Promotional medicine content, claims, clinical research references, and company/doctor verification rules must be reviewed against applicable legal and regulatory requirements before production launch.

### Security + File Handling Rules

- Verification documents, campaign media, voice notes, and clinical research attachments are stored through a backend storage abstraction, not as raw local filesystem paths exposed to clients.
- File metadata must include `OwnerType`, `OwnerId`, `Purpose`, `OriginalFileName`, `ContentType`, `SizeBytes`, `StorageKey`, `CreatedAtUtc`, and optional review status.
- Allowed file types and maximum sizes must be configurable per purpose:
  - Doctor/company verification documents.
  - Campaign images/videos.
  - Voice notes.
  - Clinical research attachments.
- Private documents and protected campaign assets are served through authorized backend endpoints or signed URLs; verification documents must never be public.
- Add a malware/virus scanning placeholder before a file can become approved or visible.
- Add rate limiting for login, registration, refresh, top-up, withdrawal, and interaction endpoints.
- Add refresh-token reuse detection; if a revoked refresh token is reused, revoke the user's active refresh-token family.
- Add password reset and email/phone verification support as security infrastructure. Email verification uses a six-digit, hashed, 10-minute OTP for Doctor and Company registration; development/testing delivery is redirected to `medibridge7@gmail.com` while the email body identifies the originally registered address.
- Admin actions, authentication-sensitive events, financial events, and document review actions must be audit logged.

## Architecture (Backend)

Target Onion Architecture:

- `MediBridge.Core`: entities, enums, interfaces, domain rules (no EF, no HTTP)
- `MediBridge.Repository`: EF Core + SQL Server persistence, migrations, repo + UoW
- `MediBridge.Services`: use-cases/business orchestration, DTOs, mapping
- `MediBridge.APIs`: controllers (HTTP-only), middleware, DI wiring

Non-negotiables:

- Controllers do not contain business logic.
- Persistence only via repositories + unit of work.
- All secured endpoints use JWT + role-aware authorization.
- All responses use the standard envelope:
  - `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`
- Global exception middleware wraps errors; no raw stack traces returned.

## Core Data Model (Minimum)

### Identity

- `ApplicationUser` (Identity)
  - `Role`: Doctor | Company | Admin
  - `IsApproved`: bool
  - `CreatedAtUtc`
  - Soft-delete: `IsDeleted`, `DeletedAtUtc`

- `RefreshToken`
  - `Token` (string, unique constraint)
  - `UserId` (FK to ApplicationUser)
  - `ExpiresAtUtc`
  - `IsRevoked`: bool
  - `CreatedAtUtc`
  - `ReplacedByToken` (string, optional, for rotation tracing)

### Domain

- `Doctor`
  - `Specialization`, `ExperienceYears`, `Location`
  - `DailyMessageLimit` (Admin-controlled preference)
  - `MinimumWeeklyRequirement` (Admin-controlled preference)
  - Optional doctor-requested preferences: `RequestedDailyMessageLimit`, `RequestedMinimumWeeklyRequirement` (admin review only; not automatically enforced)
  - `ActivityScore` (0-100, system-computed daily)
  - `Status`: Active | Warned | Suspended
  - `PricePerMessage` (admin-controlled; nullable/0 means inactive/cannot receive ads)
  - Soft-delete: `IsDeleted`, `DeletedAtUtc`

- `Company`
  - `Name`, `LicenseNumber`, verification metadata
  - Soft-delete: `IsDeleted`, `DeletedAtUtc`

- `Campaign` / `Advertisement`
  - `CompanyId`
  - Title
  - MediaUrl (image/video)
  - VoiceNoteUrl (optional)
  - ClinicalResearchInfo
  - Description
  - Status: Draft | PendingReview | Approved | Rejected | Active | Paused | Completed | Cancelled
  - CreatedAtUtc
  - Soft-delete: `IsDeleted`, `DeletedAtUtc`

- `CampaignTarget`
  - `CampaignId`, `DoctorId`
  - Snapshot fields used for targeting: specialization, experience, location, activity score, price
  - `CreatedAtUtc`

- `CampaignReviewHistory`
  - `CampaignId`, `AdminUserId`
  - `Decision`: Approved | Rejected | ChangesRequested
  - `Reason` / `Notes`
  - `CreatedAtUtc`

### Messaging + Queue

- `DoctorMessageQueue`
  - `DoctorId`, `CampaignId` (or AdvertisementId)
  - `QueuedAtUtc`
  - `Status`: Queued | Activated | Cancelled
  - Ordering key: `(DoctorId, QueuedAtUtc, Id)`

- `DoctorAdDelivery`
  - `DoctorId`, `CampaignId`
  - `CompanyId` (denormalized from campaign for reporting and ownership checks)
  - `DeliveryDateEgypt` (date-only)
  - `DeliveredAtUtc`
  - `ReadAtUtc` / `ViewedAtUtc` (nullable; tracks opening separately from Accept/Reject)
  - `Status`: Active | Accepted | Rejected | Expired
  - `InteractedAtUtc` (nullable)
  - `FeedbackText` (nullable)
  - `FeedbackCreatedAtUtc` (nullable)
  - `FeedbackQualityStatus` (optional; Pending | Accepted | Flagged)
  - `PricePerMessageSnapshot` (decimal(18,2))
  - `PlatformFeePercentSnapshot` (decimal)
  - `PlatformFeeAmount` (decimal(18,2))
  - `DoctorEarnings` (decimal(18,2))
  - Concurrency control: `ConcurrencyToken` (rowversion / guid for optimistic locking)
  - Reservation fields:
    - `ReservedAmount` (decimal(18,2))
    - `ReservationStatus`: Reserved | Released | Charged

### Wallet

Wallet balances are stored for fast reads, and every mutation must be mirrored by an
append-only `WalletTransaction` in the same database transaction.

- `Wallet`
  - `OwnerType`: Doctor | Company
  - `OwnerId`
  - `AvailableBalance` (decimal(18,2))
  - `ReservedBalance` (decimal(18,2))
  - Soft-delete: `IsDeleted`, `DeletedAtUtc`

- `WalletTransaction`
  - `WalletId`
  - `Type`: TopUp | Reserve | Release | Charge | Earn | WithdrawRequest | WithdrawDecision | Payout
  - `Amount` (decimal(18,2))
  - `RelatedDeliveryId` (nullable)
  - `IdempotencyKey` (unique per operation scope)
  - `CreatedAtUtc`
  - Description/metadata

- `WithdrawalRequest`
  - `DoctorId`, `Amount`
  - `Status`: Requested | Approved | Rejected | Paid | Failed
  - Admin approval metadata
  - Concurrency control: `ConcurrencyToken`

### Files / Media

- `StoredFile`
  - `OwnerType`: Doctor | Company | Admin | Campaign
  - `OwnerId`
  - `Purpose`: VerificationDocument | CampaignMedia | VoiceNote | ClinicalResearchAttachment
  - `OriginalFileName`
  - `ContentType`
  - `SizeBytes`
  - `StorageKey`
  - `Visibility`: Private | Protected | Public
  - `ReviewStatus`: Pending | Approved | Rejected
  - `CreatedAtUtc`
  - `ReviewedAtUtc` (nullable)
  - `ReviewedByAdminId` (nullable)

### Audit/Policy/History

- Doctor pricing changes (history)
- Platform fee policy (history, effective dates)
- Doctor Activity Score history (rolling snapshots)
- Admin actions (approvals, suspensions, payout decisions)
- Campaign review history
- Authentication-sensitive events
- File/document review actions
- Financial operation audit trail

### Reporting Read Models / Counters

For company and admin dashboards, expose campaign analytics through queries or read models:

- Delivered count
- Accepted count
- Rejected count
- Expired count
- Feedback count
- Total reserved amount
- Total charged amount
- Total doctor earnings
- Total platform fee

## State Machines (Deterministic)

### Queue Item

- Queued -> Activated (when delivery created and reservation succeeds)
- Queued remains Queued if reservation fails (skip and continue scanning)

### Delivery

- Active -> Accepted (settle)
- Active -> Rejected (settle)
- Active -> Expired (no interaction by day end)

### Company Wallet

- Reserve (activation): Available -= Price, Reserved += Price
- Release (expiry): Reserved -= Price, Available += Price
- Charge (interaction): Reserved -= Price

### Doctor Wallet

- Earn (interaction): Available += (Price - RoundedFee)

## Background Jobs (Hangfire)

All jobs must be idempotent and concurrency-safe.

### Job 2: Expiry Cleaner (00:00 Egypt) — RUNS FIRST
This job must run first at midnight Egypt time to expire yesterday's uninteracted deliveries and release their funds back to the companies' wallets. This ensures that the released balances are available for the Daily Injector to activate new messages.

**Process**:
1. Select yesterday's `Active` deliveries with no interaction (`Status == Active`).
2. Update `Status = Expired` and `ReservationStatus = Released` using optimistic concurrency checking (`ConcurrencyToken` check).
3. Settle transaction in ledger: deduct from Company Wallet `ReservedBalance` and add back to `AvailableBalance`.

### Job 1: Daily Injector (00:05 Egypt) — RUNS SECOND
This job runs second, allowing a 5-minute gap for the Expiry Cleaner to release company funds safely.

**Process**:
For each doctor:
1. Identify today’s `DeliveryDateEgypt` and check if the doctor already has activated deliveries for today.
2. Select FIFO queue items for the doctor and attempt activation until reaching `DailyMessageLimit` (computed globally across all companies' approved campaigns targeting the doctor).
3. For each candidate queue item, ensure no duplicate delivery exists for the same `(DoctorId, DeliveryDateEgypt, CampaignId)` using a database unique constraint.
4. Confirm the campaign is still eligible for delivery (`Approved`/deliverable and not `Paused`, `Cancelled`, `Rejected`, or `Completed`).
5. Attempt reservation against the targeted company's wallet:
   - Compute snapshotted fields: `PricePerMessageSnapshot` (doctor's price) and `PlatformFeePercentSnapshot` (active platform fee percentage).
   - Calculate platform fee (rounded to 2 decimals) and doctor's earnings.
   - If company `AvailableBalance >= PricePerMessageSnapshot`:
     - Create `DoctorAdDelivery` with `Status=Active`, `ReservedAmount=PricePerMessageSnapshot`, and `ReservationStatus=Reserved`.
     - Update Company Wallet: `AvailableBalance -= PricePerMessageSnapshot`, `ReservedBalance += PricePerMessageSnapshot`.
     - Mark `DoctorMessageQueue` item as `Activated`.
   - If reservation fails due to insufficient company balance:
     - Leave the queue item as `Queued` (do not cancel).
     - Skip this item and continue scanning the next FIFO queue item to fill the doctor's daily limit.

### Job 3: Weekly Enforcement (Monday 00:00 Egypt)

1. For each doctor, compute weekly interactions count (Accept + Reject) for the last week.
2. If below the doctor's `MinimumWeeklyRequirement`: record a violation event.
3. Maintain a rolling window count (last 8 weeks) and expose warnings.
4. Admin applies reduce limit / suspension manually after repeated violations.

### Job 4: Daily Activity Score (00:30 Egypt)

This job runs daily to automatically compute and persist the Doctor Activity Score based on rolling 30-day performance data.

**Process**:
1. For each doctor, query delivery records from the last 30 days.
2. If the doctor has 0 deliveries in the last 30 days, default their `ActivityScore = 95` (new doctor safeguard).
3. Otherwise, compute the three sub-scores:
   - **ResponseSpeedScore**: For all interacted deliveries (Accept or Reject) in the 30-day window, average of `(1 - (InteractedAtUtc - DeliveredAtUtc).TotalMinutes / 1440.0) * 100`, clamped between 0 and 100.
   - **EngagementScore**: `(Interacted Deliveries Count / Total Deliveries Count) * 100`.
   - **FeedbackScore**: `(Deliveries with feedback text length >= 15 characters / Interacted Deliveries Count) * 100`.
   - If Total Deliveries Count > 0 and Interacted Count is 0, set `ResponseSpeedScore = 0`, `EngagementScore = 0`, and `FeedbackScore = 0`.
4. Calculate final score: `ActivityScore = 0.4 * ResponseSpeedScore + 0.3 * EngagementScore + 0.3 * FeedbackScore`.
5. Round `ActivityScore` to 1 decimal place and update the `ActivityScore` field on the Doctor entity.
6. Write the snapshot to `ActivityScoreHistory` for audit trails and performance tracking.

## API Contract (Backend)

### Auth

- POST `/api/auth/register-doctor` (multipart; doc upload metadata)
- POST `/api/auth/register-company`
- POST `/api/auth/login` (denies JWT if `IsApproved=false`)
- POST `/api/auth/refresh`
- POST `/api/auth/logout`
- POST `/api/auth/forgot-password`
- POST `/api/auth/reset-password`
- POST `/api/auth/verify-contact` (legacy one-time token or Email OTP with original registered email)
- POST `/api/auth/request-contact-verification` (resend Email OTP with cooldown and old OTP supersession)

### Doctor

- GET `/api/doctor/messages/today`
- PUT `/api/doctor/messages/{deliveryId}/read`
- POST `/api/doctor/messages/{deliveryId}/interact` (Accept/Reject + optional feedback)
- PUT `/api/doctor/settings` (notification preferences only; DailyMessageLimit and MinimumWeeklyRequirement are read-only and managed by Admin)
- POST `/api/doctor/settings/limit-request` (optional request for admin review; does not change enforced limits automatically)
- GET `/api/doctor/wallet`
- POST `/api/doctor/wallet/withdraw`

### Company

- GET `/api/company/doctors` (filters incl. specialization/experience/location/activity/price)
- POST `/api/company/campaigns` (create + target list)
- GET `/api/company/campaigns`
- GET `/api/company/campaigns/{id}`
- GET `/api/company/campaigns/{id}/deliveries`
- GET `/api/company/campaigns/{id}/feedback`
- GET `/api/company/campaigns/{id}/analytics`
- GET `/api/company/wallet`
- POST `/api/company/wallet/topup` (gateway stub acceptable in v1)

### Admin

- GET `/api/admin/pending-accounts`
- PUT `/api/admin/accounts/{id}/approve`
- GET `/api/admin/campaigns/pending-review`
- PUT `/api/admin/campaigns/{id}/review`
- PUT `/api/admin/doctors/{id}/price`
- PUT `/api/admin/platform-fee` (policy update)
- GET `/api/admin/violations`
- PUT `/api/admin/doctors/{id}/status` (reduce limit / suspend / activate)
- GET `/api/admin/statistics`

### Files

- POST `/api/files` (authorized upload; purpose-specific validation)
- GET `/api/files/{id}` (authorized retrieval or signed URL handoff)
- PUT `/api/admin/files/{id}/review` (verification/document/media review decision)

## Implementation Milestones (Execution Order)

1. Foundation: solution split + response envelope + exception middleware
2. Identity/JWT/roles + approval gate + refresh token strategy
3. EF Core schema + migrations + repositories/UoW + soft-delete/concurrency token
4. File storage abstraction + verification document/media metadata + security audit plumbing
5. Doctor filtering + campaign creation + queue insert + wallet validations
6. Campaign review/moderation workflow before activation
7. Hangfire setup + expiry cleaner at 00:00 Egypt + daily injector at 00:05 Egypt
8. Interaction endpoint + read tracking + settlement (reserve -> charge/earn) with idempotency
9. Weekly enforcement (rolling 8 weeks) + daily activity score job (Job 4)
10. Company campaign reporting + feedback + analytics endpoints
11. Admin tooling (approvals, pricing, fee policy, campaign review, enforcement actions, withdrawals)
12. Testing: money correctness + queue determinism + job idempotency + authorization ownership

## Implementation Phases (Step-by-Step)

This section breaks down the execution order into developer-friendly phases. It does
not change any rules or decisions; it references earlier sections for authoritative
details.

### Phase 1: Foundation & Setup

Objective: Establish the solution structure, API standards, and cross-cutting middleware
required by the constitution.

Detailed tasks:

- Create the solution and 4 projects (`MediBridge.Core`, `MediBridge.Repository`,
  `MediBridge.Services`, `MediBridge.APIs`) and wire project references inward-only.
- Add the standard response envelope contract and ensure all controllers return it.
- Implement global exception middleware that returns the standard envelope and does not
  leak raw stack traces.
- Add request logging and correlation id plumbing.
- Configure Swagger for Development only.
- Add configuration scaffolding for SQL Server connection and JWT settings.
- Add rate limiting, audit logging interfaces, and current-user/ownership helper abstractions.

Related components:

- See: Architecture (Backend)
- See: Non-negotiables (standard envelope + global exception middleware)

Exit criteria (Definition of Done):

- A sample endpoint returns `{ Code, Message, Data }` envelope.
- An unhandled exception returns the standard envelope with a safe message.
- Project references enforce Core <- Repository <- Services <- APIs layering.

### Phase 2: Identity & Approval

Objective: Implement secure access (JWT + roles) and the approval gate for Doctor and
Company accounts.

Detailed tasks:

- Add ASP.NET Core Identity integration and persistence wiring.
- Implement JWT issuance and validation.
- Define roles: Admin, Doctor, Company.
- Implement doctor registration with verification document metadata capture.
- Implement company registration with company info and verification metadata.
- Default `IsApproved=false` on registration.
- Deny JWT issuance during login when `IsApproved=false`.
- Enforce role-aware authorization on secured endpoints.
- Implement refresh token rotation, logout revocation, password reset scaffolding, email/phone verification stubs, and refresh-token reuse detection.

Related components:

- Entities: `ApplicationUser` (Identity)
- APIs: Auth endpoints in API Contract (Backend)

Exit criteria (Definition of Done):

- Pending Doctor/Company accounts cannot obtain JWT tokens.
- Role-based authorization is enforced using JWT on secured routes.
- Admin account exists (seeded or created) to perform approvals.

### Phase 3: Database & Core Models

Objective: Implement the EF Core model, migrations, repositories, and unit-of-work for
the minimum domain required to support queueing, delivery, and wallet ledger semantics.

Detailed tasks:

- Implement Core entities and enums from Core Data Model (Minimum).
- Define repository interfaces in `MediBridge.Core`.
- Implement `DbContext`, entity configurations, and migrations in `MediBridge.Repository`.
- Implement repository implementations and Unit of Work.
- Implement wallet ledger primitives (see Core Data Model + State Machines).
- Add audit/policy tables for price changes, platform fee policy history, campaign review, file review, authentication-sensitive events, and admin actions.
- Add indexes/constraints:
  - Queue ordering: `(DoctorId, Status, QueuedAtUtc, Id)`.
  - Delivery uniqueness: `(DoctorId, DeliveryDateEgypt, CampaignId)`.
  - Campaign reporting: `(CompanyId, CreatedAtUtc)`.
  - Wallet ledger browsing: `(WalletId, CreatedAtUtc)`.
  - Wallet transaction idempotency: unique `IdempotencyKey` per operation scope.

Related components:

- Entities: Doctor, Company, Campaign/Advertisement, DoctorMessageQueue, DoctorAdDelivery,
  Wallet, WalletTransaction, WithdrawalRequest
- See: State Machines (Deterministic)

Exit criteria (Definition of Done):

- Initial migration applies cleanly against SQL Server.
- Services can persist and query core entities only through repositories + unit of work.
- Money fields are stored with 2-decimal precision (EGP) and transaction types are represented.
- Core uniqueness, ownership, concurrency, and idempotency constraints exist in the database model.

### Phase 4: File Storage, Verification & Security Plumbing

Objective: Provide backend-controlled storage and review metadata for verification documents,
campaign media, voice notes, and clinical research attachments.

Detailed tasks:

- Implement file metadata entity and storage abstraction.
- Validate content type, size, purpose, and owner.
- Store private verification files separately from campaign-visible assets.
- Serve protected files only through authorized endpoints or signed URL handoff.
- Add malware/virus scanning placeholder and admin file review workflow.
- Audit upload, review, and access events for sensitive files.

Related components:

- Entities: File metadata, file review history, admin audit logs
- APIs: Files endpoints in API Contract (Backend)

Exit criteria (Definition of Done):

- Users can upload purpose-specific files through controlled backend endpoints.
- Verification documents are private and admin-reviewable.
- Campaign assets cannot become visible until validation/review rules pass.

### Phase 5: Campaign & Queue

Objective: Enable companies to target doctors and create campaigns that enqueue doctor-specific
messages for the next daily injection.

Implemented Phase 5 slice:

- Company doctor search is available at `GET /api/company/doctors` with specialization,
  experience, location, activity score, price filters, standard pagination, and deterministic
  ordering by activity score descending, price ascending, then stable identifier ascending.
- Campaign submission is available at `POST /api/company/campaigns` for approved company
  users, requires an `Idempotency-Key`, required content, at least one approved campaign
  asset, and 1-100 unique eligible target doctors.
- Accepted campaign submissions are saved as `PendingReview`, persist immutable
  `CampaignTarget` snapshots, and create no queue rows before approval.
- Campaign list/detail are available at `GET /api/company/campaigns` and
  `GET /api/company/campaigns/{id}` for the owning company only.
- Approved-campaign queue creation exists as trusted service behavior and creates
  retry-safe `DoctorMessageQueue` rows per still-eligible target with FIFO ordering keys.
- Company wallet query and MVP stub top-up are available at `GET /api/company/wallet` and
  `POST /api/company/wallet/topup`; top-up credits available balance only, creates an
  append-only `TopUp` transaction plus available-balance ledger entry, and uses idempotency
  to prevent duplicate financial effects.
- Phase 5 scope excludes daily injector jobs, expiry jobs, doctor inbox/read/interact,
  settlement, reporting analytics, withdrawals, weekly enforcement, activity score jobs,
  and production payment gateway integration.

Related components:

- Entities: `Campaign`/`Advertisement`, `DoctorMessageQueue`, `Company`, `Wallet`, `WalletTransaction`
- APIs: Company endpoints in API Contract (Backend)

Exit criteria (Definition of Done):

- A company can filter and select doctors, then create a campaign for admin review.
- Approved campaigns produce queued items.
- Queue items are stored per doctor with deterministic FIFO ordering.
- Company wallet can be topped up (stub) and queried.

### Phase 6: Campaign Review & Moderation

Objective: Ensure pharmaceutical promotional content is reviewed by admin before delivery.

Detailed tasks:

- Implement admin pending campaign list.
- Implement campaign review decision endpoint.
- Record review decision, reviewer, notes, and timestamp.
- Allow rejected campaigns to remain visible to the owning company with rejection reason.
- Only `Approved` campaigns can enqueue messages and later become `Active`.

Related components:

- Entities: Campaign, CampaignReviewHistory, AdminActionLog
- APIs: Admin campaign review endpoints

Exit criteria (Definition of Done):

- No campaign can be delivered before approval.
- Companies can see campaign status and review outcome.
- Admin review decisions are auditable.

### Phase 7: Delivery & Expiry Jobs

Objective: Implement the daily activation pipeline and expiry cleanup in Egypt time, including
reservation of funds at activation.

Detailed tasks:

- Configure Hangfire (storage, server, dashboard access policy if used).
- Schedule the Expiry Cleaner to run at 00:00 Egypt time.
- Schedule the Daily Injector to run at 00:05 Egypt time.
- Implement Job 1 Daily Injector per Background Jobs (Hangfire).
- Implement Job 2 Expiry Cleaner per Background Jobs (Hangfire).
- Implement doctor “today inbox” endpoint using `DeliveryDateEgypt`.
- Add idempotency guarantees for both jobs (no duplicate activation, reservation, expiry, or release).

Related components:

- Jobs: Job 1 Daily Injector, Job 2 Expiry Cleaner
- Entities: `DoctorMessageQueue`, `DoctorAdDelivery`, `Wallet`, `WalletTransaction`
- APIs: Doctor today messages endpoint

Exit criteria (Definition of Done):

- At 00:00 Egypt, yesterday's uninteracted deliveries expire and release reserved funds.
- At 00:05 Egypt, new deliveries become Active up to the doctor’s daily limit.
- Activation reserves company funds and records transactions consistently.
- At midnight, un-interacted Active deliveries expire and release reserved funds.
- Doctor can only see messages for the current Egypt day.

### Phase 8: Interaction & Payments

Objective: Implement exactly-once settlement on doctor interaction (Accept/Reject) that converts
reservations into final charges and doctor earnings.

Detailed tasks:

- Implement read/open tracking endpoint using `ReadAtUtc` without settling payment.
- Implement doctor interaction endpoint (Accept/Reject + optional feedback).
- Implement settlement per Locked Decisions (Rules), Snapshotting + Rounding, and State Machines.
- Implement idempotency for interaction retries (no double charge, no double earn).

Related components:

- Entity: `DoctorAdDelivery` (Active -> Accepted/Rejected)
- Wallet: Reserve/Charge/Earn transactions
- API: POST `/api/doctor/messages/{deliveryId}/interact`

Exit criteria (Definition of Done):

- Opening/reading a message is tracked but does not charge the company or earn money for the doctor.
- Accept/Reject triggers exactly one charge and exactly one earn.
- Fee is calculated first and rounded to 2 decimals; earnings use `Price - RoundedFee`.
- Expired deliveries cannot be settled.

### Phase 9: Activity & Weekly Enforcement

Objective: Implement system-computed activity scoring (Job 4) and weekly minimum enforcement (Job 3) with a rolling 8-week violations window.

Detailed tasks:

- Implement Job 4 Daily Activity Score per Background Jobs (Hangfire) and Locked Decisions.
- Implement Job 3 Weekly Enforcement per Background Jobs (Hangfire) and Locked Decisions.
- Implement admin-visible endpoints for violations and doctor status changes (warn/reduce/suspend).

Related components:

- Jobs: Job 3 Weekly Enforcement, Job 4 Daily Activity Score
- Entities: Doctor, violation events/audit records, activity score history
- APIs: Admin violations endpoint, admin doctor status endpoint

Exit criteria (Definition of Done):

- Activity score is auto-calculated daily and stored with historical logs.
- Activity score is usable for company-side doctor filtering.
- Weekly violations are tracked for the last 8 weeks with warnings 1–5.
- Admin can review violations and apply reduce limit / suspension actions with audit.

### Phase 10: Company Reporting & Analytics

Objective: Allow companies to see campaign results, interactions, feedback, and spending.

Detailed tasks:

- Implement company campaign list/detail endpoints.
- Implement delivery, feedback, and analytics endpoints for each campaign.
- Enforce ownership checks so companies can only access their own campaign data.
- Include counters for delivered, accepted, rejected, expired, feedback count, spend, doctor earnings, and platform fee.
- Add pagination to deliveries and feedback.

Related components:

- Entities: Campaign, DoctorAdDelivery, WalletTransaction
- APIs: Company campaign reporting endpoints

Exit criteria (Definition of Done):

- Companies can monitor campaign performance and interaction results.
- Company analytics match wallet ledger totals.
- Ownership authorization is enforced on all reporting endpoints.

### Phase 11: Admin Tools

Objective: Provide admin controls for approvals, pricing, fee policy, enforcement, and payouts.

Detailed tasks:

- Implement account approval workflow for doctor/company verification.
- Implement campaign review and moderation workflow.
- Implement doctor price management with history/audit.
- Implement platform fee policy update endpoint (history/audit; snapshot applied at activation).
- Implement payout management (withdrawal request + admin decision + payout stub + status tracking).
- Implement system statistics endpoint.

Related components:

- APIs: Admin endpoints in API Contract (Backend)
- Entities: Pricing policy history, platform fee policy history, withdrawal requests, admin audit logs

Exit criteria (Definition of Done):

- Admin can approve accounts and manage pricing and platform fee policy.
- Admin can review campaigns and protected files before they become active/visible.
- Withdrawal requests can be reviewed and approved/rejected with audit.
- Enforcement actions (reduce limit/suspend/activate) are recorded and take effect.

### Phase 12: Testing & Quality

Objective: Prove money correctness, queue determinism, and job idempotency under retries.

Detailed tasks:

- Implement unit tests listed in Testing Requirements (Minimum to Ship Safely).
- Implement integration tests listed in Testing Requirements (Minimum to Ship Safely).
- Add test utilities for Egypt-time boundaries (day and week windows).
- Add fixtures for wallets and deliveries to validate rounding and ledger behavior.
- Add authorization ownership tests for doctor, company, and admin endpoints.
- Add campaign review tests proving unapproved campaigns cannot be delivered.
- Add file access tests proving private verification files are not public.
- Run automated tests as a build gate.

Related components:

- See: Testing Requirements (Minimum to Ship Safely)

Exit criteria (Definition of Done):

- Unit tests cover reservation/release/charge invariants, rounding, injector skip behavior, and expiry.
- Integration tests cover approval gate, injector idempotency, interact idempotency, and day boundary.
- Test suite is repeatable and passes on CI/build machine.

## Testing Requirements (Minimum to Ship Safely)

Unit tests:

- Reservation/Release/Charge invariants
- Fee rounding behavior
- Exactly-once settlement for interaction
- Injector behavior when reservation fails (skip and continue)
- Expiry releases reserved funds
- Activity score when delivered messages have zero interactions
- Campaign status transitions and review rules
- Wallet transaction idempotency key behavior

Integration tests:

- Auth approval gate (cannot login/get token while pending)
- Daily injector idempotency (no duplicate deliveries/reservations)
- Interact endpoint idempotency (no double charge/earn)
- Today inbox day-boundary correctness (Egypt date)
- Expiry at 00:00 Egypt runs before injection at 00:05 Egypt
- Company cannot access another company's campaigns, feedback, analytics, or wallet
- Doctor cannot read or interact with another doctor's delivery
- Unapproved campaigns cannot be injected or delivered
- Private verification files require authorized access
- Refresh-token reuse detection revokes the active token family

## Future Considerations

### Advanced Product Features

- AI summary for messages and clinical research content.
- Advanced analytics for companies.
- Priority messages with explicit fairness and pricing rules.
- Mobile application push notifications.
- Recommendation system for matching campaigns to doctors.

### Notification Hooks (Phase 7+)

Although not part of the initial core backend MVP, the system architecture should support event hooks to dispatch notifications. Initially, these hooks will be exposed as domain events within `MediBridge.Core`, allowing future background processors to handle them:
- **Doctor Notifications**:
  - "You have new active daily messages to review" (triggered at `00:05 Egypt time` by Job 1).
  - "A withdrawal request status has changed" (triggered when Admin decides a payout).
- **Company Notifications**:
  - "A campaign message was Accepted/Rejected by the doctor" (triggered on interaction).
- **Admin Notifications**:
  - "A new withdrawal request is pending review" (triggered on doctor withdrawal submit).
  - "A new doctor/company account is pending verification approval" (triggered on registration).
