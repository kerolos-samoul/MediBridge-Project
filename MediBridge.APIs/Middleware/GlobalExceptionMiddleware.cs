using MediBridge.APIs.Contracts;
using MediBridge.Services.Interfaces;
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
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path} {CorrelationId}", context.Request.Method, context.Request.Path, correlationId);
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
