using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MediBridge.IntegrationTests;

public static class Phase8InteractionTestHelpers
{
    public static readonly DateTime DefaultUtcNow = new(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly DefaultTodayEgypt = new(2026, 7, 10);

    public static Task<Phase7DoctorSeed> SeedApprovedActiveDoctorAsync(
        IServiceProvider services,
        string userId,
        string doctorId,
        string email,
        decimal pricePerMessage,
        int dailyMessageLimit,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            services,
            userId,
            doctorId,
            email,
            pricePerMessage,
            dailyMessageLimit,
            createdAtUtc,
            cancellationToken);
    }

    public static Task<Phase7CompanySeed> SeedApprovedCompanyAsync(
        IServiceProvider services,
        string userId,
        string companyId,
        string email,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        return Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            services,
            userId,
            companyId,
            email,
            createdAtUtc,
            cancellationToken);
    }

    public static Task SeedCurrentEgyptDateDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly currentBusinessDateEgypt,
        DateTime deliveredAtUtc,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime createdAtUtc,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        DateTime? readAtUtc = null,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        DateTime? feedbackCreatedAtUtc = null,
        DateTime? expiredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAdDeliveryAsync(
            services,
            deliveryId,
            doctorId,
            campaignId,
            companyId,
            currentBusinessDateEgypt,
            deliveredAtUtc,
            status,
            reservationStatus,
            pricePerMessageSnapshot,
            platformFeePercentSnapshot,
            platformFeeAmount,
            doctorEarnings,
            reservedAmount,
            createdAtUtc,
            readAtUtc,
            interactedAtUtc,
            feedbackText,
            feedbackCreatedAtUtc,
            expiredAtUtc,
            cancellationToken);
    }

    public static Task SeedPriorEgyptDateDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly currentBusinessDateEgypt,
        DateTime deliveredAtUtc,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime createdAtUtc,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        DateTime? readAtUtc = null,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        DateTime? feedbackCreatedAtUtc = null,
        DateTime? expiredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAdDeliveryAsync(
            services,
            deliveryId,
            doctorId,
            campaignId,
            companyId,
            currentBusinessDateEgypt.AddDays(-1),
            deliveredAtUtc,
            status,
            reservationStatus,
            pricePerMessageSnapshot,
            platformFeePercentSnapshot,
            platformFeeAmount,
            doctorEarnings,
            reservedAmount,
            createdAtUtc,
            readAtUtc,
            interactedAtUtc,
            feedbackText,
            feedbackCreatedAtUtc,
            expiredAtUtc,
            cancellationToken);
    }

    public static Task SeedFutureEgyptDateDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly currentBusinessDateEgypt,
        DateTime deliveredAtUtc,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime createdAtUtc,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        DateTime? readAtUtc = null,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        DateTime? feedbackCreatedAtUtc = null,
        DateTime? expiredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        return SeedDoctorAdDeliveryAsync(
            services,
            deliveryId,
            doctorId,
            campaignId,
            companyId,
            currentBusinessDateEgypt.AddDays(1),
            deliveredAtUtc,
            status,
            reservationStatus,
            pricePerMessageSnapshot,
            platformFeePercentSnapshot,
            platformFeeAmount,
            doctorEarnings,
            reservedAmount,
            createdAtUtc,
            readAtUtc,
            interactedAtUtc,
            feedbackText,
            feedbackCreatedAtUtc,
            expiredAtUtc,
            cancellationToken);
    }

    public static async Task SeedDoctorAdDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly deliveryDateEgypt,
        DateTime deliveredAtUtc,
        DeliveryStatus status,
        ReservationStatus reservationStatus,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        DateTime createdAtUtc,
        DateTime? readAtUtc = null,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        DateTime? feedbackCreatedAtUtc = null,
        DateTime? expiredAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await context.DoctorAdDeliveries.AddAsync(new DoctorAdDelivery
        {
            Id = deliveryId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            CompanyId = companyId,
            DeliveryDateEgypt = deliveryDateEgypt,
            DeliveredAtUtc = deliveredAtUtc,
            ReadAtUtc = readAtUtc,
            Status = status,
            InteractedAtUtc = interactedAtUtc,
            FeedbackText = feedbackText,
            FeedbackCreatedAtUtc = feedbackCreatedAtUtc,
            PricePerMessageSnapshot = pricePerMessageSnapshot,
            PlatformFeePercentSnapshot = platformFeePercentSnapshot,
            PlatformFeeAmount = platformFeeAmount,
            DoctorEarnings = doctorEarnings,
            ReservedAmount = reservedAmount,
            ReservationStatus = reservationStatus,
            ExpiredAtUtc = expiredAtUtc,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = interactedAtUtc ?? readAtUtc ?? expiredAtUtc
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public static Task SeedCompanyWalletAsync(
        IServiceProvider services,
        string walletId,
        string companyId,
        string companyUserId,
        decimal availableBalance,
        decimal reservedBalance,
        DateTime createdAtUtc,
        bool isDeleted = false,
        DateTime? deletedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        return SeedWalletAsync(
            services,
            walletId,
            WalletOwnerType.Company,
            companyId,
            companyUserId,
            availableBalance,
            reservedBalance,
            createdAtUtc,
            isDeleted,
            deletedAtUtc,
            cancellationToken);
    }

    public static Task SeedDoctorWalletAsync(
        IServiceProvider services,
        string walletId,
        string doctorId,
        string doctorUserId,
        decimal availableBalance,
        decimal reservedBalance,
        DateTime createdAtUtc,
        bool isDeleted = false,
        DateTime? deletedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        return SeedWalletAsync(
            services,
            walletId,
            WalletOwnerType.Doctor,
            doctorId,
            doctorUserId,
            availableBalance,
            reservedBalance,
            createdAtUtc,
            isDeleted,
            deletedAtUtc,
            cancellationToken);
    }

    public static async Task SeedWalletTransactionAsync(
        IServiceProvider services,
        string transactionId,
        string walletId,
        WalletTransactionType operationType,
        string idempotencyKey,
        decimal amount,
        DateTime createdAtUtc,
        string? relatedDeliveryId = null,
        string? description = null,
        string? metadata = null,
        string? correctsTransactionId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await context.WalletTransactions.AddAsync(new WalletTransaction
        {
            Id = transactionId,
            WalletId = walletId,
            OperationType = operationType,
            IdempotencyKey = idempotencyKey,
            Amount = amount,
            RelatedDeliveryId = relatedDeliveryId,
            Description = description,
            Metadata = metadata,
            CreatedAtUtc = createdAtUtc,
            CorrectsTransactionId = correctsTransactionId
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedWalletLedgerEntryAsync(
        IServiceProvider services,
        string ledgerEntryId,
        string transactionId,
        string walletId,
        WalletLedgerEntryDirection direction,
        decimal amount,
        WalletBalanceType balanceType,
        DateTime createdAtUtc,
        string? campaignId = null,
        string? deliveryId = null,
        string? doctorId = null,
        string? companyId = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await context.WalletLedgerEntries.AddAsync(new WalletLedgerEntry
        {
            Id = ledgerEntryId,
            WalletTransactionId = transactionId,
            WalletId = walletId,
            Direction = direction,
            Amount = amount,
            BalanceType = balanceType,
            Currency = "EGP",
            CampaignId = campaignId,
            MessageDeliveryId = deliveryId,
            DoctorId = doctorId,
            CompanyId = companyId,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = createdAtUtc
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    public static async Task<Phase8SettlementScenarioSeed> SeedSettlementScenarioAsync(
        ConfiguredWebAppFactory factory,
        string label,
        DeliveryStatus status = DeliveryStatus.Active,
        ReservationStatus reservationStatus = ReservationStatus.Reserved,
        DateTime? readAtUtc = null,
        DateTime? interactedAtUtc = null,
        string? feedbackText = null,
        DateTime? feedbackCreatedAtUtc = null,
        decimal companyAvailableBalance = 200m,
        decimal companyReservedBalance = 50m,
        decimal doctorAvailableBalance = 25m,
        decimal doctorReservedBalance = 0m)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var doctor = await SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"doctor-user-{suffix}",
            $"doctor-{suffix}",
            $"doctor-{suffix}@example.test",
            50m,
            10,
            DefaultUtcNow.AddDays(-10));
        var company = await SeedApprovedCompanyAsync(
            factory.Services,
            $"company-user-{suffix}",
            $"company-{suffix}",
            $"company-{suffix}@example.test",
            DefaultUtcNow.AddDays(-10));
        var campaignId = $"campaign-{suffix}";
        var deliveryId = $"delivery-{suffix}";
        var companyWalletId = $"company-wallet-{suffix}";
        var doctorWalletId = $"doctor-wallet-{suffix}";

        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services,
            campaignId,
            company.CompanyId,
            CampaignStatus.Approved,
            DefaultUtcNow.AddDays(-2),
            DefaultUtcNow.AddDays(-3),
            false,
            null);
        await SeedCompanyWalletAsync(
            factory.Services,
            companyWalletId,
            company.CompanyId,
            company.UserId,
            companyAvailableBalance,
            companyReservedBalance,
            DefaultUtcNow.AddDays(-2));
        await SeedDoctorWalletAsync(
            factory.Services,
            doctorWalletId,
            doctor.DoctorId,
            doctor.UserId,
            doctorAvailableBalance,
            doctorReservedBalance,
            DefaultUtcNow.AddDays(-2));
        await SeedCurrentEgyptDateDeliveryAsync(
            factory.Services,
            deliveryId,
            doctor.DoctorId,
            campaignId,
            company.CompanyId,
            DefaultTodayEgypt,
            DefaultUtcNow.AddMinutes(-30),
            pricePerMessageSnapshot: 50m,
            platformFeePercentSnapshot: 20m,
            platformFeeAmount: 10m,
            doctorEarnings: 40m,
            reservedAmount: 50m,
            DefaultUtcNow.AddMinutes(-30),
            status,
            reservationStatus,
            readAtUtc,
            interactedAtUtc,
            feedbackText,
            feedbackCreatedAtUtc);

        return new Phase8SettlementScenarioSeed(
            suffix,
            doctor.UserId,
            doctor.DoctorId,
            company.UserId,
            company.CompanyId,
            campaignId,
            deliveryId,
            companyWalletId,
            doctorWalletId);
    }

    public static HttpClient CreateDoctorClient(ConfiguredWebAppFactory factory, string doctorUserId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateToken("Doctor", doctorUserId));
        return client;
    }

    public static HttpRequestMessage CreateInteractRequest(
        string deliveryId,
        string idempotencyKey,
        string decision,
        string? feedbackText = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/doctor/messages/{deliveryId}/interact")
        {
            Content = JsonContent.Create(new
            {
                Decision = decision,
                FeedbackText = feedbackText
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    public static async Task<JsonElement> ReadSuccessDataAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal("Message interaction settled.", document.RootElement.GetProperty("Message").GetString());
        return document.RootElement.GetProperty("Data").Clone();
    }

    public static async Task AssertSafeEmptyEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expectedStatusCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
        var body = document.RootElement.ToString();
        Assert.DoesNotContain("Idempotency-Key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company-wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor-wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReservedBalance", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AvailableBalance", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<Phase8SettlementSnapshot> SnapshotAsync(
        ConfiguredWebAppFactory factory,
        string deliveryId,
        string companyWalletId,
        string doctorWalletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.Id == deliveryId);
        var companyWallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == companyWalletId);
        var doctorWallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == doctorWalletId);
        var chargeTransaction = await context.WalletTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationType == WalletTransactionType.Charge && item.RelatedDeliveryId == deliveryId);
        var earnTransaction = await context.WalletTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OperationType == WalletTransactionType.Earn && item.RelatedDeliveryId == deliveryId);

        return new Phase8SettlementSnapshot(
            delivery.Status,
            delivery.ReservationStatus,
            delivery.ReadAtUtc,
            delivery.InteractedAtUtc,
            delivery.FeedbackText,
            delivery.FeedbackCreatedAtUtc,
            companyWallet.AvailableBalance,
            companyWallet.ReservedBalance,
            doctorWallet.AvailableBalance,
            doctorWallet.ReservedBalance,
            await context.WalletTransactions.CountAsync(item => item.RelatedDeliveryId == deliveryId),
            await context.WalletLedgerEntries.CountAsync(item => item.MessageDeliveryId == deliveryId),
            await context.DeliveryInteractionOperations.CountAsync(item => item.DeliveryId == deliveryId),
            await context.AuditEvents.CountAsync(item => item.TargetId == deliveryId && item.EventType == "Phase8DeliveryInteractionSettled"),
            await context.AuditEvents.CountAsync(item => item.TargetId == deliveryId && item.EventType == "Phase8DeliveryInteractionReplayed"),
            await context.AuditEvents.CountAsync(item => item.TargetId == deliveryId && item.EventType == "Phase8DeliveryInteractionConflict"),
            chargeTransaction?.Id,
            chargeTransaction?.IdempotencyKey,
            chargeTransaction?.Amount,
            earnTransaction?.Id,
            earnTransaction?.IdempotencyKey,
            earnTransaction?.Amount,
            await context.WalletLedgerEntries
                .AsNoTracking()
                .AnyAsync(item => item.MessageDeliveryId == deliveryId
                    && item.WalletId == companyWalletId
                    && item.BalanceType == WalletBalanceType.Reserved
                    && item.Direction == WalletLedgerEntryDirection.Debit
                    && item.Amount == 50m),
            await context.WalletLedgerEntries
                .AsNoTracking()
                .AnyAsync(item => item.MessageDeliveryId == deliveryId
                    && item.WalletId == doctorWalletId
                    && item.BalanceType == WalletBalanceType.Available
                    && item.Direction == WalletLedgerEntryDirection.Credit
                    && item.Amount == 40m));
    }

    public sealed class FixedClockFactory(DateTime utcNow) : ConfiguredWebAppFactory
    {
        private readonly AdjustableTimeProvider clock = new(utcNow);

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }
    }

    private static async Task SeedWalletAsync(
        IServiceProvider services,
        string walletId,
        WalletOwnerType ownerType,
        string ownerId,
        string ownerUserId,
        decimal availableBalance,
        decimal reservedBalance,
        DateTime createdAtUtc,
        bool isDeleted,
        DateTime? deletedAtUtc,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        await context.Wallets.AddAsync(new Wallet
        {
            Id = walletId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OwnerUserId = ownerUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = createdAtUtc,
            IsDeleted = isDeleted,
            DeletedAtUtc = deletedAtUtc
        }, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class AdjustableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTime utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}

public sealed record Phase8SettlementScenarioSeed(
    string Suffix,
    string DoctorUserId,
    string DoctorId,
    string CompanyUserId,
    string CompanyId,
    string CampaignId,
    string DeliveryId,
    string CompanyWalletId,
    string DoctorWalletId);

public sealed record Phase8SettlementSnapshot(
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    DateTime? ReadAtUtc,
    DateTime? InteractedAtUtc,
    string? FeedbackText,
    DateTime? FeedbackCreatedAtUtc,
    decimal CompanyAvailableBalance,
    decimal CompanyReservedBalance,
    decimal DoctorAvailableBalance,
    decimal DoctorReservedBalance,
    int WalletTransactionCount,
    int WalletLedgerEntryCount,
    int InteractionOperationCount,
    int SettlementAuditCount,
    int ReplayAuditCount,
    int ConflictAuditCount,
    string? ChargeTransactionId,
    string? ChargeIdempotencyKey,
    decimal? ChargeAmount,
    string? EarnTransactionId,
    string? EarnIdempotencyKey,
    decimal? EarnAmount,
    bool HasCompanyReservedDebitLedger,
    bool HasDoctorAvailableCreditLedger);
