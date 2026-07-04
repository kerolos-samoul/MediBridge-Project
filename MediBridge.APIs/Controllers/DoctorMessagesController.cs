using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Interfaces;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/doctor/messages")]
[Authorize(Policy = AuthorizationPolicies.Phase7DoctorMessagesRead)]
[EnableRateLimiting(RateLimitPolicyNames.Phase7DoctorMessagesRead)]
public sealed class DoctorMessagesController : ControllerBase
{
    private readonly IDoctorMessageService doctorMessageService;
    private readonly ICurrentUserContext currentUserContext;

    public DoctorMessagesController(IDoctorMessageService doctorMessageService, ICurrentUserContext currentUserContext)
    {
        this.doctorMessageService = doctorMessageService;
        this.currentUserContext = currentUserContext;
    }

    [HttpGet("today")]
    [ProducesResponseType(typeof(ApiEnvelope<TodayInboxDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiEnvelope<TodayInboxDto>>> GetToday(
        [FromQuery(Name = "PageSize")] int? pageSize,
        [FromQuery(Name = "Cursor")] string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await doctorMessageService.GetTodayInboxAsync(
            currentUserContext.UserId ?? string.Empty,
            pageSize,
            cursor,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Today's messages retrieved.", result));
    }

    [HttpGet("{deliveryId}/assets/{fileId}/access")]
    [ProducesResponseType(typeof(ApiEnvelope<DeliveryAssetAccessGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiEnvelope<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiEnvelope<DeliveryAssetAccessGrantDto>>> GetAssetAccess(
        string deliveryId,
        string fileId,
        CancellationToken cancellationToken)
    {
        var result = await doctorMessageService.CreateDeliveryAssetAccessGrantAsync(
            currentUserContext.UserId ?? string.Empty,
            deliveryId,
            fileId,
            cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Asset access granted.", result));
    }
}
