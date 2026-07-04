using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using MediBridge.APIs.Config;
using MediBridge.APIs.Extensions;
using MediBridge.Core.Interfaces.Time;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7HangfireRegistrationTests
{
    [Fact]
    public void AddMediBridgeDeliveryJobs_RegistersServerAndValidatedBoundedOptions()
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=Phase7Registration;Integrated Security=true;TrustServerCertificate=true",
            ["DeliveryJobs:Enabled"] = "true",
            ["DeliveryJobs:AutomaticRetryAttempts"] = "5",
            ["DeliveryJobs:WorkerCount"] = "4",
            ["DeliveryJobs:QueueName"] = "delivery",
            ["DeliveryJobs:TimeZoneId"] = "Africa/Cairo"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediBridgeDeliveryJobs(configuration);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DeliveryJobOptions>>().Value;
        Assert.Equal((5, 4, "delivery", "Africa/Cairo"),
            (options.AutomaticRetryAttempts, options.WorkerCount, options.QueueName, options.TimeZoneId));
    }

    [Fact]
    public void Registrar_AddsExactlyTwoStableCairoDefinitionsIdempotently()
    {
        var manager = new RecordingRecurringJobManager();
        var clock = new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero)));
        var registrar = new RecurringDeliveryJobRegistrar(
            manager,
            clock,
            Options.Create(new DeliveryJobOptions()),
            NullLogger<RecurringDeliveryJobRegistrar>.Instance);

        registrar.Register();
        registrar.Register();

        Assert.Equal(4, manager.Registrations.Count);
        Assert.Equal(2, manager.Registrations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manager.Registrations, item => Assert.Equal("delivery", item.Job.Queue));
        Assert.All(manager.Registrations, item => Assert.Equal(clock.TimeZone.Id, item.Options.TimeZone.Id));
        Assert.Contains(manager.Registrations, item => item.Id == RecurringDeliveryJobRegistrar.ExpiryRecurringJobId
            && item.Cron == RecurringDeliveryJobRegistrar.ExpiryCron
            && item.Job.Type == typeof(IDeliveryExpiryService));
        Assert.Contains(manager.Registrations, item => item.Id == RecurringDeliveryJobRegistrar.InjectorRecurringJobId
            && item.Cron == RecurringDeliveryJobRegistrar.InjectorCron
            && item.Job.Type == typeof(IDailyDeliveryInjectorService));
    }

    [Fact]
    public async Task Enqueuer_PersistsTheSameServiceInterfaceMethodsAndExpiryFirstContinuation()
    {
        var client = new RecordingBackgroundJobClient();
        var enqueuer = new HangfireDeliveryJobEnqueuer(client, Options.Create(new DeliveryJobOptions()));

        var expiry = await enqueuer.EnqueueExpiryAsync();
        var injector = await enqueuer.EnqueueInjectorContinuationAsync(expiry.SchedulerJobId);
        await enqueuer.EnqueueInjectorAsync();

        Assert.Equal(3, client.Created.Count);
        Assert.Equal(typeof(IDeliveryExpiryService), client.Created[0].Job.Type);
        Assert.Equal(nameof(IDeliveryExpiryService.RunAsync), client.Created[0].Job.Method.Name);
        Assert.IsType<EnqueuedState>(client.Created[0].State);
        var awaiting = Assert.IsType<AwaitingState>(client.Created[1].State);
        Assert.Equal(expiry.SchedulerJobId, awaiting.ParentId);
        Assert.Equal(typeof(IDailyDeliveryInjectorService), client.Created[1].Job.Type);
        Assert.Equal(typeof(IDailyDeliveryInjectorService), client.Created[2].Job.Type);
        Assert.Equal("job-2", injector.SchedulerJobId);
    }

    [Fact]
    public async Task Host_MapsNeitherHangfireDashboardNorManualJobRoutes()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/hangfire")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync("/api/jobs/retry", null)).StatusCode);
    }

    [Fact]
    public async Task SqlStorage_ContainsExactlyTwoRecurringRowsAfterRepeatedRegistration()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var storage = factory.Services.GetRequiredService<JobStorage>();
        var registrar = new RecurringDeliveryJobRegistrar(
            new RecurringJobManager(storage),
            factory.Services.GetRequiredService<IEgyptBusinessClock>(),
            Options.Create(new DeliveryJobOptions { Enabled = true }),
            NullLogger<RecurringDeliveryJobRegistrar>.Instance);

        registrar.Register();
        registrar.Register();

        using var connection = storage.GetConnection();
        var jobs = connection.GetRecurringJobs();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(
            new[] { RecurringDeliveryJobRegistrar.InjectorRecurringJobId, RecurringDeliveryJobRegistrar.ExpiryRecurringJobId },
            jobs.Select(job => job.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.All(jobs, job => Assert.Equal("delivery", job.Job.Queue));
    }

    private sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        public List<(string Id, Job Job, string Cron, RecurringJobOptions Options)> Registrations { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
            => Registrations.Add((recurringJobId, job, cronExpression, options));

        public void Trigger(string recurringJobId) { }
        public void RemoveIfExists(string recurringJobId) { }
    }

    private sealed class RecordingBackgroundJobClient : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, state));
            return $"job-{Created.Count}";
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;
        public FixedTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class WebAppFactory : ConfiguredWebAppFactory;
}
