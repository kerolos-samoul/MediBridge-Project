using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CampaignReviewModerationContractTests
{
    [Fact]
    public async Task AdminAndCompanyReviewRoutes_ReturnStandardCanonicalEnvelopes()
    {
        using var factory = new CampaignReviewContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedWorkflowAsync(factory);
        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        var pending = await adminClient.GetAsync("/api/admin/campaigns/pending-review");
        var detail = await adminClient.GetAsync($"/api/admin/campaigns/{seed.CampaignId}/review-detail");
        using var reviewRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/campaigns/{seed.CampaignId}/review")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(new
            {
                decision = "ChangesRequested",
                reason = "Please revise the campaign.",
                notes = "Internal only."
            })
        };
        reviewRequest.Headers.Add("Idempotency-Key", "review-contract-001");
        var review = await adminClient.SendAsync(reviewRequest);

        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await review.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal("RevisionRequired", data.GetProperty("decision").GetString());
            Assert.Equal("RevisionRequired", data.GetProperty("status").GetString());
            Assert.True(data.GetProperty("canResubmit").GetBoolean());
        }

        using var companyClient = factory.CreateClient();
        Phase5ContractTestHelpers.AuthorizeAsCompany(companyClient, seed.CompanyUserId);
        var outcome = await companyClient.GetAsync($"/api/company/campaigns/{seed.CampaignId}/review-outcome");
        var update = await companyClient.PutAsync(
            $"/api/company/campaigns/{seed.CampaignId}",
            Phase5ContractTestHelpers.CreateJsonContent(new
            {
                title = "Revised contract campaign",
                description = "Revised contract description",
                clinicalResearchInfo = (string?)null
            }));
        using var submitRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/company/campaigns/{seed.CampaignId}/submit");
        submitRequest.Headers.Add("Idempotency-Key", "submit-contract-001");
        var submit = await companyClient.SendAsync(submitRequest);

        Assert.Equal(HttpStatusCode.OK, outcome.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await outcome.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal("RevisionRequired", data.GetProperty("status").GetString());
            Assert.Equal("Please revise the campaign.", data.GetProperty("publicReason").GetString());
            Assert.False(data.TryGetProperty("notes", out _));
        }

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal("PendingReview", data.GetProperty("status").GetString());
            Assert.True(data.TryGetProperty("estimatedCost", out _));
            Assert.True(data.TryGetProperty("submittedAtUtc", out _));
            Assert.False(data.TryGetProperty("reservedAmount", out _));
        }
    }

    [Fact]
    public async Task ReviewDetail_WhenStorageProviderIsUnavailable_Returns503Envelope()
    {
        using var factory = new UnavailableCampaignReviewContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await SeedWorkflowAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        var response = await client.GetAsync($"/api/admin/campaigns/{seed.CampaignId}/review-detail");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 503);
    }

    [Theory]
    [InlineData("GET", "/api/admin/campaigns/pending-review")]
    [InlineData("POST", "/api/admin/campaigns/campaign-1/review")]
    [InlineData("PUT", "/api/company/campaigns/campaign-1")]
    [InlineData("POST", "/api/company/campaigns/campaign-1/submit")]
    [InlineData("GET", "/api/company/campaigns/campaign-1/review-outcome")]
    public async Task ReviewRoutes_RequireAuthentication(string method, string route)
    {
        using var factory = new CampaignReviewContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method is "POST" or "PUT")
        {
            request.Content = Phase5ContractTestHelpers.CreateJsonContent(new { });
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WorkflowSeed> SeedWorkflowAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = CreateUser($"admin-{suffix}@example.test", UserRole.Admin);
        var companyUser = CreateUser($"company-{suffix}@example.test", UserRole.Company);
        var doctorUser = CreateUser($"doctor-{suffix}@example.test", UserRole.Doctor);
        var company = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = companyUser.Id,
            CompanyName = "007 Contract Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contract Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };
        var doctor = new DoctorProfile
        {
            Id = $"doctor-{suffix}",
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"doctor-verification/{suffix}",
            ActivityScore = 90m,
            Status = DoctorMarketplaceStatus.Active,
            PricePerMessage = 50m,
            DailyMessageLimit = 10
        };
        var submittedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        var campaign = new Campaign
        {
            Id = $"campaign-{suffix}",
            CompanyId = company.Id,
            Title = "Contract campaign",
            Description = "Contract description",
            Status = CampaignStatus.PendingReview,
            SubmittedAtUtc = submittedAtUtc,
            CreatedAtUtc = submittedAtUtc
        };
        var target = new CampaignTarget
        {
            Id = $"target-{suffix}",
            CampaignId = campaign.Id,
            DoctorId = doctor.Id,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = doctor.PricePerMessage.Value,
            CreatedAtUtc = submittedAtUtc
        };
        var media = new StoredFile
        {
            Id = $"media-{suffix}",
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = company.Id,
            RelatedCampaignId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "campaign.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{suffix}/campaign.png",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Pending,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            CreatedAtUtc = submittedAtUtc
        };
        var wallet = new Wallet
        {
            Id = $"wallet-{suffix}",
            OwnerType = WalletOwnerType.Company,
            OwnerId = company.Id,
            OwnerUserId = companyUser.Id,
            AvailableBalance = 1000m,
            Currency = "EGP",
            CreatedAtUtc = submittedAtUtc
        };

        await context.Users.AddRangeAsync(admin, companyUser, doctorUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddAsync(target);
        await context.StoredFiles.AddAsync(media);
        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return new WorkflowSeed(admin.Id, companyUser.Id, campaign.Id);
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
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
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record WorkflowSeed(string AdminUserId, string CompanyUserId, string CampaignId);

    private sealed class CampaignReviewContractWebAppFactory : ContractWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider, CampaignReviewFakeStorageProvider>();
            });
        }
    }

    private sealed class CampaignReviewFakeStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(
            FileStorageUploadRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(
            string storageKey,
            StoredFileStorageResourceType resourceType,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new FileStorageAccessGrant(
                $"https://storage.example.test/private/{Uri.EscapeDataString(storageKey)}",
                expiresAtUtc));

        public Task DeleteAsync(
            string storageKey,
            StoredFileStorageResourceType resourceType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class UnavailableCampaignReviewContractWebAppFactory : ContractWebAppFactory
    {
        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton<IFileStorageProvider, UnavailableCampaignReviewStorageProvider>();
            });
        }
    }

    private sealed class UnavailableCampaignReviewStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResponse> UploadAsync(
            FileStorageUploadRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FileStorageAccessGrant> CreatePrivateAccessGrantAsync(
            string storageKey,
            StoredFileStorageResourceType resourceType,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Storage provider operation failed.");

        public Task DeleteAsync(
            string storageKey,
            StoredFileStorageResourceType resourceType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
