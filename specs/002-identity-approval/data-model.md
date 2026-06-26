# Data Model: Identity and Approval (Phase 2)

## Entities

## Persistence Direction

All Phase 2 entities are designed for SQL Server persistence through Entity Framework Core in `MediBridge.Repository`. `MediBridge.Core` defines pure domain entities and contracts only; EF Core mappings, Identity store types, DbContext configuration, migrations, and SQL Server-specific details belong in `MediBridge.Repository`.

### ApplicationUser

Pure domain identity record. This entity must not inherit from ASP.NET Identity framework types; infrastructure-specific Identity user records live in the Repository layer and map to this domain model.

**Fields**:

- `Id`: unique user identifier
- `Email`: required, unique when provided
- `PhoneNumber`: optional, unique when provided
- `Role`: `Admin`, `Doctor`, or `Company`
- `AccountStatus`: `Pending`, `Approved`, `Rejected`, `Suspended`, or `Inactive`
- `EmailVerified`: boolean
- `PhoneVerified`: boolean
- `CreatedAtUtc`: creation timestamp
- `ApprovedAtUtc`: nullable approval timestamp
- `LastStatusChangedAtUtc`: nullable status-change timestamp
- `IsDeleted`: soft-delete flag
- `DeletedAtUtc`: nullable deletion timestamp

**Relationships**:

- One Doctor user has zero or one `DoctorProfile`.
- One Company user has zero or one `CompanyProfile`.
- One user has many `RefreshCredential`, `PasswordResetFlow`, `ContactVerificationFlow`, `AdminAccountDecision`, `AccountResubmissionToken`, `AccountResubmission`, and `AuthenticationAuditEvent` records.

**Validation Rules**:

- Doctor and Company registrations start with `AccountStatus = Pending`.
- Token issuance is allowed only when `AccountStatus = Approved` and `IsDeleted = false`.
- `Pending`, `Rejected`, `Suspended`, `Inactive`, and soft-deleted users cannot receive access credentials.
- Role cannot change through public registration update/resubmission flows.

### DoctorProfile

Doctor-specific profile captured during registration.

**Fields**:

- `Id`: unique profile identifier
- `UserId`: owning `ApplicationUser`
- `Specialization`: required
- `ExperienceYears`: required non-negative number
- `Location`: required
- `VerificationDocumentType`: required
- `VerificationOriginalFileName`: required
- `VerificationContentType`: required
- `VerificationSizeBytes`: required positive number
- `VerificationReference`: required client-provided reference or placeholder
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp

**Validation Rules**:

- Profile must belong to a user with role `Doctor`.
- Verification metadata does not represent stored file content in Phase 2.

### CompanyProfile

Company-specific profile captured during registration.

**Fields**:

- `Id`: unique profile identifier
- `UserId`: owning `ApplicationUser`
- `CompanyName`: required
- `LicenseNumber`: required, unique
- `ContactName`: required
- `VerificationDocumentType`: required
- `VerificationOriginalFileName`: required
- `VerificationContentType`: required
- `VerificationSizeBytes`: required positive number
- `VerificationReference`: required client-provided reference or placeholder
- `CreatedAtUtc`: creation timestamp
- `UpdatedAtUtc`: nullable update timestamp

**Validation Rules**:

- Profile must belong to a user with role `Company`.
- `LicenseNumber` must not duplicate another active Company profile.

### RefreshCredential

Persisted refresh credential used for single-use session renewal.

**Fields**:

- `Id`: unique credential identifier
- `TokenHash`: required unique token hash
- `UserId`: owning `ApplicationUser`
- `FamilyId`: credential family identifier
- `ExpiresAtUtc`: expiration timestamp, default policy 7 days unless configuration overrides it
- `RevokedAtUtc`: nullable revocation timestamp
- `RevocationReason`: nullable reason such as `Rotated`, `Logout`, `PasswordChanged`, `Suspended`, `ReuseDetected`
- `ReplacedByTokenHash`: nullable replacement token hash
- `CreatedAtUtc`: creation timestamp

**Validation Rules**:

- Refresh credential is valid only when not expired, not revoked, owning user is not deleted, and owning user has `AccountStatus = Approved`.
- Every successful refresh revokes the presented credential and creates a replacement.
- Reuse of an expired/revoked/replaced credential revokes the active credentials in the same family and records an audit event.

### PasswordResetFlow

Time-limited, single-use account recovery flow.

**Fields**:

- `Id`: unique flow identifier
- `UserId`: nullable owning user for matched accounts
- `TokenHash`: required unique token hash for matched accounts
- `ExpiresAtUtc`: expiration timestamp
- `ConsumedAtUtc`: nullable completion timestamp
- `CreatedAtUtc`: creation timestamp
- `RequestCorrelationId`: request trace identifier

**Validation Rules**:

- Completion succeeds only for unexpired, unconsumed flows linked to an eligible user.
- Successful password reset revokes active refresh credentials for the user.
- Initiation response must not reveal whether a submitted contact exists.

