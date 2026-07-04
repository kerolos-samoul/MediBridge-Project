using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediBridge.IntegrationTests;

internal sealed class Phase7InjectorTestHarness : IAsyncDisposable
{
    private readonly ConfiguredWebAppFactory factory;
    private readonly string adminUserId;

    private Phase7InjectorTestHarness(
        DateTime utcNow,
        string adminUserId,
        string doctorUserId,
        string doctorId,
        ConfiguredWebAppFactory factory)
    {
        UtcNow = utcNow;
        this.adminUserId = adminUserId;
        DoctorUserId = doctorUserId;
        DoctorId = doctorId;
        this.factory = factory;
    }

    public IServiceProvider Services => factory.Services;
    public DateTime UtcNow { get; }
    public DateOnly BusinessDateEgypt => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(UtcNow, ResolveCairo()));
    public string DoctorUserId { get; }
    public string DoctorId { get; }

    public static async Task<Phase7InjectorTestHarness> CreateAsync(
        DateTime? utcNow = null,
        int dailyLimit = 2,
        bool seedPolicy = true,
        DbCommandInterceptor? commandInterceptor = null)
    {
        var now = utcNow ?? new DateTime(2026, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        ConfiguredWebAppFactory factory = commandInterceptor is null
            ? new WebAppFactory()
            : new InterceptingWebAppFactory(commandInterceptor);
        var harness = new Phase7InjectorTestHarness(
            now,
            $"admin-{Guid.NewGuid():N}",
            $"doctor-user-{Guid.NewGuid():N}",
            $"doctor-{Guid.NewGuid():N}",
            factory);
        await harness.factory.InitializeDatabaseAsync();
        await harness.SeedAdminAsync();
        await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            harness.Services,
            harness.DoctorUserId,
            harness.DoctorId,
            $"{harness.DoctorId}@example.com",
            50m,
            dailyLimit,
            now.AddDays(-10));
        if (seedPolicy)
        {
            await harness.AddPolicyAsync("policy", 20m, now.AddDays(-30), null);
        }

        return harness;
    }

    public Task<Phase7DoctorSeed> AddApprovedDoctorAsync(string name, int dailyLimit = 1, decimal pricePerMessage = 50m)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"doctor-user-{name}-{suffix}";
        var doctorId = $"doctor-{name}-{suffix}";
        return Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            Services,
            userId,
            doctorId,
            $"{doctorId}@example.com",
            pricePerMessage,
            dailyLimit,
            UtcNow.AddDays(-10));
    }

    public Task AddPolicyAsync(string name, decimal percent, DateTime effectiveFromUtc, DateTime? effectiveToUtc) =>
        Phase7DeliveryTestHelpers.SeedEffectiveFeePolicyAsync(
            Services,
            $"{name}-{Guid.NewGuid():N}",
            adminUserId,
            percent,
            effectiveFromUtc,
            effectiveToUtc,
            effectiveFromUtc);

    public async Task<InjectorCandidateSeed> AddCandidateAsync(
        string name,
        DateTime submittedAtUtc,
        DateTime queuedAtUtc,
        decimal availableBalance = 100m,
        CampaignStatus campaignStatus = CampaignStatus.Approved,
        bool seedWallet = true,
        string currency = "EGP")
    {
        var suffix = Guid.NewGuid().ToString("N");
        var companyUserId = $"company-user-{name}-{suffix}";
        var companyId = $"company-{name}-{suffix}";
        var campaignId = $"campaign-{name}-{suffix}";
        var queueId = $"queue-{name}-{suffix}";
        var walletId = $"wallet-{name}-{suffix}";
        await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            Services,
            companyUserId,
            companyId,
            $"{companyId}@example.com",
            UtcNow.AddDays(-10));
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            Services,
            campaignId,
            companyId,
            campaignStatus,
            submittedAtUtc,
            UtcNow.AddDays(-5),
            false,
            null);
        if (seedWallet)
        {
            await Phase7DeliveryTestHelpers.SeedCompanyWalletAsync(
                Services,
                walletId,
                companyId,
                companyUserId,
                availableBalance,
                0m,
                UtcNow.AddDays(-5));
            if (!string.Equals(currency, "EGP", StringComparison.Ordinal))
            {
                await WithContextAsync(async context =>
                {
                    var wallet = await context.Wallets.SingleAsync(item => item.Id == walletId);
                    wallet.Currency = currency;
                    await context.SaveChangesAsync();
                });
            }
        }

        await Phase7DeliveryTestHelpers.SeedFifoQueueRowAsync(
            Services,
            queueId,
            campaignId,
            DoctorId,
            submittedAtUtc,
            queuedAtUtc,
            queuedAtUtc);
        return new InjectorCandidateSeed(name, queueId, campaignId, companyId, companyUserId, walletId);
    }

    public async Task SeedExistingDeliveryAsync(string name, DeliveryStatus status)
    {
        var candidate = await AddCandidateAsync(
            $"existing-{name}",
            UtcNow.AddDays(-3),
            UtcNow.AddDays(-3),
            availableBalance: 100m);
        await WithContextAsync(async context =>
        {
            var queue = await context.DoctorMessageQueues.SingleAsync(item => item.Id == candidate.QueueId);
            queue.MarkCancelled(UtcNow.AddDays(-2));
            await context.SaveChangesAsync();
        });
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            Services,
            $"delivery-existing-{name}-{Guid.NewGuid():N}",
            DoctorId,
            candidate.CampaignId,
            candidate.CompanyId,
            BusinessDateEgypt,
            UtcNow.AddHours(-1),
            status,
            status == DeliveryStatus.Active ? ReservationStatus.Reserved : ReservationStatus.Charged,
            50m,
            20m,
            10m,
            40m,
            50m,
            UtcNow.AddHours(-1));
    }

    public async Task SeedOverdueDeliveryAsync(InjectorCandidateSeed candidate)
    {
        await Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            Services,
            $"delivery-overdue-{Guid.NewGuid():N}",
            DoctorId,
            candidate.CampaignId,
            candidate.CompanyId,
            BusinessDateEgypt.AddDays(-1),
            UtcNow.AddDays(-1),
            DeliveryStatus.Active,
            ReservationStatus.Reserved,
            50m,
            20m,
            10m,
            40m,
            50m,
            UtcNow.AddDays(-1));
        await WithContextAsync(async context =>
        {
            var wallet = await context.Wallets.SingleAsync(item => item.Id == candidate.WalletId);
            wallet.ReservedBalance += 50m;
            await context.SaveChangesAsync();
        });
    }

    public async Task<DailyInjectorResultDto> RunAsync(int batchSize = 100)
    {
        using var scope = Services.CreateScope();
        var service = new DailyDeliveryInjectorService(
            scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>(),
            new EgyptBusinessClock(new FixedTimeProvider(UtcNow)),
            new DeliverySettlementSnapshotCalculator(),
            new DeliveryCandidateEligibilityPolicy(),
            batchSize);
        return await service.RunAsync();
    }

    public async Task WithContextAsync(Func<MediBridgeDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>());
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    private async Task SeedAdminAsync()
    {
        await WithContextAsync(async context =>
        {
            var email = $"{adminUserId}@example.com";
            await context.Users.AddAsync(new MediBridgeIdentityUser
            {
                Id = adminUserId,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                Role = UserRole.Admin,
                AccountStatus = AccountStatus.Approved,
                EmailVerified = true,
                EmailConfirmed = true,
                CreatedAtUtc = UtcNow.AddDays(-30)
            });
            await context.SaveChangesAsync();
        });
    }

    private static TimeZoneInfo ResolveCairo()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;
        public FixedTimeProvider(DateTime now) => this.now = new DateTimeOffset(now);
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InterceptingWebAppFactory : ConfiguredWebAppFactory
    {
        private readonly DbCommandInterceptor interceptor;

        public InterceptingWebAppFactory(DbCommandInterceptor interceptor)
        {
            this.interceptor = interceptor;
        }

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>(options =>
                    options
                        .UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection"))
                        .AddInterceptors(interceptor));
            });
        }
    }
}

internal sealed record InjectorCandidateSeed(
    string Name,
    string QueueId,
    string CampaignId,
    string CompanyId,
    string CompanyUserId,
    string WalletId);
