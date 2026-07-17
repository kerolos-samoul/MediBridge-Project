using System.Net;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class CorsPreflightIntegrationTests
{
    public static TheoryData<string, string, string> PreflightRequests => new()
    {
        { "/api/auth/login", "POST", "content-type" },
        { "/api/company/wallet/topup", "POST", "authorization,content-type,idempotency-key,x-correlation-id" },
        { "/api/doctor/messages/delivery-1/interact", "POST", "authorization,content-type,idempotency-key" }
    };

    [Theory]
    [MemberData(nameof(PreflightRequests))]
    public async Task CorsPreflight_FromFrontendOrigin_IsAllowedBeforeAuthAndRateLimiting(
        string path,
        string requestedMethod,
        string requestedHeaders)
    {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost:5173");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", requestedMethod);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", requestedHeaders);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains(requestedMethod, response.Headers.GetValues("Access-Control-Allow-Methods").Single());

        var allowHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single();
        foreach (var requestedHeader in requestedHeaders.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains(requestedHeader, allowHeaders, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
