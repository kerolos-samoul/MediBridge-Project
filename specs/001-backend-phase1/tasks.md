# Tasks: Backend Foundation and Setup (Phase 1)

**Input**: Design documents from `/specs/001-backend-phase1/`
**Prerequisites**: `plan.md` (required), `spec.md` (required), `research.md`, `data-model.md`, `contracts/foundation-api.yaml`, `quickstart.md`

**Tests**: Included because the specification and quickstart define explicit validation scenarios and measurable outcomes (envelope compliance, safe errors, correlation behavior, Swagger environment policy, rate-limit readiness, security abstraction boundaries, and p95 regression guardrail).

**Constitution Note**: Tasks enforce Onion layering, service-owned business logic, repository/UoW discipline (scaffolded for later phases), JWT configuration readiness, standard API envelope, global exception middleware, rate-limit scaffolding, audit logging interfaces, current-user context, and ownership helper abstractions.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`) for story-phase tasks only
- Every task includes exact file path(s)
- Story labels include `[US1]`, `[US2]`, `[US3]`, and `[US4]`.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create missing project/test scaffolding and establish executable solution layout.

- [X] T001 Create missing class library projects `MediBridge.Core/MediBridge.Core.csproj`, `MediBridge.Repository/MediBridge.Repository.csproj`, and `MediBridge.Services/MediBridge.Services.csproj` and add them to `MediBridge.slnx`
- [X] T002 Create layer folder scaffolding files `MediBridge.Core/Entities/.gitkeep`, `MediBridge.Core/Interfaces/.gitkeep`, `MediBridge.Repository/Data/.gitkeep`, `MediBridge.Repository/Repositories/.gitkeep`, `MediBridge.Repository/UnitOfWork/.gitkeep`, `MediBridge.Services/Interfaces/.gitkeep`, `MediBridge.Services/Services/.gitkeep`, `MediBridge.Services/DTOs/.gitkeep`
- [X] T003 Configure project references in `MediBridge.Repository/MediBridge.Repository.csproj`, `MediBridge.Services/MediBridge.Services.csproj`, and `MediBridge.APIs/MediBridge.APIs.csproj` to maintain inward-only Onion dependencies
- [X] T004 [P] Add/update API package dependencies in `MediBridge.APIs/MediBridge.APIs.csproj`: `Microsoft.AspNetCore.Authentication.JwtBearer` (8.0.*) and confirm `Swashbuckle.AspNetCore` (6.6.2)
- [X] T005 [P] Create contract test project at `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj`
- [X] T006 [P] Create integration test project at `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`
- [X] T007 Add test projects to `MediBridge.slnx` and add API project references in `tests/contract/MediBridge.ContractTests/MediBridge.ContractTests.csproj` and `tests/integration/MediBridge.IntegrationTests/MediBridge.IntegrationTests.csproj`
- [X] T008 [P] Create shared test host bootstrap file `tests/integration/MediBridge.IntegrationTests/TestHost/WebAppFactory.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build cross-cutting pipeline and config foundations required before user-story delivery.

**CRITICAL**: No user story implementation starts before this phase is complete.

- [X] T009 Create response envelope contract `MediBridge.APIs/Contracts/ApiEnvelope.cs`
- [X] T010 [P] Create response envelope factory helper `MediBridge.APIs/Contracts/ApiEnvelopeFactory.cs`
- [X] T011 Create global exception middleware `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs`
- [X] T012 [P] Create correlation middleware `MediBridge.APIs/Middleware/CorrelationIdMiddleware.cs`
- [X] T013 [P] Create metadata-only request logging middleware `MediBridge.APIs/Middleware/RequestLoggingMiddleware.cs`
- [X] T014 Create middleware registration extensions `MediBridge.APIs/Extensions/ApplicationBuilderExtensions.cs`
- [X] T015 [P] Create service registration extensions `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`
- [X] T016 Create configuration option classes `MediBridge.APIs/Config/JwtOptions.cs` and `MediBridge.APIs/Config/DatabaseOptions.cs`
- [X] T017 Update runtime configuration scaffolding in `MediBridge.APIs/appsettings.json` and `MediBridge.APIs/appsettings.Development.json` with `ConnectionStrings` and `Jwt` sections
- [X] T018 Wire foundational services and middleware pipeline order in `MediBridge.APIs/Program.cs`

**Checkpoint**: Foundation ready - user-story phases can begin.

