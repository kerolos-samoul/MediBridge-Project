# Feature Specification: Identity and Approval (Phase 2)

**Feature Branch**: `[002-identity-approval]`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Phase 2: Identity & Approval in backend plan"

## Clarifications

### Session 2026-05-31

- Q: How should Phase 2 represent account lifecycle beyond initial approval? -> A: Use a single account status: `Pending`, `Approved`, `Rejected`, `Suspended`, `Inactive`, plus soft-delete for deletion.
- Q: What verification document handling belongs in Phase 2 registration? -> A: Capture verification metadata only: document type, original file name, content type, size, and a client-provided reference or placeholder.
- Q: Should contact verification be required before production login token issuance? -> A: Doctor and Company token issuance requires both `AccountStatus = Approved` and completed email verification. The seeded Admin account is exempt because it is created verified.
- Q: Can rejected Doctor or Company accounts resubmit corrected registration information? -> A: Rejected accounts may resubmit corrected registration/verification metadata and return to `Pending`.
- Q: How can rejected accounts resubmit if they cannot receive JWT access tokens? -> A: Use a time-limited, single-use resubmission token issued for the rejected account; resubmission remains public token-based and does not require JWT.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pending Account Registration (Priority: P1)

As a Doctor or Company representative, I can register an account with the information required for later verification so MediBridge can capture new users without allowing unapproved access.

**Why this priority**: Registration is the entry point for both marketplace sides and creates the approval backlog that admins must review before accounts can use secured features.

**Independent Test**: Submit Doctor and Company registration requests, then confirm each account is created with account status `Pending` and cannot access protected product areas.

**Acceptance Scenarios**:

1. **Given** a Doctor provides valid identity, professional profile, contact, credential, and verification metadata containing document type, original file name, content type, size, and a client-provided reference or placeholder, **When** registration is submitted, **Then** a Doctor account and profile are created with account status `Pending`.
2. **Given** a Company representative provides valid account, company, license, contact, and verification metadata containing document type, original file name, content type, size, and a client-provided reference or placeholder, **When** registration is submitted, **Then** a Company account and profile are created with account status `Pending`.
3. **Given** a registration request omits required identity, role, contact, profile, or verification metadata, **When** registration is submitted, **Then** the request is rejected with a clear validation message and no approved account is created.

---

### User Story 2 - Approved Login and Token Lifecycle (Priority: P1)

As an approved Doctor, Company user, or Admin, I can sign in, refresh my session, and sign out securely so only authorized users can access role-appropriate backend capabilities.

**Why this priority**: Secure token access and approval gating are prerequisites for every later phase, including campaigns, wallets, queueing, and admin tools.

**Independent Test**: Attempt login, refresh, logout, password change, and token reuse flows for approved and pending users, then verify access decisions and token state changes.

**Acceptance Scenarios**:

1. **Given** a registered Doctor or Company account has account status `Pending`, **When** the user attempts to log in, **Then** no access token is issued and the user receives an approval-pending response.
2. **Given** a Doctor or Company user with account status `Approved` and `EmailVerified = true` provides valid credentials, **When** login succeeds, **Then** the user receives role-bearing access credentials and a refresh credential suitable for future renewal.
3. **Given** a valid refresh credential is used, **When** the session is refreshed, **Then** the old refresh credential becomes unusable and a replacement refresh credential is issued.
4. **Given** a user logs out, changes password, is soft-deleted, or is suspended by an admin, **When** existing refresh credentials are used afterward, **Then** they are rejected.
5. **Given** a previously revoked refresh credential is reused, **When** reuse is detected, **Then** the user's active refresh-token family is revoked.
6. **Given** a Doctor or Company user has account status `Approved` but `EmailVerified = false`, **When** the user logs in with valid credentials or refreshes a session, **Then** token issuance is denied until email verification is completed.

---

### User Story 3 - Role-Aware Access Control (Priority: P2)

As the platform owner, I need secured routes to enforce Doctor, Company, and Admin roles so users cannot reach capabilities outside their approved role.

**Why this priority**: Role boundaries protect medical professionals, pharmaceutical companies, platform administration, and later financial workflows from unauthorized access.

**Independent Test**: Exercise representative secured routes with anonymous, pending, Doctor, Company, and Admin principals, then confirm only the intended role can proceed.

**Acceptance Scenarios**:

