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

public sealed class AdminDoctorPricingContractTests
{
    [Fact]
    public async Task PutAdminDoctorPrice_ReturnsPriceEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminAndDoctorAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                PricePerMessage = 75.25m,
                Reason = "Contract rate"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(seed.DoctorId, data.GetProperty("doctorId").GetString());
        Assert.Equal(75.25m, data.GetProperty("pricePerMessage").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
        Assert.True(data.GetProperty("pricingIsActive").GetBoolean());
    }

    [Fact]
    public async Task DeactivateAdminDoctorPrice_ReturnsInactivePriceEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminAndDoctorAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Pause paid campaigns." }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(seed.DoctorId, data.GetProperty("doctorId").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("pricePerMessage").ValueKind);
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
        Assert.False(data.GetProperty("pricingIsActive").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10.001")]
    public async Task PutAdminDoctorPrice_WithInvalidPrice_ReturnsValidationEnvelope(string? price)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminAndDoctorAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                PricePerMessage = price is null ? (decimal?)null : decimal.Parse(price),
                Reason = "Invalid"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task DeactivateAdminDoctorPrice_ValidatesAuthorizationNotFoundAndConflict()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedAdminAndDoctorAsync(factory);
        var companyUserId = await SeedUserAsync(factory, UserRole.Company);

        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Unauthorized" }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var companyClient = factory.CreateClient();
        companyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Company", companyUserId));
        using var forbidden = await companyClient.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Forbidden" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));
        using var validation = await adminClient.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);

        using var notFound = await adminClient.PutAsync(
            $"/api/admin/doctors/{Guid.NewGuid():N}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Unknown doctor" }));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

        using var first = await adminClient.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Pause paid campaigns." }));
        using var second = await adminClient.PutAsync(
            $"/api/admin/doctors/{seed.DoctorId}/price/deactivate",
            Phase5ContractTestHelpers.CreateJsonContent(new { Reason = "Already paused." }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private static async Task<PricingSeed> SeedAdminAndDoctorAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = CreateUser($"pricing-admin-{suffix}@example.test", UserRole.Admin);
        var doctorUser = CreateUser($"pricing-doctor-{suffix}@example.test", UserRole.Doctor);
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            PricePerMessage = 50m,
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };

        await context.Users.AddRangeAsync(admin, doctorUser);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();
        return new PricingSeed(admin.Id, doctor.Id);
    }

    private static async Task<string> SeedUserAsync(ContractWebAppFactory factory, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = CreateUser($"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@example.test", role);
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
    {
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Role = role,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record PricingSeed(string AdminUserId, string DoctorId);
}
