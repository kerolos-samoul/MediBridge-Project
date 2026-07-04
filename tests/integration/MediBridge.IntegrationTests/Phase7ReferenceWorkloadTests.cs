using System.Data.Common;
using System.Diagnostics;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace MediBridge.IntegrationTests;

public sealed class Phase7ReferenceWorkloadTests
{
    private const int DoctorCount = 1_000;
    private const int CandidatesPerDoctor = 10;
    private const int FundedEligiblePerDoctor = 8;
    private const int TemporarilyPausedPerDoctor = 1;
    private const int UnderfundedPerDoctor = 1;
    private const int BatchSize = 100;
    private const decimal Price = 50m;
    private static readonly DateTime UtcNow = new(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc);
    private readonly ITestOutputHelper output;

    public Phase7ReferenceWorkloadTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Reference_distribution_is_fixed_and_documented()
    {
        Assert.Equal(10_000, DoctorCount * CandidatesPerDoctor);
        Assert.Equal(CandidatesPerDoctor,
            FundedEligiblePerDoctor + TemporarilyPausedPerDoctor + UnderfundedPerDoctor);
        Assert.Equal(8_000, DoctorCount * FundedEligiblePerDoctor);
        Assert.Equal(1_000, DoctorCount * TemporarilyPausedPerDoctor);
        Assert.Equal(1_000, DoctorCount * UnderfundedPerDoctor);
        Assert.Equal(100, BatchSize);
    }

    [Phase7PerformanceFact]
    public async Task SqlServer2022_reference_daily_cycle_meets_throughput_ordering_and_safety_targets()
    {
        await using var sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await sql.StartAsync();
        await AssertSqlServer2022Async(sql.GetConnectionString());

        _ = await ExecuteCleanRunAsync(sql.GetConnectionString(), "warmup", measured: false);

        var measured = new List<ReferenceRunMeasurement>(3);
        for (var run = 1; run <= 3; run++)
        {
            measured.Add(await ExecuteCleanRunAsync(sql.GetConnectionString(), $"measured-{run}", measured: true));
        }

        var elapsed = measured.Select(item => item.Elapsed).Order().ToArray();
        var queryCounts = measured.Select(item => item.CommandCount).ToArray();
        var allocations = measured.Select(item => item.AllocatedBytes).ToArray();
        output.WriteLine(
            "Phase7 daily cycle: p50={0} p95={1} p99={2}; commands=[{3}]; allocatedBytes=[{4}]; failures=0",
            Percentile(elapsed, 0.50),
            Percentile(elapsed, 0.95),
            Percentile(elapsed, 0.99),
            string.Join(",", queryCounts),
            string.Join(",", allocations));

        Assert.All(measured, item => Assert.True(
            item.Elapsed <= TimeSpan.FromMinutes(5),
            $"{item.Name} took {item.Elapsed}, exceeding five minutes."));
        Assert.All(queryCounts, count => Assert.InRange(count, 1, 220_000));
        Assert.Single(queryCounts.Distinct());
        Assert.True(
            allocations.Max() <= allocations.Min() * 1.25,
            $"Allocated bytes were not stable: {string.Join(",", allocations)}.");
    }

    private async Task<ReferenceRunMeasurement> ExecuteCleanRunAsync(
        string containerConnectionString,
        string name,
        bool measured)
    {
        var builder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = $"Phase7Reference_{Guid.NewGuid():N}"
        };
        var observer = new DailyCycleCommandObserver();
        await using var factory = new ReferenceWorkloadFactory(builder.ConnectionString, observer);
        await factory.InitializeDatabaseAsync();

