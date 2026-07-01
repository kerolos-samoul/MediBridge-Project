using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/wallet")]
[Authorize(Policy = AuthorizationPolicies.CompanyOnly)]
public sealed class CompanyWalletController : ControllerBase
{
    private readonly ICompanyWalletService companyWalletService;

    public CompanyWalletController(ICompanyWalletService companyWalletService)
    {
        this.companyWalletService = companyWalletService;
    }

    /// <summary>
    /// Get or repair the approved company's active wallet.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiEnvelope<CompanyWalletDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<CompanyWalletDto>>> GetCompanyWallet(CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var wallet = await companyWalletService.GetCompanyWalletAsync(companyUserId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", wallet));
        }
        catch (Exception exception) when (exception is WorkflowValidationException or WorkflowUnauthorizedException or WorkflowForbiddenException or WorkflowNotFoundException or WorkflowConflictException)
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    /// <summary>
    /// Start a mock top-up checkout that succeeds immediately.
    /// </summary>
    [HttpPost("mock-checkout")]
    [ProducesResponseType(typeof(ApiEnvelope<MockPaymentResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiEnvelope<MockPaymentResultDto>>> CreateMockTopUp(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] MockTopUpRequestDto request,
        CancellationToken cancellationToken)
    {
        var companyUserId = GetUserId();
        if (string.IsNullOrWhiteSpace(companyUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        try
        {
            var result = await companyWalletService.CreateMockTopUpAsync(companyUserId, idempotencyKey, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (Exception exception) when (exception is WorkflowValidationException or WorkflowUnauthorizedException or WorkflowForbiddenException or WorkflowNotFoundException or WorkflowConflictException)
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    private string? GetUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    }
}
