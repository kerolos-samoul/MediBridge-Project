using System.Security.Claims;
using FluentValidation;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminAccountsController : ControllerBase
{
    private readonly IAdminAccountService adminAccountService;

    public AdminAccountsController(IAdminAccountService adminAccountService)
    {
        this.adminAccountService = adminAccountService;
    }

    [HttpGet("pending-accounts")]
    public async Task<ActionResult<ApiEnvelope<PendingAccountPageDto>>> ListPendingAccounts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await adminAccountService.ListPendingAccountsAsync(pageNumber, pageSize, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
    }

    [HttpPut("accounts/{id}/decision")]
    public async Task<ActionResult<ApiEnvelope<AccountDecisionResultDto>>> DecideAccount(
        string id,
        [FromBody] AdminAccountDecisionRequestDto request,
        CancellationToken cancellationToken)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var result = await adminAccountService.ApplyDecisionAsync(adminUserId, id, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Forbidden.", null));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status404NotFound, "Not found.", null));
        }
    }
}
