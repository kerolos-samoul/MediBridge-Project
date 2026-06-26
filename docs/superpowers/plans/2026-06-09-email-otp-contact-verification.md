# Email OTP Contact Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a hashed, resendable, email-delivered OTP contact verification flow for Doctor and Company registration.

**Architecture:** Controllers stay thin and delegate to `IAuthService`. Services coordinate OTP generation, hashing, email delivery, cooldown, attempts, and audit events through Core interfaces. Repository owns EF Core mappings, migrations, and SQL Server queries for flow lifecycle state.

**Tech Stack:** C#/.NET 8, ASP.NET Core Web API, FluentValidation, EF Core SQL Server, xUnit integration/contract tests.

---

### Task 1: Tests First

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/DoctorRegistrationIntegrationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/CompanyRegistrationIntegrationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/ContactVerificationIntegrationTests.cs`
- Modify: `tests/integration/MediBridge.IntegrationTests/TestHost/ConfiguredWebAppFactory.cs`

- [ ] Add tests proving registration creates hashed email OTP flows and sends override-recipient mail.
- [ ] Add tests proving valid, replayed, expired, invalid, max-attempt, resend, and cooldown OTP behavior.
- [ ] Run focused tests and confirm failures are due to missing OTP implementation.

### Task 2: Core Contracts and Schema

**Files:**
- Modify: `MediBridge.Core/Entities/Identity/ContactVerificationFlow.cs`
- Modify: `MediBridge.Core/Interfaces/Identity/IContactVerificationFlowRepository.cs`
- Modify: `MediBridge.Core/Enums/AuthAuditEventType.cs`
- Modify: `MediBridge.Repository/Configurations/Identity/IdentityLifecycleConfigurations.cs`
- Modify: `MediBridge.Repository/Repositories/Identity/IdentityRepositories.cs`

- [ ] Add lifecycle fields for failed attempts, max-attempt lock, supersession, and last send timestamp.
- [ ] Add repository methods for active flow lookup, user/email lookup, supersession, and cooldown support.
- [ ] Add audit enum values for requested/sent/failed/expired/max-attempt contact verification events.

### Task 3: Service and API Behavior

**Files:**
- Create: `MediBridge.Services/Config/ContactVerificationOptions.cs`
- Create: `MediBridge.Services/Config/SmtpEmailOptions.cs`
- Create: `MediBridge.Services/Interfaces/IEmailSender.cs`
- Create: `MediBridge.Services/DTOs/Auth/RequestContactVerificationDto.cs`
- Create: `MediBridge.Services/DTOs/Auth/EmailMessageDto.cs`
- Create: `MediBridge.Services/Services/SmtpEmailSender.cs`
- Modify: `MediBridge.Services/DTOs/Auth/VerifyContactRequestDto.cs`
- Modify: `MediBridge.Services/Validators/Auth/VerifyContactRequestValidator.cs`
- Modify: `MediBridge.Services/Services/AuthService.cs`
- Modify: `MediBridge.Services/Interfaces/IAuthService.cs`
- Modify: `MediBridge.APIs/Controllers/AuthController.cs`

- [ ] Add options and email abstraction.
- [ ] Implement registration OTP issuance after successful Doctor/Company creation.
- [ ] Implement OTP verification in `VerifyContactAsync`.
- [ ] Implement `RequestContactVerificationAsync` and controller route.

### Task 4: Wiring, Configuration, and Migrations

**Files:**
- Modify: `MediBridge.Services/Extensions/IdentityServiceCollectionExtensions.cs`
- Modify: `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`
- Modify: `MediBridge.APIs/appsettings.json`
- Modify: `MediBridge.APIs/appsettings.Development.json`
- Create: `MediBridge.Repository/Migrations/<timestamp>_EmailOtpContactVerification.cs`

- [ ] Register options and SMTP sender.
- [ ] Add appsettings values requested for testing/graduation use.
- [ ] Generate and inspect EF migration for contact verification lifecycle columns.

### Task 5: Docs, Verification, and Smoke

**Files:**
- Modify: `docs/backend-plan.md`
- Modify: `specs/002-identity-approval/tasks.md`
- Modify: `specs/002-identity-approval/quickstart.md`
- Modify: `specs/002-identity-approval/contracts/identity-approval-api.yaml`
- Modify: `MediBridge.APIs/MediBridge.APIs.http`

- [ ] Update docs and API examples.
- [ ] Run `dotnet build`.
- [ ] Run full `dotnet test`.
- [ ] Run focused auth/contact verification tests.
- [ ] Run manual smoke tests and record request/response/database/email evidence.
