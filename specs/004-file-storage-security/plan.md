# Implementation Plan: File Storage, Verification & Security Plumbing (Phase 4)

**Branch**: `[004-file-storage-security]` | **Date**: 2026-06-06 | **Spec**: [spec.md](./spec.md)  
**Input**: Feature specification from `/specs/004-file-storage-security/spec.md`

## Summary

Deliver Phase 4 secure file handling by extending the Phase 3 stored-file metadata foundation into a full upload, review, private-access, replacement, delete/unavailable, and storage-provider workflow. The implementation approach keeps file domain records, enums, and repository/service contracts in `MediBridge.Core`; SQL Server metadata, review history, and audit persistence in `MediBridge.Repository`; upload validation, owner authorization, Cloudinary-backed storage abstraction, access-grant creation, review decisions, replacement linking, delete/unavailable handling, retry cleanup, and audit orchestration in `MediBridge.Services`; and HTTP-only file controllers, rate-limit policy wiring, options validation, and response-envelope handling in `MediBridge.APIs`. Phase 4 deliberately excludes campaign submission, queue activation, doctor message viewing, wallet settlement, reporting read models, public galleries, and mandatory malware scanning.

## Technical Context

**Language/Version**: C# / .NET 8  
**Primary Dependencies**: ASP.NET Core Web API foundation, ASP.NET Core Identity/JWT baseline, existing role authorization policies, existing response envelope and global exception middleware, existing fixed-window rate limiting, Entity Framework Core SQL Server, Repository + Unit of Work infrastructure, Cloudinary .NET SDK behind a storage-provider abstraction  
**Storage**: SQL Server through EF Core implementations in `MediBridge.Repository` for file metadata, file review history, access-grant audit records, and file audit events; Cloudinary stores file content using private or authenticated assets; only non-secret storage references are persisted  
**Testing**: `dotnet test .\MediBridge.slnx`; add focused unit, integration, and contract coverage for exact file type/extension validation, upload limits, 20/hour upload rate limiting, secret-only configuration, private access expiry, role/owner authorization, owner soft-delete/rejection restrictions, append-only review history, replacement upload, delete/unavailable behavior, audit safety, API envelope behavior, and layering boundaries  
**Target Platform**: ASP.NET Core HTTP APIs on server-hosted runtime  
**Project Type**: Layered web service (Onion Architecture)  
**Performance Goals**: Valid upload request validation completes before provider upload in 100% of invalid cases; private access grant creation completes in under 1 second for authorized users in integration validation; 100% of private access grants older than 10 minutes are rejected; rate-limit validation rejects upload attempt 21 within one hour  
**Constraints**: Keep `MediBridge.Core` free of EF Core, ASP.NET Core HTTP, and Cloudinary SDK types; keep controllers HTTP-only; do not store `CLOUDINARY_URL`, API secret, provider credential URLs, signed URLs, or private access tokens in appsettings, source-controlled files, logs, API responses, audit events, or database records; Cloudinary configuration must use secret configuration/environment variables; all SQL persistence goes through Repository + Unit of Work; upload limits are documents 10 MB, images 10 MB, audio 25 MB, video 100 MB; allowed MIME types are `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `image/jpeg`, `image/png`, `audio/mpeg`, and `video/mp4`; allowed extensions are `.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, and `.mp4`; private file grants expire after 10 minutes; safety scanning is deferred and scan status is metadata only  
**Scale/Scope**: Phase 4 adds file upload/retrieval/review/replacement/delete APIs, service abstractions, SQL metadata/history extensions, provider integration, and tests; it builds on Phase 3 stored-file metadata and does not implement campaign submission, queue jobs, delivery workflows, wallet settlement, reporting, public galleries, or mandatory malware scanning

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Phase 0 Gate Assessment

