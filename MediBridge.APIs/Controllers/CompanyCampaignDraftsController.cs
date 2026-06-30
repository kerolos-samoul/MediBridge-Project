using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/campaigns/drafts")]
[Authorize(Policy = AuthorizationPolicies.Phase5CompanyCampaignAccess)]
public sealed class CompanyCampaignDraftsController : ControllerBase
{
    private readonly ICampaignDraftService campaignDraftService;

    public CompanyCampaignDraftsController(ICampaignDraftService campaignDraftService)
    {
        this.campaignDraftService = campaignDraftService;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    public async Task<ActionResult<ApiEnvelope<CampaignDraftDto>>> CreateDraft(
        [FromBody] CreateCampaignDraftRequestDto? request,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var draft = await campaignDraftService.CreateDraftAsync(actorUserId, request, cancellationToken);
        return StatusCode(
            StatusCodes.Status201Created,
            ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", draft));
    }
}
