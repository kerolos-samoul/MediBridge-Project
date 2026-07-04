using Hangfire;
using MediBridge.APIs.Config;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Extensions;

public sealed class RecurringDeliveryJobRegistrar
{
    public const string ExpiryRecurringJobId = "medibridge-expiry-cleaner";
    public const string InjectorRecurringJobId = "medibridge-daily-injector";
    public const string ExpiryCron = "0 0 * * *";
    public const string InjectorCron = "5 0 * * *";

    private readonly IRecurringJobManager manager;
    private readonly IEgyptBusinessClock clock;
    private readonly DeliveryJobOptions options;
    private readonly ILogger<RecurringDeliveryJobRegistrar> logger;

    public RecurringDeliveryJobRegistrar(
        IRecurringJobManager manager,
        IEgyptBusinessClock clock,
        IOptions<DeliveryJobOptions> options,
        ILogger<RecurringDeliveryJobRegistrar> logger)
    {
        this.manager = manager;
        this.clock = clock;
        this.options = options.Value;
        this.logger = logger;
    }

    public void Register()
    {
        if (!options.Enabled)
        {
            return;
        }

        var recurringOptions = new RecurringJobOptions
        {
            TimeZone = clock.TimeZone
        };
        manager.AddOrUpdate<IDeliveryExpiryService>(
            ExpiryRecurringJobId,
            options.QueueName,
            service => service.RunAsync(CancellationToken.None),
            ExpiryCron,
            recurringOptions);
        manager.AddOrUpdate<IDailyDeliveryInjectorService>(
            InjectorRecurringJobId,
            options.QueueName,
            service => service.RunAsync(CancellationToken.None),
            InjectorCron,
            recurringOptions);

        logger.LogInformation(
            "Registered delivery recurring jobs {ExpiryJobId} and {InjectorJobId} in queue {QueueName} using time zone {TimeZoneId}.",
            ExpiryRecurringJobId,
            InjectorRecurringJobId,
            options.QueueName,
            clock.TimeZone.Id);
    }
}
