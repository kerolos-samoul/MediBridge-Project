# Post-Merge Blockers Bugfix Design

## Overview

This design addresses three production blockers using targeted fixes:
1. **P1 (Database Schema Drift)**: Verify and apply existing Phase 7 migration to development-e2e worktree database
2. **P2 (Password-Reset Token)**: Integrate plaintext token delivery with existing IEmailSender/SMTP infrastructure
3. **P3 (OpenAPI Documentation)**: Add operation filters for bearer security scheme and required Idempotency-Key headers

All fixes leverage existing infrastructure without introducing new dependencies or architectural changes.

## Glossary

- **Bug_Condition (C)**: The condition that triggers each bug
- **Property (P)**: The desired behavior when bug conditions are fixed
- **Preservation**: Existing functionality that must remain unchanged
- **Phase 7 Migration**: `20260702174103_AddPhase7DeliveryExpiryJobs.cs` - adds DeliveryJobRuns, DeliveryRecoveryDispatches tables and new columns
- **development-e2e worktree**: Separate git worktree used for E2E testing with isolated database instance
- **IEmailSender**: Existing email abstraction in `MediBridge.Services` for SMTP delivery
- **Operation Filter**: Swashbuckle extensibility point for modifying OpenAPI document generation

## Bug Details

### Bug Condition

**P1: Database Schema Drift**
```
FUNCTION isBugCondition_P1(dbContext)
  INPUT: dbContext of type ApplicationDbContext
  OUTPUT: boolean
  
  RETURN NOT EXISTS(DeliveryJobRuns table) OR
         NOT EXISTS(DeliveryRecoveryDispatches table) OR
         NOT EXISTS(DoctorMessageQueues.RowVersion column) OR
         NOT EXISTS(DoctorAdDeliveries.ExpiredAtUtc column)
END FUNCTION
```

**P2: Password-Reset Token Discarded**
```
FUNCTION isBugCondition_P2(resetRequest)
  INPUT: resetRequest of type PasswordResetRequest
  OUTPUT: boolean
  
  RETURN TokenGenerated(resetRequest) AND NOT TokenEmailed(resetRequest)
END FUNCTION
```

**P3: OpenAPI Documentation Gaps**
```
FUNCTION isBugCondition_P3(openApiDoc)
  INPUT: openApiDoc of type OpenApiDocument
  OUTPUT: boolean
  
  RETURN NOT HasBearerSecurityScheme(openApiDoc) OR
         NOT HasRequiredIdempotencyKeyHeader(openApiDoc.mutationEndpoints)
END FUNCTION
```

### Examples

**P1: Campaign Approval Failure**
- User approves campaign via admin endpoint → HTTP 500 (DeliveryJobRuns table missing)
- Expected: HTTP 200 with queue rows created

**P2: Password Reset Non-Functional**
- User requests password reset → Token generated and hashed, but email never sent
- User attempts reset → Cannot proceed without token
- Expected: User receives email with plaintext reset token

**P3: API Consumer Confusion**
- User inspects Swagger → No bearer security scheme visible
- User inspects POST /campaigns → Idempotency-Key not marked as required
- Expected: Security scheme documented, headers clearly marked

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Existing EF Core migrations and __EFMigrationsHistory integrity
- Pre-Phase-7 database queries (Users, Campaigns, DoctorProfiles, etc.)
- Token hashing, expiry, and single-use validation
- User login and existing auth flows
- Existing email infrastructure for other features
- Swagger UI rendering and endpoint visibility
- Runtime bearer token and idempotency key validation

**Scope:**
All database operations NOT involving Phase 7 objects should be unaffected. All authentication operations NOT involving password reset should be unaffected. All API runtime behavior should be unaffected (only documentation changes).

## Hypothesized Root Cause

### P1: Database Schema Drift

The most likely cause is database configuration mismatch between worktrees:

1. **Separate Database Instances**: The development-e2e worktree may use a different database connection string pointing to an isolated database that was not migrated when Phase 7 was manually applied to the main database

2. **Migration History Divergence**: __EFMigrationsHistory in the E2E database may not include `20260702174103_AddPhase7DeliveryExpiryJobs`