### ContactVerificationFlow

Time-limited, single-use email or phone verification flow.

**Fields**:

- `Id`: unique flow identifier
- `UserId`: owning `ApplicationUser`
- `Channel`: `Email` or `Phone`
- `DestinationHash`: hashed destination value
- `TokenHash`: required unique token hash
- `ExpiresAtUtc`: expiration timestamp
- `ConsumedAtUtc`: nullable completion timestamp
- `CreatedAtUtc`: creation timestamp

**Validation Rules**:

- Completion succeeds only once for unexpired flows.
- Completion marks the matching contact channel as verified.
- Incomplete verification does not block Phase 2 token issuance when account status is `Approved`.

### AdminAccountDecision

Admin decision record for approval, rejection, suspension, inactivation, or reactivation.

**Fields**:

- `Id`: unique decision identifier
- `AdminUserId`: Admin actor
- `TargetUserId`: target account
- `Decision`: `Approve`, `Reject`, `Suspend`, `Inactivate`, or `Reactivate`
- `ResultingAccountStatus`: account status after the decision
- `Reason`: required for rejection and suspension
- `Notes`: optional
- `CreatedAtUtc`: decision timestamp

**Validation Rules**:

- Only Admin users can create decision records.
- Rejection and suspension require a reason.
- Approval changes account status to `Approved`.
- Rejection changes account status to `Rejected`.
- Suspension changes account status to `Suspended` and revokes active refresh credentials.
- Reactivation changes eligible accounts to `Approved`.

### AccountResubmission

Corrected metadata submitted by a rejected Doctor or Company account.

**Fields**:

- `Id`: unique resubmission identifier
- `UserId`: owning rejected account
- `SubmittedAtUtc`: submission timestamp
- `UpdatedProfileFields`: summary of changed registration/profile fields
- `UpdatedVerificationMetadata`: corrected verification metadata

**Validation Rules**:

- Allowed only when account status is `Rejected`.
- Requires a valid, unexpired, unconsumed `AccountResubmissionToken`.
- Resubmission changes account status to `Pending`.
- Resubmission does not grant access credentials.

### AccountResubmissionToken

Time-limited, single-use token authorizing a rejected account to submit corrected registration or verification metadata without JWT access.

**Fields**:

- `Id`: unique token identifier
- `UserId`: owning rejected account
- `TokenHash`: required unique token hash
- `ExpiresAtUtc`: expiration timestamp
- `ConsumedAtUtc`: nullable consumption timestamp
- `CreatedAtUtc`: creation timestamp
- `CreatedByAdminDecisionId`: Admin rejection decision that created or enabled this token

**Validation Rules**:

- Token is valid only for the associated user while that user's account status is `Rejected`.
- Token can be consumed exactly once.
- Successful resubmission consumes the token and changes account status to `Pending`.
- Expired or consumed tokens cannot authorize resubmission.

### AuthenticationAuditEvent

Security-relevant audit event for identity and approval workflows.

**Fields**:

- `Id`: unique event identifier
- `EventType`: registration, login success, login denial, refresh, logout, password reset completion, verification completion, refresh reuse detection, Admin decision, or resubmission
- `ActorUserId`: nullable actor
- `TargetUserId`: nullable target
- `Role`: nullable actor/target role
- `Outcome`: success or denial category
- `Reason`: nullable safe reason
- `CorrelationId`: request trace identifier
- `CreatedAtUtc`: event timestamp

**Validation Rules**:

- Authentication-sensitive events must be append-only.
- Audit events must not store passwords, plaintext tokens, request bodies, or response bodies.

## State Transitions

### AccountStatus

```text
Public registration -> Pending
Pending -> Approved       (Admin approval)
Pending -> Rejected       (Admin rejection with reason)
Rejected -> Pending       (Doctor/Company resubmission)
Approved -> Suspended     (Admin suspension)
Suspended -> Approved     (Admin reactivation)
Approved -> Inactive      (Admin/security inactivation)
Inactive -> Approved      (Admin reactivation)
Any status -> Soft-deleted (soft delete only; no hard delete)
```

### RefreshCredential

```text
Issued -> Rotated/Replaced (successful refresh)
Issued -> Revoked         (logout, password reset, suspension, inactivation)
Rotated/Replaced -> ReuseDetected (presented again after rotation)
ReuseDetected -> FamilyRevoked
Expired -> Rejected
```

### PasswordResetFlow and ContactVerificationFlow

```text
Created -> Consumed (valid completion)
Created -> Expired  (time limit elapsed)
Consumed -> Rejected on replay
Expired -> Rejected on completion attempt
```

### AccountResubmissionToken

```text
Created -> Consumed (valid rejected-account resubmission)
Created -> Expired  (time limit elapsed)
Consumed -> Rejected on replay
Expired -> Rejected on resubmission attempt
```
