using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/statistics")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminStatisticsController : ControllerBase
{
    private readonly IAdminStatisticsService service;

    public AdminStatisticsController(IAdminStatisticsService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Returns read-only admin statistics for an inclusive Egypt business date range of up to 90 days.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiEnvelope<AdminStatisticsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<AdminStatisticsDto>>> Get(
        [FromQuery] string? fromDateEgypt,
        [FromQuery] string? toDateEgypt,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(fromDateEgypt, toDateEgypt, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }
}
