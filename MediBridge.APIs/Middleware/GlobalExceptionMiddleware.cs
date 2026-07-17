using MediBridge.APIs.Contracts;
using MediBridge.Services.Interfaces;
using FluentValidation;
using System.Text.Json;

namespace MediBridge.APIs.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, IWebHostEnvironment environment, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _ = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);
            if (ex is Phase7StorageUnavailableException or Phase9ServiceUnavailableException)
            {
                _logger.LogWarning("Storage unavailable while processing {Method} {Path} {CorrelationId}", context.Request.Method, context.Request.Path, correlationId);
            }
            else if (ex is Phase7WorkflowException or Phase9WorkflowException)
            {
                _logger.LogInformation("Safe workflow request rejected with {ExceptionType} while processing {Method} {Path} {CorrelationId}", ex.GetType().Name, context.Request.Method, context.Request.Path, correlationId);
            }
            else
            {
                _logger.LogError(
                    "Unhandled {ExceptionType} while processing {Method} {Path} {CorrelationId}",
                    ex.GetType().Name,
                    context.Request.Method,
                    context.Request.Path,
                    correlationId);
            }
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            var (statusCode, message) = ex switch
            {
                Phase5ValidationException => (StatusCodes.Status400BadRequest, "Validation failed."),
                Phase5ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden."),
                Phase5NotFoundException => (StatusCodes.Status404NotFound, "Not found."),
                Phase5ConflictException => (StatusCodes.Status409Conflict, "Conflict."),
                Phase5ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service unavailable."),
                ValidationException => (StatusCodes.Status400BadRequest, "Validation failed."),
                UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden."),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found."),
                Phase7BadRequestException => (StatusCodes.Status400BadRequest, "Invalid request."),
                Phase7ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden."),
                Phase7NotFoundException => (StatusCodes.Status404NotFound, "Not found."),
                Phase7ConflictException => (StatusCodes.Status409Conflict, "Conflict."),
                Phase7StorageUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Storage unavailable."),
                Phase9ValidationException => (StatusCodes.Status400BadRequest, "Validation failed."),
                Phase9ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden."),
                Phase9NotFoundException => (StatusCodes.Status404NotFound, "Not found."),
                Phase9ConflictException => (StatusCodes.Status409Conflict, "Conflict."),
                Phase9ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service unavailable."),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(
                ApiEnvelopeFactory.Create(statusCode, message, data: (object?)null),
                new JsonSerializerOptions { PropertyNamingPolicy = null },
                context.RequestAborted);
        }
    }
}
