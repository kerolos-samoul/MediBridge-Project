# Feature Specification: Backend Foundation and Setup (Phase 1)

**Feature Branch**: `[001-backend-phase1]`  
**Created**: 2026-04-23  
**Status**: Draft  
**Input**: User description: "follow docs/backend-plan.md do not modify existing logic Implement Phase 1 only"

## Clarifications

### Session 2026-04-23

- Q: Should the standard envelope apply to all API responses or only selected paths? -> A: Apply `{ Code, Message, Data }` to all API responses, including framework-generated 4xx/5xx and validation errors.
- Q: What request logging sensitivity should Phase 1 enforce? -> A: Log metadata only (method, path, status, duration, and correlation identifier) and never log request/response bodies or auth tokens.
- Q: How should correlation IDs behave when clients omit or send invalid IDs? -> A: Accept optional `X-Correlation-ID` only when it matches `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`; if missing or invalid, generate a server correlation ID and return it in the response header.
- Q: Should HTTP status codes be preserved or replaced by envelope `Code` only? -> A: Preserve existing HTTP status codes (2xx/4xx/5xx) and add the standard envelope in the response body.
- Q: What performance regression guardrail should Phase 1 enforce? -> A: For `/weatherforecast`, run 3 post-change runs of 5 minutes each at 10 concurrent users and at least 1000 requests per run under the same environment as baseline; each run must keep p95 <= 110% of baseline.
- Q: How should the backend plan v1.3 Phase 1 security-readiness additions be scoped? -> A: Add reusable rate-limiting policy scaffolding, audit logging interfaces, current-user context, and ownership authorization helper abstractions without implementing Phase 2+ business endpoints or persistence.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Standard API Response Contract (Priority: P1)

As an API consumer, I receive a consistent response structure for successful and failed requests so integrations are predictable and low-risk.

**Why this priority**: Consistent contracts are the foundation for every downstream integration and test flow in later phases.

**Independent Test**: Call a representative endpoint and confirm successful responses are always returned in the agreed envelope shape without changing business outcome values.

**Acceptance Scenarios**:

1. **Given** any API request path, **When** the system returns success or failure (including framework-generated validation and 4xx/5xx paths), **Then** the response uses `{ Code, Message, Data }` and `Data` contains the expected payload or `null`.
2. **Given** an endpoint that previously returned a business result, **When** Phase 1 changes are applied, **Then** the business decision and payload meaning remain unchanged except for envelope standardization.
3. **Given** an API failure or success path, **When** the response is returned, **Then** the existing HTTP status semantics remain unchanged and the envelope is added in the response body.

---

### User Story 2 - Safe Global Error Handling (Priority: P1)

As a platform operator, I need all unhandled backend errors to be returned safely and consistently so users receive stable failures and support can investigate quickly.

**Why this priority**: Safe error handling prevents sensitive leakage and avoids inconsistent client-side failure behavior.

**Independent Test**: Trigger an unhandled exception path and verify the response is standardized, safe, and traceable through correlation information.

**Acceptance Scenarios**:

1. **Given** an unhandled exception during request processing, **When** the error is returned to the client, **Then** the response uses the standard envelope and does not expose stack traces or internal details.
2. **Given** a failed request, **When** support reviews logs, **Then** a correlation identifier links the client-visible failure to the server-side error record.
3. **Given** a request with missing or invalid `X-Correlation-ID`, **When** the request is processed, **Then** the system generates a correlation identifier and returns it in the response header for traceability.

---

### User Story 3 - Phase 1 Operational Baseline (Priority: P2)

As a backend engineer, I need the foundation setup (layer boundaries, environment-safe API docs, configuration scaffolding, and request logging) so future phases can be built without architecture drift.

**Why this priority**: This is required to deliver later authentication, persistence, and queue/wallet features safely and consistently.

**Independent Test**: Verify layering boundaries, environment-based documentation behavior, and startup configuration readiness without introducing new business behavior.

**Acceptance Scenarios**:

1. **Given** the service runs in development, **When** an engineer accesses API documentation, **Then** documentation is available and reflects the standard response contract.
2. **Given** the service runs in a non-development environment, **When** the same documentation route is requested, **Then** documentation is not exposed.
3. **Given** project dependency review, **When** references are inspected, **Then** layer direction remains inward-only across Core, Repository, Services, and APIs.

---

### User Story 4 - Security and Ownership Readiness (Priority: P2)

As a backend engineer, I need reusable security and ownership foundations so later authentication, wallet, withdrawal, campaign, and interaction endpoints can apply consistent controls without duplicating policy logic.

**Why this priority**: Backend plan v1.3 adds cross-cutting security foundations that must be established before sensitive Phase 2+ features are built.

**Independent Test**: Inspect service registration and abstractions to confirm named rate-limit policies, audit logging contracts, current-user context, and ownership authorization helpers are available without adding out-of-scope endpoint behavior.

