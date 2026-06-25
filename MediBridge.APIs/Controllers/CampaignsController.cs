using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/campaigns")]
[Authorize(Policy = AuthorizationPolicies.CompanyOnly)]
public sealed class CampaignsController : ControllerBase
{
    private readonly ICampaignWorkflowService campaignWorkflowService;

    public CampaignsController(ICampaignWorkflowService campaignWorkflowService)
    {
        this.campaignWorkflowService = campaignWorkflowService;
    }

    /// <summary>
    /// Preview eligible priced doctors and estimated campaign cost.
    /// </summary>
    [HttpGet("{campaignId}/target-preview")]
    [ProducesResponseType(typeof(ApiEnvelope<TargetPreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<TargetPreviewDto>>> PreviewTargets(
        string campaignId,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var preview = await campaignWorkflowService.PreviewTargetsAsync(companyUserId, campaignId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", preview));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Get aggregate queue counts for an owned campaign.
    /// </summary>
    [HttpGet("{campaignId}/queue-summary")]
    [ProducesResponseType(typeof(ApiEnvelope<QueueSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<QueueSummaryDto>>> GetQueueSummary(
        string campaignId,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var summary = await campaignWorkflowService.GetQueueSummaryAsync(companyUserId, campaignId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", summary));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Create an owned draft campaign.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<CampaignDto>>> CreateDraft(
        [FromBody] CreateCampaignDraftRequestDto request,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var campaign = await campaignWorkflowService.CreateDraftAsync(companyUserId, request, cancellationToken);
            return StatusCode(
                StatusCodes.Status201Created,
                ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Created", campaign));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Upload campaign media for an owned draft campaign.
    /// </summary>
    [HttpPost("{campaignId}/assets")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignAssetDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiEnvelope<CampaignAssetDto>>> UploadAsset(
        string campaignId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (file is null || file.Length <= 0 || string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.ContentType))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var originalFileName = Path.GetFileName(file.FileName);
        var request = new CampaignAssetUploadRequestDto(
            originalFileName,
            file.ContentType,
            file.Length);

        try
        {
            await using var content = file.OpenReadStream();
            var asset = await campaignWorkflowService.UploadAssetAsync(companyUserId, campaignId, request, content, cancellationToken);
            return StatusCode(
                StatusCodes.Status201Created,
                ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Created", asset));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Replace a pending or rejected campaign media asset for an owned draft campaign.
    /// </summary>
    [HttpPost("{campaignId}/assets/{assetId}/replacement")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignAssetDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiEnvelope<CampaignAssetDto>>> ReplaceAsset(
        string campaignId,
        string assetId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (file is null || file.Length <= 0 || string.IsNullOrWhiteSpace(file.FileName) || string.IsNullOrWhiteSpace(file.ContentType))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var request = new CampaignAssetUploadRequestDto(
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Length);

        try
        {
            await using var content = file.OpenReadStream();
            var asset = await campaignWorkflowService.ReplaceAssetAsync(companyUserId, campaignId, assetId, request, content, cancellationToken);
            return StatusCode(
                StatusCodes.Status201Created,
                ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Created", asset));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Delete a pending or rejected campaign media asset for an owned draft campaign.
    /// </summary>
    [HttpDelete("{campaignId}/assets/{assetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DeleteAsset(
        string campaignId,
        string assetId,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            await campaignWorkflowService.DeleteAssetAsync(companyUserId, campaignId, assetId, cancellationToken);
            return NoContent();
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Submit campaign for admin review and reserve company funds.
    /// </summary>
    [HttpPost("{campaignId}/submit")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<CampaignSubmissionDto>>> Submit(
        string campaignId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            var submission = await campaignWorkflowService.SubmitCampaignAsync(
                companyUserId,
                campaignId,
                idempotencyKey,
                cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", submission));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    private string? GetUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    private static bool IsWorkflowException(Exception exception)
        => exception is WorkflowValidationException
            or WorkflowUnauthorizedException
            or WorkflowForbiddenException
            or WorkflowNotFoundException
            or WorkflowConflictException
            or MediBridge.Core.Interfaces.Files.FileStorageUnavailableException;
}