3. **Worktree Isolation**: Git worktrees maintain separate working directories but share git history - the E2E worktree's database may have been left behind during manual migration

### P2: Password-Reset Token Discarded

Based on the bug description, the issue is code flow:

1. **Missing Email Call**: `AuthService.cs` generates the plaintext token, hashes it, persists the hash, but never calls IEmailSender with the plaintext token

2. **Premature Variable Disposal**: The plaintext token may be generated but goes out of scope or is reassigned before email sending logic

3. **Incomplete Implementation**: The password reset feature may have been partially implemented with token generation complete but email delivery step missing

### P3: OpenAPI Documentation Gaps

The Swagger configuration in `Program.cs` lacks operation filters:

1. **No Security Scheme Declaration**: The bearer authentication scheme exists at runtime but is not declared in the OpenAPI document

2. **No Operation Filter for Headers**: Required headers like Idempotency-Key are validated at runtime but not documented as required in OpenAPI

3. **Missing Parameter Descriptions**: Admin pricing endpoint parameters lack clarifying descriptions

## Correctness Properties

Property 1: Bug Condition - Phase 7 Migration Applied

_For any_ database where Phase 7 objects are missing (isBugCondition_P1 returns true), the fix SHALL apply migration `20260702174103_AddPhase7DeliveryExpiryJobs` idempotently, enabling campaign approval and delivery job workflows without HTTP 500 errors.

**Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7**

Property 2: Bug Condition - Password Reset Token Delivered

_For any_ password reset request where the token is generated but not emailed (isBugCondition_P2 returns true), the fixed AuthService SHALL send the plaintext token via IEmailSender while preserving hashing, expiry, and single-use semantics.

**Validates: Requirements 2.8, 2.9, 2.10, 2.11**

Property 3: Bug Condition - OpenAPI Security and Headers Documented

_For any_ OpenAPI document where bearer security scheme or required Idempotency-Key headers are missing (isBugCondition_P3 returns true), the fixed Swagger configuration SHALL apply operation filters to document bearer authentication and mark Idempotency-Key as required on mutation endpoints.

**Validates: Requirements 2.12, 2.13, 2.14**

Property 4: Preservation - Existing Database Operations

_For any_ database operation that does NOT involve Phase 7 objects (isBugCondition_P1 returns false), the fixed code SHALL produce exactly the same results as the original code, preserving all pre-Phase-7 queries and __EFMigrationsHistory integrity.

**Validates: Requirements 3.1, 3.2, 3.3**

Property 5: Preservation - Existing Auth and Email Flows

_For any_ authentication operation that is NOT a password reset request (isBugCondition_P2 returns false), the fixed AuthService SHALL produce exactly the same results as the original code, preserving login, token validation, expiry checking, and existing email infrastructure usage.

**Validates: Requirements 3.4, 3.5, 3.6, 3.7**

Property 6: Preservation - Runtime API Behavior

_For any_ API endpoint request that does NOT involve OpenAPI documentation generation (isBugCondition_P3 returns false), the fixed code SHALL produce exactly the same runtime behavior as the original code, preserving Swagger UI rendering, endpoint visibility, bearer token validation, and idempotency key validation.

**Validates: Requirements 3.8, 3.9, 3.10, 3.11**

## Fix Implementation

### P1: Database Schema Drift Fix

**Verification Strategy:**
1. **Locate development-e2e worktree**: Search for git worktree configuration (`.git/worktrees/` or git worktree list)
2. **Identify E2E database connection**: Check appsettings in development-e2e worktree for connection string
3. **Query migration history**: Execute `SELECT MigrationId FROM __EFMigrationsHistory` to check if `20260702174103_AddPhase7DeliveryExpiryJobs` is present

**File**: `MediBridge.Repository/Migrations/20260702174103_AddPhase7DeliveryExpiryJobs.cs`

**Specific Changes**:
1. **No Code Changes Required**: The migration already exists and is correct
2. **Apply Migration to E2E Database**: Use EF Core CLI to apply migration
   - Navigate to development-e2e worktree
   - Run `dotnet ef database update --project MediBridge.Repository --startup-project MediBridge.APIs`
   - Verify migration applied successfully via __EFMigrationsHistory query

