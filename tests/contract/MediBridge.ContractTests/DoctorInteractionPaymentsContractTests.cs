using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Controllers;
using MediBridge.APIs.OpenApi;
using MediBridge.APIs.Config;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class DoctorInteractionPaymentsContractTests
{
    [Fact]
    public void Phase8DoctorInteractionPaymentsScaffold_Compiles()
    {
    }

    [Fact]
    public void MarkReadContract_DocumentsEnvelopeResponses_AndDoesNotRequireIdempotencyKey()
    {
        var method = typeof(DoctorMessagesController).GetMethod("MarkRead", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var put = Assert.Single(method!.GetCustomAttributes<HttpPutAttribute>());
        Assert.Equal("{deliveryId}/read", put.Template);

        var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        Assert.Contains(responseTypes, item => item.StatusCode == 200 && item.Type == typeof(ApiEnvelope<MarkReadResultDto>));
        Assert.Contains(responseTypes, item => item.StatusCode == 401 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 403 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 404 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 429 && item.Type == typeof(ApiEnvelope<object>));

        Assert.DoesNotContain(
            method.GetParameters(),
            parameter => parameter.GetCustomAttributes().Any(attribute =>
                attribute.GetType().Name.Contains("Header", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.Name, "idempotencyKey", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MarkReadSuccessEnvelope_UsesExactPascalCaseShape()
    {
        var readAtUtc = new DateTime(2026, 7, 10, 9, 15, 0, DateTimeKind.Utc);
        var envelope = new ApiEnvelope<MarkReadResultDto>(
            200,
            "Message read recorded.",
            new MarkReadResultDto
            {
                DeliveryId = "delivery-123",
                ReadAtUtc = readAtUtc,
                ReadStatus = InteractionPaymentResultStatus.Created
            });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        var root = document.RootElement;

        Assert.Equal(["Code", "Message", "Data"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Message read recorded.", root.GetProperty("Message").GetString());

        var data = root.GetProperty("Data");
        Assert.Equal(["DeliveryId", "ReadAtUtc", "ReadStatus"], data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("delivery-123", data.GetProperty("DeliveryId").GetString());
        Assert.Equal("Created", data.GetProperty("ReadStatus").GetString());
        Assert.Equal(DateTimeKind.Utc, data.GetProperty("ReadAtUtc").GetDateTime().Kind);
    }

    [Fact]
    public void InteractContract_DocumentsRequiredIdempotencyKey_Envelopes_AndRateLimit()
    {
        var method = typeof(DoctorMessagesController).GetMethod("Interact", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var post = Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Equal("{deliveryId}/interact", post.Template);

        Assert.Single(method.GetCustomAttributes<RequireIdempotencyKeyAttribute>());
        var rateLimit = Assert.Single(method.GetCustomAttributes<EnableRateLimitingAttribute>());
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, rateLimit.PolicyName);

        var responseTypes = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        Assert.Contains(responseTypes, item => item.StatusCode == 200 && item.Type == typeof(ApiEnvelope<InteractDeliveryResultDto>));
        Assert.Contains(responseTypes, item => item.StatusCode == 400 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 401 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 403 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 404 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 409 && item.Type == typeof(ApiEnvelope<object>));
        Assert.Contains(responseTypes, item => item.StatusCode == 429 && item.Type == typeof(ApiEnvelope<object>));
    }

    [Fact]
    public void InteractSuccessEnvelope_UsesExactPascalCaseShape_WithoutFinancialInternals()
    {
        var interactedAtUtc = new DateTime(2026, 7, 10, 9, 15, 0, DateTimeKind.Utc);
        var envelope = new ApiEnvelope<InteractDeliveryResultDto>(
            200,
            "Message interaction settled.",
            new InteractDeliveryResultDto
            {
                DeliveryId = "delivery-123",
                Status = "Accepted",
                InteractedAtUtc = interactedAtUtc,
                FeedbackText = null,
                IdempotencyStatus = InteractionPaymentResultStatus.Created
            });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        var root = document.RootElement;

        Assert.Equal(["Code", "Message", "Data"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        Assert.Equal("Message interaction settled.", root.GetProperty("Message").GetString());

        var data = root.GetProperty("Data");
        Assert.Equal(
            ["DeliveryId", "Status", "InteractedAtUtc", "FeedbackText", "IdempotencyStatus"],
            data.EnumerateObject().Select(property => property.Name));
        Assert.Equal("delivery-123", data.GetProperty("DeliveryId").GetString());
        Assert.Equal("Accepted", data.GetProperty("Status").GetString());
        Assert.Equal("Created", data.GetProperty("IdempotencyStatus").GetString());
        Assert.Equal(DateTimeKind.Utc, data.GetProperty("InteractedAtUtc").GetDateTime().Kind);
        Assert.DoesNotContain("Balance", data.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Idempotency-Key", data.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlatformFee", data.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InteractRequestHelper_SendsRequiredIdempotencyKeyHeaderAndBody()
    {
        using var request = CreateInteractRequest("delivery-123", "idem-123456", "Accept", " useful ");

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/doctor/messages/delivery-123/interact", request.RequestUri?.OriginalString);
        Assert.True(request.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.Equal("idem-123456", Assert.Single(values));
        Assert.NotNull(request.Content);
    }

    [Fact]
    public async Task InteractFeedbackContract_DocumentsOptionalNullableMaxLength_AndRejectsOversizedFeedback()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var requestSchema = schemas.EnumerateObject()
            .Single(schema => schema.Name.EndsWith(nameof(InteractDeliveryRequestDto), StringComparison.Ordinal))
            .Value;

        var requiredProperties = requestSchema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        Assert.Equal(["Decision"], requiredProperties);
        var feedbackSchema = requestSchema.GetProperty("properties").GetProperty(nameof(InteractDeliveryRequestDto.FeedbackText));
        Assert.True(feedbackSchema.GetProperty("nullable").GetBoolean());
        Assert.Equal(1000, feedbackSchema.GetProperty("maxLength").GetInt32());

        var exception = Assert.Throws<Phase8BadRequestException>(
            () => InteractionPaymentValidation.NormalizeFeedback(new string('x', 1001)));
        var envelope = ApiEnvelopeFactory.Create<object?>(
            StatusCodes.Status400BadRequest,
            exception.Message,
            null);

        using var envelopeDocument = JsonDocument.Parse(JsonSerializer.Serialize(envelope));
        Assert.Equal(400, envelopeDocument.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, envelopeDocument.RootElement.GetProperty("Data").ValueKind);
        Assert.DoesNotContain(new string('x', 1001), envelopeDocument.RootElement.ToString(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage CreateMarkReadRequest(string deliveryId)
    {
        return new HttpRequestMessage(HttpMethod.Put, $"/api/doctor/messages/{deliveryId}/read");
    }

    private static HttpRequestMessage CreateInteractRequest(
        string deliveryId,
        string idempotencyKey,
        string decision,
        string? feedbackText = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/doctor/messages/{deliveryId}/interact")
        {
            Content = JsonContent.Create(new
            {
                Decision = decision,
                FeedbackText = feedbackText
            })
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
