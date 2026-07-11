using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyDoctorSearchContractTests
{
    [Fact]
    public async Task GetCompanyDoctors_ReturnsSuccessEnvelopeDefaultsAndResponseFields()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var companyUserId = await SeedCompanyAsync(factory);
        await SeedDoctorAsync(factory, specialization: "Cardiology", experienceYears: 8, location: "Cairo", activityScore: 90m, pricePerMessage: 25m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, companyUserId);

        var response = await client.GetAsync("/api/company/doctors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        var page = data.GetProperty("Page");
        Assert.Equal(1, page.GetProperty("PageNumber").GetInt32());
        Assert.Equal(20, page.GetProperty("PageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("TotalCount").GetInt32());
        var doctor = data.GetProperty("Items")[0];
        Assert.True(doctor.TryGetProperty("DoctorId", out _));
        Assert.Equal("Cardiology", doctor.GetProperty("Specialization").GetString());
        Assert.Equal(8, doctor.GetProperty("ExperienceYears").GetInt32());
        Assert.Equal("Cairo", doctor.GetProperty("Location").GetString());
        Assert.Equal(90m, doctor.GetProperty("ActivityScore").GetDecimal());
        Assert.Equal(25m, doctor.GetProperty("PricePerMessage").GetDecimal());
    }

    [Fact]
    public async Task GetCompanyDoctors_WithoutToken_Returns401Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/company/doctors");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 401);
    }

    [Fact]
    public async Task GetCompanyDoctors_WithDoctorRole_Returns403Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));

        var response = await client.GetAsync("/api/company/doctors");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 403);
    }

    [Theory]
    [InlineData("PageNumber=0")]
    [InlineData("PageSize=0")]
    [InlineData("PageSize=101")]
    [InlineData("minActivityScore=101")]
    [InlineData("minPrice=50&maxPrice=10")]
    public async Task GetCompanyDoctors_WithInvalidQuery_Returns400Envelope(string query)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var companyUserId = await SeedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, companyUserId);

        var response = await client.GetAsync($"/api/company/doctors?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task GetCompanyDoctors_AfterRateLimit_Returns429Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var companyUserId = await SeedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, companyUserId);

        HttpResponseMessage response = new(HttpStatusCode.OK);
        for (var i = 0; i < 11; i++)
        {
            response = await client.GetAsync("/api/company/doctors");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 429);
    }

    private static async Task<string> SeedCompanyAsync(ContractWebAppFactory targetFactory, AccountStatus status = AccountStatus.Approved)
    {
        using var scope = targetFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-company-{suffix}@example.test", UserRole.Company, status);
        var profile = new MediBridge.Core.Entities.Profiles.CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = user.Id,
            CompanyName = "Phase 5 Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };

        await context.Users.AddAsync(user);
        await context.CompanyProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static async Task SeedDoctorAsync(ContractWebAppFactory targetFactory, string specialization, int experienceYears, string location, decimal activityScore, decimal pricePerMessage)
    {
        using var scope = targetFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-doctor-{suffix}@example.test", UserRole.Doctor, AccountStatus.Approved);
        var profile = new MediBridge.Core.Entities.Profiles.DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = user.Id,
            Specialization = specialization,
            ExperienceYears = experienceYears,
            Location = location,
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}",
            ActivityScore = activityScore,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = pricePerMessage,
            DailyMessageLimit = 10
        };

        await context.Users.AddAsync(user);
        await context.DoctorProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role, AccountStatus status)
    {
        var normalized = email.ToUpperInvariant();
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            Role = role,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
