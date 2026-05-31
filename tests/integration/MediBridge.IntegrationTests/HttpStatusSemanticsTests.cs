using System.Net;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class HttpStatusSemanticsTests
{
    [Theory]
    [InlineData("/weatherforecast", HttpStatusCode.OK)]
    [InlineData("/weatherforecast?count=0", HttpStatusCode.BadRequest)]
    public async Task WeatherForecastResponses_PreserveHttpStatus(string requestPath, HttpStatusCode expectedStatus)
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(requestPath);

        Assert.Equal(expectedStatus, response.StatusCode);
    }
}