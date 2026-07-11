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
[Route("api/admin/doctors")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class AdminPricingController : ControllerBase
{
    private readonly IAdminPricingService adminPricingService;

    public AdminPricingController(IAdminPricingService adminPricingService)
    {
        this.adminPricingService = adminPricingService;
    }

    /// <summary>
    /// Sets or updates the pricing for a doctor profile.
    /// </summary>
    /// <param name="doctorId">The unique identifier of the DoctorProfile entity (not the User ID). Retrieve from GET /api/admin/pending-accounts or doctor profile endpoints.</param>
    /// <param name="request">The pricing details to set for the doctor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated doctor pricing information.</returns>
    [HttpPut("{doctorId}/price")]
    public async Task<ActionResult<ApiEnvelope<DoctorPriceDto>>> SetDoctorPrice(
        string doctorId,
        [FromBody] SetDoctorPriceRequestDto? request,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(
                ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (request is null)
        {
            return BadRequest(
                ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await adminPricingService.SetDoctorPriceAsync(
            adminUserId,
            doctorId,
            request,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{doctorId}/delivery-settings")]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorDeliverySettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<DoctorDeliverySettingsDto>>> GetDoctorDeliverySettings(
        string doctorId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await adminPricingService.GetDoctorDeliverySettingsAsync(
            adminUserId,
            doctorId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPut("{doctorId}/delivery-settings")]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorDeliverySettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<DoctorDeliverySettingsDto>>> SetDoctorDeliverySettings(
        string doctorId,
        [FromBody] SetDoctorDeliverySettingsRequestDto? request,
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

        var result = await adminPricingService.SetDoctorDeliverySettingsAsync(
            adminUserId,
            doctorId,
            request,
            cancellationToken);
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
