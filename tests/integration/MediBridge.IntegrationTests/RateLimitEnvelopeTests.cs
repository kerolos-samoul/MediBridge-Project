using System.Net;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class RateLimitEnvelopeTests
{
    [Fact]
    public async Task RateLimitRejection_ReturnsStandard429Envelope()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var acceptedResponse = await client.GetAsync("/__test/rate-limit/envelope");
            Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        }

        using var rejectedResponse = await client.GetAsync("/__test/rate-limit/envelope");

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);

        using var document = JsonDocument.Parse(await rejectedResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(429, root.GetProperty("Code").GetInt32());
        Assert.Equal("Too many requests.", root.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Data").ValueKind);
    }
}
