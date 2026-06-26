using System.Security.Claims;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Doctors;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/company/doctors")]
[Authorize(Policy = AuthorizationPolicies.Phase5CompanyDoctorSearch)]
[EnableRateLimiting(RateLimitPolicyNames.Envelope)]
public sealed class CompanyDoctorsController : ControllerBase
{
    private readonly ICompanyDoctorSearchService companyDoctorSearchService;

    public CompanyDoctorsController(ICompanyDoctorSearchService companyDoctorSearchService)
    {
        this.companyDoctorSearchService = companyDoctorSearchService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiEnvelope<EligibleDoctorPageDto>>> GetEligibleDoctors([FromQuery] EligibleDoctorSearchRequestDto request, CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        var result = await companyDoctorSearchService.SearchEligibleDoctorsAsync(actorUserId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }
}
