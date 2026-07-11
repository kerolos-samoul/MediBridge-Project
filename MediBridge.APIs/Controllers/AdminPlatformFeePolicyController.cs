using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Pricing;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/platform-fee-policy")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class AdminPlatformFeePolicyController : ControllerBase
{
    private readonly IAdminPlatformFeePolicyService platformFeePolicyService;

    public AdminPlatformFeePolicyController(IAdminPlatformFeePolicyService platformFeePolicyService)
    {
        this.platformFeePolicyService = platformFeePolicyService;
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(ApiEnvelope<PlatformFeePolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<PlatformFeePolicyDto?>>> GetCurrent(CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await platformFeePolicyService.GetCurrentAsync(adminUserId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPut]
    [ProducesResponseType(typeof(ApiEnvelope<PlatformFeePolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<PlatformFeePolicyDto>>> Set(
        [FromBody] SetPlatformFeePolicyRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await platformFeePolicyService.SetAsync(adminUserId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(
            ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return !string.IsNullOrWhiteSpace(actorUserId);
    }
}
