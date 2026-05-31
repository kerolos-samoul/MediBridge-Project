# Research: Backend Foundation and Setup (Phase 1)

## Decision 1: Apply Response Envelope to All API Responses

- Decision: Enforce `{ Code, Message, Data }` for all response paths, including framework-generated validation and 4xx/5xx failures.
- Rationale: A single response contract reduces client branching logic, improves contract-test coverage, and aligns with constitution requirement IV.
- Alternatives considered:
  - Controller-only envelope: rejected because framework-generated responses would remain inconsistent.
  - Business-endpoint-only envelope: rejected because technical endpoints would still fragment client behavior.

## Decision 2: Preserve HTTP Status Semantics

- Decision: Keep existing HTTP status codes (2xx/4xx/5xx) while adding the envelope body.
- Rationale: Preserves backward compatibility for current clients and infrastructure relying on HTTP status while still standardizing payload contract.
- Alternatives considered:
  - Always return HTTP 200 with internal code: rejected due to compatibility and observability degradation.
  - Collapse all errors to one status family: rejected because it loses protocol semantics and can break integrations.

## Decision 3: Global Exception Handling via Middleware

- Decision: Use a single global exception middleware path to transform unhandled failures into safe envelope responses.
- Rationale: Centralized error handling ensures stack traces are not exposed and keeps error shape deterministic across the API surface.
- Alternatives considered:
  - Per-controller try/catch: rejected because it is repetitive and risks inconsistent behavior.
  - Filter-only strategy: rejected as insufficient for non-controller pipeline failures.

## Decision 4: Correlation ID Strategy

- Decision: Accept optional client `X-Correlation-ID`; propagate when valid, otherwise generate server ID and return it in response headers.
- Rationale: Balances interoperability with deterministic traceability and avoids rejecting valid traffic over missing client correlation metadata.
- Alternatives considered:
  - Require client-provided ID: rejected because it introduces unnecessary request rejection risk.
  - Always ignore client IDs: rejected because cross-system tracing value is reduced.

## Decision 5: Logging Sensitivity and Content

- Decision: Log metadata only (method, path, status, duration, correlation ID); do not log request/response bodies or authentication tokens.
- Rationale: Minimizes sensitive-data leakage risk while preserving sufficient operational observability for Phase 1.
- Alternatives considered:
  - Log sanitized bodies: rejected due to residual leakage risk and complexity.
  - Full body logging with masking: rejected as overbroad and unnecessary for foundation goals.

## Decision 6: Swagger Exposure Policy

- Decision: Expose Swagger/OpenAPI only in Development environments.
- Rationale: Supports developer productivity while reducing accidental production attack-surface exposure.
- Alternatives considered:
  - Always enabled: rejected due to avoidable production exposure.
  - Always disabled: rejected because it slows local verification and contract checks.

## Decision 7: Configuration Scaffolding Scope

- Decision: Add configuration scaffolding only for SQL Server connection settings and JWT settings; do not activate Phase 2 identity flows in Phase 1.
- Rationale: Meets constitution and plan prerequisites without introducing out-of-scope behavior.
- Alternatives considered:
  - Full identity/JWT feature enablement now: rejected as Phase 2 scope.
  - No configuration placeholders: rejected because later phases would lack stable configuration contract.

## Decision 8: Performance Guardrail

- Decision: Enforce regression guardrail of <=10% p95 increase on representative endpoints against pre-Phase 1 baseline.
- Rationale: Establishes measurable non-functional protection for cross-cutting middleware additions while remaining environment-agnostic.
- Alternatives considered:
  - Fixed +50ms threshold: rejected because absolute values are less portable across environments.
  - No performance metric: rejected because latency regressions could go undetected.

## Decision 9: Testing Strategy for Phase 1

- Decision: Use layered validation with contract checks for envelope/status behavior, integration checks for middleware/correlation behavior, and regression checks for compatibility and p95 impact.
- Rationale: Directly validates all clarified requirements and creates reusable verification assets for later phases.
- Alternatives considered:
  - Unit-only testing: rejected because pipeline behavior needs end-to-end verification.
  - Manual-only smoke testing: rejected due to low repeatability and poor release confidence.

## Decision 10: Rate Limiting as Named Policy Scaffolding

- Decision: Add named rate-limit policies for backend plan v1.3 sensitive endpoint categories in Phase 1, but attach them only to current or test-host routes as needed for verification until the real Phase 2+ endpoints exist.
- Rationale: The plan now requires login, registration, refresh, top-up, withdrawal, and interaction rate limiting; named policies provide stable attachment points without inventing out-of-scope endpoint behavior.
- Alternatives considered:
  - Implement all sensitive endpoints now: rejected because it violates Phase 1 scope.
  - Defer rate limiting entirely: rejected because backend plan v1.3 explicitly adds this as a Phase 1 foundation task.

## Decision 11: Audit Logging as Core-Owned Interface First

- Decision: Define audit logging contracts and event categories in Core/Services readiness layers without adding persistent audit tables in Phase 1.
- Rationale: Later authentication, admin, financial, and document review workflows need consistent audit calls, while storage schema belongs to later persistence phases.
- Alternatives considered:
  - Write audit events directly from controllers: rejected because it violates controller thinness and layering.
  - Add audit persistence tables immediately: rejected because Phase 1 has no database schema changes.

## Decision 12: Current User and Ownership Helpers as Reusable Abstractions

- Decision: Add current-user context and ownership helper abstractions so later doctor/company/admin endpoints can enforce ownership outside controllers.
- Rationale: Backend plan v1.3 emphasizes ownership authorization; preparing the seam now prevents later duplication and keeps controller actions HTTP-only.
- Alternatives considered:
  - Inline ownership checks in future controllers: rejected because it conflicts with Onion architecture and thin-controller rules.
  - Wait until every domain entity exists: rejected because services can depend on abstractions before concrete resource repositories are implemented.

## Phase 7 Performance Guardrail Evidence

Recorded on 2026-05-31 for `tests/performance/phase1-p95-regression.ps1`.

Command attempted:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "tests\performance\phase1-p95-regression.ps1"
```

Result:

| Run | Requests | P95Ms | TargetP95Ms | Status |
| --- | ---: | ---: | ---: | --- |
| 1 | N/A | N/A | N/A | NOT RUN |
| 2 | N/A | N/A | N/A | NOT RUN |
| 3 | N/A | N/A | N/A | NOT RUN |

Pass/fail decision: FAIL - the regression comparison could not execute because the script requires mandatory `-BaselineP95Ms` input and no pre-Phase-1 p95 baseline value is recorded in the repository. The script rejected execution with `Cannot process command because of one or more missing mandatory parameters: BaselineP95Ms.`

Follow-up required before release sign-off: rerun the same harness with the real pre-Phase-1 baseline, for example:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "tests\performance\phase1-p95-regression.ps1" -BaselineP95Ms <baseline-p95-ms>
```
