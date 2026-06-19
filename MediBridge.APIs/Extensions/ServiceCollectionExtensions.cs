using MediBridge.APIs.Config;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Security;
using MediBridge.Core.Interfaces;
using MediBridge.Services.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
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
        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidator>();

        services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(options => options.Validate().Count == 0, "File storage options must match Phase 4 requirements.")
            .ValidateOnStart();

        services
            .AddOptions<CloudinaryStorageOptions>()
            .Bind(configuration.GetSection(CloudinaryStorageOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CloudinaryStorageOptions>, CloudinaryStorageOptionsValidator>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddSingleton<IAuditLogger, NoopAuditLogger>();
        services.AddSingleton<IOwnershipAuthorizationService, OwnershipAuthorizationService>();
        services.AddMediBridgeJwtAuthentication(configuration);
        services.AddMediBridgeRateLimiting(configuration);

        return services;
    }

    private static IServiceCollection AddMediBridgeJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwtOptions);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = "role"
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(AuthorizationPolicies.Admin));
            options.AddPolicy(
                AuthorizationPolicies.DoctorOnly,
                policy => policy.RequireRole(AuthorizationPolicies.Doctor));
            options.AddPolicy(
                AuthorizationPolicies.CompanyOnly,
                policy => policy.RequireRole(AuthorizationPolicies.Company));
            options.AddPolicy(
                AuthorizationPolicies.AdminFileReview,
                policy => policy.RequireRole(AuthorizationPolicies.Admin));
            options.AddPolicy(
                AuthorizationPolicies.CompanyCampaignFileUpload,
                policy => policy.RequireRole(AuthorizationPolicies.Company));
            options.AddPolicy(
                AuthorizationPolicies.DoctorVerificationUpload,
                policy => policy.RequireRole(AuthorizationPolicies.Doctor));
            options.AddPolicy(
                AuthorizationPolicies.AuthenticatedFileAccess,
                policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(
                AuthorizationPolicies.Phase5CompanyDoctorSearch,
                policy => policy.RequireRole(AuthorizationPolicies.Company));
            options.AddPolicy(
                AuthorizationPolicies.Phase5CompanyCampaignAccess,
                policy => policy.RequireRole(AuthorizationPolicies.Company));
            options.AddPolicy(
                AuthorizationPolicies.Phase5CompanyWalletAccess,
                policy => policy.RequireRole(AuthorizationPolicies.Company));
        });

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
                if (policyName is RateLimitPolicyNames.FileUpload or RateLimitPolicyNames.Phase5CampaignSubmission or RateLimitPolicyNames.Phase5WalletTopUp)
                {
                    rateLimiterOptions.AddPolicy(policyName, context =>
                    {
                        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
                        var partitionKey = string.IsNullOrWhiteSpace(userId)
                            ? $"anonymous:{context.Connection.RemoteIpAddress}"
                            : $"user:{userId}";

                        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = policy.PermitLimit,
                            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
                            QueueLimit = policy.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            AutoReplenishment = true
                        });
                    });
                    continue;
                }

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
