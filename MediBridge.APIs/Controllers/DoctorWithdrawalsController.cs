using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/doctor/withdrawals")]
[Authorize(Policy = AuthorizationPolicies.DoctorOnly)]
public sealed class DoctorWithdrawalsController : ControllerBase
{
    private readonly IWithdrawalService withdrawalService;

    public DoctorWithdrawalsController(IWithdrawalService withdrawalService)
    {
        this.withdrawalService = withdrawalService;
    }

    /// <summary>
    /// Creates a doctor withdrawal request and places the requested settled earnings on hold.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorWithdrawalDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<DoctorWithdrawalDto>>> Create(
        [FromBody] CreateWithdrawalRequestDto? request,
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

        var result = await withdrawalService.CreateWithdrawalAsync(actorUserId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
    }

    /// <summary>
    /// Lists the authenticated doctor's withdrawal requests.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiEnvelope<DoctorWithdrawalPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<DoctorWithdrawalPageDto>>> List(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] WithdrawalRequestStatus? status,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await withdrawalService.ListDoctorWithdrawalsAsync(actorUserId, pageNumber, pageSize, status, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return !string.IsNullOrWhiteSpace(actorUserId);
    }
}
