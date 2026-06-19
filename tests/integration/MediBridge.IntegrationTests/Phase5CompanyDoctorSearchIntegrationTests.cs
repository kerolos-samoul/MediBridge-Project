using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5CompanyDoctorSearchIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase5CompanyDoctorSearchIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Search_ExcludesUnapprovedSuspendedSoftDeletedAndZeroPriceDoctors()
    {
        await factory.InitializeDatabaseAsync();
        using var client = await CreateAuthorizedCompanyClientAsync();
        var specialization = $"Eligibility-{Guid.NewGuid():N}";
        var eligible = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 7, "Cairo", 90m, 40m);
        await SeedDoctorAsync(AccountStatus.Pending, DoctorMarketplaceStatus.Active, false, specialization, 7, "Cairo", 95m, 40m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Suspended, false, specialization, 7, "Cairo", 96m, 40m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, true, specialization, 7, "Cairo", 97m, 40m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 7, "Cairo", 98m, 0m);

        var response = await client.GetAsync($"/api/company/doctors?PageNumber=1&PageSize=20&specialization={Uri.EscapeDataString(specialization)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doctors = await ReadDoctorsAsync(response);
        Assert.Equal([eligible], doctors.Select(doctor => doctor.GetProperty("DoctorId").GetString()!).ToArray());
    }

    [Fact]
    public async Task Search_AppliesAllFiltersWithAndSemantics()
    {
        await factory.InitializeDatabaseAsync();
        using var client = await CreateAuthorizedCompanyClientAsync();
        var specialization = $"Oncology-{Guid.NewGuid():N}";
        var matching = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 12, "Alexandria", 88m, 75m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, "Cardiology", 12, "Alexandria", 88m, 75m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 4, "Alexandria", 88m, 75m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 12, "Cairo", 88m, 75m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 12, "Alexandria", 60m, 75m);
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 12, "Alexandria", 88m, 150m);

        var response = await client.GetAsync($"/api/company/doctors?specialization={Uri.EscapeDataString(specialization)}&minExperienceYears=10&maxExperienceYears=15&location=Alexandria&minActivityScore=80&minPrice=50&maxPrice=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doctors = await ReadDoctorsAsync(response);
        Assert.Equal([matching], doctors.Select(doctor => doctor.GetProperty("DoctorId").GetString()!).ToArray());
    }

    [Fact]
    public async Task Search_OrdersByActivityDescendingPriceAscendingThenStableId()
    {
        await factory.InitializeDatabaseAsync();
        using var client = await CreateAuthorizedCompanyClientAsync();
        var specialization = $"Ordering-{Guid.NewGuid():N}";
        var first = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 95m, 50m, "doctor-0001");
        var second = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 95m, 50m, "doctor-0002");
        var highPriceSameScore = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 95m, 70m, "doctor-0000");
        var lowerScore = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 80m, 10m, "doctor-9999");

        var response = await client.GetAsync($"/api/company/doctors?PageSize=10&specialization={Uri.EscapeDataString(specialization)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doctors = await ReadDoctorsAsync(response);
        Assert.Equal([first, second, highPriceSameScore, lowerScore], doctors.Select(doctor => doctor.GetProperty("DoctorId").GetString()!).ToArray());
    }

    [Fact]
    public async Task Repository_LoadsEligibleDoctorsBySelectedIdsOnly()
    {
        await factory.InitializeDatabaseAsync();
        var specialization = $"SelectedIds-{Guid.NewGuid():N}";
        var first = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 95m, 40m, $"selected-1-{Guid.NewGuid():N}");
        var second = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 9, "Giza", 90m, 30m, $"selected-2-{Guid.NewGuid():N}");
        await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 10, "Alexandria", 100m, 20m, $"not-selected-{Guid.NewGuid():N}");
        var suspended = await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Suspended, false, specialization, 8, "Cairo", 80m, 40m, $"suspended-{Guid.NewGuid():N}");

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<MediBridge.Core.Interfaces.IDomainUnitOfWork>().Profiles;

        var doctors = await repository.ListEligibleDoctorsByIdsAsync([second, suspended, first], CancellationToken.None);

        Assert.Equal([first, second], doctors.Select(doctor => doctor.Id).ToArray());
    }

    [Fact]
    public async Task GetCompanyDoctors_WithEmptyBodyForCampaignSubmission_Returns400Envelope()
    {
        await factory.InitializeDatabaseAsync();
        using var client = await CreateAuthorizedCompanyClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_UsesDefaultPageSizeMaximumPageSizeEmptyPageAndRejectsInvalidBounds()
    {
        await factory.InitializeDatabaseAsync();
        using var client = await CreateAuthorizedCompanyClientAsync();
        var specialization = $"Pagination-{Guid.NewGuid():N}";
        for (var index = 0; index < 25; index++)
        {
            await SeedDoctorAsync(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, specialization, 8, "Cairo", 90m - index, 50m, $"doctor-page-{index:D3}");
        }

        var escapedSpecialization = Uri.EscapeDataString(specialization);
        var defaultResponse = await client.GetAsync($"/api/company/doctors?specialization={escapedSpecialization}");
        var defaultData = await ReadDataAsync(defaultResponse);
        Assert.Equal(20, defaultData.GetProperty("Page").GetProperty("PageSize").GetInt32());
        Assert.Equal(20, defaultData.GetProperty("Items").GetArrayLength());

        var maximumResponse = await client.GetAsync($"/api/company/doctors?specialization={escapedSpecialization}&PageSize=100");
        var maximumData = await ReadDataAsync(maximumResponse);
        Assert.Equal(100, maximumData.GetProperty("Page").GetProperty("PageSize").GetInt32());

        var emptyResponse = await client.GetAsync($"/api/company/doctors?specialization={escapedSpecialization}&PageNumber=3&PageSize=20");
        var emptyData = await ReadDataAsync(emptyResponse);
        Assert.Equal(0, emptyData.GetProperty("Items").GetArrayLength());

        var invalidResponse = await client.GetAsync("/api/company/doctors?PageNumber=0&PageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    private async Task<HttpClient> CreateAuthorizedCompanyClientAsync()
    {
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", company.UserId));
        return client;
    }

    private async Task<string> SeedDoctorAsync(
        AccountStatus accountStatus,
        DoctorMarketplaceStatus marketplaceStatus,
        bool isDeleted,
        string specialization,
        int experienceYears,
        string location,
        decimal activityScore,
        decimal pricePerMessage,
        string? doctorId = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-doctor-{suffix}@example.test", UserRole.Doctor, accountStatus);
        var profile = new DoctorProfile
        {
            Id = doctorId ?? $"doctor-{suffix}",
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
            Status = marketplaceStatus,
            PricePerMessage = pricePerMessage,
            IsDeleted = isDeleted,
            DeletedAtUtc = isDeleted ? DateTime.UtcNow : null
        };

        await context.Users.AddAsync(user);
        await context.DoctorProfiles.AddAsync(profile);
        await context.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("Data").Clone();
    }

    private static async Task<IReadOnlyList<JsonElement>> ReadDoctorsAsync(HttpResponseMessage response)
    {
        var data = await ReadDataAsync(response);
        return data.GetProperty("Items").EnumerateArray().Select(item => item.Clone()).ToArray();
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