        try
        {
            await SeedReferenceDistributionAsync(factory.Services);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            observer.ResetAndEnable();
            var stopwatch = Stopwatch.StartNew();
            using (var scope = factory.Services.CreateScope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<IDailyDeliveryInjectorService>().RunAsync();
                Assert.Equal(DeliveryJobRunStatus.Succeeded, result.Outcome);
                Assert.Equal(DoctorCount * CandidatesPerDoctor, result.ExaminedCount);
                Assert.Equal(DoctorCount * FundedEligiblePerDoctor, result.ActivatedCount);
                Assert.Equal(DoctorCount * (TemporarilyPausedPerDoctor + UnderfundedPerDoctor), result.SkippedCount);
                Assert.Equal(0, result.CancelledCount);
                Assert.Equal(0, result.FailedCount);
            }

            stopwatch.Stop();
            observer.Disable();
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            await AssertReferenceOutcomesAsync(factory.Services);
            output.WriteLine(
                "Phase7 daily {0}: elapsed={1}; commands={2}; allocatedBytes={3}; measured={4}",
                name,
                stopwatch.Elapsed,
                observer.CommandCount,
                allocatedBytes,
                measured);
            return new ReferenceRunMeasurement(name, stopwatch.Elapsed, observer.CommandCount, allocatedBytes);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await context.Database.EnsureDeletedAsync();
        }
    }

    private static async Task SeedReferenceDistributionAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var admin = CreateUser("reference-admin", "reference-admin@example.test", UserRole.Admin);
        var fundedCompanyUser = CreateUser("reference-funded-user", "reference-funded@example.test", UserRole.Company);
        var underfundedCompanyUser = CreateUser("reference-underfunded-user", "reference-underfunded@example.test", UserRole.Company);
        context.Users.AddRange(admin, fundedCompanyUser, underfundedCompanyUser);
        context.CompanyProfiles.AddRange(
            CreateCompany("reference-funded-company", fundedCompanyUser.Id),
            CreateCompany("reference-underfunded-company", underfundedCompanyUser.Id));
        context.Wallets.AddRange(
            CreateWallet("reference-funded-wallet", "reference-funded-company", fundedCompanyUser.Id, DoctorCount * FundedEligiblePerDoctor * Price),
            CreateWallet("reference-underfunded-wallet", "reference-underfunded-company", underfundedCompanyUser.Id, 0m));
        context.PlatformFeePolicyHistories.Add(new PlatformFeePolicyHistory
        {
            Id = "reference-fee-policy",
            FeePercent = 20m,
            EffectiveFromUtc = UtcNow.AddDays(-30),
            ChangedByAdminUserId = admin.Id,
            CreatedAtUtc = UtcNow.AddDays(-30)
        });

        for (var doctorIndex = 0; doctorIndex < DoctorCount; doctorIndex++)
        {
            var userId = $"reference-doctor-user-{doctorIndex:D4}";
            var doctorId = $"reference-doctor-{doctorIndex:D4}";
            context.Users.Add(CreateUser(userId, $"reference-doctor-{doctorIndex:D4}@example.test", UserRole.Doctor));
            context.DoctorProfiles.Add(new DoctorProfile
            {
                Id = doctorId,
                UserId = userId,
                Specialization = "Cardiology",
                ExperienceYears = 10,
                Location = "Cairo",
                VerificationDocumentType = "License",
                VerificationOriginalFileName = $"{doctorId}.pdf",
                VerificationContentType = "application/pdf",
                VerificationSizeBytes = 1024,
                VerificationReference = $"reference/{doctorId}",
                DailyMessageLimit = CandidatesPerDoctor,
                ActivityScore = 95m,
                Status = DoctorMarketplaceStatus.Active,
                PricePerMessage = Price,
                CreatedAtUtc = UtcNow.AddDays(-30)
            });
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        const int seedBatchSize = 1_000;
        var seeded = 0;
        while (seeded < DoctorCount * CandidatesPerDoctor)
        {
            var batchEnd = Math.Min(seeded + seedBatchSize, DoctorCount * CandidatesPerDoctor);
            for (var flatIndex = seeded; flatIndex < batchEnd; flatIndex++)
            {
                var doctorIndex = flatIndex / CandidatesPerDoctor;
                var candidateIndex = flatIndex % CandidatesPerDoctor;
                var campaignId = $"reference-campaign-{doctorIndex:D4}-{candidateIndex:D2}";
                var isPaused = candidateIndex == FundedEligiblePerDoctor;
                var isUnderfunded = candidateIndex == CandidatesPerDoctor - 1;
                var submittedAtUtc = UtcNow.AddDays(-10).AddMinutes(flatIndex);
                context.Campaigns.Add(new Campaign
                {
                    Id = campaignId,
                    CompanyId = isUnderfunded ? "reference-underfunded-company" : "reference-funded-company",
                    Title = $"Reference campaign {doctorIndex:D4}-{candidateIndex:D2}",
                    Description = "Phase 7 fixed reference workload",
                    Status = isPaused ? CampaignStatus.Paused : CampaignStatus.Approved,
                    SubmittedAtUtc = submittedAtUtc,
                    CreatedAtUtc = submittedAtUtc.AddDays(-1)
                });
                context.DoctorMessageQueues.Add(new DoctorMessageQueue
                {
                    Id = $"reference-queue-{doctorIndex:D4}-{candidateIndex:D2}",
                    DoctorId = $"reference-doctor-{doctorIndex:D4}",
                    CampaignId = campaignId,
                    CampaignSubmittedAtUtc = submittedAtUtc,
                    QueuedAtUtc = UtcNow.AddMinutes(-flatIndex),
                    Status = QueueItemStatus.Queued,
                    CreatedAtUtc = UtcNow.AddDays(-1)
                });
            }

            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            seeded = batchEnd;
        }
    }

    private static async Task AssertReferenceOutcomesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(8_000, await context.DoctorAdDeliveries.CountAsync());
        Assert.Equal(8_000, await context.WalletTransactions.CountAsync(transaction => transaction.OperationType == WalletTransactionType.Reserve));
        Assert.Equal(16_000, await context.WalletLedgerEntries.CountAsync());
        Assert.Equal(8_000, await context.DoctorMessageQueues.CountAsync(item => item.Status == QueueItemStatus.Activated));
        Assert.Equal(2_000, await context.DoctorMessageQueues.CountAsync(item => item.Status == QueueItemStatus.Queued));
        Assert.Equal(0, await context.DoctorMessageQueues.CountAsync(item => item.Status == QueueItemStatus.Cancelled));
        Assert.Equal(8_000, await context.DoctorAdDeliveries.Select(item => new { item.DoctorId, item.CampaignId, item.DeliveryDateEgypt }).Distinct().CountAsync());

        var activatedQueueIds = await context.DoctorMessageQueues
            .Where(item => item.Status == QueueItemStatus.Activated)
            .Select(item => item.Id)
            .ToListAsync();
        Assert.All(activatedQueueIds, id => Assert.InRange(int.Parse(id[^2..]), 0, FundedEligiblePerDoctor - 1));

        var fundedWallet = await context.Wallets.SingleAsync(wallet => wallet.Id == "reference-funded-wallet");
        Assert.Equal(0m, fundedWallet.AvailableBalance);
        Assert.Equal(DoctorCount * FundedEligiblePerDoctor * Price, fundedWallet.ReservedBalance);
        var underfundedWallet = await context.Wallets.SingleAsync(wallet => wallet.Id == "reference-underfunded-wallet");
        Assert.Equal(0m, underfundedWallet.AvailableBalance);
        Assert.Equal(0m, underfundedWallet.ReservedBalance);
    }

    private static MediBridgeIdentityUser CreateUser(string id, string email, UserRole role)
        => new()
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
            CreatedAtUtc = UtcNow.AddDays(-30),
            ApprovedAtUtc = UtcNow.AddDays(-29)
        };

    private static CompanyProfile CreateCompany(string id, string userId)
        => new()
        {
            Id = id,
            UserId = userId,
            CompanyName = id,
            LicenseNumber = $"license-{id}",
            ContactName = "Reference Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = $"{id}.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"reference/{id}",
            CreatedAtUtc = UtcNow.AddDays(-30)
        };

    private static Wallet CreateWallet(string id, string companyId, string userId, decimal availableBalance)
        => new()
        {
            Id = id,
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = userId,
            AvailableBalance = availableBalance,
            ReservedBalance = 0m,
            Currency = "EGP",
            CreatedAtUtc = UtcNow.AddDays(-30)
        };

    private static async Task AssertSqlServer2022Async(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)";
        Assert.Equal(16, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private static TimeSpan Percentile(IReadOnlyList<TimeSpan> sorted, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private sealed class ReferenceWorkloadFactory(
        string connectionString,
        DailyCycleCommandObserver observer) : ConfiguredWebAppFactory
    {
        protected override void ConfigureAppConfigurationCore(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["DeliveryJobs:Enabled"] = "false",
                    ["DeliveryJobs:BatchSize"] = BatchSize.ToString()
                }));
        }

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider());
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>(options => options
                    .UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection"))
                    .AddInterceptors(observer));
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class DailyCycleCommandObserver : DbCommandInterceptor
    {
        private int enabled;
        private long commandCount;
        public long CommandCount => Interlocked.Read(ref commandCount);

        public void ResetAndEnable()
        {
            Interlocked.Exchange(ref commandCount, 0);
            Volatile.Write(ref enabled, 1);
        }

        public void Disable() => Volatile.Write(ref enabled, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Count();
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Count();
            return ValueTask.FromResult(result);
        }

        private void Count()
        {
            if (Volatile.Read(ref enabled) == 1)
            {
                Interlocked.Increment(ref commandCount);
            }
        }
    }

    private sealed record ReferenceRunMeasurement(
        string Name,
        TimeSpan Elapsed,
        long CommandCount,
        long AllocatedBytes);
}