3. **Idempotent Safety**: EF Core automatically skips already-applied migrations based on __EFMigrationsHistory

4. **Verification**:
   - Query `SELECT COUNT(*) FROM DeliveryJobRuns` (should succeed)
   - Query `SELECT COUNT(*) FROM DeliveryRecoveryDispatches` (should succeed)
   - Verify `DoctorMessageQueues.RowVersion` column exists
   - Verify `DoctorAdDeliveries.ExpiredAtUtc` column exists

### P2: Password-Reset Token Delivery Fix

**File**: `MediBridge.Services/Services/AuthService.cs`

**Integration Point**: Existing `IEmailSender` interface (already used for OTP emails, approval notifications)

**Specific Changes**:
1. **Identify Token Generation Location**: Find the method that generates password reset tokens (likely `RequestPasswordResetAsync` or similar)

2. **Preserve Plaintext Token**: After generating the token but before hashing, store plaintext in a local variable:
   ```csharp
   var plaintextToken = GenerateResetToken();
   var hashedToken = HashToken(plaintextToken);
   await _repository.SaveResetToken(userId, hashedToken, expiryTime);
   ```

3. **Send Email with Plaintext Token**: After persisting the hashed token, call IEmailSender:
   ```csharp
   var resetLink = $"{_appSettings.FrontendUrl}/reset-password?token={plaintextToken}";
   await _emailSender.SendPasswordResetEmailAsync(userEmail, resetLink);
   ```

4. **Security Considerations**:
   - Do NOT log the plaintext token
   - Do NOT return the plaintext token in HTTP response
   - Do NOT persist the plaintext token in database
   - Clear the plaintextToken variable immediately after email sending

5. **Email Template**: Use existing email infrastructure pattern (same as OTP emails)
   - Subject: "MediBridge Password Reset Request"
   - Body: Include reset link with embedded plaintext token
   - Expiry time notification (e.g., "This link expires in 30 minutes")

### P3: OpenAPI Documentation Fix

**File**: `MediBridge.APIs/Program.cs`

**Integration Point**: Existing Swashbuckle/SwaggerGen configuration

**Specific Changes**:

1. **Add Bearer Security Scheme**:
   ```csharp
   builder.Services.AddSwaggerGen(options =>
   {
       // Existing configuration...
       
       // Add bearer authentication scheme
       options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
       {
           Name = "Authorization",
           Type = SecuritySchemeType.Http,
           Scheme = "bearer",
           BearerFormat = "JWT",
           In = ParameterLocation.Header,
           Description = "JWT Authorization header using the Bearer scheme. Enter your token in the text input below."
       });
       
       options.AddSecurityRequirement(new OpenApiSecurityRequirement
       {
           {
               new OpenApiSecurityScheme
               {
                   Reference = new OpenApiReference
                   {
                       Type = ReferenceType.SecurityScheme,
                       Id = "Bearer"
                   }
               },
               Array.Empty<string>()
           }
       });
   });
   ```

2. **Create Operation Filter for Idempotency-Key Header**:
   Create new file: `MediBridge.APIs/Filters/IdempotencyKeyOperationFilter.cs`
   ```csharp
   public class IdempotencyKeyOperationFilter : IOperationFilter
   {
       public void Apply(OpenApiOperation operation, OperationFilterContext context)
       {
           // Identify mutation endpoints (POST, PUT, PATCH, DELETE)
           var isMutation = context.ApiDescription.HttpMethod?.ToUpper() 
               is "POST" or "PUT" or "PATCH" or "DELETE";
           
           if (isMutation)
           {
               operation.Parameters ??= new List<OpenApiParameter>();
               operation.Parameters.Add(new OpenApiParameter
               {
                   Name = "Idempotency-Key",
                   In = ParameterLocation.Header,
                   Required = true,
                   Schema = new OpenApiSchema { Type = "string" },
                   Description = "Unique identifier for idempotent request handling"
               });
           }
       }
   }
   ```