---

## Phase 3: User Story 1 - Standard API Response Contract (Priority: P1) 🎯 MVP

**Goal**: Deliver uniform `{ Code, Message, Data }` responses across success and framework-generated failure paths while preserving HTTP status semantics.

**Independent Test**: Call success and validation-failure endpoints and verify envelope presence plus unchanged HTTP status codes.

### Tests for User Story 1

- [ ] T019 [P] [US1] Add contract test for success envelope response in `tests/contract/MediBridge.ContractTests/ResponseEnvelopeSuccessContractTests.cs`
- [ ] T020 [P] [US1] Add contract test for framework-generated 400 envelope response in `tests/contract/MediBridge.ContractTests/ResponseEnvelopeValidationContractTests.cs`
- [ ] T021 [P] [US1] Add integration test asserting HTTP status preservation in `tests/integration/MediBridge.IntegrationTests/HttpStatusSemanticsTests.cs`

### Implementation for User Story 1

- [ ] T022 [US1] Create forecast query service interface and implementation in `MediBridge.Services/Interfaces/IWeatherForecastQueryService.cs` and `MediBridge.Services/Services/WeatherForecastQueryService.cs`
- [ ] T023 [US1] Configure invalid-model-state envelope mapping in `MediBridge.APIs/Program.cs`
- [ ] T024 [US1] Register `IWeatherForecastQueryService` in `MediBridge.APIs/Program.cs` and delegate action orchestration in `MediBridge.APIs/Controllers/WeatherForecastController.cs`
- [ ] T025 [US1] Align contract examples and response schemas in `specs/001-backend-phase1/contracts/foundation-api.yaml`
- [ ] T026 [US1] Add baseline-vs-post regression suite for success/validation/error flows in `tests/integration/MediBridge.IntegrationTests/BackwardCompatibilityPayloadTests.cs`

**Checkpoint**: User Story 1 is independently functional and testable.

---

## Phase 4: User Story 2 - Safe Global Error Handling (Priority: P1)

**Goal**: Ensure unhandled failures always return safe envelope responses and deterministic correlation tracing.

**Independent Test**: Trigger an unhandled exception and correlation scenarios; verify safe message, envelope shape, and traceable headers/log linkage.

### Tests for User Story 2

- [ ] T027 [P] [US2] Add integration test for unhandled exception safe envelope in `tests/integration/MediBridge.IntegrationTests/UnhandledExceptionEnvelopeTests.cs`
- [ ] T028 [P] [US2] Add integration test for valid-pattern `X-Correlation-ID` propagation in `tests/integration/MediBridge.IntegrationTests/CorrelationPropagationTests.cs`
- [ ] T029 [P] [US2] Add integration test for missing/invalid-pattern `X-Correlation-ID` fallback generation in `tests/integration/MediBridge.IntegrationTests/CorrelationGenerationTests.cs`

### Implementation for User Story 2

- [ ] T030 [US2] Implement safe exception transformation logic in `MediBridge.APIs/Middleware/GlobalExceptionMiddleware.cs` with default non-development message `An unexpected error occurred.` and `Data = null`
- [ ] T031 [US2] Implement correlation ID validation and fallback generation in `MediBridge.APIs/Middleware/CorrelationIdMiddleware.cs`
- [ ] T032 [US2] Add correlation response-header writing behavior in `MediBridge.APIs/Middleware/CorrelationIdMiddleware.cs`
- [ ] T033 [US2] Add controlled exception injection in test host `tests/integration/MediBridge.IntegrationTests/TestHost/WebAppFactory.cs` to trigger unhandled-exception scenarios without exposing a diagnostics controller route
- [ ] T034 [US2] Enforce metadata-only logging (no body/token fields) in `MediBridge.APIs/Middleware/RequestLoggingMiddleware.cs`

**Checkpoint**: User Story 2 is independently functional and testable.

---

## Phase 5: User Story 3 - Phase 1 Operational Baseline (Priority: P2)

**Goal**: Deliver environment-safe Swagger policy, config scaffolding readiness, and architecture baseline checks for future phases.

**Independent Test**: Verify Swagger appears only in Development, config binds correctly, and layer-boundary scaffolding is in place without new business behavior.

### Tests for User Story 3

