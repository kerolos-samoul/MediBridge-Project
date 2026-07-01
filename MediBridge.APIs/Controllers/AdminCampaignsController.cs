using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminCampaignsController : ControllerBase
{
    private readonly IAdminCampaignReviewService reviewService;

    public AdminCampaignsController(IAdminCampaignReviewService reviewService)
    {
        this.reviewService = reviewService;
    }

    /// <summary>
    /// Lists submitted campaigns awaiting moderation in submission order.
    /// </summary>
    [HttpGet("campaigns/pending-review")]
    [ProducesResponseType(typeof(ApiEnvelope<PendingCampaignPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<PendingCampaignPageDto>>> ListPendingCampaigns(
        [FromQuery(Name = "PageNumber")] int pageNumber = 1,
        [FromQuery(Name = "PageSize")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var page = await reviewService.ListPendingCampaignsAsync(adminUserId, pageNumber, pageSize, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", page));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Opens a submitted campaign's protected review package with short-lived signed file access.
    /// </summary>
    [HttpGet("campaigns/{campaignId}/review-detail")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignReviewDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiEnvelope<CampaignReviewDetailDto>>> GetReviewDetail(
        string campaignId,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var detail = await reviewService.GetReviewDetailAsync(adminUserId, campaignId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", detail));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Approve or reject a campaign asset.
    /// </summary>
    [HttpPost("campaign-assets/{assetId}/review")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignAssetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<CampaignAssetDto>>> ReviewAsset(
        string assetId,
        [FromBody] ReviewDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var asset = await reviewService.ReviewAssetAsync(adminUserId, assetId, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", asset));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Records an idempotent approval, rejection, or revision-required decision for a submitted campaign.
    /// </summary>
    [HttpPost("campaigns/{campaignId}/review")]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignReviewResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<CampaignReviewResultDto>>> ReviewCampaign(
        string campaignId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] ReviewDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            var result = await reviewService.ReviewCampaignAsync(adminUserId, campaignId, idempotencyKey, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Lists doctor-level queue rows for a campaign in deterministic submission and queue order.
    /// </summary>
    [HttpGet("campaigns/{campaignId}/queue")]
    [ProducesResponseType(typeof(ApiEnvelope<IReadOnlyList<QueueRowDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<IReadOnlyList<QueueRowDto>>>> GetQueue(
        string campaignId,
        CancellationToken cancellationToken)
    {
        var adminUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var rows = await reviewService.GetQueueRowsAsync(adminUserId, campaignId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", rows));
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
            or FileStorageUnavailableException;
}
