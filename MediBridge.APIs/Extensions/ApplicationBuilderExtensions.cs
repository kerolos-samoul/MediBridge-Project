using MediBridge.APIs.Contracts;
using MediBridge.APIs.Middleware;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace MediBridge.APIs.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseFoundationPipeline(this IApplicationBuilder app)
    {
        app.UseMiddleware<GlobalExceptionMiddleware>();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }

    public static IApplicationBuilder UseEnvelopeStatusCodePages(this IApplicationBuilder app)
    {
        return app.UseStatusCodePages(async statusCodeContext =>
        {
            var response = statusCodeContext.HttpContext.Response;
            if (response.HasStarted || response.ContentLength.HasValue || response.Body.CanWrite is false)
            {
                return;
            }

            var statusCode = response.StatusCode;
            response.ContentType = "application/json";

            var message = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad request.",
                StatusCodes.Status401Unauthorized => "Unauthorized.",
                StatusCodes.Status403Forbidden => "Forbidden.",
                StatusCodes.Status404NotFound => "Not found.",
                StatusCodes.Status405MethodNotAllowed => "Method not allowed.",
                StatusCodes.Status408RequestTimeout => "Request timed out.",
                StatusCodes.Status429TooManyRequests => "Too many requests.",
                _ => "An error occurred."
            };

            await response.WriteAsJsonAsync(
                ApiEnvelopeFactory.Create<object?>(statusCode, message, null),
                new JsonSerializerOptions { PropertyNamingPolicy = null },
                statusCodeContext.HttpContext.RequestAborted);
        });
    }
}
