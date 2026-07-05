# Bugfix Requirements Document

## Introduction

This bugfix addresses three production blockers discovered after merging feature 008-delivery-expiry-jobs into the local development branch. These issues prevent critical workflows: campaign approval fails with HTTP 500, password reset is completely non-functional, and API documentation lacks essential security metadata. All three must be resolved before production deployment.

**Investigation Status**: 58% complete - workspace structure, migrations, campaign/delivery code paths, auth flow, and Swagger configuration have been inspected. E2E database verification from development-e2e worktree remains pending.

**Scope**: Three P1-P3 blockers with real HTTP/E2E verification required against production-grade local environment (real SQL Server database, real Cloudinary integration).

---

## Bug Analysis

### Current Behavior (Defect)

#### 1. Database Schema Drift (P1)

1.1 WHEN AdminCampaignReviewService.ApproveCampaignAsync executes THEN the system returns HTTP 500 due to missing Phase 7 database objects

1.2 WHEN the application queries DeliveryJobRuns table THEN the system throws SQL error because table does not exist

1.3 WHEN the application queries DeliveryRecoveryDispatches table THEN the system throws SQL error because table does not exist

1.4 WHEN the application accesses DoctorMessageQueues.RowVersion column THEN the system throws SQL error because column does not exist

1.5 WHEN the application accesses DoctorAdDeliveries.ExpiredAtUtc column THEN the system throws SQL error because column does not exist

1.6 WHEN verifying E2E database from development-e2e worktree THEN migration state is unknown (may be out of sync with main database)

#### 2. Password-Reset Token Discarded (P2)

2.1 WHEN AuthService.cs generates a password reset token THEN the system hashes and persists the token but discards the plaintext before sending any email

2.2 WHEN a user requests password reset THEN the system never sends the reset token via email

2.3 WHEN a user attempts to reset their password THEN they cannot complete the flow because they never receive the token

#### 3. OpenAPI/API Discoverability Hardening (P3)

3.1 WHEN API consumers inspect Swagger documentation THEN bearer authentication security scheme is not declared

3.2 WHEN API consumers inspect mutation endpoint documentation THEN required Idempotency-Key header is not marked as required

3.3 WHEN API consumers inspect admin pricing endpoint documentation THEN DoctorProfile ID parameter semantics are unclear

---

### Expected Behavior (Correct)

#### 1. Database Schema Drift (P1)

2.1 WHEN verifying E2E database from development-e2e worktree THEN the system SHALL check if migration 20260702174103_AddPhase7DeliveryExpiryJobs has been applied

2.2 WHEN Phase 7 migration is missing from E2E database THEN the system SHALL apply existing migration 20260702174103_AddPhase7DeliveryExpiryJobs without creating duplicate

2.3 WHEN AdminCampaignReviewService.ApproveCampaignAsync executes after migration THEN the system SHALL return success without HTTP 500

2.4 WHEN the application queries DeliveryJobRuns table after migration THEN the system SHALL execute query successfully

2.5 WHEN the application queries DeliveryRecoveryDispatches table after migration THEN the system SHALL execute query successfully

2.6 WHEN the application accesses DoctorMessageQueues.RowVersion column after migration THEN the system SHALL read column value successfully

2.7 WHEN the application accesses DoctorAdDeliveries.ExpiredAtUtc column after migration THEN the system SHALL read column value successfully

#### 2. Password-Reset Token Discarded (P2)

2.8 WHEN AuthService.cs generates a password reset token THEN the system SHALL persist the hashed token and send the plaintext token via existing IEmailSender/SMTP infrastructure

2.9 WHEN a user requests password reset THEN the system SHALL deliver reset token via email before the token expires

2.10 WHEN a user receives the reset token via email THEN the system SHALL preserve hashing, expiry, and single-use semantics

2.11 WHEN implementing email delivery THEN the system SHALL not leak plaintext tokens in logs, responses, or unintended channels

#### 3. OpenAPI/API Discoverability Hardening (P3)

2.12 WHEN API consumers inspect Swagger documentation THEN bearer authentication security scheme SHALL be declared in OpenAPI specification

2.13 WHEN API consumers inspect mutation endpoint documentation THEN required Idempotency-Key header SHALL be marked as required through operation filters

