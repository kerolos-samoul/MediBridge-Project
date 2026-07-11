using System.Net;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task DoctorReadRateLimit_RejectedRequestDoesNotCallServiceMutation()
    {
        var service = new CountingDoctorMessageService();
        await using var factory = new DoctorReadRateLimitFactory(service);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateToken("Doctor", "doctor-rate-limit-user"));

        for (var i = 0; i < 10; i++)
        {
            using var acceptedResponse = await client.PutAsync("/api/doctor/messages/delivery-rate-limit/read", null);
            Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        }

        using var rejectedResponse = await client.PutAsync("/api/doctor/messages/delivery-rate-limit/read", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.Equal(10, service.MarkReadCalls);
        using var document = JsonDocument.Parse(await rejectedResponse.Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    private sealed class DoctorReadRateLimitFactory(CountingDoctorMessageService service) : ConfiguredWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDoctorMessageService>();
                services.AddSingleton<IDoctorMessageService>(service);
            });
        }
    }

    private sealed class CountingDoctorMessageService : IDoctorMessageService
    {
        public int MarkReadCalls { get; private set; }

        public Task<TodayInboxDto> GetTodayInboxAsync(string actorUserId, int? pageSize = null, string? cursor = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(string actorUserId, string deliveryId, string fileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReadTrackingResultDto> MarkReadAsync(string actorUserId, string deliveryId, CancellationToken cancellationToken = default)
        {
            MarkReadCalls++;
            return Task.FromResult(new ReadTrackingResultDto(deliveryId, DeliveryStatus.Active, DateTime.UtcNow, AlreadyRead: false));
        }

        public Task<DoctorInteractionResultDto> InteractAsync(string actorUserId, string deliveryId, string? idempotencyKey, DoctorInteractionRequestDto request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
