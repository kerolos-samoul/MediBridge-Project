using System.Net;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class RateLimitPolicyRegistrationTests
{
    [Theory]
    [InlineData("/__test/rate-limit/login")]
    [InlineData("/__test/rate-limit/registration")]
    [InlineData("/__test/rate-limit/refresh")]
    [InlineData("/__test/rate-limit/company-top-up")]
    [InlineData("/__test/rate-limit/doctor-withdrawal")]
    [InlineData("/__test/rate-limit/doctor-interaction")]
    public async Task RequiredRateLimitPolicies_CanBeAttachedToEndpoints(string path)
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
