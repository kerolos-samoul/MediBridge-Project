using Hangfire;
using MediBridge.APIs.Config;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Extensions;

public sealed class RecurringActivityEnforcementJobRegistrar
{
    public const string DailyScoreRecurringJobId = "medibridge-daily-activity-score";
    public const string WeeklyEnforcementRecurringJobId = "medibridge-weekly-enforcement";
    public const string SuspensionExpiryRecurringJobId = "medibridge-suspension-expiry";
    public const string DailyScoreCron = "30 0 * * *";
    public const string WeeklyEnforcementCron = "0 0 * * 1";
    public const string SuspensionExpiryCron = "*/5 * * * *";

    private readonly IRecurringJobManager manager;
    private readonly IEgyptBusinessClock clock;
    private readonly DeliveryJobOptions options;
    private readonly ILogger<RecurringActivityEnforcementJobRegistrar> logger;

    public RecurringActivityEnforcementJobRegistrar(
        IRecurringJobManager manager,
        IEgyptBusinessClock clock,
        IOptions<DeliveryJobOptions> options,
        ILogger<RecurringActivityEnforcementJobRegistrar> logger)
    {
        this.manager = manager;
        this.clock = clock;
        this.options = options.Value;
        this.logger = logger;
    }

    public void Register()
    {
        var recurringOptions = new RecurringJobOptions
        {
            TimeZone = clock.TimeZone
        };

        manager.AddOrUpdate<IActivityScoreService>(
            DailyScoreRecurringJobId,
            options.QueueName,
            service => service.RunDailyScoreAsync(null, null, CancellationToken.None),
            DailyScoreCron,
            recurringOptions);
        manager.AddOrUpdate<IWeeklyEnforcementService>(
            WeeklyEnforcementRecurringJobId,
            options.QueueName,
            service => service.RunWeeklyEnforcementAsync(null, null, CancellationToken.None),
            WeeklyEnforcementCron,
            recurringOptions);
        manager.AddOrUpdate<IActivityScoreService>(
            SuspensionExpiryRecurringJobId,
            options.QueueName,
            service => service.ExpireSuspensionsAsync(null, CancellationToken.None),
            SuspensionExpiryCron,
            recurringOptions);

        logger.LogInformation(
            "Registered Phase 9 activity enforcement recurring jobs in time zone {TimeZoneId}.",
            clock.TimeZone.Id);
    }
}