- [X] T035 [P] [US3] Add integration test for Development-only Swagger exposure in `tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs`
- [X] T036 [P] [US3] Add configuration binding test for `JwtOptions` and `DatabaseOptions` in `tests/integration/MediBridge.IntegrationTests/ConfigurationBindingTests.cs`
- [X] T037 [P] [US3] Add p95 regression harness in `tests/performance/phase1-p95-regression.ps1` using fixed profile (3 runs, 5 minutes, 10 concurrent users, >=1000 requests/run)

### Implementation for User Story 3

- [X] T038 [US3] Add configuration binding and validation wiring in `MediBridge.APIs/Program.cs` for `MediBridge.APIs/Config/JwtOptions.cs` and `MediBridge.APIs/Config/DatabaseOptions.cs`
- [X] T039 [US3] Finalize Swagger environment guard behavior in `MediBridge.APIs/Program.cs`
- [X] T040 [US3] Add architecture test in `tests/integration/MediBridge.IntegrationTests/LayeringBoundaryTests.cs` asserting controllers delegate to services and contain no business orchestration logic
- [X] T041 [US3] Add operational logging format verification test in `tests/integration/MediBridge.IntegrationTests/RequestLoggingPolicyTests.cs`
- [X] T042 [US3] Update executable verification steps for this story in `specs/001-backend-phase1/quickstart.md`

**Checkpoint**: User Story 3 is independently functional and testable.

---

## Phase 6: User Story 4 - Security and Ownership Readiness (Priority: P2)

**Goal**: Add backend plan v1.3 foundation scaffolding for rate limiting, audit logging interfaces, current-user context, and ownership helper abstractions without implementing Phase 2+ business workflows.

**Independent Test**: Verify named rate-limit policies, 429 envelope behavior, and layer-safe abstractions exist for later authentication, wallet, withdrawal, campaign, and interaction endpoints.

### Tests for User Story 4

- [X] T043 [P] [US4] Add tests proving required rate-limit policy names are registered in `tests/integration/MediBridge.IntegrationTests/RateLimitPolicyRegistrationTests.cs`
- [X] T044 [P] [US4] Add test-host 429 envelope verification in `tests/integration/MediBridge.IntegrationTests/RateLimitEnvelopeTests.cs`
- [X] T045 [P] [US4] Add architecture tests for audit/current-user/ownership layer boundaries in `tests/integration/MediBridge.IntegrationTests/SecurityReadinessBoundaryTests.cs`

### Implementation for User Story 4

- [X] T046 [US4] Create rate-limit policy names/options in `MediBridge.APIs/Config/RateLimitOptions.cs` for login, registration, refresh, company top-up, doctor withdrawal, and doctor interaction categories
- [X] T047 [US4] Register named rate-limit policies and standard 429 envelope rejection behavior in `MediBridge.APIs/Program.cs` and `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`
- [X] T048 [US4] Create audit event contracts and logger interface in `MediBridge.Core/Interfaces/IAuditLogger.cs`
- [X] T049 [US4] Create current-user context contract in `MediBridge.Core/Interfaces/ICurrentUserContext.cs`
- [X] T050 [US4] Create ownership authorization helper contract in `MediBridge.Core/Interfaces/IOwnershipAuthorizationService.cs`
- [X] T051 [US4] Implement API current-user adapter in `MediBridge.APIs/Security/HttpCurrentUserContext.cs`
- [X] T052 [US4] Add service registration for current-user/audit/ownership abstractions in `MediBridge.APIs/Extensions/ServiceCollectionExtensions.cs`
- [X] T053 [US4] Update executable verification steps for this story in `specs/001-backend-phase1/quickstart.md`

**Checkpoint**: User Story 4 is independently functional and testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, compliance evidence, and cross-story readiness checks.

