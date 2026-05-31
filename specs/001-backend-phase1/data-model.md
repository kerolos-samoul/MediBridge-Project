# Data Model: Backend Foundation and Setup (Phase 1)

## Overview

Phase 1 introduces operational API-contract and security-readiness entities rather than business-domain entities. No queue, wallet, campaign, settlement, identity, or audit-persistence entities are introduced in this phase.

## Entities

### 1) ResponseEnvelope

- Purpose: Canonical response payload wrapper returned for all API outcomes.
- Fields:
  - `Code` (integer, required): Application-level outcome code corresponding to success/error intent.
  - `Message` (string, required): Human-readable summary for clients.
  - `Data` (object or null, required): Business payload for success or `null` for failures/no payload scenarios.
- Validation rules:
  - `Code` MUST be present on all responses.
  - `Message` MUST be present and non-empty for both success and failure responses.
  - `Data` MUST always be present as a property, nullable when no payload is applicable.
- Relationships:
  - Wraps all controller and middleware-originated responses.

### 2) CorrelationContext

- Purpose: Request-level trace context used to correlate client responses and server logs.
- Fields:
  - `CorrelationId` (string, required): Effective correlation identifier for the request lifecycle.
  - `Source` (enum-like string, required): `ClientProvided` or `ServerGenerated`.
  - `RequestMethod` (string, required): HTTP method for metadata logging.
  - `RequestPath` (string, required): Request path for metadata logging.
  - `ResponseStatus` (integer, required): Final HTTP status code.
  - `DurationMs` (number, required): End-to-end request processing duration.
- Validation rules:
  - If request header `X-Correlation-ID` is valid, `CorrelationId` MUST equal header value.
  - If request header is missing/invalid, system MUST generate `CorrelationId` and include it in response header.
  - `CorrelationId` MUST be included in all request lifecycle log events.
- Relationships:
  - 1:1 with each handled HTTP request.
  - Referenced by logging and error response generation.

### 3) ApiErrorDescriptor

- Purpose: Safe error representation generated for unhandled exceptions.
- Fields:
  - `Code` (integer, required): Error outcome code represented in `ResponseEnvelope`.
  - `Message` (string, required): Safe, non-sensitive client-facing error message.
  - `CorrelationId` (string, required): Trace token tied to failure instance.
  - `HttpStatus` (integer, required): Preserved protocol status code.
- Validation rules:
  - Must never include stack traces or internal exception details.
  - Must map into `ResponseEnvelope` with `Data = null` unless explicit non-sensitive payload is defined.
- Relationships:
  - Produced by global exception middleware.
  - Serialized into response via `ResponseEnvelope`.

### 4) RuntimeConfigurationProfile

- Purpose: Runtime configuration contract required for Phase 1 foundation behavior and future phase readiness.
- Fields:
  - `ConnectionStrings:DefaultConnection` (string, optional in local dev, required for deployed environments)
  - `Jwt:Issuer` (string, required placeholder for Phase 1)
  - `Jwt:Audience` (string, required placeholder for Phase 1)
  - `Jwt:SigningKey` (string, required placeholder for Phase 1)
  - `Swagger:EnabledInDevelopmentOnly` (boolean, required)
  - `Logging:MetadataOnly` (boolean, required)
- Validation rules:
  - Production-like environments MUST disable Swagger endpoints.
  - Logging policy MUST enforce metadata-only mode.
- Relationships:
  - Consumed by API startup and middleware wiring.

### 5) RateLimitPolicyDescriptor

- Purpose: Named policy description for sensitive endpoint categories added by backend plan v1.3.
- Fields:
  - `PolicyName` (string, required): Stable name used by endpoint registration.
  - `EndpointCategory` (string, required): Login, Registration, Refresh, CompanyTopUp, DoctorWithdrawal, or DoctorInteraction.
  - `PermitLimit` (integer, required): Maximum allowed requests in the configured window.
  - `WindowSeconds` (integer, required): Evaluation window for the policy.
  - `QueueLimit` (integer, required): Maximum queued requests, normally zero for sensitive operations.
  - `RejectionStatusCode` (integer, required): Must be 429 when enforced.
