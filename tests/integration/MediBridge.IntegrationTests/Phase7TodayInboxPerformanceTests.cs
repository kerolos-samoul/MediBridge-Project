using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
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

public sealed class Phase7TodayInboxPerformanceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc);
    private readonly ITestOutputHelper output;

    public Phase7TodayInboxPerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void BenchmarkRequestAllocation_StaysWithinProductionLimitForEveryDoctor()
    {
        var allocation = CreateRequestAllocation(doctorCount: 4, warmupCount: 20, measuredCount: 200);

        Assert.Equal(220, allocation.Count);
        Assert.All(
            allocation.GroupBy(doctorIndex => doctorIndex),
            partition => Assert.InRange(partition.Count(), 1, 60));
    }

    [Phase7PerformanceFact]
    public async Task ReferenceSqlServer2022Profile_MeetsInboxLatencyAndSafetyTarget()
    {
        await using var sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sql.StartAsync();
        var storage = new Phase4FakeFileStorageProvider();
        var queryObserver = new InboxQueryObserver();
        await using var factory = new PerformanceFactory(sql.GetConnectionString(), storage, queryObserver);
        await factory.InitializeDatabaseAsync();
        var userIds = await SeedPagesAsync(factory.Services, doctorCount: 4);
        var clients = userIds.Select(userId =>
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", userId));
            return client;
        }).ToArray();

        try
        {
            foreach (var doctorIndex in CreateRoundRobinAllocation(clients.Length, requestCount: 20))
            {
                using var warmup = await clients[doctorIndex].GetAsync("/api/doctor/messages/today?PageSize=100");
                Assert.Equal(HttpStatusCode.OK, warmup.StatusCode);
            }

            queryObserver.ResetAndEnable();
            var measuredAllocation = CreateRoundRobinAllocation(clients.Length, requestCount: 200);
            var durations = new long[measuredAllocation.Count];
            var failures = 0;
            using var concurrency = new SemaphoreSlim(10, 10);
            await Task.WhenAll(measuredAllocation.Select(async (doctorIndex, index) =>
            {
                await concurrency.WaitAsync();
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    using var response = await clients[doctorIndex].GetAsync("/api/doctor/messages/today?PageSize=100");
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
            queryObserver.Disable();

            Array.Sort(durations);
            var withinTarget = durations.Count(value => value <= 1000);
            var p50 = durations[99];
            var p95 = durations[189];
            var p99 = durations[197];
            output.WriteLine(
                "Phase7 inbox: p50={0}ms p95={1}ms p99={2}ms within1s={3}/200 failures={4} deliveryQueries={5} assetQueries={6}",
                p50,
                p95,
                p99,
                withinTarget,
                failures,
                queryObserver.DeliveryQueryCount,
                queryObserver.AssetQueryCount);

            Assert.Equal(0, failures);
            Assert.True(withinTarget >= 190, $"Only {withinTarget}/200 requests completed within 1s. p50={p50}ms p95={p95}ms p99={p99}ms.");
            Assert.Equal(200, queryObserver.DeliveryQueryCount);
            Assert.Equal(200, queryObserver.AssetQueryCount);
            Assert.Equal(0, storage.AccessGrantCallCount);
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    private static async Task<IReadOnlyList<string>> SeedPagesAsync(IServiceProvider services, int doctorCount)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            services, $"company-user-{suffix}", $"company-{suffix}", $"company-{suffix}@example.test", UtcNow.AddDays(-10));
        var doctors = new List<Phase7DoctorSeed>(doctorCount);
        for (var doctorIndex = 0; doctorIndex < doctorCount; doctorIndex++)
        {
            doctors.Add(await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
                services,
                $"user-{doctorIndex}-{suffix}",
                $"doctor-{doctorIndex}-{suffix}",
                $"doctor-{doctorIndex}-{suffix}@example.test",
                50m,
                100,
                UtcNow.AddDays(-10)));
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        for (var doctorIndex = 0; doctorIndex < doctors.Count; doctorIndex++)
        {
            for (var index = 0; index < 100; index++)
            {
                var campaignId = $"campaign-{doctorIndex}-{index:D3}-{suffix}";
                var fileId = $"file-{doctorIndex}-{index:D3}-{suffix}";
                db.Campaigns.Add(new Campaign
                {
                    Id = campaignId,
                    CompanyId = company.CompanyId,
                    Title = $"Campaign {doctorIndex}-{index}",
                    Description = "Reference workload",
                    Status = CampaignStatus.Approved,
                    SubmittedAtUtc = UtcNow.AddDays(-2),
                    CreatedAtUtc = UtcNow.AddDays(-3)
                });
                db.StoredFiles.Add(new StoredFile
                {
                    Id = fileId,
                    OwnerType = StoredFileOwnerType.Company,
                    OwnerId = company.CompanyId,
                    RelatedCampaignId = campaignId,
                    Purpose = StoredFilePurpose.CampaignMedia,
                    OriginalFileName = $"asset-{doctorIndex}-{index}.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 1024,
                    StorageKey = $"private/{fileId}",
                    StorageProvider = "TestStorage",
                    StorageResourceType = StoredFileStorageResourceType.Raw,
                    StorageDeliveryType = StoredFileStorageDeliveryType.Private,
                    Visibility = StoredFileVisibility.Private,
                    ReviewStatus = StoredFileReviewStatus.Approved,
                    UploadStatus = StoredFileUploadStatus.Stored,
                    SafetyScanStatus = StoredFileSafetyScanStatus.Passed,
                    CreatedAtUtc = UtcNow.AddDays(-2),
                    ReviewedAtUtc = UtcNow.AddDays(-1)
                });
                db.DoctorAdDeliveries.Add(new DoctorAdDelivery
                {
                    Id = $"delivery-{doctorIndex}-{index:D3}-{suffix}",
                    DoctorId = doctors[doctorIndex].DoctorId,
                    CampaignId = campaignId,
                    CompanyId = company.CompanyId,
                    DeliveryDateEgypt = new DateOnly(2026, 7, 3),
                    DeliveredAtUtc = UtcNow.AddMinutes(index - 100),
                    Status = DeliveryStatus.Active,
                    ReservationStatus = ReservationStatus.Reserved,
                    PricePerMessageSnapshot = 50m,
                    PlatformFeePercentSnapshot = 20m,
                    PlatformFeeAmount = 10m,
                    DoctorEarnings = 40m,
                    ReservedAmount = 50m,
                    CreatedAtUtc = UtcNow.AddMinutes(index - 100)
                });
            }
        }

        await db.SaveChangesAsync();
        return doctors.Select(doctor => doctor.UserId).ToArray();
    }

    private static IReadOnlyList<int> CreateRequestAllocation(int doctorCount, int warmupCount, int measuredCount)
    {
        return CreateRoundRobinAllocation(doctorCount, warmupCount + measuredCount);
    }

    private static IReadOnlyList<int> CreateRoundRobinAllocation(int doctorCount, int requestCount)
    {
        if (doctorCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(doctorCount));
        }

        return Enumerable.Range(0, requestCount).Select(index => index % doctorCount).ToArray();
    }

    private sealed class PerformanceFactory(
        string connectionString,
        IFileStorageProvider storageProvider,
        InboxQueryObserver queryObserver) : ConfiguredWebAppFactory
    {
        protected override void ConfigureAppConfigurationCore(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
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
                services.RemoveAll<IFileStorageProvider>();
                services.AddSingleton(storageProvider);
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>(options => options
                    .UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection"))
                    .AddInterceptors(queryObserver));
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class InboxQueryObserver : DbCommandInterceptor
    {
        private int enabled;
        private int deliveryQueryCount;
        private int assetQueryCount;

        public int DeliveryQueryCount => Volatile.Read(ref deliveryQueryCount);
        public int AssetQueryCount => Volatile.Read(ref assetQueryCount);

        public void ResetAndEnable()
        {
            Interlocked.Exchange(ref deliveryQueryCount, 0);
            Interlocked.Exchange(ref assetQueryCount, 0);
            Volatile.Write(ref enabled, 1);
        }

        public void Disable() => Volatile.Write(ref enabled, 0);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref enabled) == 1)
            {
                if (command.CommandText.Contains("[DoctorAdDeliveries]", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref deliveryQueryCount);
                }

                if (command.CommandText.Contains("[StoredFiles]", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref assetQueryCount);
                }
            }

            return ValueTask.FromResult(result);
        }
    }
}