1. **Given** an unauthenticated request targets a secured route, **When** the request is evaluated, **Then** access is denied.
2. **Given** an approved Doctor attempts to access Company-only or Admin-only capabilities, **When** authorization is evaluated, **Then** access is denied.
3. **Given** an approved Company user attempts to access Doctor-only or Admin-only capabilities, **When** authorization is evaluated, **Then** access is denied.
4. **Given** an Admin accesses account-approval capabilities, **When** authorization is evaluated, **Then** access is allowed according to the Admin role.

---

### User Story 4 - Admin Account Approval (Priority: P2)

As an Admin, I can review pending Doctor and Company accounts and approve or reject them with an auditable decision so only verified accounts can participate in MediBridge.

**Why this priority**: The approval gate is the business control that turns registration data into trusted platform access.

**Independent Test**: Review pending accounts as Admin, approve and reject samples, then verify login behavior, account status, decision notes, and audit records.

**Acceptance Scenarios**:

1. **Given** pending Doctor and Company accounts exist, **When** an Admin views pending accounts, **Then** the Admin can see enough submitted profile and verification metadata to make a decision.
2. **Given** an Admin approves a pending account with verified email, **When** the user next logs in with valid credentials, **Then** access credentials can be issued for the approved role.
3. **Given** an Admin rejects a pending account with a reason, **When** the user attempts to log in, **Then** access remains blocked and the decision reason is retained for review.
4. **Given** an Admin approval, rejection, suspension, or reactivation decision occurs, **When** the decision is saved, **Then** the decision is audit logged with actor, target, decision, reason, and time.
5. **Given** a Doctor or Company account has account status `Rejected`, **When** the user resubmits corrected registration or verification metadata with a valid time-limited single-use resubmission token, **Then** the account returns to account status `Pending` for a new Admin decision.
6. **Given** a pending Doctor or Company account has not completed email verification, **When** an Admin attempts to approve the account, **Then** the decision is rejected with HTTP `409` and the account remains `Pending`.

---

### User Story 5 - Account Recovery and Contact Verification Readiness (Priority: P3)

As a registered user, I can start password reset and contact verification flows so the platform has the required security infrastructure before production delivery providers are connected.

**Why this priority**: Recovery and verification are required security infrastructure, but provider-specific delivery can remain stubbed for the MVP.

**Independent Test**: Request password reset and email or phone verification for registered users, then confirm time-limited one-time flows are recorded and cannot be replayed.

**Acceptance Scenarios**:

1. **Given** a registered user requests a password reset, **When** the request is accepted, **Then** a time-limited reset flow is created without revealing whether the account exists to unauthorized observers.
2. **Given** a user completes password reset with a valid unused reset credential, **When** the new password is accepted, **Then** existing refresh credentials for that user are revoked.
3. **Given** a user requests email verification or registration issues an email verification challenge, **When** the request is accepted, **Then** a time-limited verification flow is recorded, delivered through the configured provider when available, and can be completed once.
4. **Given** a Doctor or Company user has account status `Approved` but has not completed email verification, **When** the user logs in with valid credentials, **Then** token issuance is denied until verification is complete.

### Edge Cases

