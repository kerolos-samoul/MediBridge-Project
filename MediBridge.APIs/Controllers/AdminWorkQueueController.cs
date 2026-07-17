using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/work-queue")]
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public sealed class AdminWorkQueueController : ControllerBase
{
    private readonly IAdminWorkQueueService service;

    public AdminWorkQueueController(IAdminWorkQueueService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Lists safe, paginated admin work queue items across pending operational sources.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiEnvelope<AdminWorkQueuePageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiEnvelope<AdminWorkQueuePageDto>>> List(
        [FromQuery] int? PageNumber,
        [FromQuery] int? PageSize,
        [FromQuery] string? category,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(PageNumber, PageSize, category, status, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }
}
