using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;
using Xunit;
using Xunit.Abstractions;

namespace MediBridge.IntegrationTests;

public sealed class Phase8InteractionPerformanceTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc);
    private readonly ITestOutputHelper output;

    public Phase8InteractionPerformanceTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void BenchmarkAllocation_StaysInsideDefaultDoctorInteractionThrottle()
    {
        var allocation = CreateRoundRobinAllocation(workerCount: 50, requestCount: 420);

        Assert.Equal(420, allocation.Count);
        Assert.All(
            allocation.GroupBy(worker => worker),
            partition => Assert.InRange(partition.Count() + 1, 1, 10));
    }

    [Phase8PerformanceFact]
    public async Task ReferenceSqlServer2022Profile_MeetsReadAndReplayLatencyTarget()
    {
        await using var sql = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sql.StartAsync();
        await using var factory = new PerformanceFactory(sql.GetConnectionString());
        await factory.InitializeDatabaseAsync();
        var fixtures = new List<Phase8ReadTrackingIntegrationTests.Phase8Fixture>(capacity: 50);
        for (var index = 0; index < 50; index++)
        {
            fixtures.Add(await Phase8ReadTrackingIntegrationTests.SeedPhase8FixtureAsync(factory.Services, $"perf-{index:D2}"));
        }

        var clients = fixtures.Select((fixture, index) =>
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", fixture.DoctorUserId));
            client.DefaultRequestHeaders.Add("Idempotency-Key", $"phase8-performance-key-{index:D2}");
            return client;
        }).ToArray();

        try
        {
            for (var index = 0; index < fixtures.Count; index++)
            {
                using var settle = await PostReplayInteractionAsync(clients[index], fixtures[index].DeliveryId);
                Assert.Equal(HttpStatusCode.OK, settle.StatusCode);
            }

            foreach (var worker in CreateRoundRobinAllocation(clients.Length, requestCount: 20))
            {
                using var warmRead = await clients[worker].PutAsync($"/api/doctor/messages/{fixtures[worker].DeliveryId}/read", null);
                Assert.Equal(HttpStatusCode.OK, warmRead.StatusCode);
            }

            var workload = CreateMeasuredWorkload(workerCount: clients.Length, readCount: 200, replayCount: 200);
            var durations = new long[workload.Count];
            var failures = 0;
            using var concurrency = new SemaphoreSlim(10, 10);
            await Task.WhenAll(workload.Select(async (work, index) =>
            {
                await concurrency.WaitAsync();
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    using var response = work.IsRead
                        ? await clients[work.WorkerIndex].PutAsync($"/api/doctor/messages/{fixtures[work.WorkerIndex].DeliveryId}/read", null)
                        : await PostReplayInteractionAsync(clients[work.WorkerIndex], fixtures[work.WorkerIndex].DeliveryId);
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

            Array.Sort(durations);
            var withinTarget = durations.Count(value => value <= 1000);
            output.WriteLine(
                "Phase8 interaction: p50={0}ms p95={1}ms p99={2}ms within1s={3}/400 failures={4}",
                Percentile(durations, 0.50),
                Percentile(durations, 0.95),
                Percentile(durations, 0.99),
                withinTarget,
                failures);

            Assert.Equal(0, failures);
            Assert.True(withinTarget >= 380, $"Only {withinTarget}/400 requests completed within 1s.");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            foreach (var fixture in fixtures)
            {
                Assert.Equal(2, await db.WalletTransactions.CountAsync(transaction => transaction.RelatedDeliveryId == fixture.DeliveryId));
                Assert.Equal(2, await db.WalletLedgerEntries.CountAsync(entry => entry.MessageDeliveryId == fixture.DeliveryId));
                Assert.Equal(1, await db.DeliveryInteractions.CountAsync(interaction => interaction.DeliveryId == fixture.DeliveryId));
            }
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    private static Task<HttpResponseMessage> PostReplayInteractionAsync(HttpClient client, string deliveryId)
    {
        return client.PostAsync(
            $"/api/doctor/messages/{deliveryId}/interact",
            new StringContent("""{"Outcome":"Accept","Feedback":"Performance useful feedback."}""", Encoding.UTF8, "application/json"));
    }

    private static IReadOnlyList<int> CreateRoundRobinAllocation(int workerCount, int requestCount)
    {
        return Enumerable.Range(0, requestCount).Select(index => index % workerCount).ToArray();
    }

    private static IReadOnlyList<MeasuredWork> CreateMeasuredWorkload(int workerCount, int readCount, int replayCount)
    {
        var reads = Enumerable.Range(0, readCount).Select(index => new MeasuredWork(index % workerCount, IsRead: true));
        var replays = Enumerable.Range(0, replayCount).Select(index => new MeasuredWork(index % workerCount, IsRead: false));
        return reads.Concat(replays).ToArray();
    }

    private static long Percentile(IReadOnlyList<long> sortedDurations, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sortedDurations.Count * percentile) - 1, 0, sortedDurations.Count - 1);
        return sortedDurations[index];
    }

    private sealed record MeasuredWork(int WorkerIndex, bool IsRead);

    private sealed class PerformanceFactory(string connectionString) : ConfiguredWebAppFactory
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
                services.RemoveAll<DbContextOptions<MediBridgeDbContext>>();
                services.AddDbContext<MediBridgeDbContext>(options =>
                    options.UseSqlServer(context.Configuration.GetConnectionString("DefaultConnection")));
            });
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }
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
