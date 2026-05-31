using MediBridge.APIs.Middleware;

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
}
