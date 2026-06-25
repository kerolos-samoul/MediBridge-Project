using MediBridge.Services.Interfaces;
using MediBridge.Core.Interfaces.Files;
using Microsoft.AspNetCore.Mvc;

namespace MediBridge.APIs.Contracts;

public static class WorkflowActionResultMapper
{
    public static ObjectResult ToActionResult(Exception exception)
    {
        return exception switch
        {
            WorkflowValidationException validationException => Create(StatusCodes.Status400BadRequest, validationException.Message),
            WorkflowUnauthorizedException unauthorizedException => Create(StatusCodes.Status401Unauthorized, unauthorizedException.Message),
            WorkflowForbiddenException forbiddenException => Create(StatusCodes.Status403Forbidden, forbiddenException.Message),
            WorkflowNotFoundException notFoundException => Create(StatusCodes.Status404NotFound, notFoundException.Message),
            WorkflowConflictException conflictException => Create(StatusCodes.Status409Conflict, conflictException.Message),
            FileStorageUnavailableException => Create(StatusCodes.Status503ServiceUnavailable, "File storage is temporarily unavailable."),
            _ => throw exception
        };
    }

    private static ObjectResult Create(int statusCode, string message)
    {
        return new ObjectResult(ApiEnvelopeFactory.Create<object?>(statusCode, message, null))
        {
            StatusCode = statusCode
        };
    }
}