- Validation rules:
  - All backend plan v1.3 sensitive endpoint categories MUST have a policy descriptor.
  - Policy rejection responses MUST use `ResponseEnvelope` with preserved HTTP 429 semantics when a policy is attached.
- Relationships:
  - Registered by API startup.
  - Referenced by later auth, wallet, withdrawal, and interaction endpoints.

### 6) AuditEventContract

- Purpose: Core-owned contract for recording auditable events in later phases without choosing a Phase 1 persistence implementation.
- Fields:
  - `Category` (string, required): AuthenticationSensitive, AdminAction, Financial, DocumentReview, or System.
  - `Action` (string, required): Stable action name.
  - `ActorUserId` (string or null): Authenticated actor when available.
  - `ActorRole` (string or null): Actor role when available.
  - `SubjectType` (string or null): Resource type being acted on.
  - `SubjectId` (string or null): Resource identifier being acted on.
  - `CorrelationId` (string or null): Request trace identifier when available.
  - `CreatedAtUtc` (datetime, required): Event creation time.
- Validation rules:
  - Must not require HTTP framework types in Core.
  - Must not include request body, response body, auth tokens, or sensitive file content.
- Relationships:
  - Used by Services and later Repository implementations through an audit logger interface.

### 7) CurrentUserContext

- Purpose: Request-aware identity snapshot for authorization and ownership checks.
- Fields:
  - `IsAuthenticated` (boolean, required)
  - `UserId` (string or null)
  - `Role` (string or null): Doctor, Company, Admin, or null.
  - `IsApproved` (boolean or null): Available once identity claims provide it.
  - `CorrelationId` (string or null)
- Validation rules:
  - Public/anonymous routes MUST receive an anonymous context rather than an exception.
  - Authenticated routes MUST expose user id and role when claims are present.
- Relationships:
  - Adapted from HTTP claims in APIs.
  - Consumed by Services and ownership helpers through abstractions.

### 8) OwnershipRequirement

- Purpose: Reusable description of doctor/company ownership or admin access checks for future secured resources.
- Fields:
  - `ResourceType` (string, required): Delivery, Campaign, Wallet, Settings, File, or other secured resource type.
  - `OwnerType` (string, required): Doctor, Company, Admin, or System.
  - `OwnerId` (string, required): Resource owner identifier.
  - `RequiredRole` (string or null): Role needed when direct ownership is not enough.
- Validation rules:
  - Doctor resources must only pass for the owning doctor or an authorized admin policy.
  - Company resources must only pass for the owning company or an authorized admin policy.
  - Controllers must delegate ownership decisions to helpers/services and remain HTTP-only.
- Relationships:
  - Consumes `CurrentUserContext`.
  - Produces authorization decisions for future secured workflows.

## State Transitions

### Request Lifecycle State

1. `Received` -> request enters pipeline.
2. `Correlated` -> effective correlation ID determined (client-provided valid or server-generated).
3. `Processed` -> controller/middleware produces success or failure outcome.
4. `Wrapped` -> response normalized into `ResponseEnvelope`.
5. `Completed` -> metadata log emitted with status and duration.

### Rate Limit Rejection State

1. `PolicySelected` -> endpoint has a named rate-limit policy attached.
2. `LimitExceeded` -> request exceeds the configured policy.
3. `Rejected` -> HTTP status 429 is selected.
4. `EnvelopeComposed` -> rejection rendered as `ResponseEnvelope` with `Data = null`.
5. `Returned` -> response sent with preserved HTTP 429 and correlation header when available.

### Error Lifecycle State

1. `ExceptionThrown` -> unhandled exception occurs.
2. `ExceptionCaptured` -> global exception middleware captures exception.
3. `Sanitized` -> safe client message chosen; sensitive internals suppressed.
4. `EnvelopeComposed` -> error rendered as `ResponseEnvelope` with `Data = null`.
5. `Returned` -> response sent with preserved HTTP status and correlation header.

## Data Volume and Retention Assumptions

- One `CorrelationContext` record-equivalent per request event stream.
- Logging footprint increases due to metadata per request but excludes high-volume body logging.
- No new persistent domain tables are introduced by Phase 1.
