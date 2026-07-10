using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionPerformanceTests
{
    private const int DoctorCount = 100;
    private const int DeliveriesPerDoctor = 10;
    private const int TotalDeliveries = DoctorCount * DeliveriesPerDoctor;
    private const int WarmupReads = 20;
    private const int WarmupInteractions = 20;
    private const int MeasuredReads = 200;
    private const int MeasuredInteractions = 200;
    private const int Concurrency = 10;
    private const decimal Price = 50m;
    private const decimal PlatformFee = 10m;
    private const decimal DoctorEarnings = 40m;
    private static readonly DateTime UtcNow = new(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly TodayEgypt = new(2026, 7, 10);
    private readonly ITestOutputHelper output;

    public Phase8InteractionPerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void ReferenceProfileShape_MatchesDocumentedPhase8Workload()
    {
        Assert.Equal(1_000, TotalDeliveries);
        Assert.Equal(20, WarmupReads);
        Assert.Equal(20, WarmupInteractions);
        Assert.Equal(200, MeasuredReads);
        Assert.Equal(200, MeasuredInteractions);
        Assert.Equal(10, Concurrency);
    }

    [Phase8PerformanceFact]
    public async Task ReferenceSqlServer2022Profile_MeetsReadInteractionLatencyAndFinancialSafetyTargets()
    {
        await using var sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await sql.StartAsync();

        var observer = new InteractionCommandObserver();
        await using var factory = new PerformanceFactory(sql.GetConnectionString(), observer);
        await factory.InitializeDatabaseAsync();
        var workload = await SeedReferenceWorkloadAsync(factory.Services);
        var clients = workload.Doctors.Select(doctor =>
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                TestJwtFactory.CreateToken("Doctor", doctor.UserId));
            return client;
        }).ToArray();

        try
        {
            await RunReadsAsync(clients, workload.ReadWarmupTargets);
            await RunInteractionsAsync(clients, workload.InteractionWarmupTargets, "warmup");

            observer.ResetAndEnable();
            var readDurations = await RunReadsAsync(clients, workload.ReadMeasuredTargets);
            var interactionDurations = await RunInteractionsAsync(clients, workload.InteractionMeasuredTargets, "measured");
            observer.Disable();

            await AssertFinancialSafetyAsync(
                factory.Services,
                workload.ReadWarmupTargets
                    .Concat(workload.ReadMeasuredTargets)
                    .Select(target => target.DeliveryId)
                    .ToArray(),
                workload.InteractionWarmupTargets
                    .Concat(workload.InteractionMeasuredTargets)
                    .Select(target => target.DeliveryId)
                    .ToArray());

            AssertLatency("Phase8 read", readDurations);
            AssertLatency("Phase8 interact", interactionDurations);
            output.WriteLine(
                "Phase8 interaction profile: read p50={0}ms p95={1}ms p99={2}ms; interact p50={3}ms p95={4}ms p99={5}ms; commands={6}",
                Percentile(readDurations, 0.50),
                Percentile(readDurations, 0.95),
                Percentile(readDurations, 0.99),
                Percentile(interactionDurations, 0.50),
                Percentile(interactionDurations, 0.95),
                Percentile(interactionDurations, 0.99),
                observer.CommandCount);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<Phase8PerformanceWorkload> SeedReferenceWorkloadAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        const string companyUserId = "phase8-performance-company-user";
        const string companyId = "phase8-performance-company";
        context.Users.Add(CreateUser(companyUserId, "phase8-performance-company@example.test", UserRole.Company));
        context.CompanyProfiles.Add(new CompanyProfile
        {
            Id = companyId,
            UserId = companyUserId,
            CompanyName = "Phase 8 Performance Company",
            LicenseNumber = "phase8-performance-license",
            ContactName = "Phase 8 Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "phase8-company-license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 2048,
            VerificationReference = "phase8/performance/company-license",
            CreatedAtUtc = UtcNow.AddDays(-30)
        });
        context.Wallets.Add(new Wallet
        {
            Id = "phase8-performance-company-wallet",
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = companyUserId,
            AvailableBalance = 0m,
            ReservedBalance = TotalDeliveries * Price,
            Currency = "EGP",
            CreatedAtUtc = UtcNow.AddDays(-30)
        });

        var doctors = new List<Phase8PerformanceDoctor>(DoctorCount);
        var deliveries = new List<Phase8PerformanceTarget>(TotalDeliveries);
        for (var doctorIndex = 0; doctorIndex < DoctorCount; doctorIndex++)
        {
            var userId = $"phase8-performance-doctor-user-{doctorIndex:D3}";
            var doctorId = $"phase8-performance-doctor-{doctorIndex:D3}";
            context.Users.Add(CreateUser(userId, $"phase8-performance-doctor-{doctorIndex:D3}@example.test", UserRole.Doctor));
            context.DoctorProfiles.Add(new DoctorProfile
            {
                Id = doctorId,
                UserId = userId,
                Specialization = "Cardiology",
                ExperienceYears = 10,
                Location = "Cairo",
                VerificationDocumentType = "License",
                VerificationOriginalFileName = $"{doctorId}-license.pdf",
                VerificationContentType = "application/pdf",
                VerificationSizeBytes = 1024,
                VerificationReference = $"phase8/performance/{doctorId}/license",
                DailyMessageLimit = DeliveriesPerDoctor,
                ActivityScore = 95m,
                Status = DoctorMarketplaceStatus.Active,
                PricePerMessage = Price,
                CreatedAtUtc = UtcNow.AddDays(-30)
            });
            context.Wallets.Add(new Wallet
            {
                Id = $"phase8-performance-doctor-wallet-{doctorIndex:D3}",
                OwnerType = WalletOwnerType.Doctor,
                OwnerId = doctorId,
                OwnerUserId = userId,
                AvailableBalance = 0m,
                ReservedBalance = 0m,
                Currency = "EGP",
                CreatedAtUtc = UtcNow.AddDays(-30)
            });
            doctors.Add(new Phase8PerformanceDoctor(userId, doctorId));
        }

        for (var flatIndex = 0; flatIndex < TotalDeliveries; flatIndex++)
        {
            var doctorIndex = flatIndex / DeliveriesPerDoctor;
            var campaignId = $"phase8-performance-campaign-{flatIndex:D4}";
            var deliveryId = $"phase8-performance-delivery-{flatIndex:D4}";
            context.Campaigns.Add(new Campaign
            {
                Id = campaignId,
                CompanyId = companyId,
                Title = $"Phase 8 performance campaign {flatIndex:D4}",
                Description = "Phase 8 interaction performance profile",
                ClinicalResearchInfo = "Phase 8 reference workload",
                Status = CampaignStatus.Approved,
                SubmittedAtUtc = UtcNow.AddDays(-2),
                CreatedAtUtc = UtcNow.AddDays(-3)
            });
            context.DoctorAdDeliveries.Add(new DoctorAdDelivery
            {
                Id = deliveryId,
                DoctorId = doctors[doctorIndex].DoctorId,
                CampaignId = campaignId,
                CompanyId = companyId,
                DeliveryDateEgypt = TodayEgypt,
                DeliveredAtUtc = UtcNow.AddMinutes(-flatIndex),
                Status = DeliveryStatus.Active,
                ReservationStatus = ReservationStatus.Reserved,
                PricePerMessageSnapshot = Price,
                PlatformFeePercentSnapshot = 20m,
                PlatformFeeAmount = PlatformFee,
                DoctorEarnings = DoctorEarnings,
                ReservedAmount = Price,
                CreatedAtUtc = UtcNow.AddMinutes(-flatIndex)
            });
            deliveries.Add(new Phase8PerformanceTarget(doctorIndex, deliveryId));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        return new Phase8PerformanceWorkload(
            doctors,
            deliveries.Take(WarmupReads).ToArray(),
            deliveries.Skip(WarmupReads).Take(MeasuredReads).ToArray(),
            deliveries.Skip(WarmupReads + MeasuredReads).Take(WarmupInteractions).ToArray(),
            deliveries.Skip(WarmupReads + MeasuredReads + WarmupInteractions).Take(MeasuredInteractions).ToArray());
    }

    private static async Task<long[]> RunReadsAsync(
        IReadOnlyList<HttpClient> clients,
        IReadOnlyList<Phase8PerformanceTarget> targets)
    {
        var durations = new long[targets.Count];
        var failures = 0;
        using var concurrency = new SemaphoreSlim(Concurrency, Concurrency);
        await Task.WhenAll(targets.Select(async (target, index) =>
        {
            await concurrency.WaitAsync();
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var response = await clients[target.DoctorIndex]
                    .PutAsync($"/api/doctor/messages/{target.DeliveryId}/read", content: null);
                stopwatch.Stop();
                durations[index] = stopwatch.ElapsedMilliseconds;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Interlocked.Increment(ref failures);
                }
            }
            finally
            {
                concurrency.Release();
            }
        }));

        Assert.Equal(0, failures);
        Array.Sort(durations);
        return durations;
    }

    private static async Task<long[]> RunInteractionsAsync(
        IReadOnlyList<HttpClient> clients,
        IReadOnlyList<Phase8PerformanceTarget> targets,
        string prefix)
    {
        var durations = new long[targets.Count];
        var failures = 0;
        using var concurrency = new SemaphoreSlim(Concurrency, Concurrency);
        await Task.WhenAll(targets.Select(async (target, index) =>
        {
            await concurrency.WaitAsync();
            try
            {
                using var request = Phase8InteractionTestHelpers.CreateInteractRequest(
                    target.DeliveryId,
                    $"phase8-{prefix}-{index:D4}",
                    index % 2 == 0 ? "Accept" : "Reject");
                var stopwatch = Stopwatch.StartNew();
                using var response = await clients[target.DoctorIndex].SendAsync(request);
                stopwatch.Stop();
                durations[index] = stopwatch.ElapsedMilliseconds;
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    Interlocked.Increment(ref failures);
                }
            }
            finally
            {
                concurrency.Release();
            }
        }));

        Assert.Equal(0, failures);
        Array.Sort(durations);
        return durations;
    }

    private static async Task AssertFinancialSafetyAsync(
        IServiceProvider services,
        IReadOnlyCollection<string> readDeliveryIds,
        IReadOnlyCollection<string> interactedDeliveryIds)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        Assert.Equal(0, await context.WalletTransactions
            .CountAsync(transaction => readDeliveryIds.Contains(transaction.RelatedDeliveryId!)));
        Assert.Equal(interactedDeliveryIds.Count, await context.WalletTransactions
            .Where(transaction => transaction.OperationType == WalletTransactionType.Charge
                && interactedDeliveryIds.Contains(transaction.RelatedDeliveryId!))
            .Select(transaction => transaction.RelatedDeliveryId)
            .Distinct()
            .CountAsync());
        Assert.Equal(interactedDeliveryIds.Count, await context.WalletTransactions
            .Where(transaction => transaction.OperationType == WalletTransactionType.Earn
                && interactedDeliveryIds.Contains(transaction.RelatedDeliveryId!))
            .Select(transaction => transaction.RelatedDeliveryId)
            .Distinct()
            .CountAsync());
        Assert.Equal(interactedDeliveryIds.Count * 2, await context.WalletTransactions
            .CountAsync(transaction => interactedDeliveryIds.Contains(transaction.RelatedDeliveryId!)));
        Assert.Equal(interactedDeliveryIds.Count * 2, await context.WalletLedgerEntries
            .CountAsync(entry => interactedDeliveryIds.Contains(entry.MessageDeliveryId!)));
        Assert.Equal(interactedDeliveryIds.Count, await context.DeliveryInteractionOperations
            .CountAsync(operation => interactedDeliveryIds.Contains(operation.DeliveryId)));
    }

    private void AssertLatency(string label, IReadOnlyList<long> sortedDurations)
    {
        var withinTarget = sortedDurations.Count(duration => duration <= 1000);
        output.WriteLine(
            "{0}: p50={1}ms p95={2}ms p99={3}ms within1s={4}/{5}",
            label,
            Percentile(sortedDurations, 0.50),
            Percentile(sortedDurations, 0.95),
            Percentile(sortedDurations, 0.99),
            withinTarget,
            sortedDurations.Count);
        Assert.True(
            withinTarget >= sortedDurations.Count * 0.95m,
            $"{label} completed only {withinTarget}/{sortedDurations.Count} requests within 1s.");
    }

    private static long Percentile(IReadOnlyList<long> sorted, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static MediBridgeIdentityUser CreateUser(string id, string email, UserRole role)
    {
        return new MediBridgeIdentityUser
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
            ApprovedAtUtc = UtcNow.AddDays(-29),
            CreatedAtUtc = UtcNow.AddDays(-30)
        };
    }

    private sealed class PerformanceFactory(
        string connectionString,
        InteractionCommandObserver observer) : ConfiguredWebAppFactory
    {
        protected override void ConfigureAppConfigurationCore(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString
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

    private sealed class InteractionCommandObserver : DbCommandInterceptor
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

    private sealed record Phase8PerformanceDoctor(string UserId, string DoctorId);

    private sealed record Phase8PerformanceTarget(int DoctorIndex, string DeliveryId);

    private sealed record Phase8PerformanceWorkload(
        IReadOnlyList<Phase8PerformanceDoctor> Doctors,
        IReadOnlyList<Phase8PerformanceTarget> ReadWarmupTargets,
        IReadOnlyList<Phase8PerformanceTarget> ReadMeasuredTargets,
        IReadOnlyList<Phase8PerformanceTarget> InteractionWarmupTargets,
        IReadOnlyList<Phase8PerformanceTarget> InteractionMeasuredTargets);
}

public sealed class Phase8PerformanceFactAttribute : FactAttribute
{
    public Phase8PerformanceFactAttribute()
    {
#if !RELEASE
        Skip = "The Phase 8 reference profile requires a Release build.";
#else
        if (!string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE8_PERFORMANCE"), "1", StringComparison.Ordinal))
        {
            Skip = "Set MEDIBRIDGE_PHASE8_PERFORMANCE=1 on the documented SQL Server 2022 reference profile to collect evidence.";
        }
        else if (!string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE8_DEDICATED"), "1", StringComparison.Ordinal) ||
                 !string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE8_SSD"), "1", StringComparison.Ordinal))
        {
            Skip = "The Phase 8 reference profile requires an explicitly dedicated, SSD-backed host (MEDIBRIDGE_PHASE8_DEDICATED=1 and MEDIBRIDGE_PHASE8_SSD=1).";
        }
        else if (Environment.ProcessorCount < 4 || GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < 8L * 1024 * 1024 * 1024)
        {
            Skip = "The Phase 8 reference profile requires at least four dedicated vCPUs and 8 GB available RAM.";
        }
        else if (!System.Runtime.GCSettings.IsServerGC)
        {
            Skip = "The Phase 8 reference profile requires Server GC.";
        }
        else if (Debugger.IsAttached)
        {
            Skip = "The Phase 8 reference profile cannot run under a debugger.";
        }
#endif
    }
}
