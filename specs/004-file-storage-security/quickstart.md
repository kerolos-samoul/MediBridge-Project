# Quickstart: File Storage, Verification & Security Plumbing (Phase 4)

This quickstart validates the Phase 4 planning assumptions and expected implementation surface.

## Prerequisites

- .NET 8 SDK installed.
- SQL Server available locally.
- Current branch: `004-file-storage-security`.
- `MediBridge.APIs/appsettings.Development.json` has `ConnectionStrings:DefaultConnection` pointing at a disposable validation database.
- Cloudinary credentials are available through a secret channel such as an environment variable. Do not put the value in `appsettings.json` or any tracked file.

Example disposable SQL Server connection string:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=MediBridge_Phase4;Trusted_Connection=True;TrustServerCertificate=True"
}
```

Set the Cloudinary secret for the local process without writing it to tracked config:

```powershell
$env:CLOUDINARY_URL = "<secret value from Cloudinary console>"
```

The value pasted earlier in chat should be rotated before real use.

## Validate Tracked Configuration Has No Storage Secret

```powershell
rg -n "cloudinary://|CLOUDINARY_URL|api_secret|api_key" . --glob "!**/.git/**"
```

Expected result:

- No credential URL or secret value appears in tracked configuration.
- References to configuration key names are allowed only in code, tests, or docs when they do not include secret values.

## Validate Build

```powershell
dotnet build .\MediBridge.slnx
```

Expected result:

- Solution builds.
- `MediBridge.Core` has no references to EF Core, ASP.NET Core HTTP types, or Cloudinary SDK types.
- Controllers have no references to Cloudinary SDK types, provider clients, `MediBridgeDbContext`, or repository implementations.

## Apply Migrations

```powershell
dotnet ef database update --project .\MediBridge.Repository --startup-project .\MediBridge.APIs
```

Expected result:

- Phase 4 migration applies cleanly.
- Existing Phase 2 identity/approval and Phase 3 stored-file metadata remain intact.
- File review and access-grant audit tables are present.

## Validate Automated Tests

```powershell
dotnet test .\MediBridge.slnx
```

Focused Phase 4 validation commands:

```powershell
dotnet test .\tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj --filter "FullyQualifiedName~Phase4"
dotnet test .\tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "FullyQualifiedName~Phase4"
dotnet test .\tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "FullyQualifiedName~File"
```

Expected Phase 4 coverage:

- Valid verification uploads create private stored-file metadata.
- Invalid uploads are rejected before provider upload: empty file, unsafe name, unsupported MIME type, unsupported extension, mismatched MIME/extension pair, document over 10 MB, image over 10 MB, audio over 25 MB, and video over 100 MB.
- Only these MIME types are accepted: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `image/jpeg`, `image/png`, `audio/mpeg`, and `video/mp4`.
- Only these extensions are accepted: `.pdf`, `.docx`, `.jpg`, `.jpeg`, `.png`, `.mp3`, and `.mp4`.
- Upload attempt 21 within one hour is rejected for the same authenticated user.
- Missing Cloudinary secret fails validation when uploads are enabled.
- Private file access grants older than 10 minutes are rejected.
- Unauthorized file access attempts return envelope responses without private storage references, provider diagnostics, signed URLs, or secrets.
- Replacement uploads preserve the original file metadata and review history, link the replacement file, make the original unavailable, and set the replacement file to pending review.
- Delete requests make the file unavailable, preserve metadata and review history, call provider deletion through the storage abstraction, and emit a non-secret audit event.
- Files owned by soft-deleted, rejected, suspended, or inactive Doctor/Company owners deny normal non-Admin access and replacement.
- Admin review decisions are append-only and corrections link to the original decision.
- File workflow audit events contain no raw file contents, provider secrets, private tokens, signed URLs, or provider error payloads.
- Architecture tests find zero controller references to provider clients or persistence infrastructure.

## Manual API Smoke Flow

Use an authenticated Doctor or Company token:

```http
POST /api/files/verification-documents
Content-Type: multipart/form-data
Authorization: Bearer <token>
```

Expected result:

- Response envelope has `Code = 201`.
- `Data` includes file id, purpose, current review status, content type, size, and created time.
- `Data` does not include Cloudinary credentials, signed URL, or raw provider diagnostics.

Use an Admin token:

```http
PUT /api/admin/files/{fileId}/review
Content-Type: application/json
Authorization: Bearer <admin-token>

{
  "Decision": "Approved",
  "Reason": "Verification document accepted."
}
```

Expected result:

- Response envelope has `Code = 200`.
- Review history contains the decision.
- Original file metadata remains linked and auditable.

Request a private access grant:

```http
POST /api/files/{fileId}/access
Authorization: Bearer <authorized-token>
```

Expected result:

- Response envelope has `Code = 200`.
- Access grant expires in 10 minutes.
- Stored SQL metadata and audit records do not persist the grant URL or token.

Replace a rejected or outdated file:

```http
POST /api/files/{fileId}/replacement
Content-Type: multipart/form-data
Authorization: Bearer <authorized-owner-token>
```

Expected result:

- Response envelope has `Code = 201`.
- Original file metadata and review history remain available for audit.
- Original file is unavailable for normal evidence/asset use.
- Replacement file is linked to the original and has pending review status.

Delete a file:

```http
DELETE /api/files/{fileId}
Authorization: Bearer <authorized-token>
```

Expected result:

- Response envelope has `Code = 200`.
- File is unavailable for normal access and readiness checks.
- Historical metadata and review records remain auditable.
- Provider deletion is attempted through the storage abstraction.

## Scope Guard

Do not validate later-phase behavior during Phase 4:

- No campaign submission workflow.
- No queue injection or daily delivery activation.
- No doctor message viewing workflow.
- No wallet settlement or top-up behavior.
- No reporting read model.
- No public file gallery.
- No mandatory malware scan gate.
