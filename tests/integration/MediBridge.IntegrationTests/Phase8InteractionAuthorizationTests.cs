using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionAuthorizationTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("read")]
    [InlineData("interact")]
    public async Task UnauthenticatedRequests_Return401(string endpoint)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        using var client = factory.CreateClient();

        using var response = await SendPhase8RequestAsync(client, endpoint, "delivery-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Company", "read")]
    [InlineData("Company", "interact")]
    [InlineData("Admin", "read")]
    [InlineData("Admin", "interact")]
    public async Task NonDoctorRoles_Return403BeforeServiceMutation(string role, string endpoint)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, $"{role}-phase8-user"));
        if (endpoint == "interact")
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"role-{role}-key-0001");
        }

        using var response = await SendPhase8RequestAsync(client, endpoint, "delivery-1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("interact")]
    public async Task AnotherDoctorCannotDiscoverOrMutateDelivery(string endpoint)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var fixture = await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, $"cross-auth-{endpoint}");
        var other = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services,
            $"cross-auth-user-{Guid.NewGuid():N}",
            $"cross-auth-doctor-{Guid.NewGuid():N}",
            $"cross-auth-{Guid.NewGuid():N}@example.test",
            100m,
            10,
            UtcNow.AddDays(-5));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", other.UserId));
        if (endpoint == "interact")
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"cross-auth-key-{Guid.NewGuid():N}");
        }

        using var response = await SendPhase8RequestAsync(client, endpoint, fixture.DeliveryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await db.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == fixture.DeliveryId);
        Assert.Null(delivery.ReadAtUtc);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
        Assert.Equal(ReservationStatus.Reserved, delivery.ReservationStatus);
        Assert.Equal(0, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
    }

    [Theory]
    [InlineData("suspended", "read")]
    [InlineData("suspended", "interact")]
    [InlineData("deleted", "read")]
    [InlineData("deleted", "interact")]
    [InlineData("unapproved", "read")]
    [InlineData("unapproved", "interact")]
    public async Task NonActiveApprovedDoctors_Return403(string doctorState, string endpoint)
    {
        await using var factory = new Phase8ReadTrackingIntegrationTests.FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var userId = await SeedDoctorForStateAsync(factory.Services, doctorState);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", userId));
        if (endpoint == "interact")
        {
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"doctor-state-key-{Guid.NewGuid():N}");
        }

        using var response = await SendPhase8RequestAsync(client, endpoint, "delivery-1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendPhase8RequestAsync(HttpClient client, string endpoint, string deliveryId)
    {
        return endpoint switch
        {
            "read" => client.PutAsync($"/api/doctor/messages/{deliveryId}/read", null),
            "interact" => client.PostAsync(
                $"/api/doctor/messages/{deliveryId}/interact",
                new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json")),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "Unknown endpoint.")
        };
    }

    private static async Task<string> SeedDoctorForStateAsync(IServiceProvider services, string doctorState)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return doctorState switch
        {
            "suspended" => (await Phase7DeliveryTestHelpers.SeedSuspendedDoctorAsync(
                services,
                $"suspended-user-{suffix}",
                $"suspended-doctor-{suffix}",
                $"suspended-{suffix}@example.test",
                100m,
                10,
                UtcNow.AddDays(-5))).UserId,
            "deleted" => (await Phase7DeliveryTestHelpers.SeedDeletedDoctorAsync(
                services,
                $"deleted-user-{suffix}",
                $"deleted-doctor-{suffix}",
                $"deleted-{suffix}@example.test",
                100m,
                10,
                UtcNow.AddDays(-5),
                UtcNow.AddDays(-1))).UserId,
            "unapproved" => await SeedUnapprovedDoctorAsync(services, suffix),
            _ => throw new ArgumentOutOfRangeException(nameof(doctorState), doctorState, "Unknown doctor state.")
        };
    }

    private static async Task<string> SeedUnapprovedDoctorAsync(IServiceProvider services, string suffix)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var userId = $"unapproved-user-{suffix}";
        var doctorId = $"unapproved-doctor-{suffix}";
        var email = $"unapproved-{suffix}@example.test";
        db.Users.Add(new MediBridgeIdentityUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Pending,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = UtcNow.AddDays(-5)
        });
        db.DoctorProfiles.Add(new DoctorProfile
        {
            Id = doctorId,
            UserId = userId,
            Specialization = "Cardiology",
            ExperienceYears = 10,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase8-auth/{suffix}",
            DailyMessageLimit = 10,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 100m,
            CreatedAtUtc = UtcNow.AddDays(-5)
        });
        await db.SaveChangesAsync();
        return userId;
    }
}
