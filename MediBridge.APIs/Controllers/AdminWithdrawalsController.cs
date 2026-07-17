using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/withdrawals")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminWithdrawalsController : ControllerBase
{
    private readonly IWithdrawalService withdrawalService;

    public AdminWithdrawalsController(IWithdrawalService withdrawalService)
    {
        this.withdrawalService = withdrawalService;
    }

    /// <summary>
    /// Lists withdrawal requests for admin review and payout tracking.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWithdrawalPageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<AdminWithdrawalPageDto>>> List(
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] WithdrawalRequestStatus? status,
        [FromQuery] string? doctorId,
        [FromQuery] DateTime? requestedFromUtc,
        [FromQuery] DateTime? requestedToUtc,
        [FromQuery] DateTime? reviewedFromUtc,
        [FromQuery] DateTime? reviewedToUtc,
        [FromQuery] decimal? minimumAmount,
        [FromQuery] decimal? maximumAmount,
        [FromQuery] string? payoutReference,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await withdrawalService.ListAdminWithdrawalsAsync(
            adminUserId,
            pageNumber,
            pageSize,
            status,
            doctorId,
            requestedFromUtc,
            requestedToUtc,
            reviewedFromUtc,
            reviewedToUtc,
            minimumAmount,
            maximumAmount,
            payoutReference,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    /// <summary>
    /// Approves a requested withdrawal while preserving the held funds.
    /// </summary>
    [HttpPut("{withdrawalId}/approve")]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<AdminWithdrawalDto>>> Approve(
        string withdrawalId,
        [FromBody] AdminWithdrawalDecisionRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var adminUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await withdrawalService.ApproveAsync(adminUserId, withdrawalId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    /// <summary>
    /// Rejects a requested withdrawal and releases the held amount.
    /// </summary>
    [HttpPut("{withdrawalId}/reject")]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<AdminWithdrawalDto>>> Reject(
        string withdrawalId,
        [FromBody] AdminWithdrawalDecisionRequestDto? request,
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

        var result = await withdrawalService.RejectAsync(adminUserId, withdrawalId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    /// <summary>
    /// Marks an approved withdrawal as paid using a reference-only payout record.
    /// </summary>
    [HttpPut("{withdrawalId}/mark-paid")]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<AdminWithdrawalDto>>> MarkPaid(
        string withdrawalId,
        [FromBody] MarkWithdrawalPaidRequestDto? request,
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

        var result = await withdrawalService.MarkPaidAsync(adminUserId, withdrawalId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    /// <summary>
    /// Marks an approved withdrawal as failed before funds leave the platform and releases the hold.
    /// </summary>
    [HttpPut("{withdrawalId}/mark-failed")]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWithdrawalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<AdminWithdrawalDto>>> MarkFailed(
        string withdrawalId,
        [FromBody] MarkWithdrawalFailedRequestDto? request,
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

        var result = await withdrawalService.MarkFailedAsync(adminUserId, withdrawalId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return !string.IsNullOrWhiteSpace(actorUserId);
    }
}
