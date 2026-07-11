using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionRateLimitTests
{
    [Fact]
    public async Task ThrottledReadRequest_DoesNotCallReadMutation()
    {
        var service = new CountingDoctorMessageService();
        await using var factory = new DoctorInteractionRateLimitFactory(service);
        using var client = CreateDoctorClient(factory, "phase8-read-rate-user", idempotencyKey: null);

        for (var i = 0; i < 10; i++)
        {
            using var accepted = await client.PutAsync("/api/doctor/messages/rate-limit-delivery/read", null);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var rejected = await client.PutAsync("/api/doctor/messages/rate-limit-delivery/read", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(10, service.MarkReadCalls);
        Assert.Equal(0, service.InteractCalls);
    }

    [Fact]
    public async Task ThrottledInteractRequest_DoesNotCallSettlementMutation()
    {
        var service = new CountingDoctorMessageService();
        await using var factory = new DoctorInteractionRateLimitFactory(service);
        using var client = CreateDoctorClient(factory, "phase8-interact-rate-user", "phase8-rate-key-0001");

        for (var i = 0; i < 10; i++)
        {
            using var accepted = await client.PostAsync(
                "/api/doctor/messages/rate-limit-delivery/interact",
                new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        using var rejected = await client.PostAsync(
            "/api/doctor/messages/rate-limit-delivery/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(10, service.InteractCalls);
        Assert.Equal(0, service.MarkReadCalls);
    }

    private static HttpClient CreateDoctorClient(
        DoctorInteractionRateLimitFactory factory,
        string userId,
        string? idempotencyKey)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", userId));
        if (idempotencyKey is not null)
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        }

        return client;
    }

    private sealed class DoctorInteractionRateLimitFactory(CountingDoctorMessageService service) : ConfiguredWebAppFactory
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
        public int InteractCalls { get; private set; }

        public Task<TodayInboxDto> GetTodayInboxAsync(string actorUserId, int? pageSize = null, string? cursor = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(string actorUserId, string deliveryId, string fileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReadTrackingResultDto> MarkReadAsync(string actorUserId, string deliveryId, CancellationToken cancellationToken = default)
        {
            MarkReadCalls++;
            return Task.FromResult(new ReadTrackingResultDto(deliveryId, DeliveryStatus.Active, DateTime.UtcNow, AlreadyRead: true));
        }

        public Task<DoctorInteractionResultDto> InteractAsync(string actorUserId, string deliveryId, string? idempotencyKey, DoctorInteractionRequestDto request, CancellationToken cancellationToken = default)
        {
            InteractCalls++;
            return Task.FromResult(new DoctorInteractionResultDto(
                deliveryId,
                DeliveryStatus.Accepted,
                ReservationStatus.Charged,
                DateTime.UtcNow,
                ReadAtUtc: null,
                FeedbackAccepted: false,
                FeedbackQualifiesForScore: false,
                ChargeAmount: 100m,
                DoctorEarnings: 87.65m,
                PlatformFeeAmount: 12.35m,
                Replayed: false));
        }
    }
}
