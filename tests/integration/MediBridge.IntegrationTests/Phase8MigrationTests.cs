using System.Data;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8MigrationTests
{
    private const string PreviousMigration = "20260702174103_AddPhase7DeliveryExpiryJobs";

    [Fact]
    public async Task Phase8Migration_AppliesFromEmptyDatabase_AndCreatesInteractionSchema()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();

        await context.Database.GetService<IMigrator>().MigrateAsync();

        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DeliveryInteractionOperations'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DeliveryInteractionOperations_DoctorId_DeliveryId_IdempotencyKey' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DeliveryInteractionOperations_DeliveryId_Decision'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_WalletTransactions_OperationType_IdempotencyKey' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_CampaignId' AND [is_unique] = 1"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.indexes WHERE [name] = 'IX_DoctorAdDeliveries_DoctorId_DeliveryDateEgypt_Id'"));
        Assert.Equal(1, await ExecuteScalarIntAsync(context, "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DeliveryInteractionOperations') AND [name] = 'ConcurrencyToken' AND [system_type_id] = 189"));
        Assert.Equal(1000, await ExecuteScalarIntAsync(context, "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DoctorAdDeliveries' AND COLUMN_NAME = 'FeedbackText'"));
        Assert.Equal(1000, await ExecuteScalarIntAsync(context, "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DeliveryInteractionOperations' AND COLUMN_NAME = 'FeedbackText'"));
    }

    [Fact]
    public async Task Phase8Migration_RollbackAndReapply_PreservesDeliveryAndWalletBusinessRows()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await SeedBusinessRowsAsync(context);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        await AssertBusinessRowsAsync(context);

        await migrator.MigrateAsync(PreviousMigration);
        context.ChangeTracker.Clear();
        await AssertBusinessRowsAsync(context);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        await AssertBusinessRowsAsync(context);
    }

    [Fact]
    public async Task Phase8Migration_RejectsExistingFeedbackLongerThan1000InsteadOfTruncating()
    {
        await using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.EnsureDeletedAsync();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        await SeedBusinessRowsAsync(context, feedbackText: new string('x', 1001));

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());

        Assert.Contains("cannot narrow DoctorAdDeliveries.FeedbackText", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedBusinessRowsAsync(MediBridgeDbContext context, string? feedbackText = "phase8 existing feedback")
    {
        var createdAtUtc = new DateTime(2026, 7, 10, 7, 0, 0, DateTimeKind.Utc);
        context.Users.AddRange(
            CreateUser("phase8-migration-doctor-user", "phase8-migration-doctor@example.com", UserRole.Doctor),
            CreateUser("phase8-migration-company-user", "phase8-migration-company@example.com", UserRole.Company));
        context.DoctorProfiles.Add(new DoctorProfile
        {
            Id = "phase8-migration-doctor",
            UserId = "phase8-migration-doctor-user",
            Specialization = "Cardiology",
            Location = "Cairo",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "doctor.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/doctor",
            DailyMessageLimit = 10,
            PricePerMessage = 50m,
            CreatedAtUtc = createdAtUtc
        });
        context.CompanyProfiles.Add(new CompanyProfile
        {
            Id = "phase8-migration-company",
            UserId = "phase8-migration-company-user",
            CompanyName = "Migration Company",
            LicenseNumber = $"phase8-migration-license-{Guid.NewGuid():N}",
            ContactName = "Migration Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "company.pdf",
            VerificationContentType = "application/pdf",
            VerificationReference = "migration/company",
            CreatedAtUtc = createdAtUtc
        });
        context.Campaigns.Add(new Campaign
        {
            Id = "phase8-migration-campaign",
            CompanyId = "phase8-migration-company",
            Title = "Phase 8 migration",
            Description = "Phase 8 migration fixture",
            Status = CampaignStatus.Approved,
            CreatedAtUtc = createdAtUtc
        });
        context.DoctorAdDeliveries.Add(new DoctorAdDelivery
        {
            Id = "phase8-migration-delivery",
            DoctorId = "phase8-migration-doctor",
            CampaignId = "phase8-migration-campaign",
            CompanyId = "phase8-migration-company",
            DeliveryDateEgypt = new DateOnly(2026, 7, 10),
            DeliveredAtUtc = createdAtUtc.AddMinutes(10),
            Status = DeliveryStatus.Accepted,
            ReservationStatus = ReservationStatus.Charged,
            InteractedAtUtc = createdAtUtc.AddMinutes(20),
            FeedbackText = feedbackText,
            FeedbackCreatedAtUtc = feedbackText is null ? null : createdAtUtc.AddMinutes(20),
            PricePerMessageSnapshot = 50m,
            PlatformFeePercentSnapshot = 20m,
            PlatformFeeAmount = 10m,
            DoctorEarnings = 40m,
            ReservedAmount = 50m,
            CreatedAtUtc = createdAtUtc
        });
        context.Wallets.AddRange(
            new Wallet
            {
                Id = "phase8-migration-company-wallet",
                OwnerType = WalletOwnerType.Company,
                OwnerId = "phase8-migration-company",
                OwnerUserId = "phase8-migration-company-user",
                AvailableBalance = 100m,
                ReservedBalance = 50m,
                Currency = "EGP",
                CreatedAtUtc = createdAtUtc
            },
            new Wallet
            {
                Id = "phase8-migration-doctor-wallet",
                OwnerType = WalletOwnerType.Doctor,
                OwnerId = "phase8-migration-doctor",
                OwnerUserId = "phase8-migration-doctor-user",
                AvailableBalance = 40m,
                ReservedBalance = 0m,
                Currency = "EGP",
                CreatedAtUtc = createdAtUtc
            });

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static async Task AssertBusinessRowsAsync(MediBridgeDbContext context)
    {
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(delivery => delivery.Id == "phase8-migration-delivery");
        Assert.Equal(DeliveryStatus.Accepted, delivery.Status);
        Assert.Equal(ReservationStatus.Charged, delivery.ReservationStatus);
        Assert.Equal(50m, delivery.ReservedAmount);
        Assert.Equal(40m, delivery.DoctorEarnings);
        Assert.Equal("phase8 existing feedback", delivery.FeedbackText);

        var companyWallet = await context.Wallets.AsNoTracking().SingleAsync(wallet => wallet.Id == "phase8-migration-company-wallet");
        var doctorWallet = await context.Wallets.AsNoTracking().SingleAsync(wallet => wallet.Id == "phase8-migration-doctor-wallet");
        Assert.Equal(100m, companyWallet.AvailableBalance);
        Assert.Equal(50m, companyWallet.ReservedBalance);
        Assert.Equal(40m, doctorWallet.AvailableBalance);
        Assert.Equal(0m, doctorWallet.ReservedBalance);
    }

    private static MediBridgeIdentityUser CreateUser(string id, string email, UserRole role) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        Role = role,
        AccountStatus = AccountStatus.Approved,
        EmailVerified = true,
        EmailConfirmed = true,
        CreatedAtUtc = new DateTime(2026, 7, 10, 6, 0, 0, DateTimeKind.Utc)
    };

    private static async Task<int> ExecuteScalarIntAsync(MediBridgeDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
