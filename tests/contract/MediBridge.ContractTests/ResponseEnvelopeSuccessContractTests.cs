using System.Net;
using System.Text.Json;
using MediBridge.APIs.Contracts;
using MediBridge.ContractTests.TestHost;
using MediBridge.Services.DTOs.Messaging;
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

    [Fact]
    public void Phase7DoctorSuccessEnvelopes_KeepExactPascalCaseShapeAndNullableCursor()
    {
        var inbox = new ApiEnvelope<TodayInboxDto>(
            200,
            "Today's messages retrieved.",
            new TodayInboxDto(new DateOnly(2026, 7, 4), [], null));
        var access = new ApiEnvelope<DeliveryAssetAccessGrantDto>(
            200,
            "Asset access granted.",
            new DeliveryAssetAccessGrantDto("https://storage.example.test/grant", new DateTime(2026, 7, 4, 12, 10, 0, DateTimeKind.Utc)));

        using var inboxDocument = JsonDocument.Parse(JsonSerializer.Serialize(inbox));
        Assert.Equal(["Code", "Message", "Data"], inboxDocument.RootElement.EnumerateObject().Select(property => property.Name));
        var inboxData = inboxDocument.RootElement.GetProperty("Data");
        Assert.Equal(["BusinessDateEgypt", "Items", "NextCursor"], inboxData.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, inboxData.GetProperty("NextCursor").ValueKind);

        using var accessDocument = JsonDocument.Parse(JsonSerializer.Serialize(access));
        Assert.Equal(["Code", "Message", "Data"], accessDocument.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["AccessUrl", "ExpiresAtUtc"],
            accessDocument.RootElement.GetProperty("Data").EnumerateObject().Select(property => property.Name));
    }
}
