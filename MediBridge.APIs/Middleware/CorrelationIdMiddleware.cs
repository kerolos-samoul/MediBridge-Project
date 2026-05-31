using System.Text.RegularExpressions;

namespace MediBridge.APIs.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";
    private static readonly Regex CorrelationIdPattern = new("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static string ResolveCorrelationId(Microsoft.Extensions.Primitives.StringValues incoming)
    {
        var candidate = incoming.ToString();
        return !string.IsNullOrWhiteSpace(candidate) && CorrelationIdPattern.IsMatch(candidate)
            ? candidate
            : Guid.NewGuid().ToString("N");
    }

    public static string GetCorrelationId(HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
}