- Duplicate registrations for an existing email, phone, or company license must be rejected without creating competing pending accounts.
- Pending or rejected Doctor and Company accounts must never receive access credentials.
- Rejected, suspended, inactive, or soft-deleted users must not receive new access credentials or refresh existing sessions.
- Refresh credentials must be single-use; replaying a rotated or revoked credential must invalidate the active credential family.
- Registration, login, refresh, password reset, and verification flows must respect the Phase 1 rate-limit categories once applied.
- Admin approval decisions must not upload, store, review, or mutate verification file contents directly; file storage and review remain in the dedicated file-storage phase.
- Account approval must not create wallet, campaign, queue, delivery, pricing, or payout behavior.
- Public account recovery responses must avoid account enumeration.
- Email verification failures or incomplete email verification block Doctor and Company token issuance and block Admin approval until verification succeeds; the seeded Admin remains verified by construction.
- Rejected accounts that resubmit corrected registration or verification metadata must remain blocked from token issuance until an Admin approves the new pending review.
- Rejected-account resubmission tokens must be time-limited, single-use, and invalid after successful resubmission, expiry, or account approval.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow Doctor registration with account credentials, role, contact details, specialization, experience, location, and verification metadata containing document type, original file name, content type, size, and a client-provided reference or placeholder.
- **FR-002**: System MUST allow Company registration with account credentials, role, company name, license number, contact details, and verification metadata containing document type, original file name, content type, size, and a client-provided reference or placeholder.
- **FR-003**: System MUST default all newly registered Doctor and Company accounts to account status `Pending` and prevent them from receiving access credentials until status is `Approved` and email is verified.
- **FR-004**: System MUST create or identify an Admin account that can manage account approval workflows.
- **FR-005**: System MUST authenticate users with valid credentials and issue role-bearing access credentials to Doctor and Company users only when account status is `Approved`, email is verified, and soft-delete state is not deleted. Seeded Admin authentication requires an approved, verified Admin account.
- **FR-006**: System MUST deny login and token issuance for users whose account status is `Pending`, `Rejected`, `Suspended`, or `Inactive`, for users that are soft-deleted, and for Doctor or Company users whose email is not verified.
- **FR-007**: System MUST support roles for Admin, Doctor, and Company and enforce role-aware authorization on secured routes.
- **FR-008**: System MUST support refresh credentials with expiration, single-use rotation, logout revocation, password-change revocation, suspension revocation, and reuse detection that revokes the active credential family.
- **FR-009**: System MUST allow approved users to log out and revoke active refresh credentials for that user session.
- **FR-010**: System MUST support password reset initiation and completion with time-limited, single-use reset flows while preventing public account enumeration.
- **FR-011**: System MUST support email verification initiation, resend, delivery status reporting, and completion through time-limited, single-use verification flows.
- **FR-011A**: System MUST require completed email verification before Doctor or Company token issuance and before Admin approval. The seeded Admin account is exempt because it is created verified.
- **FR-012**: System MUST allow Admin users to list pending Doctor and Company accounts with submitted profile and verification metadata needed for review.
- **FR-013**: System MUST allow Admin users to approve or reject pending Doctor and Company accounts with an optional decision note for approvals and a required reason for rejections.
- **FR-014**: System MUST allow Admin users to suspend and reactivate user accounts when required for security or policy enforcement.
- **FR-014A**: System MUST represent user lifecycle with a single account status value of `Pending`, `Approved`, `Rejected`, `Suspended`, or `Inactive`, plus soft-delete state for deletion.
- **FR-014B**: System MUST allow rejected Doctor and Company accounts to resubmit corrected registration or verification metadata using a time-limited, single-use resubmission token and return account status to `Pending` for a new Admin decision.
- **FR-015**: System MUST record audit events for registration, login success, login denial due to approval/state, refresh, logout, password reset completion, verification completion, refresh-token reuse detection, and Admin account decisions.
- **FR-016**: System MUST validate required registration, login, refresh, password reset, verification, and approval inputs and return clear validation errors through the standard response envelope.
- **FR-017**: System MUST preserve Phase 1 response envelope, safe error handling, metadata-only request logging, correlation behavior, and configured rate-limit categories for sensitive identity flows.
- **FR-018**: System MUST NOT implement campaign, queue, delivery, wallet, settlement, payout, file content upload, file storage, file-storage review, pricing, or platform-fee workflows as part of Phase 2 identity and approval.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-004`, `FR-012` to `FR-014` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`.
  - `FR-005` to `FR-011`, `FR-016`, `FR-017` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`.
  - `FR-015` -> `MediBridge.Core` audit contracts, `MediBridge.Services` orchestration, `MediBridge.Repository` persistence, `MediBridge.APIs` authenticated context.
  - `FR-018` -> all layers enforce Phase 2 scope boundaries.
- **CA-002 Controller Boundary**: Controllers remain HTTP-only; registration, approval, credential issuance, refresh rotation, revocation, verification, and audit decisions are delegated to service-layer orchestration.
- **CA-003 SQL Persistence Boundary**: Phase 2 persistence targets SQL Server through EF Core in `MediBridge.Repository`, behind Repository + Unit of Work contracts defined in `MediBridge.Core`; controllers and services do not depend on EF Core directly.
- **CA-004 Response Contract**: All identity and approval responses use `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` while preserving appropriate HTTP status semantics.
- **CA-005 Error Handling**: Validation and business denials return safe messages, and unexpected errors continue through global exception middleware with no raw stack traces exposed.
- **CA-006 Security**: Secured flows require role-aware authorization for Doctor, Company, and Admin roles, and account approval is required before Doctor or Company access credentials are issued.
- **CA-007 Ambiguity Control**: Queue and wallet behavior is out of scope for Phase 2; no queue or wallet ambiguity is introduced in this spec.
- **CA-008 Queue Determinism**: Not in scope for Phase 2.
- **CA-009 Wallet Determinism**: Not in scope for Phase 2.

### Key Entities *(include if feature involves data)*

- **Application User**: Platform identity record with role, account status (`Pending`, `Approved`, `Rejected`, `Suspended`, or `Inactive`), contact identifiers, creation time, and soft-delete state.
- **Doctor Profile**: Doctor-specific registration profile containing specialization, experience, location, verification metadata fields, and approval-relevant details.
- **Company Profile**: Company-specific registration profile containing company name, license number, verification metadata fields, and approval-relevant details.
- **Verification Metadata**: Registration-time document metadata containing document type, original file name, content type, size, and a client-provided reference or placeholder; it does not represent stored file contents in Phase 2.
- **Refresh Credential**: Session renewal record with token identity, owning user, expiration, revocation state, replacement link, creation time, and family relationship for reuse detection.
- **Password Reset Flow**: Time-limited, single-use account recovery request tied to a user without exposing account existence publicly.
- **Contact Verification Flow**: Time-limited, single-use email or phone verification request ready for later provider delivery.
- **Admin Account Decision**: Approval, rejection, suspension, inactivation, or reactivation decision with actor, target user, resulting account status, reason or note, and decision time.
- **Account Resubmission**: Corrected registration or verification metadata submitted by a rejected Doctor or Company account, changing account status back to `Pending` without granting access.
- **Account Resubmission Token**: Time-limited, single-use token associated with a rejected account that authorizes one corrected resubmission without requiring JWT access.
- **Authentication Audit Event**: Security-relevant event describing identity actions and admin decisions for later monitoring and investigation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sampled Doctor and Company registrations create accounts with account status `Pending`, not approved access.
- **SC-002**: 100% of sampled `Pending`, `Rejected`, `Suspended`, `Inactive`, and soft-deleted users are denied access credential issuance.
- **SC-003**: 100% of sampled Doctor and Company users with account status `Approved`, verified email, and valid credentials can complete login and receive role-bearing access credentials in under 2 minutes from starting the login attempt.
- **SC-004**: 100% of sampled secured-route checks deny anonymous users and users with the wrong role.
- **SC-005**: 100% of sampled refresh attempts rotate refresh credentials, reject reused credentials, and revoke the active credential family when reuse is detected.
- **SC-006**: 100% of sampled logout, password-change, and admin-suspension flows make previously active refresh credentials unusable.
- **SC-007**: 100% of sampled Admin approval and rejection decisions are reflected in the user's next login outcome and have an audit event with actor, target, decision, and timestamp.
- **SC-007A**: 100% of sampled rejected Doctor and Company account resubmissions return the account to `Pending` and continue denying token issuance until Admin approval.
- **SC-008**: 100% of sampled account recovery responses avoid publicly revealing whether an account exists.
- **SC-008A**: 100% of sampled Doctor and Company users with account status `Approved` and incomplete email verification are denied access credentials, and 100% of sampled Admin approval attempts for unverified pending Doctor or Company accounts return `409` while leaving the account `Pending`.
- **SC-009**: 95% of valid registration, login, refresh, logout, password reset, verification, and approval requests complete with user-visible results in under 2 seconds in the QA environment.
- **SC-010**: Architecture checks confirm 0 controller actions contain identity business rules, credential rotation rules, approval decisions, or audit construction logic.
- **SC-011**: Scope validation confirms 0 Phase 2 changes implement campaign, queue, delivery, wallet, settlement, payout, pricing, platform-fee, or file-review workflows.

## Assumptions

- Phase 1 foundation behavior, including the response envelope, safe exception handling, correlation IDs, metadata-only logging, JWT settings scaffolding, rate-limit policy names, current-user context, ownership helpers, and audit abstraction, remains available.
- Registration requires verification metadata only: document type, original file name, content type, size, and a client-provided reference or placeholder. Binary file upload, storage, and review behavior is delivered in the dedicated file-storage phase.
- Admin account creation may be satisfied by seeding or another controlled creation process, as long as an Admin can perform approval decisions before Doctor and Company accounts are approved.
- Contact verification and password reset provider delivery must be safe to replace with real providers, and recorded flows must be time-limited, single-use, and auditable.
- Email verification is a production security gate for Doctor and Company token issuance and Admin approval; the seeded Admin account is created verified.
- The internal role value `Company` is the implementation code for the constitution term `Pharmaceutical Company`; user-facing documentation may display "Pharmaceutical Company".
- Doctor and Company approval uses a single account-level approval gate in this phase; deeper verification-document review can be added in the file-storage phase.
- Account suspension is a security/account-state control in this phase and does not implement later weekly-enforcement policy automation.
- Rejected-account resubmission uses a time-limited, single-use resubmission token, updates review metadata only, and does not bypass Admin approval or grant immediate access.
