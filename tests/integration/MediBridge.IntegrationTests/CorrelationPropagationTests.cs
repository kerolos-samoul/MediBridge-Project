using System.Net.Http;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public class CorrelationPropagationTests
{
    [Fact]
    public async Task ValidCorrelationHeader_IsPropagatedToResponse()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/weatherforecast");
        const string correlation = "abc123XYZ";
        req.Headers.Add("X-Correlation-ID", correlation);

        var response = await client.SendAsync(req);

        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        var returned = response.Headers.GetValues("X-Correlation-ID").FirstOrDefault();
        Assert.Equal(correlation, returned);
    }
}