3. **Register Operation Filter**:
   In `Program.cs` SwaggerGen configuration:
   ```csharp
   options.OperationFilter<IdempotencyKeyOperationFilter>();
   ```

4. **Clarify Admin Pricing Endpoint DoctorProfile ID**:
   Add XML documentation comments to the admin pricing endpoint controller action:
   ```csharp
   /// <param name="doctorProfileId">The unique identifier of the DoctorProfile entity 
   /// (not the User ID). Retrieve from GET /doctors endpoint.</param>
   ```
   
   Ensure `options.IncludeXmlComments()` is configured in SwaggerGen to read XML docs.

## Testing Strategy

### Validation Approach

The testing strategy uses real HTTP/E2E verification with production-grade local environment:
1. **Exploratory Phase**: Surface counterexamples on unfixed code to confirm root causes
2. **Fix Verification**: Apply fixes and verify bug conditions are resolved
3. **Preservation Verification**: Ensure unchanged behaviors remain intact

All verification uses:
- Real SQL Server database
- Real Cloudinary integration
- Real SMTP email delivery (or mock IEmailSender if SMTP not available)
- Swagger UI inspection

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples demonstrating each bug BEFORE implementing fixes. Confirm or refute root cause analysis.

**Test Plan**: Execute real HTTP requests against unfixed code to observe failures.

**Test Cases**:
1. **P1 - Campaign Approval HTTP 500** (will fail on unfixed code):
   - Authenticate as admin
   - Approve a campaign via POST /admin/campaigns/{id}/approve
   - Observe HTTP 500 with SQL error for missing DeliveryJobRuns table
   
2. **P2 - No Password Reset Email** (will fail on unfixed code):
   - Request password reset via POST /auth/forgot-password
   - Check email inbox or IEmailSender mock
   - Observe that no email was sent despite HTTP 200 response
   
3. **P3 - Missing OpenAPI Metadata** (will fail on unfixed code):
   - Navigate to /swagger
   - Inspect OpenAPI JSON document
   - Observe missing bearer security scheme
   - Observe Idempotency-Key not marked as required on POST /campaigns

**Expected Counterexamples**:
- P1: SQL exception stack trace showing missing table/column
- P2: IEmailSender.SendPasswordResetEmailAsync never called (verify via logging or breakpoint)
- P3: OpenAPI JSON lacks `securityDefinitions.Bearer` and required Idempotency-Key parameters

### Fix Checking

**Goal**: Verify that for all inputs where bug conditions hold, fixed functions produce expected behavior.

**P1 Fix Checking:**
```
FOR ALL dbContext WHERE isBugCondition_P1(dbContext) DO
  Apply_Phase7_Migration(dbContext)
  result := AdminCampaignReviewService.ApproveCampaignAsync'(campaignId)
  ASSERT result.StatusCode = 200
  ASSERT DeliveryJobRuns contains new row
END FOR
```

**Test Plan**:
- Verify E2E database missing Phase 7 migration
- Apply migration via `dotnet ef database update`
- Approve campaign via POST /admin/campaigns/{id}/approve
- Assert HTTP 200 response
- Query DeliveryJobRuns table to verify rows created

**P2 Fix Checking:**
```
FOR ALL resetRequest WHERE isBugCondition_P2(resetRequest) DO
  result := AuthService.RequestPasswordReset'(resetRequest)
  ASSERT EmailSent(result)
  ASSERT TokenHashed(result.storedToken)
  ASSERT NOT TokenLeaked(result.plaintext)
END FOR
```

**Test Plan**:
- Request password reset via POST /auth/forgot-password
- Verify IEmailSender.SendPasswordResetEmailAsync called with plaintext token
- Verify email contains reset link with token
- Verify database contains hashed token (not plaintext)
- Verify HTTP response does not contain plaintext token
- Use received token to complete reset via POST /auth/reset-password

