using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Pricing;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/doctors")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminPricingController : ControllerBase
{
    private readonly IAdminPricingService adminPricingService;

    public AdminPricingController(IAdminPricingService adminPricingService)
    {
        this.adminPricingService = adminPricingService;
    }

    /// <summary>
    /// Set an approved doctor's positive price per message.
    /// </summary>
    [HttpPut("{doctorId}/price")]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorPriceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiEnvelope<DoctorPriceDto>>> SetDoctorPrice(
        string doctorId,
        [FromBody] SetDoctorPriceRequestDto request,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var price = await adminPricingService.SetDoctorPriceAsync(adminUserId, doctorId, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", price));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    private static bool IsWorkflowException(Exception exception)
        => exception is WorkflowValidationException
            or WorkflowUnauthorizedException
            or WorkflowForbiddenException
            or WorkflowNotFoundException
            or WorkflowConflictException;
}