- [X] T054 [P] Run contract and integration test suite against `MediBridge.slnx` and record output notes in `specs/001-backend-phase1/quickstart.md`
- [X] T055 [P] Validate implemented API behavior against `specs/001-backend-phase1/contracts/foundation-api.yaml` and update mismatched examples in the same file
- [ ] T056 Execute end-to-end quickstart and regression suite, then append endpoint-by-endpoint diff summary and final pass/fail evidence in `specs/001-backend-phase1/quickstart.md`
- [ ] T057 Run baseline/post comparison using `tests/performance/phase1-p95-regression.ps1` and append per-run p95 table plus pass/fail decision to `specs/001-backend-phase1/research.md`
- [X] T058 Perform final constitution gate evidence update in `specs/001-backend-phase1/plan.md`
- [X] T059 Remove diagnostics-controller assumptions and verify no new diagnostic routes are exposed in production by asserting route surface in `tests/integration/MediBridge.IntegrationTests/UnhandledExceptionEnvelopeTests.cs`
- [X] T060 Validate `specs/001-backend-phase1/` against `docs/backend-plan.md` v1.3 and confirm Phase 1 includes rate-limit/audit/current-user/ownership readiness without queue, wallet, settlement, identity, or audit-persistence implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies; start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1; blocks all user-story work.
- **Phase 3 (US1)**: Depends on Phase 2.
- **Phase 4 (US2)**: Depends on Phase 2 (can run in parallel with US1 if staffed).
- **Phase 5 (US3)**: Depends on Phase 2; recommended after US1/US2 for lower risk.
- **Phase 6 (US4)**: Depends on Phase 2; recommended before Phase 2+ backend feature work because it provides shared security-readiness scaffolding.
- **Phase 7 (Polish)**: Depends on all selected user stories being complete.

### User Story Completion Order

- **MVP path**: US1 only (after Setup + Foundational)
- **Recommended full order**: US1 -> US2 -> US3 -> US4
- **Parallel-capable order**: US1 and US2 in parallel after Phase 2, then US3 and US4 in parallel, then Polish

### Within Each User Story

- Write tests first for that story, then implement middleware/controller/config logic.
- Keep story tasks isolated to listed files to preserve independent testability.

### Parallel Opportunities

- Setup: `T004`, `T005`, `T006`, `T008`
- Foundational: `T010`, `T012`, `T013`, `T015`
- US1 tests: `T019`, `T020`, `T021`
- US2 tests: `T027`, `T028`, `T029`
- US3 tests/harness: `T035`, `T036`, `T037`
- US4 tests: `T043`, `T044`, `T045`
- Polish: `T054`, `T055`

---

## Parallel Example: User Story 1

```bash
# Run US1 tests in parallel
Task T019 - tests/contract/MediBridge.ContractTests/ResponseEnvelopeSuccessContractTests.cs
Task T020 - tests/contract/MediBridge.ContractTests/ResponseEnvelopeValidationContractTests.cs
Task T021 - tests/integration/MediBridge.IntegrationTests/HttpStatusSemanticsTests.cs
```

## Parallel Example: User Story 2

```bash
# Run US2 tests in parallel
Task T027 - tests/integration/MediBridge.IntegrationTests/UnhandledExceptionEnvelopeTests.cs
Task T028 - tests/integration/MediBridge.IntegrationTests/CorrelationPropagationTests.cs
Task T029 - tests/integration/MediBridge.IntegrationTests/CorrelationGenerationTests.cs
```

## Parallel Example: User Story 3

```bash
# Run US3 readiness checks in parallel
Task T035 - tests/integration/MediBridge.IntegrationTests/SwaggerEnvironmentPolicyTests.cs
Task T036 - tests/integration/MediBridge.IntegrationTests/ConfigurationBindingTests.cs
Task T037 - tests/performance/phase1-p95-regression.ps1
```

## Parallel Example: User Story 4

```bash
# Run US4 readiness checks in parallel
Task T043 - tests/integration/MediBridge.IntegrationTests/RateLimitPolicyRegistrationTests.cs
Task T044 - tests/integration/MediBridge.IntegrationTests/RateLimitEnvelopeTests.cs
Task T045 - tests/integration/MediBridge.IntegrationTests/SecurityReadinessBoundaryTests.cs
```

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete US1 tasks (`T019`-`T026`).
3. Validate envelope + status semantics before moving on.

### Incremental Delivery

1. Foundation complete (`T001`-`T018`).
2. Deliver US1 (`T019`-`T026`) and validate.
3. Deliver US2 (`T027`-`T034`) and validate.
4. Deliver US3 (`T035`-`T042`) and validate.
5. Deliver US4 (`T043`-`T053`) and validate.
6. Finish cross-cutting polish (`T054`-`T060`).

### Notes for Clear Execution (for smaller/cheaper models)

- Execute tasks strictly by ID order unless `[P]` is present.
- Do not start any `[US*]` task before finishing all Phase 2 tasks.
- When a task lists multiple files, update all listed files in one logical change.
- Use `quickstart.md` and contract file as the acceptance source of truth during implementation.
