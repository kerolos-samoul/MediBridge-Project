using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.OpenApi;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/wallet")]
[Authorize(Policy = AuthorizationPolicies.Phase5CompanyWalletAccess)]
public sealed class CompanyWalletController : ControllerBase
{
    private readonly ICompanyWalletService companyWalletService;

    public CompanyWalletController(ICompanyWalletService companyWalletService)
    {
        this.companyWalletService = companyWalletService;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    public async Task<ActionResult<ApiEnvelope<CompanyWalletDto>>> GetCompanyWallet([FromQuery] int PageNumber = 1, [FromQuery] int PageSize = 20, CancellationToken cancellationToken = default)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var result = await companyWalletService.GetCompanyWalletAsync(actorUserId, PageNumber, PageSize, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("topup")]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitPolicyNames.Phase5WalletTopUp)]
    public async Task<ActionResult<ApiEnvelope<TopUpCompanyWalletResultDto>>> TopUpCompanyWallet([FromBody] TopUpCompanyWalletRequestDto? request, CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        if (request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await companyWalletService.TopUpCompanyWalletAsync(actorUserId, idempotencyKey, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("mock-checkout")]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitPolicyNames.Phase5WalletTopUp)]
    public async Task<ActionResult<ApiEnvelope<MockPaymentResultDto>>> CreateMockTopUp(
        [FromBody] MockTopUpRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId, out var unauthorizedResult))
        {
            return unauthorizedResult;
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || request is null)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }

        var result = await companyWalletService.CreateMockTopUpAsync(
            actorUserId,
            idempotencyKey,
            request,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private bool TryGetActorUserId(out string actorUserId, out ActionResult unauthorizedResult)
    {
        actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        unauthorizedResult = Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        return string.IsNullOrWhiteSpace(actorUserId) is false;
    }
}
