# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# / .NET 8 (MUST unless constitution is amended)  
**Primary Dependencies**: ASP.NET Core Web API, DI, JWT Bearer auth, Swagger/OpenAPI, Entity Framework Core SQL Server  
**Storage**: SQL Server via EF Core implementations in `MediBridge.Repository` behind Repository + Unit of Work abstractions (MUST)  
**Testing**: `dotnet test` with project-selected .NET test framework  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: [Feature-specific measurable targets]  
**Constraints**: [Role security, latency, compliance, and operational limits]  
**Scale/Scope**: [Expected users, endpoints, data volume, and rollout scope]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- Layering gate: Changes map cleanly to `MediBridge.Core`, `MediBridge.Repository`,
  `MediBridge.Services`, and `MediBridge.APIs` with inward-only dependencies.
- Controller gate: Controllers contain HTTP-only concerns and delegate business logic
  to services.
- Data gate: Repository and Unit of Work patterns are used for persistence changes;
  SQL persistence targets SQL Server through EF Core in `MediBridge.Repository`;
  direct data access from controllers is absent.
- Security gate: Secured routes specify JWT authentication and role-aware
  authorization requirements.
- API contract gate: Standard response envelope and global exception middleware impact
  are defined.
- Scope gate: Out-of-scope items are listed and queue/wallet ambiguities are resolved
  before implementation tasks are generated.
- Queue gate: Queue ordering, daily limits, and day-end expiry behavior are explicitly
  defined and testable.
- Wallet gate: Debit/credit rules, platform fee behavior, and transaction atomicity are
  explicitly defined and testable.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
MediBridge.Core/
├── Entities/
├── Interfaces/
└── [Domain Logic]

MediBridge.Repository/
├── Data/
├── Repositories/
└── UnitOfWork/

MediBridge.Services/
├── Interfaces/
├── Services/
└── DTOs/

MediBridge.APIs/
├── Controllers/
├── Middleware/
└── Config/

tests/
├── contract/
├── integration/
└── unit/
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