2.14 WHEN API consumers inspect admin pricing endpoint documentation THEN DoctorProfile ID parameter semantics SHALL be clarified through descriptions or annotations

---

### Unchanged Behavior (Regression Prevention)

#### 1. Database Schema Drift (P1)

3.1 WHEN Phase 7 migration has already been applied to a database THEN the system SHALL CONTINUE TO skip migration application (idempotent behavior)

3.2 WHEN querying pre-Phase-7 database objects (Users, Campaigns, DoctorProfiles, etc.) THEN the system SHALL CONTINUE TO execute queries successfully

3.3 WHEN existing EF Core migrations run THEN the system SHALL CONTINUE TO maintain __EFMigrationsHistory integrity

#### 2. Password-Reset Token Discarded (P2)

3.4 WHEN generating reset tokens THEN the system SHALL CONTINUE TO use secure hashing algorithms (not plaintext storage)

3.5 WHEN validating expired or already-used reset tokens THEN the system SHALL CONTINUE TO reject them

3.6 WHEN user login flow executes THEN the system SHALL CONTINUE TO work without regression

3.7 WHEN existing email infrastructure (IEmailSender/SMTP) is used by other features THEN the system SHALL CONTINUE TO function correctly

#### 3. OpenAPI/API Discoverability Hardening (P3)

3.8 WHEN Swagger UI renders existing endpoints THEN the system SHALL CONTINUE TO display all endpoints correctly

3.9 WHEN API consumers use existing documented endpoints THEN the system SHALL CONTINUE TO accept requests following current patterns

3.10 WHEN bearer tokens are validated at runtime THEN the system SHALL CONTINUE TO enforce authentication without behavior change

3.11 WHEN idempotency keys are validated at runtime THEN the system SHALL CONTINUE TO enforce idempotency without behavior change

---

## Bug Condition Derivation

### P1: Database Schema Drift

**Bug Condition Function:**
```pascal
FUNCTION isBugCondition_P1(dbContext)
  INPUT: dbContext of type ApplicationDbContext
  OUTPUT: boolean
  
  // Returns true when Phase 7 objects are missing
  RETURN NOT EXISTS(dbContext.DeliveryJobRuns) OR
         NOT EXISTS(dbContext.DeliveryRecoveryDispatches) OR
         NOT EXISTS(DoctorMessageQueues.RowVersion) OR
         NOT EXISTS(DoctorAdDeliveries.ExpiredAtUtc)
END FUNCTION
```

**Property Specification (Fix Checking):**
```pascal
// Property: Campaign Approval Succeeds After Migration
FOR ALL dbContext WHERE isBugCondition_P1(dbContext) DO
  Apply_Phase7_Migration(dbContext)
  result ← AdminCampaignReviewService.ApproveCampaignAsync'(dbContext, campaignId)
  ASSERT result.StatusCode = 200 AND NOT result.IsError
END FOR
```

**Preservation Goal:**
```pascal
// Property: Existing Data and Migrations Preserved
FOR ALL dbContext WHERE NOT isBugCondition_P1(dbContext) DO
  ASSERT QueryPrePhase7Tables(dbContext) = QueryPrePhase7Tables'(dbContext)
  ASSERT __EFMigrationsHistory(dbContext) = __EFMigrationsHistory'(dbContext)
END FOR
```

---

### P2: Password-Reset Token Discarded

**Bug Condition Function:**
```pascal
FUNCTION isBugCondition_P2(resetRequest)
  INPUT: resetRequest of type PasswordResetRequest
  OUTPUT: boolean
  
  // Returns true when reset token is generated but not emailed
  RETURN TokenGenerated(resetRequest) AND NOT TokenEmailed(resetRequest)
END FUNCTION
```

**Property Specification (Fix Checking):**
```pascal
// Property: Reset Token Delivered via Email
FOR ALL resetRequest WHERE isBugCondition_P2(resetRequest) DO
  result ← AuthService.RequestPasswordReset'(resetRequest)
  ASSERT EmailSent(result) AND 
         TokenHashed(result.storedToken) AND 
         NOT TokenLeaked(result.plaintext)
END FOR
```

