<!--
Sync Impact Report
- Version change: 1.1.0 -> 1.2.0
- Modified principles:
  - I. Onion Layer Separation (NON-NEGOTIABLE): clarified Repository as SQL Server + EF Core persistence boundary
  - III. Repository and Unit of Work Transaction Discipline: explicitly requires EF Core-backed repositories for SQL persistence
  - Technical Standards & Architecture: added SQL Server + Entity Framework Core as required persistence stack
- Added principles:
  - None
- Added sections:
  - None
- Removed sections:
  - None
- Templates requiring updates:
  - ✅ .specify/templates/plan-template.md
  - ✅ .specify/templates/spec-template.md
  - ✅ .specify/templates/tasks-template.md
  - ✅ .github/copilot-instructions.md
  - ✅ reviewed: .opencode/command/*.md (no SQL-specific command drift found)
- Follow-up TODOs:
  - None
-->
# MediBridge Constitution

## Core Principles

### I. Onion Layer Separation (NON-NEGOTIABLE)
MediBridge MUST implement strict Onion Architecture boundaries:
`MediBridge.Core` (entities, interfaces, domain logic), `MediBridge.Repository`
(SQL Server persistence through Entity Framework Core), `MediBridge.Services`
(business orchestration), and `MediBridge.APIs` (HTTP controllers and API wiring).
Dependencies MUST point inward only. `MediBridge.Core` MUST remain free from
infrastructure and HTTP concerns.
Rationale: strict boundaries preserve domain integrity and keep the platform maintainable.

### II. Service-Owned Use Cases and Thin Controllers
Controllers MUST handle only HTTP request/response concerns and MUST delegate all business
decisions to `MediBridge.Services`. Controllers MUST NOT contain queue logic, wallet logic,
pricing logic, approval decisions, or persistence orchestration.
Rationale: thin controllers and centralized services produce consistent and testable behavior.

### III. Repository and Unit of Work Transaction Discipline
All persistence MUST be performed through Repository and Unit of Work abstractions in
`MediBridge.Repository`, defined by contracts in `MediBridge.Core` and consumed by
`MediBridge.Services`. SQL persistence MUST target SQL Server and MUST use Entity
Framework Core in `MediBridge.Repository` as the ORM implementation. Direct SQL,
Entity Framework `DbContext`, or other ORM access from controllers is prohibited.
Wallet and delivery state mutations that belong to one business action MUST be
committed atomically in a single unit of work.
Rationale: disciplined persistence patterns reduce data inconsistency and scaling risk.

### IV. Secure and Uniform API Contract
All secured endpoints MUST use JWT authentication and MUST enforce role-aware authorization
for Doctor, Pharmaceutical Company, and Admin workflows. Every API response MUST use the
envelope `{ "Code": <int>, "Message": <string>, "Data": <object|null> }`.
All unhandled exceptions MUST be transformed by global exception middleware, and raw stack
traces MUST NOT be returned to clients.
Rationale: predictable contracts and centralized failure handling improve reliability.

### V. Deterministic Queue and Wallet Rules
Queue and wallet behavior MUST be explicit and deterministic in every feature:
- Daily delivery MUST enforce each doctor's `DailyMessageLimit`.
- Queue ordering MUST be FIFO by campaign submission time unless a documented priority rule
  is approved.
- Delivered messages for a day MUST expire at day end and MUST NOT auto-carry to the next day.
- Campaign creation MUST validate company wallet sufficiency before acceptance.
- Doctor earnings MUST trigger on any valid interaction outcome (accept or reject).
- `PricePerMessage` MUST be admin-controlled.
- Platform fee MUST be configuration-driven (default 20% unless overridden by admin policy).
Rationale: financial and distribution rules require deterministic behavior for trust and audit.

### VI. Scope-Strict and Clarification-First Delivery
Implementation MUST stay within the requested feature scope. Unrequested functionality MUST
NOT be introduced. Any ambiguity in queue system or wallet logic MUST be clarified before
coding. Code MUST follow .NET naming conventions and SOLID design.
Rationale: scope discipline and early clarifications prevent costly rework.

## Technical Standards & Architecture

- Framework and language MUST be .NET 8 with C#.
- Data storage MUST be SQL Server.
- SQL persistence MUST use Entity Framework Core in `MediBridge.Repository`.
- Repository and Unit of Work abstractions MUST remain the service-facing persistence
  boundary even when EF Core is the underlying implementation.
- Authentication for secured routes MUST be JWT.
- User role model MUST include Doctor, Pharmaceutical Company, and Admin.
- API orchestration MUST reside in `MediBridge.Services`; `MediBridge.APIs` remains HTTP-only.
- Domain contracts and entities MUST remain in `MediBridge.Core`.

## Domain and Financial Directives

- Doctor registration MUST require verification documents and MUST remain unapproved until
  admin review completes.
- Campaign targeting MUST support doctor filters for specialization, experience, location,
  and activity-related eligibility.
- Wallet ledgers MUST record Charge, Earn, Withdraw, and Refund transaction types with
  immutable history entries.
- Weekly minimum interaction policy MUST define escalation states (warning, restriction,
  suspension) before implementation.
- Any optional escrow/hold-funds mode MUST define reservation and release states before merge.

## Development Workflow & Quality Gates

- Every plan MUST pass Constitution Check gates for layering, thin controllers,
  repository/unit-of-work usage, JWT/role enforcement, API envelope compliance,
  exception middleware behavior, and queue/wallet determinism.
- Every specification MUST include explicit constitution alignment and identify queue/wallet
  ambiguities before planning.
- Every tasks list MUST include work items for queue and wallet invariants when those
  domains are in scope.
- Pull requests MUST provide evidence of constitutional compliance or an approved exception.
- Reviews MUST reject non-compliant code until remediated.

## Governance

This constitution supersedes conflicting development practices for MediBridge.

- Amendment Process: Any amendment MUST be proposed in a documented pull request that
  includes changed principles, migration impact, and dependent artifact updates.
  Maintainer approval is required before merge.
- Versioning Policy: Semantic versioning applies. MAJOR for incompatible governance changes,
  MINOR for new principles/sections or materially expanded mandates, PATCH for wording-only
  clarifications.
- Compliance Review: Constitution alignment MUST be verified during planning, during pull
  request review, and before release promotion. Non-compliant changes MUST be blocked or
  remediated before merge.

**Version**: 1.2.0 | **Ratified**: 2026-04-23 | **Last Amended**: 2026-05-31
