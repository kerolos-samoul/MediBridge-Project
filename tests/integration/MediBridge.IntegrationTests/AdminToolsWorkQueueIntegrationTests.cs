using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminToolsWorkQueueIntegrationTests
{
    [Fact]
    public async Task WorkQueue_ReturnsAllSeededCategoriesWithSafeFields()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await SeedWorkQueueSourcesAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        using var response = await client.GetAsync("/api/admin/work-queue?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storage-key-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("Data");
        var categories = data.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("category").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Account", categories);
        Assert.Contains("File", categories);
        Assert.Contains("Campaign", categories);
        Assert.Contains("Enforcement", categories);
        Assert.Contains("Withdrawal", categories);
        Assert.True(data.GetProperty("categoryCounts").TryGetProperty("Withdrawal", out _));
        var withdrawal = data.GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("category").GetString() == "Withdrawal");
        Assert.Contains("100.00 EGP", withdrawal.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkQueue_UsesStableOrderingAndPagination()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await SeedWorkQueueSourcesAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        using var firstResponse = await client.GetAsync("/api/admin/work-queue?PageNumber=1&PageSize=2");
        using var secondResponse = await client.GetAsync("/api/admin/work-queue?PageNumber=2&PageSize=2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var firstIds = await ReadItemIdsAsync(firstResponse);
        var secondIds = await ReadItemIdsAsync(secondResponse);

        Assert.Equal(2, firstIds.Count);
        Assert.Equal(2, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WorkQueue_RemovesAccountFileAndCampaignItemsAfterModerationDecisions()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var admin = await SeedWorkQueueSourcesAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin));

        string accountId;
        string fileId;
        string campaignId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            accountId = await db.Users
                .Where(user => user.AccountStatus == AccountStatus.Pending && user.Role == UserRole.Doctor)
                .Select(user => user.Id)
                .SingleAsync();
            fileId = await db.StoredFiles
                .Where(file => file.ReviewStatus == StoredFileReviewStatus.Pending)
                .Select(file => file.Id)
                .SingleAsync();
            campaignId = await db.Campaigns
                .Where(campaign => campaign.Status == CampaignStatus.PendingReview)
                .Select(campaign => campaign.Id)
                .SingleAsync();
        }

        using var accountResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{accountId}/decision", new { Decision = "Approve" });
        using var fileResponse = await client.PutAsJsonAsync($"/api/admin/files/{fileId}/review", new { Decision = "Approved" });
        using var campaignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { decision = "ChangesRequested", reason = "Please revise the campaign.", notes = "Internal note." })
        };
        campaignRequest.Headers.Add("Idempotency-Key", "work-queue-review-001");
        using var campaignResponse = await client.SendAsync(campaignRequest);

        Assert.Equal(HttpStatusCode.OK, accountResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, campaignResponse.StatusCode);

        using var queueResponse = await client.GetAsync("/api/admin/work-queue?PageNumber=1&PageSize=20");
        Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
        var itemIds = await ReadItemIdsAsync(queueResponse);
        Assert.DoesNotContain(accountId, itemIds);
        Assert.DoesNotContain(fileId, itemIds);
        Assert.DoesNotContain(campaignId, itemIds);
    }

    private static async Task<IReadOnlyList<string>> ReadItemIdsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("itemId").GetString()!)
            .ToArray();
    }

    internal static async Task<string> SeedWorkQueueSourcesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.SpecifyKind(new DateTime(2026, 7, 13, 8, 0, 0), DateTimeKind.Utc);
        var admin = new MediBridgeIdentityUser
        {
            Id = $"admin-{Guid.NewGuid():N}",
            UserName = $"admin-{Guid.NewGuid():N}",
            Email = $"admin-{Guid.NewGuid():N}@example.com",
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now
        };
        var pendingDoctor = new MediBridgeIdentityUser
        {
            Id = $"doctor-user-{Guid.NewGuid():N}",
            UserName = $"doctor-{Guid.NewGuid():N}",
            Email = $"doctor-{Guid.NewGuid():N}@example.com",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Pending,
            CreatedAtUtc = now.AddMinutes(1)
        };
        var approvedDoctor = new MediBridgeIdentityUser
        {
            Id = $"approved-doctor-user-{Guid.NewGuid():N}",
            UserName = $"approved-doctor-{Guid.NewGuid():N}",
            Email = $"approved-doctor-{Guid.NewGuid():N}@example.com",
            Role = UserRole.Doctor,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now
        };
        var approvedCompany = new MediBridgeIdentityUser
        {
            Id = $"company-user-{Guid.NewGuid():N}",
            UserName = $"company-{Guid.NewGuid():N}",
            Email = $"company-{Guid.NewGuid():N}@example.com",
            Role = UserRole.Company,
            AccountStatus = AccountStatus.Approved,
            CreatedAtUtc = now
        };
        var doctorProfile = new DoctorProfile
        {
            Id = $"doctor-{Guid.NewGuid():N}",
            UserId = approvedDoctor.Id,
            Specialization = "Cardiology",
            ExperienceYears = 8,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "safe-ref",
            DailyMessageLimit = 10,
            MinimumWeeklyRequirement = 1,
            PricePerMessage = 25m,
            PricingIsActive = true,
            CreatedAtUtc = now
        };
        var companyProfile = new CompanyProfile
        {
            Id = $"company-{Guid.NewGuid():N}",
            UserId = approvedCompany.Id,
            CompanyName = "Safe Pharma",
            LicenseNumber = $"LIC-{Guid.NewGuid():N}",
            ContactName = "Operations",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "company-safe-ref",
            CreatedAtUtc = now
        };
        db.Users.AddRange(admin, pendingDoctor, approvedDoctor, approvedCompany);
        db.DoctorProfiles.Add(doctorProfile);
        db.CompanyProfiles.Add(companyProfile);
        db.StoredFiles.Add(new StoredFile
        {
            Id = $"file-{Guid.NewGuid():N}",
            OwnerType = StoredFileOwnerType.Doctor,
            OwnerId = doctorProfile.Id,
            Purpose = StoredFilePurpose.VerificationDocument,
            OriginalFileName = "safe-name.pdf",
            ContentType = "application/pdf",
            StorageKey = "storage-key-secret",
            StorageProvider = "provider-secret",
            ReviewStatus = StoredFileReviewStatus.Pending,
            CreatedAtUtc = now.AddMinutes(2)
        });
        db.Campaigns.Add(new Campaign
        {
            Id = $"campaign-{Guid.NewGuid():N}",
            CompanyId = companyProfile.Id,
            Title = "Campaign",
            Description = "Campaign pending review",
            Status = CampaignStatus.PendingReview,
            CreatedAtUtc = now.AddMinutes(3),
            SubmittedAtUtc = now.AddMinutes(3)
        });
        var decision = new WeeklyEnforcementDecision
        {
            Id = $"decision-{Guid.NewGuid():N}",
            DoctorId = doctorProfile.Id,
            WeekStartDateEgypt = new DateOnly(2026, 7, 6),
            WeekEndDateEgypt = new DateOnly(2026, 7, 13),
            MinimumWeeklyRequirement = 3,
            InteractionCount = 1,
            Decision = WeeklyEnforcementDecisionType.Violation,
            RollingViolationCountAfterDecision = 1,
            CreatedAtUtc = now.AddMinutes(4)
        };
        db.WeeklyEnforcementDecisions.Add(decision);
        db.DoctorWeeklyViolations.Add(new DoctorWeeklyViolation
        {
            Id = $"violation-{Guid.NewGuid():N}",
            DoctorId = doctorProfile.Id,
            WeeklyEnforcementDecisionId = decision.Id,
            WeekStartDateEgypt = decision.WeekStartDateEgypt,
            WeekEndDateEgypt = decision.WeekEndDateEgypt,
            MinimumWeeklyRequirement = 3,
            InteractionCount = 1,
            RollingViolationCount = 1,
            CreatedAtUtc = now.AddMinutes(4)
        });
        db.WithdrawalRequests.Add(new WithdrawalRequest
        {
            Id = $"withdrawal-{Guid.NewGuid():N}",
            DoctorId = doctorProfile.Id,
            Amount = 100m,
            Status = WithdrawalRequestStatus.Requested,
            RequestedAtUtc = now.AddMinutes(5)
        });
        await db.SaveChangesAsync();
        return admin.Id;
    }
}
