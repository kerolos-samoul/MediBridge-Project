using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/activity-jobs")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class AdminActivityJobsController : ControllerBase
{
    private readonly IAdminActivityEnforcementService service;

    public AdminActivityJobsController(IAdminActivityEnforcementService service)
    {
        this.service = service;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiEnvelope<IReadOnlyList<ActivityJobRunDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<IReadOnlyList<ActivityJobRunDto>>>> GetStatus(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await service.ListJobStatusAsync(take, adminUserId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("run-score")]
    [ProducesResponseType(typeof(ApiEnvelope<ActivityJobRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public Task<ActionResult<ApiEnvelope<ActivityJobRunDto>>> RunScore(
        [FromBody] RunDailyActivityScoreRequestDto? request,
        CancellationToken cancellationToken)
    {
        return ExecuteJobAsync(request, service.RunDailyScoreAsync, cancellationToken);
    }

    [HttpPost("run-weekly-enforcement")]
    [ProducesResponseType(typeof(ApiEnvelope<ActivityJobRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public Task<ActionResult<ApiEnvelope<ActivityJobRunDto>>> RunWeeklyEnforcement(
        [FromBody] RunWeeklyEnforcementRequestDto? request,
        CancellationToken cancellationToken)
    {
        return ExecuteJobAsync(request, service.RunWeeklyEnforcementAsync, cancellationToken);
    }

    [HttpPost("run-suspension-expiry")]
    [ProducesResponseType(typeof(ApiEnvelope<ActivityJobRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<ActivityJobRunDto>>> RunSuspensionExpiry(CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await service.RunSuspensionExpiryAsync(adminUserId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private async Task<ActionResult<ApiEnvelope<ActivityJobRunDto>>> ExecuteJobAsync<TRequest>(
        TRequest? request,
        Func<TRequest, string, CancellationToken, Task<ActivityJobRunDto>> operation,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        if (request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await operation(request, adminUserId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return !string.IsNullOrWhiteSpace(actorUserId);
    }
}
