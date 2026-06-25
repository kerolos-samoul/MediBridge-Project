# Research: Identity and Approval (Phase 2)

## Decision: Use ASP.NET Core Identity as the account store foundation

**Rationale**: The backend plan explicitly calls for Identity integration, and the repository project already includes `Microsoft.AspNetCore.Identity.EntityFrameworkCore`. Identity supplies proven password hashing, user lookup, lockout-compatible account primitives, and extensibility for role claims while still allowing MediBridge-specific fields such as `AccountStatus` and soft-delete state.

**Alternatives considered**:

- Custom user/password tables: rejected because password security and role integration would duplicate framework behavior.
- External identity provider only: rejected for MVP because the backend plan requires local approval gating, refresh-token persistence, and Admin account control.

## Decision: Use `AccountStatus` for lifecycle gating

**Rationale**: Clarification selected one lifecycle value: `Pending`, `Approved`, `Rejected`, `Suspended`, or `Inactive`, plus soft-delete for deletion. This avoids conflicting Boolean flags and makes login, Admin decisions, resubmission, and tests deterministic.

**Alternatives considered**:

- `IsApproved` only: rejected because rejection, suspension, and inactive states need distinct business meaning.
- Multiple flags: rejected because flag combinations can become contradictory.

## Decision: Gate token issuance on `AccountStatus = Approved`, verified email, and not deleted

**Rationale**: The production security baseline requires denial of JWT issuance when unapproved or when a Doctor/Company email remains unverified. This keeps approval and contact verification aligned before marketplace access while preserving the seeded Admin as verified by construction.

**Alternatives considered**:

- Gate only on account status: rejected because it permits approved but unverified Doctor/Company accounts to access secured marketplace capabilities.
- Allow pending login with limited token: rejected because the spec requires pending accounts to receive no access credentials.

## Decision: Store refresh credentials with rotation family tracking

**Rationale**: The backend plan requires refresh tokens stored in the database, default 7-day expiry, single-use rotation, full revocation on logout/password change/suspension, and reuse detection that revokes the active token family. A family identifier plus replacement link makes reuse detection and audit evidence straightforward.

**Alternatives considered**:

- Stateless refresh tokens: rejected because reuse detection and family revocation require persisted state.
- Multi-use refresh tokens: rejected because the backend plan requires single-use rotation.

## Decision: Capture verification metadata only in Phase 2

**Rationale**: Clarification selected metadata-only registration: document type, original file name, content type, size, and client-provided reference or placeholder. This satisfies Admin review context without building file upload/storage/review before the dedicated Phase 4 file-storage work.

**Alternatives considered**:

- Actual file upload during registration: rejected because it would create a temporary storage path outside the Phase 4 file abstraction.
- No verification metadata: rejected because Admin approval needs submitted evidence context.

## Decision: Make rejected-account resubmission return status to `Pending`

**Rationale**: Clarification selected a recoverable rejection path. Resubmission updates corrected registration/verification metadata, records an audit/resubmission event, and keeps token issuance blocked until Admin approval.

**Alternatives considered**:

- Final rejection: rejected because users would have no product path to correct mistakes.
- Metadata edit while staying rejected: rejected because it creates a hidden Admin reopen workflow and unclear review queue behavior.

## Decision: Use provider-stub-ready reset and verification flows

**Rationale**: The backend plan requires password reset and email/phone verification support as security infrastructure even if providers are stubbed. Phase 2 records time-limited, single-use flows and audit events while keeping outbound delivery replaceable later.

**Alternatives considered**:

- Defer reset/verification entirely: rejected because the backend plan names them as Phase 2 security infrastructure.
- Integrate a concrete email/SMS provider now: rejected because provider choice is not necessary to validate Phase 2 backend behavior.

## Decision: Keep account recovery responses non-enumerating

**Rationale**: Public password reset initiation must not reveal whether an email or phone exists. The service can return the same envelope response for accepted recovery initiation regardless of lookup outcome while only creating a reset flow when a matching account is eligible.

**Alternatives considered**:

- Return not-found for unknown accounts: rejected due to account enumeration risk.
- Always create reset records for unknown contacts: rejected because it pollutes data and has no owner.
