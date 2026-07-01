using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/campaigns")]
[Authorize(Policy = AuthorizationPolicies.Phase5CompanyCampaignAccess)]
public sealed class CompanyCampaignsController : ControllerBase
{
    private readonly ICampaignWorkflowService campaignWorkflowService;

    public CompanyCampaignsController(ICampaignWorkflowService campaignWorkflowService)
    {
        this.campaignWorkflowService = campaignWorkflowService;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    public async Task<ActionResult<ApiEnvelope<CampaignPageDto>>> GetCampaigns([FromQuery] CampaignStatus? status, [FromQuery] int PageNumber = 1, [FromQuery] int PageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await campaignWorkflowService.GetCompanyCampaignsAsync(actorUserId, status, PageNumber, PageSize, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{campaignId}")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    public async Task<ActionResult<ApiEnvelope<CampaignDetailDto>>> GetCampaignDetail(string campaignId, CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await campaignWorkflowService.GetCompanyCampaignDetailAsync(actorUserId, campaignId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{campaignId}/target-preview")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    [ProducesResponseType(typeof(ApiEnvelope<TargetPreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<TargetPreviewDto>>> GetTargetPreview(
        string campaignId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await campaignWorkflowService.PreviewTargetsAsync(
            actorUserId,
            campaignId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{campaignId}/queue-summary")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    [ProducesResponseType(typeof(ApiEnvelope<QueueSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<QueueSummaryDto>>> GetQueueSummary(
        string campaignId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await campaignWorkflowService.GetQueueSummaryAsync(
            actorUserId,
            campaignId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPut("{campaignId}")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<CampaignDraftDto>>> UpdateCampaign(
        string campaignId,
        [FromBody] UpdateCampaignRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await campaignWorkflowService.UpdateCampaignAsync(
            actorUserId,
            campaignId,
            request,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("{campaignId}/submit")]
    [EnableRateLimiting(RateLimitPolicyNames.Phase5CampaignSubmission)]
    [ProducesResponseType(typeof(ApiEnvelope<CampaignSubmissionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<CampaignSubmissionDto>>> SubmitExistingCampaign(
        string campaignId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
        var result = await campaignWorkflowService.SubmitCampaignAsync(
            actorUserId,
            campaignId,
            idempotencyKey,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{campaignId}/review-outcome")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    [ProducesResponseType(typeof(ApiEnvelope<CompanyReviewOutcomeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<CompanyReviewOutcomeDto>>> GetReviewOutcome(
        string campaignId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await campaignWorkflowService.GetReviewOutcomeAsync(
            actorUserId,
            campaignId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return string.IsNullOrWhiteSpace(actorUserId) is false;
    }
}
