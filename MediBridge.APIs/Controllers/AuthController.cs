using System.Security.Claims;
using FluentValidation;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService authService;

    public AuthController(IAuthService authService)
    {
        this.authService = authService;
    }

    [HttpPost("register-doctor")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Registration)]
    public async Task<ActionResult<ApiEnvelope<RegistrationResultDto>>> RegisterDoctor([FromBody] RegisterDoctorRequestDto request, CancellationToken cancellationToken)
    {
        return await ExecuteRegistrationAsync(() => authService.RegisterDoctorAsync(request, cancellationToken));
    }

    [HttpPost("register-company")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Registration)]
    public async Task<ActionResult<ApiEnvelope<RegistrationResultDto>>> RegisterCompany([FromBody] RegisterCompanyRequestDto request, CancellationToken cancellationToken)
    {
        return await ExecuteRegistrationAsync(() => authService.RegisterCompanyAsync(request, cancellationToken));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public async Task<ActionResult<ApiEnvelope<AuthResultDto>>> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.LoginAsync(request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (AccountStatusDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Account status denied.", null));
        }
        catch (AuthDeniedException)
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Refresh)]
    public async Task<ActionResult<ApiEnvelope<AuthResultDto>>> Refresh([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.RefreshAsync(request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (ValidationException)
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }
        catch (RefreshReuseDetectedException)
        {
            return Conflict(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status409Conflict, "Refresh reuse detected.", null));
        }
        catch (AuthDeniedException)
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }
    }

    [Authorize]
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicyNames.Refresh)]
    public async Task<ActionResult<ApiEnvelope<object?>>> Logout([FromBody] RefreshRequestDto request, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            await authService.LogoutAsync(userId, request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status200OK, "Success", null));
        }
        catch (AuthDeniedException)
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public async Task<ActionResult<ApiEnvelope<object?>>> ForgotPassword([FromBody] ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            await authService.ForgotPasswordAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status202Accepted, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status202Accepted, "Accepted", null));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public async Task<ActionResult<ApiEnvelope<object?>>> ResetPassword([FromBody] ResetPasswordRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            await authService.ResetPasswordAsync(request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status200OK, "Success", null));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (AuthDeniedException)
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }
    }

    [HttpPost("verify-contact")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public async Task<ActionResult<ApiEnvelope<object?>>> VerifyContact([FromBody] VerifyContactRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            await authService.VerifyContactAsync(request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status200OK, "Success", null));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
    }

    [HttpPost("resubmit-registration")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Registration)]
    public async Task<ActionResult<ApiEnvelope<RegistrationResultDto>>> ResubmitRegistration([FromBody] ResubmissionRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.ResubmitRegistrationAsync(request, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (AccountStatusDeniedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiEnvelopeFactory.Create<object?>(StatusCodes.Status403Forbidden, "Account status denied.", null));
        }
        catch (RegistrationConflictException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
    }

    private async Task<ActionResult<ApiEnvelope<RegistrationResultDto>>> ExecuteRegistrationAsync(Func<Task<RegistrationResultDto>> operation)
    {
        try
        {
            var result = await operation();
            return StatusCode(StatusCodes.Status201Created, ApiEnvelopeFactory.Create(StatusCodes.Status201Created, "Success", result));
        }
        catch (ValidationException)
        {
            return BadRequest(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status400BadRequest, "Validation failed.", null));
        }
        catch (RegistrationConflictException)
        {
            return Conflict(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status409Conflict, "Duplicate email, phone, or license.", null));
        }
    }
}
