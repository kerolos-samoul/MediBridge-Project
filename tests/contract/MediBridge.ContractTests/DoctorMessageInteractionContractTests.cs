using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Controllers;
using MediBridge.APIs.OpenApi;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.DTOs.Messaging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class DoctorMessageInteractionContractTests
{
    [Fact]
    public void OpenApiFixture_ContainsPhase8ReadAndInteractPaths()
    {
        var contract = File.ReadAllText(FindRepoFile("specs/010-interaction-payments/contracts/interaction-payments-api.yaml"));

        Assert.Contains("/api/doctor/messages/{deliveryId}/read:", contract, StringComparison.Ordinal);
        Assert.Contains("/api/doctor/messages/{deliveryId}/interact:", contract, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", contract, StringComparison.Ordinal);
        Assert.Contains("EnvelopeReadTracking", contract, StringComparison.Ordinal);
        Assert.Contains("EnvelopeInteractionResult", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerActions_UseDoctorInteractionRateLimit_AndDocumentedRoutes()
    {
        var read = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.MarkRead))!;
        var interact = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.Interact))!;

        Assert.Equal("{deliveryId}/read", read.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("{deliveryId}/interact", interact.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, read.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, interact.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    [Fact]
    public void PublicDtos_ContainOnlyDoctorSafeInteractionFields()
    {
        Assert.Equal(
            new[] { "DeliveryId", "Status", "ReadAtUtc", "AlreadyRead" },
            typeof(ReadTrackingResultDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new[] { "Outcome", "Feedback" },
            typeof(DoctorInteractionRequestDto).GetProperties().Select(property => property.Name));
        Assert.Equal(
            new[] { "DeliveryId", "Status", "ReservationStatus", "InteractedAtUtc", "ReadAtUtc", "FeedbackAccepted", "FeedbackQualifiesForScore", "ChargeAmount", "DoctorEarnings", "PlatformFeeAmount", "Replayed" },
            typeof(DoctorInteractionResultDto).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void MarkRead_DocumentsSuccessAndSafeErrorEnvelopes()
    {
        var read = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.MarkRead))!;
        var responseTypes = read
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => (attribute.StatusCode, attribute.Type))
            .ToDictionary(pair => pair.StatusCode, pair => pair.Type);

        Assert.Equal(typeof(ApiEnvelope<ReadTrackingResultDto>), responseTypes[200]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[401]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[403]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[404]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[429]);
    }

    [Fact]
    public void OpenApiFixture_DocumentsReadSuccessForActiveAcceptedRejectedAndSafeErrors()
    {
        var contract = File.ReadAllText(FindRepoFile("specs/010-interaction-payments/contracts/interaction-payments-api.yaml"));

        Assert.Contains("may record or replay ReadAtUtc for Active, Accepted, or Rejected current-day deliveries", contract, StringComparison.Ordinal);
        Assert.Contains("without changing settlement, balances, transactions, ledger entries, or outcome state", contract, StringComparison.Ordinal);
        Assert.Contains("EnvelopeReadTracking", contract, StringComparison.Ordinal);
        foreach (var statusCode in new[] { "'200':", "'401':", "'403':", "'404':", "'429':" })
        {
            Assert.Contains(statusCode, contract, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Interact_DocumentsSuccessSafeErrorsAndRequiresIdempotencyKey()
    {
        var interact = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.Interact))!;
        var responseTypes = interact
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => (attribute.StatusCode, attribute.Type))
            .ToDictionary(pair => pair.StatusCode, pair => pair.Type);

        Assert.NotNull(interact.GetCustomAttribute<RequireIdempotencyKeyAttribute>());
        Assert.Equal(typeof(ApiEnvelope<DoctorInteractionResultDto>), responseTypes[200]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[400]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[401]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[403]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[404]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[409]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[429]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[503]);
    }

    [Theory]
    [InlineData("""{"Outcome":"Maybe"}""")]
    [InlineData("""{"Outcome":"Accept","Feedback":"<b>unsafe</b>"}""")]
    [InlineData("""{"Outcome":"Reject","Feedback":"[label](https://example.test)"}""")]
    public async Task Interact_InvalidOutcomeOrFeedback_ReturnsSafeBadRequestEnvelope(string body)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (userId, _) = await SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor", userId));
        client.DefaultRequestHeaders.Add("Idempotency-Key", "contract-validation-key-0001");

        using var response = await client.PostAsync(
            "/api/doctor/messages/missing-delivery/interact",
            new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task Interact_InvalidIdempotencyKey_IsRejectedAndRedacted()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (userId, _) = await SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor", userId));
        const string rawKey = "short";
        client.DefaultRequestHeaders.Add("Idempotency-Key", rawKey);

        using var response = await client.PostAsync(
            "/api/doctor/messages/missing-delivery/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rawKey, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpMethodName.Put, "/api/doctor/messages/delivery-1/read")]
    [InlineData(HttpMethodName.Post, "/api/doctor/messages/delivery-1/interact")]
    public async Task Phase8Routes_RequireAuthentication(HttpMethodName method, string route)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(method == HttpMethodName.Put ? HttpMethod.Put : HttpMethod.Post, route);
        if (method == HttpMethodName.Post)
        {
            request.Content = new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Interact_RequiresIdempotencyKeyBeforeDeliveryMutation()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var (userId, _) = await SeedApprovedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor", userId));

        using var response = await client.PostAsync(
            "/api/doctor/messages/missing-delivery/interact",
            new StringContent("""{"Outcome":"Accept"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, document.RootElement.GetProperty("Code").GetInt32());
        Assert.DoesNotContain("Idempotency-Key", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string UserId, string DoctorId)> SeedApprovedDoctorAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"phase8-contract-doctor-user-{suffix}";
        var doctorId = $"phase8-contract-doctor-{suffix}";
        var email = $"phase8-contract-doctor-{suffix}@example.test";
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
            VerificationReference = $"phase8-contract/{suffix}",
            DailyMessageLimit = 10,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 50m,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (userId, doctorId);
    }

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}

public enum HttpMethodName
{
    Put,
    Post
}
