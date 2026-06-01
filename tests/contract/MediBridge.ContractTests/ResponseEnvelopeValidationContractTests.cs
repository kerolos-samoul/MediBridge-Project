using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class ResponseEnvelopeValidationContractTests
{
    [Fact]
    public async Task GetWeatherForecast_WithInvalidQuery_ReturnsValidationEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/weatherforecast?count=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(400, root.GetProperty("Code").GetInt32());
        Assert.Equal("Validation failed.", root.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Data").ValueKind);
    }
}