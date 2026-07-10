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
    public void DoctorMessageSuccessEnvelopes_KeepExactPascalCaseShapeAndNullableFields()
    {
        var inbox = new ApiEnvelope<TodayInboxDto>(
            200,
            "Today's messages retrieved.",
            new TodayInboxDto(new DateOnly(2026, 7, 4), [], null));
        var access = new ApiEnvelope<DeliveryAssetAccessGrantDto>(
            200,
            "Asset access granted.",
            new DeliveryAssetAccessGrantDto("https://storage.example.test/grant", new DateTime(2026, 7, 4, 12, 10, 0, DateTimeKind.Utc)));
        var read = new ApiEnvelope<MarkReadResultDto>(
            200,
            "Message read recorded.",
            new MarkReadResultDto
            {
                DeliveryId = "delivery-123",
                ReadAtUtc = new DateTime(2026, 7, 10, 10, 15, 0, DateTimeKind.Utc),
                ReadStatus = "Created"
            });
        var interaction = new ApiEnvelope<InteractDeliveryResultDto>(
            200,
            "Message interaction settled.",
            new InteractDeliveryResultDto
            {
                DeliveryId = "delivery-123",
                Status = "Accepted",
                InteractedAtUtc = new DateTime(2026, 7, 10, 10, 20, 0, DateTimeKind.Utc),
                FeedbackText = null,
                IdempotencyStatus = "Replayed"
            });

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

        using var readDocument = JsonDocument.Parse(JsonSerializer.Serialize(read));
        Assert.Equal(["Code", "Message", "Data"], readDocument.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            ["DeliveryId", "ReadAtUtc", "ReadStatus"],
            readDocument.RootElement.GetProperty("Data").EnumerateObject().Select(property => property.Name));

        using var interactionDocument = JsonDocument.Parse(JsonSerializer.Serialize(interaction));
        Assert.Equal(["Code", "Message", "Data"], interactionDocument.RootElement.EnumerateObject().Select(property => property.Name));
        var interactionData = interactionDocument.RootElement.GetProperty("Data");
        Assert.Equal(
            ["DeliveryId", "Status", "InteractedAtUtc", "FeedbackText", "IdempotencyStatus"],
            interactionData.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, interactionData.GetProperty("FeedbackText").ValueKind);
        Assert.DoesNotContain("Balance", interactionData.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Idempotency-Key", interactionData.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlatformFee", interactionData.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
