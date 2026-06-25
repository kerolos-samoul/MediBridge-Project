# Quickstart: Identity and Approval (Phase 2)

## Prerequisites

- .NET 8 SDK installed.
- SQL Server connection string configured for the API environment.
- JWT settings configured with issuer, audience, signing key, and access-token lifetime.
- Phase 1 foundation remains enabled: response envelope, global exception middleware, correlation IDs, request logging, rate-limit policies, current-user context, ownership helpers, and audit abstraction.

## Build and Test

```powershell
dotnet restore .\MediBridge.slnx
dotnet build .\MediBridge.slnx
dotnet test .\MediBridge.slnx
dotnet format .\MediBridge.slnx --verify-no-changes
```

Expected result:

- Build succeeds.
- Contract tests verify envelope shape for identity endpoints.
- Integration tests verify account status token gate, role authorization, refresh rotation/reuse detection, logout/password/suspension revocation, rejected-account resubmission, and non-enumerating password reset initiation.
- Formatting verification reports no required changes.

## Manual Smoke Flow

1. Start the API in Development.
2. Register a Doctor with valid account, contact, profile, and verification metadata.
3. Confirm response `Data.accountStatus` is `Pending`.
4. Attempt login for the Doctor.
5. Confirm no access token is issued while status is `Pending`.
6. Attempt to approve the unverified Doctor account as Admin.
7. Confirm approval returns conflict (`409`) and the account remains `Pending`.
8. Complete email verification for the Doctor.
9. Login as Admin.
10. List pending accounts with pagination.
11. Approve the verified Doctor account.
12. Login as the Doctor.
13. Confirm access token and refresh token are returned.
14. Refresh the session.
15. Confirm the old refresh token cannot be used again.
16. Logout with the current refresh token.
17. Confirm the logged-out refresh token cannot be used.

## Rejected Resubmission Smoke Flow

1. Register a Company with valid company and verification metadata.
2. Reject the Company account as Admin with a reason.
3. Attempt Company login and confirm token issuance is denied.
4. Submit corrected registration or verification metadata for the rejected account using the time-limited single-use resubmission token issued for that rejection.
5. Confirm account status returns to `Pending`.
6. Confirm token issuance remains denied until Admin approval.

## Password Reset and Contact Verification Smoke Flow

1. Request password reset for a known account and an unknown account.
2. Confirm both initiation responses use the same accepted envelope shape.
3. Complete reset for the known account with a valid reset token.
4. Confirm existing refresh credentials for that user are revoked.
5. Start or resend email verification for a registered account.
6. Complete verification once and confirm replay is rejected.
7. Confirm an approved but unverified Doctor or Company account cannot receive tokens until email verification is complete.

## Out of Scope Guard

During Phase 2 validation, scan source and tests to confirm no implementation of:

- Campaign creation or review.
- Queue insertion, activation, delivery, expiry, or daily limits.
- Wallet balances, ledger entries, settlement, payout, top-up, or platform fees.
- File content upload, storage abstraction, file serving, malware scanning, or file review.

## Phase 2 Performance Smoke

Use the Phase 2 smoke script against a locally running API to record QA latency for identity flows. The default target is 2 seconds per sampled request.

```powershell
.\tests\performance\phase2-identity-smoke.ps1 -BaseUrl "https://localhost:5001"
```

Optional parameters allow Admin approval, rejected resubmission, password reset, and contact verification tokens to be supplied when those one-time credentials are available from the QA fixture or controlled delivery stub.
