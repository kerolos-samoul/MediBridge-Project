using System.Security.Claims;
using FluentValidation;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController : ControllerBase
{
    private readonly IFileWorkflowService fileWorkflowService;

    public FilesController(IFileWorkflowService fileWorkflowService)
    {
        this.fileWorkflowService = fileWorkflowService;
    }

    [HttpPost("verification-documents")]
    [EnableRateLimiting(RateLimitPolicyNames.FileUpload)]
    public async Task<ActionResult<ApiEnvelope<FileDto>>> UploadVerificationDocument([FromForm] VerificationDocumentUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request.File is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var upload = new FileWorkflowUpload(request.File.FileName, request.File.ContentType, request.File.Length, stream);
            var result = await fileWorkflowService.UploadVerificationDocumentAsync(actorUserId, role, upload, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
    }

    [HttpPost("{fileId}/access")]
    public async Task<ActionResult<ApiEnvelope<FileAccessGrantDto>>> CreatePrivateAccessGrant(string fileId, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        try
        {
            var result = await fileWorkflowService.CreatePrivateAccessGrantAsync(actorUserId, role, fileId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpGet("{fileId}")]
    public async Task<ActionResult<ApiEnvelope<FileAccessGrantDto>>> GetPrivateAccessGrant(
        string fileId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        try
        {
            var result = await fileWorkflowService.CreatePrivateAccessGrantAsync(actorUserId, role, fileId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpDelete("{fileId}")]
    public async Task<ActionResult<ApiEnvelope<DeleteFileResultDto>>> DeleteFile(string fileId, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        try
        {
            var result = await fileWorkflowService.DeleteFileAsync(actorUserId, role, fileId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpPost("{fileId}/replacement")]
    [EnableRateLimiting(RateLimitPolicyNames.FileUpload)]
    public async Task<ActionResult<ApiEnvelope<FileDto>>> ReplaceFile(string fileId, [FromForm] FileReplacementUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request.File is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var upload = new FileWorkflowUpload(request.File.FileName, request.File.ContentType, request.File.Length, stream);
            var result = await fileWorkflowService.ReplaceFileAsync(actorUserId, role, fileId, upload, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpPost("~/api/campaigns/{campaignId}/files")]
    [Authorize(Policy = AuthorizationPolicies.CompanyCampaignFileUpload)]
    [EnableRateLimiting(RateLimitPolicyNames.FileUpload)]
    public async Task<ActionResult<ApiEnvelope<FileDto>>> UploadCampaignFile(string campaignId, [FromForm] CampaignFileUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out var role, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request.File is null ||
            !Enum.TryParse<StoredFilePurpose>(request.Purpose, ignoreCase: true, out var purpose) ||
            purpose is not (StoredFilePurpose.CampaignMedia or StoredFilePurpose.VoiceNote or StoredFilePurpose.ClinicalResearchAttachment))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            await using var stream = request.File.OpenReadStream();
            var upload = new FileWorkflowUpload(request.File.FileName, request.File.ContentType, request.File.Length, stream);
            var result = await fileWorkflowService.UploadCampaignFileAsync(actorUserId, campaignId, purpose, upload, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpPost("~/api/company/campaigns/{campaignId}/assets")]
    [Authorize(Policy = AuthorizationPolicies.CompanyCampaignFileUpload)]
    [EnableRateLimiting(RateLimitPolicyNames.FileUpload)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiEnvelope<FileDto>>> UploadCampaignAsset(
        string campaignId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out _, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (file is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var upload = new FileWorkflowUpload(file.FileName, file.ContentType, file.Length, stream);
            var result = await fileWorkflowService.UploadCampaignFileAsync(
                actorUserId,
                campaignId,
                StoredFilePurpose.CampaignMedia,
                upload,
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpPost("~/api/company/campaigns/{campaignId}/assets/{assetId}/replacement")]
    [Authorize(Policy = AuthorizationPolicies.CompanyCampaignFileUpload)]
    [EnableRateLimiting(RateLimitPolicyNames.FileUpload)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiEnvelope<FileDto>>> ReplaceCampaignAsset(
        string campaignId,
        string assetId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out _, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (file is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var upload = new FileWorkflowUpload(file.FileName, file.ContentType, file.Length, stream);
            var result = await fileWorkflowService.ReplaceCampaignFileAsync(
                actorUserId,
                campaignId,
                assetId,
                upload,
                cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    [HttpDelete("~/api/company/campaigns/{campaignId}/assets/{assetId}")]
    [Authorize(Policy = AuthorizationPolicies.CompanyCampaignFileUpload)]
    public async Task<IActionResult> DeleteCampaignAsset(
        string campaignId,
        string assetId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var actorUserId, out _, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        try
        {
            await fileWorkflowService.DeleteCampaignFileAsync(actorUserId, campaignId, assetId, cancellationToken);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }

    private bool TryGetActor(out string actorUserId, out UserRole role, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        role = default;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return false;
        }

        if (!TryGetRole(out role))
        {
            unauthorizedResult = StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
            return false;
        }

        return true;
    }

    private bool TryGetRole(out UserRole role)
    {
        role = default;
        var roleValue = User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse(roleValue, ignoreCase: true, out role);
    }
}

public sealed class VerificationDocumentUploadRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class FileReplacementUploadRequest
{
    public IFormFile File { get; set; } = null!;
}

public sealed class CampaignFileUploadRequest
{
    public string Purpose { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
