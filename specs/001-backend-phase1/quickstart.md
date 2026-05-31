# Quickstart: Backend Foundation and Setup (Phase 1)

## Goal

Verify Phase 1 foundation behavior for envelope standardization, safe global errors, correlation headers, metadata-only logging, development-only Swagger, configuration readiness, rate-limit policy scaffolding, audit logging interfaces, and current-user/ownership abstractions.

## Preconditions

- Repository is on branch `001-backend-phase1`.
- .NET 8 SDK is installed.
- `MediBridge.APIs` project restores and builds.
- Pre-Phase 1 p95 baseline data exists for representative endpoint comparisons.

## 1) Build and Run

```powershell
dotnet restore "D:\My Project\MediBridge Project\MediBridge\MediBridge.slnx"
dotnet build "D:\My Project\MediBridge Project\MediBridge\MediBridge.slnx"
dotnet run --project "D:\My Project\MediBridge Project\MediBridge\MediBridge.APIs\MediBridge.APIs.csproj"
```

## 2) Verify Standard Envelope on Success

```powershell
curl -i "https://localhost:5001/weatherforecast" -H "X-Correlation-ID: sample-123"
```

Expected:

- HTTP status remains semantically correct (success path stays 2xx).
- Response body has `Code`, `Message`, `Data`.
- `X-Correlation-ID` response header is present.

## 3) Verify Correlation Fallback Behavior

```powershell
curl -i "https://localhost:5001/weatherforecast"
```

Expected:

- Server returns generated `X-Correlation-ID` header when request header is missing.
- Request lifecycle logs contain method/path/status/duration/correlation identifier.

Correlation ID validation rule for all checks in this guide:

- Valid client value MUST match `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`.
- Missing or invalid values MUST be replaced with a server-generated response header value.

## 4) Verify Safe Error Envelope

Use a known failure path (or temporary controlled throw endpoint) and request it.

Expected:

- Error response still contains `Code`, `Message`, `Data`.
- No stack trace or internal exception details are returned.
- HTTP error status semantics are preserved (4xx/5xx remains as appropriate).

## 5) Verify Logging Privacy Rules

Inspect runtime logs for sampled requests.

Expected:

- Logs include metadata only: method, path, status, duration, correlation ID.
- Logs do not include request/response bodies.
- Logs do not include auth tokens.

## 6) Verify Swagger Environment Policy

- In Development: Swagger UI and OpenAPI endpoint are accessible.
- In non-Development configuration: Swagger endpoints are disabled.

### Automated verification added in Phase 5

- `tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs` validates that Swagger JSON is available in Development and not available in Production.
- `tests/integration/MediBridge.IntegrationTests/ConfigurationBindingTests.cs` validates `JwtOptions` and `DatabaseOptions` binding from configuration.
- `tests/performance/phase1-p95-regression.ps1` is a placeholder harness for running p95 regression runs.

## 7) Verify Performance Guardrail

Run fixed-profile sampling against `/weatherforecast` under the same environment as baseline:

- 3 runs
- 5 minutes per run
- 10 concurrent users
- at least 1000 requests per run

Expected:

- Each run keeps post-change p95 <= 110% of the pre-Phase-1 baseline p95.

## 8) Verify Scope Discipline

Confirm no Phase 2+ behavior appears in this phase:

- No new queue/wallet/settlement/weekly enforcement behavior.
- No change to existing business decisions beyond envelope/error/cross-cutting concerns.

## 9) Verify Rate-Limit Policy Readiness

```powershell
dotnet test "D:\My Project\MediBridge Project\MediBridge\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj" --filter "FullyQualifiedName~RateLimitPolicyRegistrationTests|FullyQualifiedName~RateLimitEnvelopeTests"
```

Inspect startup/configuration tests for the backend plan v1.3 sensitive categories:

- Login
- Registration
- Refresh
- Company top-up
- Doctor withdrawal
- Doctor interaction

Expected:

- Each category has a named policy available for endpoint attachment.
- Any sampled/test-host rate-limit rejection returns HTTP 429.
- The 429 response body uses `Code`, `Message`, `Data`.

## 10) Verify Audit and Ownership Readiness

```powershell
dotnet test "D:\My Project\MediBridge Project\MediBridge\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj" --filter "FullyQualifiedName~SecurityReadinessBoundaryTests"
```

Inspect architecture tests and layer references.

Expected:

- Audit logging contracts are Core-owned abstractions and do not depend on HTTP or persistence implementation details.
- Current-user context can represent authenticated and anonymous requests.
- Ownership helpers support doctor-owned, company-owned, and admin-accessible resource checks.
- Controllers remain HTTP-only and do not contain business ownership rules.

## Phase 7 Final Validation Evidence

Recorded on 2026-05-31 for Phase 1 foundation validation.

### Automated test suite

```powershell
dotnet test "D:\My Project\MediBridge Project\MediBridge\MediBridge.slnx" /p:UseAppHost=false /p:UseSharedCompilation=false /p:BuildInParallel=false /m:1
```

Result:

- Contract tests: PASS, 2 passed, 0 failed.
- Integration tests: PASS, 26 passed, 0 failed.
- Initial sandboxed run was blocked by test artifact write permissions; rerun with normal filesystem access passed.

### Contract validation

Implemented behavior was validated against `specs/001-backend-phase1/contracts/foundation-api.yaml`.

- `/weatherforecast` success returns HTTP 200 with `Code = 200`, `Message = Success`, and array `Data`.
- `/weatherforecast?count=0` returns HTTP 400 with `Code = 400`, `Message = Validation failed.`, and `Data = null`.
- Controlled unhandled exception checks return HTTP 500 with `Code = 500`, `Message = An unexpected error occurred.`, and `Data = null`.
- Applied test-host rate-limit policy returns HTTP 429 with `Code = 429`, `Message = Too many requests.`, and `Data = null`.
- No contract example mismatch was found, so `foundation-api.yaml` was unchanged.

### Endpoint-by-endpoint diff summary

| Endpoint or scenario | HTTP status | Envelope | Business outcome delta | Evidence |
| --- | --- | --- | --- | --- |
| `GET /weatherforecast` | 200 preserved | PASS | None beyond allowed envelope wrapping | Contract and integration tests |
| `GET /weatherforecast?count=0` | 400 preserved | PASS | None beyond allowed validation envelope | Contract and integration tests |
| Controlled unhandled exception | 500 preserved | PASS | Safe error wrapping only | Integration tests |
| Missing or invalid `X-Correlation-ID` | 200 preserved for success sample | PASS | Generated trace header only | Integration tests |
| Development Swagger | 200 in Development | N/A | Documentation exposure only | Integration tests |
| Production Swagger | 404 in Production | N/A | Documentation hidden as required | Integration tests |
| Applied rate-limit rejection | 429 preserved | PASS | Rejection envelope only | Integration tests |

Final automated evidence status: PARTIAL PASS - contract, integration, route-surface, and quickstart-verifiable endpoint behavior passed. The live end-to-end quickstart run and p95 regression guardrail remain incomplete until the API is run against a real pre-Phase-1 `BaselineP95Ms` value.
