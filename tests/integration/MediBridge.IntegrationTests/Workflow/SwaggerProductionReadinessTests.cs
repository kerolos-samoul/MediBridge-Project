using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class SwaggerProductionReadinessTests
{
    [Fact]
    public async Task SwaggerUiAndDocument_AreGeneratedSuccessfully()
    {
        await using var factory = new DevelopmentSwaggerWebAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var indexResponse = await client.GetAsync("/swagger/index.html");
        using var documentResponse = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        var document = await documentResponse.Content.ReadAsStringAsync();
        Assert.Contains("/api/company/campaigns/{campaignId}/assets", document, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultLogging_DoesNotRegisterWindowsEventLogProvider()
    {
        using var factory = new DevelopmentSwaggerWebAppFactory();

        var providerNames = factory.Services
            .GetServices<ILoggerProvider>()
            .Select(provider => provider.GetType().FullName ?? provider.GetType().Name)
            .ToArray();

        Assert.DoesNotContain(providerNames, name =>
            name.Contains("EventLogLoggerProvider", StringComparison.Ordinal));
    }

    private sealed class DevelopmentSwaggerWebAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=MediBridgeSwaggerTests;Trusted_Connection=True;TrustServerCertificate=True;",
                    ["Jwt:Issuer"] = "MediBridge.SwaggerTests",
                    ["Jwt:Audience"] = "MediBridge.SwaggerTests.Clients",
                    ["Jwt:SigningKey"] = "SwaggerTestSigningKey-AtLeast-32-Characters-Long",
                    ["Identity:SeedDevelopmentAdmin"] = "false"
                });
            });
        }
    }
}
