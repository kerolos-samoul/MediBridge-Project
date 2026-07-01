using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignContractTests
{
    [Fact]
    public async Task PostCompanyCampaigns_IsNotARegisteredPublicOperation()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedApprovedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var response = await client.PostAsync(
            "/api/company/campaigns",
            Phase5ContractTestHelpers.CreateJsonContent(new { }));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task PostCompanyCampaignDrafts_WithValidRequest_CreatesOwnedDraftCampaign()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedApprovedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        var response = await client.PostAsync(
            "/api/company/campaigns/drafts",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                Title = "Draft campaign",
                Description = "Draft campaign description",
                ClinicalResearchInfo = "Optional research context"
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 201);
        var campaignId = data.GetProperty("CampaignId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(campaignId));
        Assert.Equal(seed.CompanyId, data.GetProperty("CompanyId").GetString());
        Assert.Equal("Draft campaign", data.GetProperty("Title").GetString());
        Assert.Equal("Draft campaign description", data.GetProperty("Description").GetString());
        Assert.Equal("Optional research context", data.GetProperty("ClinicalResearchInfo").GetString());
        Assert.Equal("Draft", data.GetProperty("Status").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var persisted = await context.Campaigns.SingleAsync(campaign => campaign.Id == campaignId);
        Assert.Equal(seed.CompanyId, persisted.CompanyId);
        Assert.Equal(CampaignStatus.Draft, persisted.Status);
    }

    [Theory]
    [InlineData("missing-title")]
    [InlineData("long-title")]
    [InlineData("missing-description")]
    [InlineData("long-description")]
    [InlineData("long-research")]
    public async Task PostCompanyCampaignDrafts_WithInvalidRequest_Returns400Envelope(string scenario)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedApprovedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        var response = await client.PostAsync(
            "/api/company/campaigns/drafts",
            Phase5ContractTestHelpers.CreateJsonContent(CreateDraftScenarioRequest(scenario)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Theory]
    [InlineData("/api/company/campaigns/drafts")]
    public async Task PostCompanyCampaignEndpoints_RequireCompanyAuthorization(string route)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymousClient = factory.CreateClient();
        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Doctor"));
        var requestBody = Phase5ContractTestHelpers.CreateJsonContent(new { });
        var doctorRequestBody = Phase5ContractTestHelpers.CreateJsonContent(new { });

        var unauthorized = await anonymousClient.PostAsync(route, requestBody);
        var forbidden = await doctorClient.PostAsync(route, doctorRequestBody);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task CompanyCampaignEndpoints_RequireCompanyAuthorizationAndReturnOwnedListDetailEnvelopes()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedApprovedCompanyAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var campaignId = await CreateDraftAsync(client);

        var listResponse = await client.GetAsync("/api/company/campaigns");
        var detailResponse = await client.GetAsync($"/api/company/campaigns/{campaignId}");
        var missingResponse = await client.GetAsync($"/api/company/campaigns/{Guid.NewGuid():N}");
        using var anonymousClient = factory.CreateClient();
        var unauthorized = await anonymousClient.GetAsync("/api/company/campaigns");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));
        var forbidden = await client.GetAsync("/api/company/campaigns");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listDocument = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(listDocument, 200);
            Assert.Equal(1, data.GetProperty("Items").GetArrayLength());
        }

        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using (var detailDocument = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal(campaignId, Phase5ContractTestHelpers.AssertDataEnvelope(detailDocument, 200).GetProperty("CampaignId").GetString());
        }

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private static object CreateDraftScenarioRequest(string scenario)
    {
        return scenario switch
        {
            "missing-title" => new { Title = "", Description = "Description", ClinicalResearchInfo = "Research" },
            "long-title" => new { Title = new string('T', 201), Description = "Description", ClinicalResearchInfo = "Research" },
            "missing-description" => new { Title = "Title", Description = "", ClinicalResearchInfo = "Research" },
            "long-description" => new { Title = "Title", Description = new string('D', 4001), ClinicalResearchInfo = "Research" },
            "long-research" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = new string('R', 4001) },
            _ => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "Research" }
        };
    }

    private static async Task<string> CreateDraftAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/company/campaigns/drafts",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                Title = "Draft campaign",
                Description = "Draft campaign description"
            }));
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("Data").GetProperty("CampaignId").GetString()!;
    }

    private static async Task<CampaignContractSeed> SeedApprovedCompanyAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var companyUser = CreateUser($"phase5-company-{suffix}@example.test", UserRole.Company, AccountStatus.Approved);
        var company = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = companyUser.Id,
            CompanyName = "Phase 5 Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };
        await context.Users.AddAsync(companyUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.SaveChangesAsync();
        return new CampaignContractSeed(companyUser.Id, company.Id);
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

    private sealed record CampaignContractSeed(string CompanyUserId, string CompanyId);
}
