# Bug Condition Exploration Test Results

**Date**: 2026-07-03  
**Test Suite**: PostMergeBlockersExplorationTests  
**Total Tests**: 11  
**Status**: Completed

---

## Executive Summary

Bug condition exploration tests have been executed to surface counterexamples demonstrating the three post-merge production blockers. The tests successfully confirmed **P3 (OpenAPI Documentation Gaps)** bugs exist, while **P1 (Database Schema Drift)** and **P2 (Password Reset)** show different behaviors than expected.

---

## Test Results by Blocker

### P1: Database Schema Drift - Phase 7 Migration Missing

**Status**: ✅ ALL TESTS PASSED (6/6)  
**Interpretation**: Phase 7 migration IS present in the test database

| Test | Status | Finding |
|------|--------|---------|
| P1_DatabaseMissingPhase7Migration_CounterexampleFound | ✅ PASSED | Migration `20260702174103_AddPhase7DeliveryExpiryJobs` exists in __EFMigrationsHistory |
| P1_DeliveryJobRunsTable_Missing_CounterexampleFound | ✅ PASSED | DeliveryJobRuns table exists |
| P1_DeliveryRecoveryDispatchesTable_Missing_CounterexampleFound | ✅ PASSED | DeliveryRecoveryDispatches table exists |
| P1_DoctorMessageQueues_RowVersionColumn_Missing_CounterexampleFound | ✅ PASSED | DoctorMessageQueues.ConcurrencyToken (RowVersion) column exists |
| P1_DoctorAdDeliveries_ExpiredAtUtcColumn_Missing_CounterexampleFound | ✅ PASSED | DoctorAdDeliveries.ExpiredAtUtc column exists |
| P1_CampaignApproval_FailsWithSqlException_DueToMissingTables | ✅ PASSED | Campaign approval succeeds without SQL exception |

**Analysis**:
The integration tests use LocalDB test databases which are properly migrated during test initialization via `InitializeDatabaseAsync()`. Phase 7 migration is correctly applied.

**Root Cause Clarification**:
The actual bug exists in the **development-e2e worktree database**, which uses a separate connection string and was not migrated when Phase 7 was manually applied to the main development database. The integration test database does NOT exhibit this bug because it's freshly created with all migrations applied.

**Action Required**: 
- The fix (Task 3.1) should verify and apply Phase 7 migration to the development-e2e worktree database
- Integration tests serve as verification tests (will pass after fix) rather than exploration tests for P1

---

### P2: Password-Reset Token Discarded - Email Not Sent

**Status**: ✅ ALL TESTS PASSED (2/2)  
**Interpretation**: Password reset emails ARE being sent

| Test | Status | Finding |
|------|--------|---------|
| P2_PasswordResetToken_GeneratedButNotEmailed_CounterexampleFound | ✅ PASSED | Password reset email WAS sent via IEmailSender |
| P2_PasswordReset_CannotCompleteFlow_BecauseTokenNeverReceived | ✅ PASSED | Password reset email delivered to TestEmailSink |

**Counterexample Expected**: Token generated and hashed but NOT emailed  
**Actual Behavior**: Token generated, hashed, AND emailed successfully

**Analysis**:
The password reset flow IS working correctly in the current codebase:
1. User requests password reset via POST /api/auth/forgot-password (returns HTTP 202 Accepted)
2. System generates plaintext token
3. System hashes token and persists to database (PasswordResetFlows table)
4. System sends email with plaintext token via IEmailSender
5. TestEmailSink receives the email

**Root Cause Re-evaluation**:
Either:
- The bug was already fixed in a previous commit
- The bug exists only in specific conditions not covered by these tests
- The bug description may be inaccurate

**Action Required**:
- Investigate actual password reset implementation in AuthService.cs
- Check git history for recent password reset changes
- Consider if bug exists only in development-e2e worktree context
- May need to clarify with user if P2 bug still exists

---

### P3: OpenAPI Documentation Gaps

**Status**: ❌ ALL TESTS FAILED (3/3) - COUNTEREXAMPLES FOUND  
**Interpretation**: All three OpenAPI documentation bugs confirmed

| Test | Status | Counterexample |
|------|--------|----------------|
| P3_OpenApiDoc_MissingBearerSecurityScheme_CounterexampleFound | ❌ FAILED | Bearer security scheme is NOT declared in components.securitySchemes.Bearer |
| P3_OpenApiDoc_IdempotencyKeyNotRequired_OnMutationEndpoints_CounterexampleFound | ❌ FAILED | Idempotency-Key header is NOT documented as required on POST /api/company/campaigns/drafts |
| P3_OpenApiDoc_AdminPricingParameter_LacksClarifyingDescription_CounterexampleFound | ❌ FAILED | DoctorProfile ID parameter on PUT /api/admin/doctors/{doctorId}/price lacks clarifying description mentioning "DoctorProfile entity" |

**Specific Findings**:

