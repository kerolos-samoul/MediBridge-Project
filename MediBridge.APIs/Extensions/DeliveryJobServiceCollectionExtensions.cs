using System.ComponentModel.DataAnnotations;
using Hangfire;
using Hangfire.SqlServer;
using MediBridge.APIs.Config;
using MediBridge.Core.Interfaces.Messaging;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Extensions;

public static class DeliveryJobServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeDeliveryJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(DeliveryJobOptions.SectionName).Get<DeliveryJobOptions>()
            ?? new DeliveryJobOptions();
        Validator.ValidateObject(options, new ValidationContext(options), validateAllProperties: true);

        services.AddOptions<DeliveryJobOptions>()
            .Bind(configuration.GetSection(DeliveryJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The delivery scheduler database connection is not configured.");
        }

        services.AddHangfire((_, hangfire) => hangfire
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new AutomaticRetryAttribute { Attempts = options.AutomaticRetryAttempts })
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                SchemaName = "HangFire"
            }));

        if (options.Enabled)
        {
            services.AddHangfireServer(server =>
            {
                server.WorkerCount = options.WorkerCount;
                server.Queues = [options.QueueName];
            });
        }

        services.AddSingleton<IDeliveryJobEnqueuer, HangfireDeliveryJobEnqueuer>();
        services.AddSingleton<RecurringDeliveryJobRegistrar>();
        return services;
    }
}
