using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class ResponseEnvelopeSuccessContractTests
{
    [Fact]
    public async Task GetWeatherForecast_ReturnsStandardEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/weatherforecast");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Success", root.GetProperty("Message").GetString());

        var data = root.GetProperty("Data");
        Assert.Equal(JsonValueKind.Array, data.ValueKind);
        Assert.Equal(5, data.GetArrayLength());

        var firstForecast = data[0];
        Assert.Equal(JsonValueKind.String, firstForecast.GetProperty("Date").ValueKind);

        var temperatureC = firstForecast.GetProperty("TemperatureC").GetInt32();
        var temperatureF = firstForecast.GetProperty("TemperatureF").GetInt32();

        Assert.Equal(32 + (int)(temperatureC / 0.5556), temperatureF);
        Assert.False(string.IsNullOrWhiteSpace(firstForecast.GetProperty("Summary").GetString()));
    }
}