using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/admin/files")]
[Authorize(Policy = AuthorizationPolicies.AdminFileReview)]
public sealed class AdminFilesController : ControllerBase
{
    private readonly IFileWorkflowService fileWorkflowService;

    public AdminFilesController(IFileWorkflowService fileWorkflowService)
    {
        this.fileWorkflowService = fileWorkflowService;
    }

    [HttpGet("pending")]
    public async Task<ActionResult<ApiEnvelope<PendingFileReviewPageDto>>> ListPendingFiles([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var adminUserId = GetUserId();
        var result = await fileWorkflowService.ListPendingReviewsAsync(adminUserId, pageNumber, pageSize, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPut("{fileId}/review")]
    public async Task<ActionResult<ApiEnvelope<FileReviewDto>>> ReviewFile(string fileId, [FromBody] FileReviewRequestDto request, CancellationToken cancellationToken)
    {
        var result = await fileWorkflowService.ReviewFileAsync(GetUserId(), fileId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpPost("~/api/admin/campaign-assets/{assetId}/review")]
    public async Task<ActionResult<ApiEnvelope<FileReviewDto>>> ReviewCampaignAsset(
        string assetId,
        [FromBody] FileReviewRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await fileWorkflowService.ReviewFileAsync(GetUserId(), assetId, request, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    [HttpGet("{fileId}/reviews")]
    public async Task<ActionResult<ApiEnvelope<IReadOnlyList<FileReviewDto>>>> GetReviewHistory(string fileId, CancellationToken cancellationToken)
    {
        var result = await fileWorkflowService.GetReviewHistoryAsync(GetUserId(), fileId, cancellationToken);
        return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", result));
    }

    private string GetUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    }
}
