using System.Diagnostics;

namespace MediBridge.APIs.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(context);
        _logger.LogInformation("Request received {Method} {Path} {CorrelationId}", context.Request.Method, context.Request.Path, correlationId);

        context.Response.OnCompleted(() =>
        {
            var durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _logger.LogInformation("Request completed {Method} {Path} {StatusCode} {DurationMs} {CorrelationId}", context.Request.Method, context.Request.Path, context.Response.StatusCode, durationMs, correlationId);
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
