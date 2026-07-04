using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class DoctorTodayMessagesContractTests
{
    [Fact]
    public async Task TodayInbox_ReturnsExactPascalCaseEmptyEnvelope_AndValidatesPaging()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (userId, _) = await SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor", userId));

        using var response = await client.GetAsync("/api/doctor/messages/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "Code", "Message", "Data" }, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(200, root.GetProperty("Code").GetInt32());
        var data = root.GetProperty("Data");
        Assert.Equal(new[] { "BusinessDateEgypt", "Items", "NextCursor" }, data.EnumerateObject().Select(property => property.Name));
        Assert.Empty(data.GetProperty("Items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("NextCursor").ValueKind);

        using var invalidPage = await client.GetAsync("/api/doctor/messages/today?PageSize=101");
        using var zeroPage = await client.GetAsync("/api/doctor/messages/today?PageSize=0");
        using var malformedPage = await client.GetAsync("/api/doctor/messages/today?PageSize=not-an-integer");
        using var invalidCursor = await client.GetAsync("/api/doctor/messages/today?Cursor=not-a-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        await AssertEmptyEnvelopeAsync(malformedPage, 400);
        await AssertEmptyEnvelopeAsync(invalidCursor, 400);
    }

    [Fact]
    public async Task TodayInbox_AfterProductionReadLimit_ReturnsStandard429Envelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (userId, _) = await SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor", userId));

        HttpResponseMessage? response = null;
        try
        {
            for (var request = 0; request < 61; request++)
            {
                response?.Dispose();
                response = await client.GetAsync("/api/doctor/messages/today");
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
            await AssertEmptyEnvelopeAsync(response, 429);
        }
        finally
        {
            response?.Dispose();
        }
    }

    [Fact]
    public void PublicDtos_ContainOnlyDocumentedFields()
    {
        Assert.Equal(
            new[] { "BusinessDateEgypt", "Items", "NextCursor" },
            typeof(MediBridge.Services.DTOs.Messaging.TodayInboxDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new[] { "DeliveryId", "CampaignId", "Status", "DeliveryDateEgypt", "DeliveredAtUtc", "Title", "Description", "ClinicalResearchInfo", "Assets" },
            typeof(MediBridge.Services.DTOs.Messaging.TodayMessageDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new[] { "FileId", "Purpose", "OriginalFileName", "ContentType", "SizeBytes", "ReviewStatus", "AccessPath" },
            typeof(MediBridge.Services.DTOs.Messaging.DeliveryAssetDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new[] { "AccessUrl", "ExpiresAtUtc" },
            typeof(MediBridge.Services.DTOs.Messaging.DeliveryAssetAccessGrantDto).GetProperties().Select(property => property.Name));
    }

    [Theory]
    [InlineData("/api/doctor/messages/today")]
    [InlineData("/api/doctor/messages/delivery-1/assets/file-1/access")]
    public async Task DoctorMessageRoutes_RequireAuthentication(string route)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(string UserId, string DoctorId)> SeedApprovedDoctorAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"doctor-user-{suffix}";
        var doctorId = $"doctor-{suffix}";
        var email = $"doctor-{suffix}@example.test";
        db.Users.Add(new MediBridgeIdentityUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
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
            VerificationReference = $"verification/{suffix}",
            DailyMessageLimit = 10,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 50m,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (userId, doctorId);
    }

    private static async Task AssertEmptyEnvelopeAsync(HttpResponseMessage response, int expectedCode)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("Message").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("Data").ValueKind);
    }
}