- Layering gate: PASS - Core owns file entities/enums/contracts, Repository owns SQL Server/EF Core mappings and repositories, Services owns upload/review/storage orchestration, and APIs owns HTTP controllers/options/rate-limit wiring.
- Controller gate: PASS - Planned controllers delegate validation, authorization, storage, review, and persistence work to services.
- Data gate: PASS - File metadata, review history, replacement links, deletion/unavailable state, scan status, and audit events persist through SQL Server/EF Core in `MediBridge.Repository`, behind Repository + Unit of Work abstractions.
- Security gate: PASS - All file APIs require JWT and Doctor, Pharmaceutical Company, or Admin role-aware authorization; storage secrets are secret-only and never tracked.
- API contract gate: PASS - File endpoints use the standard `{ "Code": <int>, "Message": <string>, "Data": <object|null> }` envelope and global exception handling.
- Scope gate: PASS - Spec explicitly excludes campaign submission, queue activation, doctor message viewing, wallet settlement, reporting read models, public file galleries, and required malware scanning.
- Queue gate: PASS - Queueing is out of scope; file readiness exposes approved/unavailable state only for later queue workflows.
- Wallet gate: PASS - Walleting is out of scope; no debit, credit, fee, transaction, or settlement behavior is added.

### Post-Phase 1 Design Re-check

- Layering gate: PASS - `data-model.md`, `contracts/file-storage-api.yaml`, `contracts/service-contracts.md`, and `quickstart.md` keep provider and persistence details out of controllers and Core.
- Controller gate: PASS - API contracts define HTTP-only request/response surfaces; business decisions are service responsibilities.
- Data gate: PASS - Data model extends `StoredFile` and adds review/access/replacement/delete/audit metadata with EF Core mapping and repository changes isolated in Repository.
- Security gate: PASS - Design requires JWT/role policies, owner checks, 10-minute grants, secret-only provider configuration, no signed-token persistence, and no raw provider diagnostics in user-visible responses.
- API contract gate: PASS - Contracts retain the standard response envelope and explicit validation/authorization failure semantics.
- Scope gate: PASS - Design artifacts retain Phase 4 exclusions and defer mandatory safety scanning.
- Queue gate: PASS - Queue behavior remains out of scope; readiness checks only expose file availability state for future workflows.
- Wallet gate: PASS - Wallet behavior remains out of scope and no financial side effects are introduced.

## Project Structure

### Documentation (this feature)

```text
specs/004-file-storage-security/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── file-storage-api.yaml
│   └── service-contracts.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output from /speckit.tasks, not created by /speckit.plan
```

### Source Code (repository root)

```text
MediBridge.Core/
├── Entities/
│   └── Files/
│       ├── StoredFile.cs
│       ├── FileReview.cs
│       └── FileAccessGrantAudit.cs
├── Enums/
│   └── Phase3DomainEnums.cs
└── Interfaces/
    └── Files/
        ├── IStoredFileRepository.cs
        ├── IFileReviewRepository.cs
        └── IFileAccessGrantAuditRepository.cs

MediBridge.Repository/
├── Configurations/
│   └── Files/
│       ├── StoredFileConfiguration.cs
│       ├── FileReviewConfiguration.cs
│       └── FileAccessGrantAuditConfiguration.cs
├── Data/
│   └── MediBridgeDbContext.cs
├── Migrations/
└── Repositories/
    └── Files/
        ├── StoredFileRepository.cs
        ├── FileReviewRepository.cs
        └── FileAccessGrantAuditRepository.cs

MediBridge.Services/
├── Config/
│   ├── FileStorageOptions.cs
│   └── CloudinaryStorageOptions.cs
├── DTOs/
│   └── Files/
├── Interfaces/
│   ├── IFileWorkflowService.cs
│   └── IFileStorageProvider.cs
├── Services/
│   ├── FileWorkflowService.cs
│   └── CloudinaryFileStorageProvider.cs
└── Validators/
    └── Files/

MediBridge.APIs/
├── Config/
│   └── RateLimitOptions.cs
├── Controllers/
│   ├── FilesController.cs
│   └── AdminFilesController.cs
├── Extensions/
│   └── ServiceCollectionExtensions.cs
└── Security/
    └── AuthorizationPolicies.cs

tests/
├── contract/
│   └── MediBridge.ContractTests/
├── integration/
│   └── MediBridge.IntegrationTests/
└── unit/
    └── MediBridge.UnitTests/
```

**Structure Decision**: Use the existing four-project Onion structure. Extend Phase 3 file metadata rather than replacing it. Add public API endpoints only for Phase 4 file workflows, including upload, private access, replacement, delete/unavailable, and admin review. Keep storage-provider calls inside Services, keep provider credentials in secret configuration, and keep SQL metadata/history persistence in Repository. Automated validation uses existing unit, integration, and contract test projects.

## Complexity Tracking

No constitutional violations were identified, so no complexity exceptions are recorded.
