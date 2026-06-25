using System.Security.Claims;
using MediBridge.APIs.Contracts;
using MediBridge.Services.DTOs.Files;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Controllers;

[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController : ControllerBase
{
    private readonly IFileAccessService fileAccessService;

    public FilesController(IFileAccessService fileAccessService)
    {
        this.fileAccessService = fileAccessService;
    }

    [HttpGet("{fileId}")]
    [ProducesResponseType(typeof(ApiEnvelope<FileAccessDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiEnvelope<object?>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ApiEnvelope<FileAccessDto>>> GetFileAccess(
        string fileId,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiEnvelopeFactory.Create<object?>(StatusCodes.Status401Unauthorized, "Authentication denied.", null));
        }

        try
        {
            var access = await fileAccessService.GetSignedAccessAsync(userId, fileId, cancellationToken);
            return Ok(ApiEnvelopeFactory.Create(StatusCodes.Status200OK, "Success", access));
        }
        catch (Exception exception) when (IsWorkflowException(exception))
        {
            return WorkflowActionResultMapper.ToActionResult(exception);
        }
    }

    private static bool IsWorkflowException(Exception exception)
        => exception is WorkflowUnauthorizedException
            or WorkflowForbiddenException
            or WorkflowNotFoundException
            or MediBridge.Core.Interfaces.Files.FileStorageUnavailableException;
}