public sealed class Phase7PerformanceFactAttribute : FactAttribute
{
    public Phase7PerformanceFactAttribute()
    {
#if !RELEASE
        Skip = "The reference profile requires a Release build.";
#else
        if (!string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE7_PERFORMANCE"), "1", StringComparison.Ordinal))
        {
            Skip = "Set MEDIBRIDGE_PHASE7_PERFORMANCE=1 on the documented SQL Server 2022 reference profile to collect evidence.";
        }
        else if (!string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE7_DEDICATED"), "1", StringComparison.Ordinal) ||
                 !string.Equals(Environment.GetEnvironmentVariable("MEDIBRIDGE_PHASE7_SSD"), "1", StringComparison.Ordinal))
        {
            Skip = "The reference profile requires an explicitly dedicated, SSD-backed host (MEDIBRIDGE_PHASE7_DEDICATED=1 and MEDIBRIDGE_PHASE7_SSD=1).";
        }
        else if (Environment.ProcessorCount < 4 || GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < 8L * 1024 * 1024 * 1024)
        {
            Skip = "The reference profile requires at least four dedicated vCPUs and 8 GB available RAM.";
        }
        else if (!System.Runtime.GCSettings.IsServerGC)
        {
            Skip = "The reference profile requires Server GC.";
        }
        else if (Debugger.IsAttached)
        {
            Skip = "The reference profile cannot run under a debugger.";
        }
#endif
    }
}