#### 3.1 Missing Bearer Security Scheme
```json
// EXPECTED in components.securitySchemes:
{
  "Bearer": {
    "type": "http",
    "scheme": "bearer",
    "bearerFormat": "JWT",
    "description": "JWT Authorization header using the Bearer scheme."
  }
}

// ACTUAL: components.securitySchemes.Bearer does NOT exist
```

**Impact**: API consumers cannot see that bearer authentication is required. The scheme exists at runtime (AuthorizeAttribute enforces it) but is not documented in OpenAPI spec.

#### 3.2 Idempotency-Key Not Marked Required
```json
// EXPECTED on POST /api/company/campaigns/drafts:
{
  "parameters": [
    {
      "name": "Idempotency-Key",
      "in": "header",
      "required": true,
      "schema": { "type": "string" },
      "description": "Unique identifier for idempotent request handling"
    }
  ]
}

// ACTUAL: Idempotency-Key parameter is missing or not marked as required
```

**Impact**: API consumers don't know Idempotency-Key is required on mutation endpoints. Runtime validation enforces it (via IdempotencyMiddleware) but it's not visible in Swagger documentation.

#### 3.3 Admin Pricing Parameter Description Missing
```csharp
// EXPECTED on PUT /api/admin/doctors/{doctorId}/price:
/// <param name="doctorId">
/// The unique identifier of the DoctorProfile entity (not the User ID).
/// Retrieve from GET /doctors endpoint.
/// </param>

// ACTUAL: Parameter description is missing or doesn't clarify DoctorProfile vs User distinction
```

**Impact**: API consumers may confuse DoctorProfile ID with User ID, leading to API call failures.

---

## Test Execution Details

**Environment**:
- Test Framework: xUnit.net v2.8.2
- Database: LocalDB (MediBridge.IntegrationTests)
- Email: TestEmailSender with TestEmailSink
- Swagger: Development environment (SwaggerGen enabled)

**Execution Time**: ~14 seconds  
**Test Database**: Isolated LocalDB instance, fresh migration applied

**Test Infrastructure**:
- `WebAppFactory`: ASP.NET Core TestServer with production configuration
- `ConfiguredWebAppFactory`: Base factory with LocalDB connection string
- `TestEmailSender`/`TestEmailSink`: Mock email infrastructure for verification

---

## Documented Counterexamples

### P3.1: Missing Bearer Security Scheme
**File**: `/swagger/v1/swagger.json`  
**Expected**: `components.securitySchemes.Bearer` with type "http", scheme "bearer"  
**Actual**: Property does not exist  
**Error**: OpenAPI document incomplete - runtime bearer authentication not documented

### P3.2: Idempotency-Key Not Required
**File**: `/swagger/v1/swagger.json`  
**Endpoint**: POST /api/company/campaigns/drafts  
**Expected**: Idempotency-Key parameter with `required: true`  
**Actual**: Parameter missing or not marked as required  
**Error**: Runtime idempotency validation not documented

### P3.3: DoctorProfile ID Unclear
**File**: `/swagger/v1/swagger.json`  
**Endpoint**: PUT /api/admin/doctors/{doctorId}/price  
**Expected**: Parameter description clarifying "DoctorProfile entity" distinction  
**Actual**: Description missing or doesn't mention DoctorProfile entity  
**Error**: API consumers confused about ID semantics

---

## Recommendations

### P1: Database Schema Drift
1. ✅ Tests written - serve as verification tests
2. ⚠️ Actual bug exists in development-e2e worktree database (different connection string)
3. 📋 Fix: Task 3.1 - Verify and apply Phase 7 migration to development-e2e database
4. ✅ After fix: Re-run these tests (should still pass)

### P2: Password Reset Token
1. ✅ Tests written - currently passing
2. ⚠️ Bug may already be fixed or not reproducible in test environment
3. 📋 Action: Investigate AuthService.cs implementation
4. 📋 Action: Check if bug exists only in development-e2e context
5. ❓ Consider clarifying with user if P2 bug still exists

### P3: OpenAPI Documentation
1. ✅ Tests written - successfully found all three counterexamples
2. ❌ Bugs confirmed - ready for fix implementation
3. 📋 Fix: Task 3.3 - Add bearer security scheme and IdempotencyKeyOperationFilter
4. ✅ After fix: Re-run these tests (should pass)

---

## Next Steps

1. **Complete Task 1**: ✅ DONE - Exploration tests written and executed
2. **Move to Task 2**: Write preservation tests (BEFORE implementing fixes)
3. **Clarify P2 Status**: Investigate why password reset tests are passing
4. **Implement Fixes**: Tasks 3.1, 3.2, 3.3
5. **Verification**: Re-run exploration tests (should pass after fixes)

---

## Test Code Location

**File**: `tests/integration/MediBridge.IntegrationTests/PostMergeBlockersExplorationTests.cs`  
**Lines**: 486 lines of C# test code  
**Coverage**: All three blockers (P1, P2, P3) with 11 property-based exploration tests

**Property Validation**: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3
