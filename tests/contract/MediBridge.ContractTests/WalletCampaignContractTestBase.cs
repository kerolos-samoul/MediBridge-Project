using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public abstract class WalletCampaignContractTestBase
{
    protected static HttpClient CreateAdminClient(ContractWebAppFactory factory, string? userId = null)
        => CreateAuthenticatedClient(factory, "Admin", userId);

    protected static HttpClient CreateCompanyClient(ContractWebAppFactory factory, string? userId = null)
        => CreateAuthenticatedClient(factory, "Company", userId);

    protected static HttpClient CreateDoctorClient(ContractWebAppFactory factory, string? userId = null)
        => CreateAuthenticatedClient(factory, "Doctor", userId);

    protected static async Task<WalletCampaignContractFixtureIds> CreateApprovedActorsAsync(ContractWebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        var adminUser = CreateUser($"contract-admin-{suffix}@medibridge.local", UserRole.Admin);
        var companyUser = CreateUser($"contract-company-{suffix}@medibridge.local", UserRole.Company);
        var doctorUser = CreateUser($"contract-doctor-{suffix}@medibridge.local", UserRole.Doctor);

        var company = new CompanyProfile
        {
            UserId = companyUser.Id,
            CompanyName = "Contract Pharma",
            LicenseNumber = $"contract-license-{suffix}",
            ContactName = "Contract Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = $"contract/{suffix}/company"
        };

        var doctor = new DoctorProfile
        {
            UserId = doctorUser.Id,
            Specialization = "Cardiology",
            ExperienceYears = 6,
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"contract/{suffix}/doctor"
        };

        await context.Users.AddRangeAsync(adminUser, companyUser, doctorUser);
        await context.CompanyProfiles.AddAsync(company);
        await context.DoctorProfiles.AddAsync(doctor);
        await context.SaveChangesAsync();

        return new WalletCampaignContractFixtureIds(adminUser.Id, companyUser.Id, company.Id, doctorUser.Id, doctor.Id);
    }

    protected static async Task<decimal> GetCompanyWalletBalanceAsync(ContractWebAppFactory factory, string companyId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        return await context.Wallets
            .Where(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == companyId && !wallet.IsDeleted)
            .Select(wallet => wallet.AvailableBalance)
            .SingleAsync();
    }

    protected static Task<WalletCampaignContractCampaignFixtureIds> CreateDraftCampaignFixtureAsync(
        ContractWebAppFactory factory,
        string companyId)
        => CreateCampaignFixtureAsync(factory, companyId, CampaignStatus.Draft, submittedAtUtc: null);

    protected static Task<WalletCampaignContractCampaignFixtureIds> CreateRevisionRequiredCampaignFixtureAsync(
        ContractWebAppFactory factory,
        string companyId,
        DateTime submittedAtUtc)
        => CreateCampaignFixtureAsync(factory, companyId, GetRevisionRequiredStatus(), submittedAtUtc);

    protected static Task<WalletCampaignContractCampaignFixtureIds> CreatePendingReviewCampaignFixtureAsync(
        ContractWebAppFactory factory,
        string companyId,
        DateTime submittedAtUtc)
        => CreateCampaignFixtureAsync(factory, companyId, CampaignStatus.PendingReview, submittedAtUtc);

    protected static Task<string> AddPendingCampaignMediaFixtureAsync(
        ContractWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaFixtureAsync(factory, campaignId, StoredFileReviewStatus.Pending);

    protected static Task<string> AddApprovedCampaignMediaFixtureAsync(
        ContractWebAppFactory factory,
        string campaignId)
        => AddCampaignMediaFixtureAsync(factory, campaignId, StoredFileReviewStatus.Approved);

    protected static async Task<string> AddTargetSnapshotFixtureAsync(
        ContractWebAppFactory factory,
        string campaignId,
        string doctorId,
        DateTime? createdAtUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var target = new CampaignTarget
        {
            CampaignId = campaignId,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 6,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 90m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };

        await context.CampaignTargets.AddAsync(target);
        await context.SaveChangesAsync();
        return target.Id;
    }

    protected static async Task<WalletCampaignContractCampaignIds> CreateCampaignForReviewAsync(
        ContractWebAppFactory factory,
        string companyId,
        string doctorId,
        CampaignStatus status = CampaignStatus.PendingReview,
        StoredFileReviewStatus assetStatus = StoredFileReviewStatus.Pending)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var createdAtUtc = DateTime.UtcNow;
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Contract campaign",
            Description = "Campaign created for moderation contract coverage.",
            Status = status,
            SubmittedAtUtc = status == CampaignStatus.PendingReview ? createdAtUtc : null,
            CreatedAtUtc = createdAtUtc
        };
        var asset = new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "campaign.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"contract/{campaign.Id}/campaign.png",
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = assetStatus,
            CreatedAtUtc = createdAtUtc
        };
        var target = new CampaignTarget
        {
            CampaignId = campaign.Id,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 6,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 90m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = campaign.CreatedAtUtc
        };
        var wallet = new Wallet
        {
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            AvailableBalance = 50m,
            ReservedBalance = 0m
        };

        await context.Campaigns.AddAsync(campaign);
        await context.StoredFiles.AddAsync(asset);
        await context.CampaignTargets.AddAsync(target);
        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();

        return new WalletCampaignContractCampaignIds(campaign.Id, asset.Id);
    }

    protected static async Task<JsonElement> AssertEnvelopeAsync(HttpResponseMessage response, int expectedCode, string? expectedMessage = null)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();

        Assert.Equal(expectedCode, root.GetProperty("Code").GetInt32());
        if (expectedMessage is not null)
        {
            Assert.Equal(expectedMessage, root.GetProperty("Message").GetString());
        }

        Assert.True(root.TryGetProperty("Data", out _));
        return root;
    }

    private static HttpClient CreateAuthenticatedClient(ContractWebAppFactory factory, string role, string? userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(role, userId));
        return client;
    }

    private static string CreateToken(string role, string? userId)
    {
        var issuedAt = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId ?? $"user-{Guid.NewGuid():N}",
            ["email"] = $"{role.ToLowerInvariant()}@example.com",
            ["name"] = $"{role.ToLowerInvariant()}@example.com",
            ["role"] = role,
            ["iss"] = "MediBridge.ContractTests",
            ["aud"] = "MediBridge.ContractTests.ApiClients",
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = issuedAt.AddMinutes(15).ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        var header = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        })));
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        var signature = Base64UrlEncode(Sign($"{header}.{body}"));
        return $"{header}.{body}.{signature}";
    }

    private static byte[] Sign(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("ContractTestSigningKey-ReplaceBeforeProduction-32Chars"));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role)
    {
        var normalized = email.ToUpperInvariant();
        var now = DateTime.UtcNow;
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
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            LastStatusChangedAtUtc = now
        };
    }

    private static async Task<WalletCampaignContractCampaignFixtureIds> CreateCampaignFixtureAsync(
        ContractWebAppFactory factory,
        string companyId,
        CampaignStatus status,
        DateTime? submittedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Contract moderation fixture",
            Description = "Campaign content created explicitly for moderation contract coverage.",
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        SetSubmittedAtUtc(campaign, submittedAtUtc);
        await context.Campaigns.AddAsync(campaign);
        await context.SaveChangesAsync();
        return new WalletCampaignContractCampaignFixtureIds(campaign.Id, CreatesWalletRow: false);
    }

    private static async Task<string> AddCampaignMediaFixtureAsync(
        ContractWebAppFactory factory,
        string campaignId,
        StoredFileReviewStatus reviewStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var asset = new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaignId,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = $"campaign-{Guid.NewGuid():N}.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"contract/{campaignId}/{Guid.NewGuid():N}.png",
            StorageState = StorageObjectState.Active,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = reviewStatus,
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.StoredFiles.AddAsync(asset);
        await context.SaveChangesAsync();
        return asset.Id;
    }

    private static CampaignStatus GetRevisionRequiredStatus()
        => Enum.TryParse<CampaignStatus>("RevisionRequired", out var status)
            ? status
            : throw new InvalidOperationException(
                "CampaignStatus.RevisionRequired must be added by Phase 2 before this fixture is used.");

    private static void SetSubmittedAtUtc(Campaign campaign, DateTime? submittedAtUtc)
    {
        if (submittedAtUtc is null)
        {
            return;
        }

        var property = typeof(Campaign).GetProperty("SubmittedAtUtc");
        if (property is null)
        {
            throw new InvalidOperationException(
                "Campaign.SubmittedAtUtc must be added by Phase 2 before this fixture is used.");
        }

        property.SetValue(campaign, submittedAtUtc);
    }
}

public sealed record WalletCampaignContractFixtureIds(
    string AdminUserId,
    string CompanyUserId,
    string CompanyProfileId,
    string DoctorUserId,
    string DoctorProfileId);

public sealed record WalletCampaignContractCampaignIds(string CampaignId, string AssetId);

public sealed record WalletCampaignContractCampaignFixtureIds(string CampaignId, bool CreatesWalletRow);
