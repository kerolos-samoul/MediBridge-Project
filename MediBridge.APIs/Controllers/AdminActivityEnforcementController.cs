using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class AdminActivityEnforcementController : ControllerBase
{
    private readonly IAdminActivityEnforcementService service;

    public AdminActivityEnforcementController(IAdminActivityEnforcementService service)
    {
        this.service = service;
    }

    [HttpGet("api/admin/violations")]
    [ProducesResponseType(typeof(ApiEnvelope<ViolationSummaryPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<ViolationSummaryPageDto>>> ListViolations(
        [FromQuery] string? doctorId,
        [FromQuery] DoctorMarketplaceStatus? status,
        [FromQuery] string? eligibility,
        [FromQuery] DateOnly? weekFrom,
        [FromQuery] DateOnly? weekTo,
        [FromQuery] int? minRollingViolations,
        [FromQuery] int PageNumber = 1,
        [FromQuery] int PageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await service.ListViolationsAsync(
            new ViolationSummaryQuery(doctorId, status, eligibility, weekFrom, weekTo, minRollingViolations, PageNumber, PageSize),
            adminUserId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPut("api/admin/doctors/{doctorId}/status")]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorEnforcementActionResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<DoctorEnforcementActionResultDto>>> ApplyStatus(
        string doctorId,
        [FromBody] DoctorEnforcementActionRequestDto? request,
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

        var result = await service.ApplyDoctorEnforcementActionAsync(
            doctorId,
            request,
            adminUserId,
            HttpContext.TraceIdentifier,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return !string.IsNullOrWhiteSpace(actorUserId);
    }
}
