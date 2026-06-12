# Email OTP Contact Verification Design

## Goal

Implement production-quality Email OTP verification for Doctor and Company onboarding while keeping email verification independent from Admin account approval.

## Architecture

Controllers remain HTTP-only and call `IAuthService`. `AuthService` owns registration, OTP issuance, verification, resend/cooldown, and audit decisions through `IIdentityUnitOfWork`, `IAuthTokenService`, and an email sender abstraction. `MediBridge.Repository` owns SQL Server persistence for the extended `ContactVerificationFlow` lifecycle fields and repository queries.

## Data Flow

1. Doctor or Company registration validates input and creates the pending user/profile in the existing transaction.
2. The service issues a six-digit numeric OTP for the registered email, hashes it with the existing token hashing strategy, supersedes prior active email OTP flows for the same user/destination, stores only the hash, and records request/sent audit events.
3. Email delivery targets the registered email at the business layer. Configuration may override physical delivery to `medibridge7@gmail.com`; the message body includes the original registered email.
4. `VerifyContactAsync` validates either a legacy verification token or an Email OTP payload. A valid OTP consumes the flow and sets `EmailVerified = true`; it never changes `AccountStatus`.
5. `POST /api/auth/request-contact-verification` reissues an email OTP for an unverified account, enforces a 60-second cooldown, supersedes the prior active flow, and sends a new email.

## Configuration

`ContactVerification` options:

- `OtpLength = 6`
- `ExpirationMinutes = 10`
- `ResendCooldownSeconds = 60`
- `MaxAttempts = 5`
- `OverrideRecipientEmail = medibridge7@gmail.com`
- `AllowOverrideRecipientEmail = true`

`Email:Smtp` options configure Gmail SMTP host, port, TLS, username, password, and from address. These are appsettings-backed for the graduation/testing environment.

## Failure Handling

Expired, consumed, superseded, wrong-channel, missing-user, invalid, and max-attempt OTPs fail with the existing validation envelope. Invalid OTP attempts increment a counter until the flow is locked. Email send failures are audited and surfaced as service failures for resend; registration persists the account/OTP flow and records the failure so the user can retry through resend without leaving a partial user/profile transaction.

## Testing

Tests cover Doctor and Company registration OTP creation, hash-only storage, length/expiry, override recipient, email body original email, valid verification, replay, expired OTP, invalid attempts, max attempts, resend invalidation, cooldown, approval independence, existing login behavior, password reset, and legacy contact verification token behavior.
