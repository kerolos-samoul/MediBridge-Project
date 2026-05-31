using System.Net.Http;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public class CorrelationGenerationTests
{
    [Fact]
    public async Task MissingCorrelationHeader_FallsBackAndIsReturned()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/weatherforecast");

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var returned = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(returned));
        // Fallback format used by middleware is Guid("N") (32 chars)
        Assert.InRange(returned.Length, 1, 128);
    }
}
