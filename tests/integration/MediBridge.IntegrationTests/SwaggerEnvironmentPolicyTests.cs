using System.Net;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MediBridge.IntegrationTests;

public class SwaggerEnvironmentPolicyTests
{
    [Fact]
    public async Task SwaggerNotExposedInProduction()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        var res = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task SwaggerExposedInDevelopment()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var res = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
