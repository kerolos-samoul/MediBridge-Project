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
}
