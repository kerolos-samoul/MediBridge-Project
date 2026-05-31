using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace MediBridge.APIs.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddSingleton<IAuditLogger, NoopAuditLogger>();
        services.AddSingleton<IOwnershipAuthorizationService, OwnershipAuthorizationService>();
        services.AddMediBridgeRateLimiting(configuration);

        return services;
    }

    private static IServiceCollection AddMediBridgeRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new RateLimitingOptions();
        configuration.GetSection(RateLimitingOptions.SectionName).Bind(options);

        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiEnvelopeFactory.Create<object?>(StatusCodes.Status429TooManyRequests, "Too many requests.", null),
                    new JsonSerializerOptions { PropertyNamingPolicy = null },
                    cancellationToken);
            };

            foreach (var policyName in RateLimitPolicyNames.All)
            {
                var policy = options.GetPolicy(policyName);
                rateLimiterOptions.AddFixedWindowLimiter(policyName, limiterOptions =>
                {
                    limiterOptions.PermitLimit = policy.PermitLimit;
                    limiterOptions.Window = TimeSpan.FromSeconds(policy.WindowSeconds);
                    limiterOptions.QueueLimit = policy.QueueLimit;
                    limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    limiterOptions.AutoReplenishment = true;
                });
            }
        });

        return services;
    }
}
