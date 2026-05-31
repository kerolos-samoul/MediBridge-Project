using System.Net;
using System.Text.Json;
using MediBridge.APIs.Middleware;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class BackwardCompatibilityPayloadTests
{
    [Fact]
    public async Task SuccessResponseKeepsBusinessPayloadInsideEnvelope()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Success", root.GetProperty("Message").GetString());

        var data = root.GetProperty("Data");
        Assert.Equal(5, data.GetArrayLength());

        foreach (var forecast in data.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.String, forecast.GetProperty("Date").ValueKind);

            var temperatureC = forecast.GetProperty("TemperatureC").GetInt32();
            Assert.Equal(32 + (int)(temperatureC / 0.5556), forecast.GetProperty("TemperatureF").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(forecast.GetProperty("Summary").GetString()));
        }
    }

    [Fact]
    public async Task ValidationFailureKeepsStatusAndReturnsEnvelopeWithoutPayload()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/weatherforecast?count=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(400, root.GetProperty("Code").GetInt32());
        Assert.Equal("Validation failed.", root.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task UnhandledExceptionReturnsSafeEnvelopeAndPreservesStatus()
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

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        using var document = JsonDocument.Parse(await reader.ReadToEndAsync());
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal(500, root.GetProperty("Code").GetInt32());
        Assert.Equal("An unexpected error occurred.", root.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Data").ValueKind);
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