**Acceptance Scenarios**:

1. **Given** sensitive future endpoint groups such as login, registration, refresh, top-up, withdrawal, and interaction, **When** rate-limit policies are registered, **Then** each category has a named policy that can be attached when the endpoint is implemented.
2. **Given** future services need to record authentication-sensitive, admin, financial, or document-review events, **When** they depend on audit logging, **Then** they can call a Core-owned audit logging interface without depending on HTTP or persistence details.
3. **Given** future doctor, company, and admin endpoints require ownership checks, **When** application code needs the authenticated principal, **Then** it can use a current-user context abstraction and ownership helper without placing business authorization rules in controllers.

### Edge Cases

- A client sends an invalid or missing `X-Correlation-ID`; the system must generate a valid correlation identifier and return it in the response header.
- An exception occurs before controller action execution; the response must still use the standard safe envelope.
- A legacy endpoint has custom response formatting; Phase 1 must standardize envelope shape without changing business decision outcomes.
- Existing clients depend on current HTTP status handling; status codes must remain unchanged while adding envelope fields.
- API documentation configuration is accidentally enabled outside development; deployment validation must detect and reject this state.
- Rate-limit policy names drift between endpoint groups; startup/test validation must detect missing policies before sensitive routes are added.
- Audit logging abstractions accidentally include HTTP-only or persistence-only dependencies; layer-boundary checks must detect the violation.
- Current-user context is accessed when no authenticated user exists; helper behavior must return an anonymous/empty context rather than throwing in public routes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST enforce a standard response envelope `{ Code, Message, Data }` for all API responses, including framework-generated validation errors and framework-generated 4xx/5xx responses.
- **FR-001A**: System MUST preserve existing HTTP status code behavior for each response path; envelope adoption MUST NOT normalize all responses to HTTP 200.
- **FR-002**: System MUST ensure at least one representative endpoint demonstrates successful responses using the standard envelope.
- **FR-003**: System MUST process all unhandled exceptions through a single global error handling path.
- **FR-004**: System MUST return a safe, non-sensitive error message for unhandled exceptions and MUST NOT expose stack traces or internal exception details.
- **FR-004A**: For unhandled exceptions in non-development environments, `Message` MUST be exactly `An unexpected error occurred.` and `Data` MUST be `null`; stack traces and internal exception details MUST never be returned.
- **FR-005**: System MUST accept optional client `X-Correlation-ID` only when it matches `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`; when missing or invalid, system MUST generate a server identifier and return it in the response header.
- **FR-006**: System MUST log request lifecycle events (request received, response sent, and outcome) with correlation context using metadata only (method, path, status, duration, correlation identifier).
- **FR-007**: System MUST NOT log request bodies, response bodies, or authentication tokens in Phase 1 request logging.
- **FR-008**: System MUST expose interactive API documentation only in development environments.
- **FR-009**: System MUST provide startup configuration scaffolding for database connection settings and token-auth settings required by later phases.
- **FR-010**: System MUST preserve onion-layer boundaries so HTTP concerns, business orchestration, persistence concerns, and domain concerns remain separated.
- **FR-011**: System MUST keep existing business logic and decision outcomes unchanged while implementing Phase 1 foundations.
- **FR-012**: System MUST keep existing endpoint behavior backward compatible except for standardized envelope wrapping and safe error wrapping.
- **FR-013**: System MUST provide named rate-limiting policy scaffolding for login, registration, refresh, company top-up, doctor withdrawal, and doctor interaction endpoint categories defined in `docs/backend-plan.md`.
- **FR-014**: System MUST ensure rate-limiting failures, where policies are applied, return preserved HTTP 429 semantics using the standard response envelope.
- **FR-015**: System MUST define a Core-owned audit logging abstraction for authentication-sensitive events, admin actions, financial events, and document/file review actions without introducing Phase 1 persistence tables.
- **FR-016**: System MUST define a current-user context abstraction that exposes authenticated user id, role, approval state when available, and anonymous state when no authenticated principal exists.
- **FR-017**: System MUST define ownership authorization helper abstractions for doctor-owned, company-owned, and admin-accessible resources while keeping controllers HTTP-only.
- **FR-018**: System MUST NOT implement Phase 2+ business endpoints, wallet behavior, queue behavior, identity flows, or audit persistence while adding these readiness foundations.

### Constitution Alignment *(mandatory)*

- **CA-001 Layer Mapping**:
  - `FR-001` to `FR-008`, `FR-012` -> `MediBridge.APIs`
  - `FR-009` -> `MediBridge.APIs`, `MediBridge.Services`
  - `FR-010` -> `MediBridge.Core`, `MediBridge.Repository`, `MediBridge.Services`, `MediBridge.APIs`
  - `FR-011` -> all existing business-rule-bearing layers remain authoritative (`MediBridge.Core`, `MediBridge.Services`, `MediBridge.Repository`)
  - `FR-013`, `FR-014`, `FR-016` -> `MediBridge.APIs` registration plus Core/Services abstractions where reusable policy is needed
  - `FR-015`, `FR-017` -> `MediBridge.Core` interfaces with `MediBridge.Services` orchestration readiness
  - `FR-018` -> all layers preserve Phase 1 scope limits
