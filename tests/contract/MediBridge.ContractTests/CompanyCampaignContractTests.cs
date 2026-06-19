using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyCampaignContractTests
{
    [Fact]
    public async Task PostCompanyCampaigns_WithValidRequest_Returns201PendingReviewEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReadyCampaignInputsAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(
                Phase5ContractTestHelpers.CreateCampaignRequest([seed.AssetId], [seed.DoctorId]))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(request);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 201);
        Assert.True(data.TryGetProperty("CampaignId", out _));
        Assert.Equal("PendingReview", data.GetProperty("Status").GetString());
        Assert.Equal(1, data.GetProperty("TargetCount").GetInt32());
        Assert.True(data.TryGetProperty("SubmittedAtUtc", out _));
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("missing-title")]
    [InlineData("missing-description")]
    [InlineData("missing-research")]
    [InlineData("missing-asset")]
    [InlineData("no-targets")]
    [InlineData("over-100-targets")]
    [InlineData("duplicate-targets")]
    public async Task PostCompanyCampaigns_WithInvalidRequest_Returns400Envelope(string scenario)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReadyCampaignInputsAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var requestBody = CreateScenarioRequest(scenario, seed.AssetId, seed.DoctorId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(requestBody)
        };
        if (scenario != "missing-key")
        {
            Phase5ContractTestHelpers.AddIdempotencyKey(request);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task PostCompanyCampaigns_WithInsufficientWalletOrConflictingIdempotency_Returns409Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReadyCampaignInputsAsync(factory, availableBalance: 10m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        using var insufficient = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(
                Phase5ContractTestHelpers.CreateCampaignRequest([seed.AssetId], [seed.DoctorId]))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(insufficient);

        var insufficientResponse = await client.SendAsync(insufficient);

        Assert.Equal(HttpStatusCode.Conflict, insufficientResponse.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await insufficientResponse.Content.ReadAsStreamAsync()))
        {
            Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 409);
        }

        var richSeed = await SeedReadyCampaignInputsAsync(factory, availableBalance: 1000m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, richSeed.CompanyUserId);
        var key = $"idem-{Guid.NewGuid():N}";
        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(
                Phase5ContractTestHelpers.CreateCampaignRequest([richSeed.AssetId], [richSeed.DoctorId]))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(first, key);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(first)).StatusCode);
        using var conflicting = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(new
            {
                Title = "Different",
                Description = "Phase 5 campaign description",
                ClinicalResearchInfo = "Phase 5 clinical research information",
                AssetIds = new[] { richSeed.AssetId },
                TargetDoctorIds = new[] { richSeed.DoctorId }
            })
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(conflicting, key);

        var conflictResponse = await client.SendAsync(conflicting);

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        using var conflictDocument = await JsonDocument.ParseAsync(await conflictResponse.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(conflictDocument.RootElement, 409);
    }

    [Fact]
    public async Task CompanyCampaignEndpoints_RequireCompanyAuthorizationAndReturnOwnedListDetailEnvelopes()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReadyCampaignInputsAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var campaignId = await SubmitCampaignAsync(client, seed.AssetId, seed.DoctorId);

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

    [Fact]
    public async Task PostCompanyCampaigns_AfterRateLimit_Returns429Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedReadyCampaignInputsAsync(factory, availableBalance: 100000m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        HttpResponseMessage response = new(HttpStatusCode.OK);
        for (var index = 0; index < 11; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
            {
                Content = Phase5ContractTestHelpers.CreateJsonContent(
                    Phase5ContractTestHelpers.CreateCampaignRequest([seed.AssetId], [seed.DoctorId]))
            };
            Phase5ContractTestHelpers.AddIdempotencyKey(request, $"idem-{Guid.NewGuid():N}");
            response = await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 429);
    }

    private static object CreateScenarioRequest(string scenario, string assetId, string doctorId)
    {
        return scenario switch
        {
            "missing-title" => new { Title = "", Description = "Description", ClinicalResearchInfo = "Research", AssetIds = new[] { assetId }, TargetDoctorIds = new[] { doctorId } },
            "missing-description" => new { Title = "Title", Description = "", ClinicalResearchInfo = "Research", AssetIds = new[] { assetId }, TargetDoctorIds = new[] { doctorId } },
            "missing-research" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "", AssetIds = new[] { assetId }, TargetDoctorIds = new[] { doctorId } },
            "missing-asset" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "Research", AssetIds = Array.Empty<string>(), TargetDoctorIds = new[] { doctorId } },
            "no-targets" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "Research", AssetIds = new[] { assetId }, TargetDoctorIds = Array.Empty<string>() },
            "over-100-targets" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "Research", AssetIds = new[] { assetId }, TargetDoctorIds = Enumerable.Range(0, 101).Select(index => $"doctor-{index}").ToArray() },
            "duplicate-targets" => new { Title = "Title", Description = "Description", ClinicalResearchInfo = "Research", AssetIds = new[] { assetId }, TargetDoctorIds = new[] { doctorId, doctorId } },
            _ => Phase5ContractTestHelpers.CreateCampaignRequest([assetId], [doctorId])
        };
    }

    private static async Task<string> SubmitCampaignAsync(HttpClient client, string assetId, string doctorId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/campaigns")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(
                Phase5ContractTestHelpers.CreateCampaignRequest([assetId], [doctorId]))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(request);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("Data").GetProperty("CampaignId").GetString()!;
    }

    private static async Task<CampaignContractSeed> SeedReadyCampaignInputsAsync(ContractWebAppFactory factory, decimal availableBalance = 1000m)
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
        var doctorUser = CreateUser($"phase5-doctor-{suffix}@example.test", UserRole.Doctor, AccountStatus.Approved);
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 7,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}",
            ActivityScore = 90m,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 50m
        };
        var asset = new StoredFile
        {
            Id = $"asset-{suffix}",
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = company.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "asset.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"safe/{suffix}",
            ReviewStatus = StoredFileReviewStatus.Approved,
            UploadStatus = StoredFileUploadStatus.Stored
        };
        var wallet = new Wallet
        {
            Id = $"wallet-{suffix}",
            OwnerType = WalletOwnerType.Company,
            OwnerId = company.Id,
            OwnerUserId = companyUser.Id,
            AvailableBalance = availableBalance,
            Currency = "EGP"
        };

        await context.Users.AddRangeAsync(companyUser, doctorUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.StoredFiles.AddAsync(asset);
        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return new CampaignContractSeed(companyUser.Id, company.Id, doctor.Id, asset.Id);
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

    private sealed record CampaignContractSeed(string CompanyUserId, string CompanyId, string DoctorId, string AssetId);
}