**Preservation Goal:**
```pascal
// Property: Token Security and Validation Preserved
FOR ALL operation WHERE NOT isBugCondition_P2(operation) DO
  // Login, token validation, expiry checking remain unchanged
  ASSERT AuthService.Operation(input) = AuthService.Operation'(input)
END FOR
```

---

### P3: OpenAPI/API Discoverability Hardening

**Bug Condition Function:**
```pascal
FUNCTION isBugCondition_P3(openApiDoc)
  INPUT: openApiDoc of type OpenApiDocument
  OUTPUT: boolean
  
  // Returns true when security/idempotency metadata is missing
  RETURN NOT HasBearerSecurityScheme(openApiDoc) OR
         NOT HasRequiredIdempotencyKeyHeader(openApiDoc.mutationEndpoints)
END FUNCTION
```

**Property Specification (Fix Checking):**
```pascal
// Property: Security and Idempotency Documented
FOR ALL openApiDoc WHERE isBugCondition_P3(openApiDoc) DO
  enhancedDoc ← ApplyOperationFilters'(openApiDoc)
  ASSERT HasBearerSecurityScheme(enhancedDoc) AND
         HasRequiredIdempotencyKeyHeader(enhancedDoc.mutationEndpoints) AND
         AdminPricingIdClarified(enhancedDoc)
END FOR
```

**Preservation Goal:**
```pascal
// Property: Existing Endpoint Documentation and Runtime Behavior Preserved
FOR ALL endpoint WHERE NOT isBugCondition_P3(endpoint) DO
  ASSERT SwaggerUI.Render(endpoint) = SwaggerUI.Render'(endpoint)
  ASSERT RuntimeValidation(endpoint.request) = RuntimeValidation'(endpoint.request)
END FOR
```

---

## Key Files

**Database/Migration:**
- `MediBridge.Repository/Migrations/20260702174103_AddPhase7DeliveryExpiryJobs.cs`
- `MediBridge.Repository/Migrations/ApplicationDbContextModelSnapshot.cs`

**Authentication/Email:**
- `MediBridge.Services/Services/AuthService.cs`
- Email/SMTP implementation files (IEmailSender interface and implementations)

**OpenAPI/Swagger:**
- `MediBridge.APIs/Program.cs` (Swagger configuration)
- OpenAPI operation filter implementations

**Campaign Approval:**
- `MediBridge.Services/Services/AdminCampaignReviewService.cs`

**Configuration:**
- `MediBridge.APIs/appsettings.json`
- `MediBridge.APIs/appsettings.Development.json`

---

## Verification Requirements

**Real HTTP/E2E verification required proving:**

1. Swagger/OpenAPI reachable
2. Admin login works
3. Doctor/Company workflow works
4. OTP/contact verification works
5. Admin account approval works
6. Wallet/top-up/idempotency works
7. File upload/access/replacement/delete/review works with real Cloudinary
8. Campaign draft/update/submit works
9. **Admin campaign approval succeeds without HTTP 500** ← P1 fix verification
10. Queue rows created after campaign approval
11. Delivery/recovery/expiry/injection jobs work (with DeliveryJobs__Enabled=true for verification only)
12. Doctor inbox/messages reflect expected delivery state
13. **Forgot/reset password works using delivered reset token** ← P2 fix verification
14. **OpenAPI documents bearer security and required Idempotency-Key** ← P3 fix verification
15. No regression in previously working workflows

**Build + automated test runs required.**

**Final report must include:**
- Real HTTP/E2E verification confirmation
- API URL used (credentials masked)
- Database server/name used (credentials masked)
- Whether delivery jobs were enabled during verification
- Migration/database state before and after (including development-e2e worktree database)
- Build result
- Automated test result
- Real HTTP/E2E result table with all 15 workflow verifications
- Git status
- Confirmation nothing was pushed and no PR opened

---

## Constraints

- Workspace root: `D:\My Project\MediBridge Project\MediBridge\`
- Real SQL Server database connection available from local environment
- May temporarily enable `DeliveryJobs__Enabled=true` **only for verification**, not commit it
- Must not push or open PRs
- Must not alter secrets or insert test-only/hardcoded data
- Migration 20260702174103 may have been applied from different worktree/config - **verify development-e2e worktree database first**
- **Do not create duplicate migration** if Phase 7 already exists
- User manually applied Phase 7 migration to one database - E2E database from development-e2e worktree needs verification