- **CA-002 Controller Boundary**: Controllers remain HTTP-only, with no new business logic added in Phase 1.
- **CA-003 Response Contract**: All API responses, including framework-generated validation and 4xx/5xx responses, conform to `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
- **CA-003A Status Semantics**: HTTP status code semantics remain intact while the standard envelope is applied in the response body.
- **CA-004 Error Handling**: Unhandled errors are returned only through global exception middleware with safe client messages.
- **CA-005 Security**: Existing secured flows continue to require JWT and role-aware authorization where already applicable; Phase 1 adds configuration readiness without changing security decisions, and logging excludes request/response bodies plus authentication tokens.
- **CA-006 Ambiguity Control**: Queue and wallet behavior is out of scope for Phase 1; no queue/wallet ambiguity is introduced in this spec.
- **CA-007 Queue Determinism**: Not in scope for Phase 1.
- **CA-008 Wallet Determinism**: Not in scope for Phase 1.
- **CA-009 Security Readiness**: Rate limiting, audit logging interfaces, current-user context, and ownership helpers are foundation-only in Phase 1 and must not add sensitive business workflows.

### Key Entities *(include if feature involves data)*

- **Response Envelope**: Standard client-facing response object with `Code`, `Message`, and `Data` used across success and failure paths.
- **Correlation Context**: Request-level tracking identifier used to connect client-visible responses with backend logs.
- **Environment Configuration Profile**: Runtime settings set that controls API documentation visibility and stores readiness values for database and token-auth configuration.
- **Rate Limit Policy Set**: Named policy configuration for sensitive endpoint categories that later phases can attach to concrete routes.
- **Audit Event Contract**: Core-owned abstraction describing auditable event categories and metadata without selecting a persistence implementation.
- **Current User Context**: Request-aware identity snapshot used by services and ownership helpers.
- **Ownership Requirement**: Reusable abstraction for confirming doctor/company ownership or admin access in future secured workflows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of sampled API responses in QA, including success, validation failures, and framework-generated 4xx/5xx errors, return the standardized `{ Code, Message, Data }` envelope.
- **SC-002**: 100% of sampled unhandled exception scenarios return `{ Code, Message, Data }` with `Message` set to `An unexpected error occurred.` and no stack trace or internal exception details.
- **SC-003**: 100% of sampled failed requests are traceable from client response header to server log using correlation identifiers.
- **SC-004**: Automated regression suite comparing baseline vs post-change responses for success, validation failure, and unhandled exception flows reports 0 business-outcome deltas (excluding allowed envelope wrapping).
- **SC-005**: In deployment validation, API documentation exposure is enabled in development and disabled in non-development in 100% of environment checks.
- **SC-006**: 100% of sampled Phase 1 request logs include method, path, status, duration, and correlation identifier, and include no request/response body content or authentication tokens.
- **SC-007**: For sampled requests with client `X-Correlation-ID` matching `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`, 100% preserve the same identifier; for missing or invalid values, 100% return a server-generated identifier in the response header.
- **SC-008**: 100% of sampled endpoints preserve pre-existing HTTP status outcomes for equivalent scenarios after Phase 1 changes.
- **SC-009**: For `/weatherforecast`, run 3 post-change runs of 5 minutes each at 10 concurrent users and at least 1000 requests per run under the same environment as baseline; each run MUST keep p95 latency <= 110% of pre-Phase-1 baseline.
- **SC-010**: Automated startup/configuration checks confirm 100% of required named rate-limit policies exist for login, registration, refresh, top-up, withdrawal, and interaction endpoint categories.
- **SC-011**: Architecture checks confirm audit logging, current-user, and ownership abstractions do not introduce outward layer dependencies or controller business logic.
- **SC-012**: Sampled rate-limit rejection behavior, where a policy is applied in test host, returns HTTP 429 with `{ Code, Message, Data }`.

## Assumptions

- Existing business logic is already correct and must not be refactored or behaviorally changed in this phase.
- Phase 1 scope is limited to foundation and cross-cutting backend setup defined in `docs/backend-plan.md`.
- Authentication and role enforcement feature delivery is handled in later phases; this phase only prepares required settings.
- Existing environments can provide required runtime values for database and token-auth configuration before deployment.
- Pre-Phase 1 p95 baseline measurements are available for representative endpoints used in regression checks.
- Queueing, wallet, settlement, and weekly enforcement logic remain out of scope until their dedicated phases.
- Backend plan v1.3 readiness work is limited to reusable scaffolding and policy contracts; concrete sensitive endpoints attach and exercise those policies in later phases.