**P3 Fix Checking:**
```
FOR ALL openApiDoc WHERE isBugCondition_P3(openApiDoc) DO
  enhancedDoc := ApplyOperationFilters'(openApiDoc)
  ASSERT HasBearerSecurityScheme(enhancedDoc)
  ASSERT HasRequiredIdempotencyKeyHeader(enhancedDoc.mutationEndpoints)
  ASSERT AdminPricingIdClarified(enhancedDoc)
END FOR
```

**Test Plan**:
- Navigate to /swagger
- Inspect OpenAPI JSON document (GET /swagger/v1/swagger.json)
- Verify `components.securitySchemes.Bearer` exists with type "http", scheme "bearer"
- Verify `security` array includes Bearer scheme
- Verify POST /campaigns operation includes Idempotency-Key parameter with `required: true`
- Verify admin pricing endpoint includes DoctorProfile ID description

### Preservation Checking

**Goal**: Verify that for all inputs where bug conditions do NOT hold, fixed functions produce same results as original.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition_P1(input) DO
  ASSERT QueryPrePhase7Tables(input) = QueryPrePhase7Tables'(input)
END FOR

FOR ALL operation WHERE NOT isBugCondition_P2(operation) DO
  ASSERT AuthService.Operation(input) = AuthService.Operation'(input)
END FOR

FOR ALL endpoint WHERE NOT isBugCondition_P3(endpoint) DO
  ASSERT RuntimeValidation(endpoint) = RuntimeValidation'(endpoint)
END FOR
```

**Testing Approach**: Property-based testing concepts applied manually via comprehensive E2E workflow verification.

**Test Plan**: Execute all 15 E2E workflow verifications and compare results before/after fixes.

**Test Cases**:
1. **Pre-Phase-7 Database Queries Preservation**:
   - Query Users, Campaigns, DoctorProfiles, Companies, Wallets
   - Verify results unchanged after Phase 7 migration
   
2. **Login Flow Preservation**:
   - Login as doctor, company, admin
   - Verify JWT tokens issued correctly
   - Verify refresh token flow works
   
3. **Existing Email Flows Preservation**:
   - OTP verification emails
   - Account approval notification emails
   - Verify these continue working after password reset email changes
   
4. **Runtime Security Preservation**:
   - Submit request with invalid bearer token → verify still rejected
   - Submit request without Idempotency-Key → verify still rejected
   - Verify runtime validation unchanged (only documentation changed)

### Unit Tests

**P1 Unit Tests:**
- Test that Phase 7 migration Up() creates all required tables and columns
- Test that Phase 7 migration Down() removes all Phase 7 objects
- Test idempotent migration application (applying twice should succeed)

**P2 Unit Tests:**
- Test password reset token generation produces unique tokens
- Test token hashing produces different hash for different tokens
- Test IEmailSender.SendPasswordResetEmailAsync receives correct parameters
- Test plaintext token not persisted in database
- Test plaintext token not logged

**P3 Unit Tests:**
- Test IdempotencyKeyOperationFilter adds parameter to POST endpoints
- Test IdempotencyKeyOperationFilter adds parameter to PUT/PATCH/DELETE endpoints
- Test IdempotencyKeyOperationFilter does NOT add parameter to GET endpoints
- Test bearer security scheme definition structure

### Property-Based Tests

**P1 Property Tests:**
- Generate random campaign IDs and verify approval succeeds after migration
- Generate random delivery job configurations and verify rows created correctly
- Test across multiple database states (empty, partially populated, fully populated)

**P2 Property Tests:**
- Generate random user emails and verify reset emails delivered
- Generate random token values and verify hashing produces valid hashes
- Test token expiry across many time scenarios (not expired, expired, far future)

**P3 Property Tests:**
- Generate random mutation endpoint paths and verify Idempotency-Key marked required
- Generate random GET endpoint paths and verify Idempotency-Key NOT marked required
- Verify bearer scheme applied to all endpoints requiring authentication

### Integration Tests

**15-Point E2E Workflow Verification:**
1. Swagger/OpenAPI reachable (verify P3 fix visible)
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

**Build + Automated Test Integration:**
- Run `dotnet build` to verify compilation
- Run `dotnet test` to execute unit and integration test suite
- Verify all tests pass after fixes applied

**Verification Report Requirements:**
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
