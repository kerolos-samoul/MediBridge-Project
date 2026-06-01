using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.IntegrationTests.TestControllers;

[ApiController]
[Route("__test/rate-limit")]
public sealed class RateLimitedTestController : ControllerBase
{
    [HttpGet("login")]
    [EnableRateLimiting(RateLimitPolicyNames.Login)]
    public IActionResult Login() => Ok(ApiEnvelopeFactory.Create(200, "Success", "login"));

    [HttpGet("registration")]
    [EnableRateLimiting(RateLimitPolicyNames.Registration)]
    public IActionResult Registration() => Ok(ApiEnvelopeFactory.Create(200, "Success", "registration"));

    [HttpGet("refresh")]
    [EnableRateLimiting(RateLimitPolicyNames.Refresh)]
    public IActionResult Refresh() => Ok(ApiEnvelopeFactory.Create(200, "Success", "refresh"));

    [HttpGet("company-top-up")]
    [EnableRateLimiting(RateLimitPolicyNames.CompanyTopUp)]
    public IActionResult CompanyTopUp() => Ok(ApiEnvelopeFactory.Create(200, "Success", "company-top-up"));

    [HttpGet("doctor-withdrawal")]
    [EnableRateLimiting(RateLimitPolicyNames.DoctorWithdrawal)]
    public IActionResult DoctorWithdrawal() => Ok(ApiEnvelopeFactory.Create(200, "Success", "doctor-withdrawal"));

    [HttpGet("doctor-interaction")]
    [EnableRateLimiting(RateLimitPolicyNames.DoctorInteraction)]
    public IActionResult DoctorInteraction() => Ok(ApiEnvelopeFactory.Create(200, "Success", "doctor-interaction"));

    [HttpGet("envelope")]
    [EnableRateLimiting(RateLimitPolicyNames.Envelope)]
    public IActionResult Envelope() => Ok(ApiEnvelopeFactory.Create(200, "Success", "envelope"));
}
