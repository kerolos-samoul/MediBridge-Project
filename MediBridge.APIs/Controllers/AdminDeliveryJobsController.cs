using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/delivery-jobs")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class AdminDeliveryJobsController : ControllerBase
{
    private readonly IAdminDeliveryJobService adminDeliveryJobService;

    public AdminDeliveryJobsController(IAdminDeliveryJobService adminDeliveryJobService)
    {
        this.adminDeliveryJobService = adminDeliveryJobService;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiEnvelope<DeliveryJobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<DeliveryJobStatusDto>>> GetStatus(CancellationToken cancellationToken)
    {
        var result = await adminDeliveryJobService.GetStatusAsync(cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("run-expiry")]
    [ProducesResponseType(typeof(ApiEnvelope<DeliveryJobEnqueueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public Task<ActionResult<ApiEnvelope<DeliveryJobEnqueueDto>>> RunExpiry(
        [FromBody] RunDeliveryJobRequestDto? request,
        CancellationToken cancellationToken)
    {
        return EnqueueJobAsync(request, adminDeliveryJobService.EnqueueExpiryAsync, cancellationToken);
    }

    [HttpPost("run-injector")]
    [ProducesResponseType(typeof(ApiEnvelope<DeliveryJobEnqueueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public Task<ActionResult<ApiEnvelope<DeliveryJobEnqueueDto>>> RunInjector(
        [FromBody] RunDeliveryJobRequestDto? request,
        CancellationToken cancellationToken)
    {
        return EnqueueJobAsync(request, adminDeliveryJobService.EnqueueInjectorAsync, cancellationToken);
    }

    private async Task<ActionResult<ApiEnvelope<DeliveryJobEnqueueDto>>> EnqueueJobAsync(
        RunDeliveryJobRequestDto? request,
        Func<string, RunDeliveryJobRequestDto, CancellationToken, Task<DeliveryJobEnqueueDto>> operation,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request is null)
        {
            return BadRequest(
                ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await operation(adminUserId, request, cancellationToken);
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
