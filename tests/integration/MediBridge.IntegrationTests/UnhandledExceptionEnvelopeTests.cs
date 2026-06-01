using System.Net;
using System.Text.Json;
using MediBridge.APIs.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public class UnhandledExceptionEnvelopeTests
{
    [Fact]
    public async Task TriggeringUnhandledException_ReturnsSafeEnvelopeAnd500()
    {
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("boom"),
            new ProductionWebHostEnvironment(),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        var services = new ServiceCollection();
        services.Configure<JsonOptions>(options => options.SerializerOptions.PropertyNamingPolicy = null);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/weatherforecast";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
        var root = doc.RootElement;

        int code = GetIntPropertyCaseInsensitive(root, "code");
        Assert.Equal(500, code);

        var message = GetStringPropertyCaseInsensitive(root, "message");
        Assert.Equal("An unexpected error occurred.", message);

        var dataKind = GetPropertyKindCaseInsensitive(root, "data");
        Assert.Equal(JsonValueKind.Null, dataKind);

        static int GetIntPropertyCaseInsensitive(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p) || el.TryGetProperty(name.Replace("c", "C"), out p))
                return p.GetInt32();
            // fallback: try common casing
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetInt32();
            }

            throw new KeyNotFoundException(name);
        }

        static string? GetStringPropertyCaseInsensitive(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p) || el.TryGetProperty(name.Replace("m", "M"), out p))
                return p.GetString();

            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }

            throw new KeyNotFoundException(name);
        }

        static JsonValueKind GetPropertyKindCaseInsensitive(JsonElement el, string name)
        {
            if (el.TryGetProperty(name, out var p) || el.TryGetProperty(name.Replace("d", "D"), out p))
                return p.ValueKind;

            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ValueKind;
            }

            throw new KeyNotFoundException(name);
        }
    }

    [Fact]
    public void ProductionRouteSurface_DoesNotExposeDiagnosticOrTestRoutes()
    {
        using var factory = new ProductionWebAppFactory();

        var endpointSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var routePatterns = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(routePatterns, route => route.Contains("__test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routePatterns, route => route.Contains("diagnostic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routePatterns, route => route.Contains("exception", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ProductionWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MediBridge.Tests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = string.Empty;

        public string EnvironmentName { get; set; } = "Production";

        public string WebRootPath { get; set; } = string.Empty;

        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
