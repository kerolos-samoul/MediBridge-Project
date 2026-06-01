using MediBridge.APIs.Contracts;
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
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(
                ApiEnvelopeFactory.Create(500, "An unexpected error occurred.", data: (object?)null),
                new JsonSerializerOptions { PropertyNamingPolicy = null },
                context.RequestAborted);
        }
    }
}
