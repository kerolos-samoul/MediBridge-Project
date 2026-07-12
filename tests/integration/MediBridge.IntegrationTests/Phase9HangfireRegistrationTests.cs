using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using MediBridge.APIs.Extensions;
using MediBridge.Core.Interfaces.Time;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.APIs.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9HangfireRegistrationTests
{
    [Fact]
    public void Registrar_AddsStableCairoActivityEnforcementDefinitionsIdempotently()
    {
        var manager = new RecordingRecurringJobManager();
        var clock = new EgyptBusinessClock(new FixedTimeProvider(new DateTimeOffset(2026, 7, 4, 0, 0, 0, TimeSpan.Zero)));
        var registrar = new RecurringActivityEnforcementJobRegistrar(
            manager,
            clock,
            Options.Create(new DeliveryJobOptions { QueueName = "delivery" }),
            NullLogger<RecurringActivityEnforcementJobRegistrar>.Instance);

        registrar.Register();
        registrar.Register();

        Assert.Equal(6, manager.Registrations.Count);
        Assert.Equal(3, manager.Registrations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(manager.Registrations, item => Assert.Equal("delivery", item.Job.Queue));
        Assert.All(manager.Registrations, item => Assert.Equal(clock.TimeZone.Id, item.Options.TimeZone.Id));
        Assert.Contains(manager.Registrations, item => item.Id == RecurringActivityEnforcementJobRegistrar.DailyScoreRecurringJobId
            && item.Cron == RecurringActivityEnforcementJobRegistrar.DailyScoreCron
            && item.Job.Type == typeof(IActivityScoreService)
            && item.Job.Method.Name == nameof(IActivityScoreService.RunDailyScoreAsync));
        Assert.Contains(manager.Registrations, item => item.Id == RecurringActivityEnforcementJobRegistrar.WeeklyEnforcementRecurringJobId
            && item.Cron == RecurringActivityEnforcementJobRegistrar.WeeklyEnforcementCron
            && item.Job.Type == typeof(IWeeklyEnforcementService));
        Assert.Contains(manager.Registrations, item => item.Id == RecurringActivityEnforcementJobRegistrar.SuspensionExpiryRecurringJobId
            && item.Cron == RecurringActivityEnforcementJobRegistrar.SuspensionExpiryCron
            && item.Job.Type == typeof(IActivityScoreService)
            && item.Job.Method.Name == nameof(IActivityScoreService.ExpireSuspensionsAsync));
    }

    [Fact]
    public async Task Host_MapsNoHangfireDashboardAndOnlyAdminActivityJobRoutes()
    {
        await using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/hangfire")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync("/api/activity-jobs/run-score", null)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/activity-jobs/status")).StatusCode);
    }

    [Fact]
    public async Task SqlStorage_ContainsPhase9RowsAfterRepeatedRegistration()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var storage = factory.Services.GetRequiredService<JobStorage>();
        var registrar = new RecurringActivityEnforcementJobRegistrar(
            new RecurringJobManager(storage),
            factory.Services.GetRequiredService<IEgyptBusinessClock>(),
            Options.Create(new DeliveryJobOptions { QueueName = "delivery" }),
            NullLogger<RecurringActivityEnforcementJobRegistrar>.Instance);

        registrar.Register();
        registrar.Register();

        using var connection = storage.GetConnection();
        var jobs = connection.GetRecurringJobs()
            .Where(job => job.Id.StartsWith("medibridge-", StringComparison.Ordinal)
                && (job.Id.Contains("activity", StringComparison.Ordinal) || job.Id.Contains("weekly", StringComparison.Ordinal) || job.Id.Contains("suspension", StringComparison.Ordinal)))
            .OrderBy(job => job.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                RecurringActivityEnforcementJobRegistrar.DailyScoreRecurringJobId,
                RecurringActivityEnforcementJobRegistrar.SuspensionExpiryRecurringJobId,
                RecurringActivityEnforcementJobRegistrar.WeeklyEnforcementRecurringJobId
            ],
            jobs.Select(job => job.Id).ToArray());
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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now;
        public FixedTimeProvider(DateTimeOffset now) => this.now = now;
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class WebAppFactory : ConfiguredWebAppFactory;
}
