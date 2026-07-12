using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9CompanyDoctorSearchScoreIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase9CompanyDoctorSearchScoreIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task CompanyDoctorSearch_UsesLatestCommittedDoctorActivityScore()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await SeedCompanyAndDoctorAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateToken("Company", seed.CompanyUserId));

        using var response = await client.GetAsync("/api/company/doctors?MinActivityScore=80&PageNumber=1&PageSize=20");

        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var item = document.RootElement.GetProperty("Data").GetProperty("Items").EnumerateArray().Single();
        Assert.Equal(seed.DoctorId, item.GetProperty("DoctorId").GetString());
        Assert.Equal(91.4m, item.GetProperty("ActivityScore").GetDecimal());
    }

    private async Task<SearchSeed> SeedCompanyAndDoctorAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyUser = CreateUser($"phase9-company-{suffix}@medibridge.local", UserRole.Company);
        var doctorUser = CreateUser($"phase9-doctor-search-{suffix}@medibridge.local", UserRole.Doctor);
        var company = new CompanyProfile
        {
            Id = $"phase9-company-{suffix}",
            UserId = companyUser.Id,
            CompanyName = "Phase 9 Pharma",
            LicenseNumber = $"phase9-license-{suffix}",
            ContactName = "Phase 9 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase9/company/{suffix}"
        };
        var doctor = new DoctorProfile
        {
            Id = $"phase9-search-doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 12,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"phase9/doctor/{suffix}",
            PricePerMessage = 50m,
            DailyMessageLimit = 10,
            MinimumWeeklyRequirement = 5,
            ActivityScore = 91.4m
        };

        await context.Users.AddRangeAsync(companyUser, doctorUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();
        return new SearchSeed(companyUser.Id, doctor.Id);
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

    private sealed record SearchSeed(string CompanyUserId, string DoctorId);
}
