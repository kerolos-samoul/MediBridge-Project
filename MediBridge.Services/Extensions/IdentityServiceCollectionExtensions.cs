using System.Reflection;
using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MediBridge.Services.Extensions;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        var tokenOptions = new AuthTokenOptions();
        configuration.GetSection(AuthTokenOptions.SectionName).Bind(tokenOptions);
        tokenOptions.Validate();
        var contactVerificationOptions = new ContactVerificationOptions();
        configuration.GetSection(ContactVerificationOptions.SectionName).Bind(contactVerificationOptions);
        var contactVerificationErrors = contactVerificationOptions.Validate();
        if (contactVerificationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", contactVerificationErrors));
        }
        var smtpEmailOptions = new SmtpEmailOptions();
        configuration.GetSection(SmtpEmailOptions.SectionName).Bind(smtpEmailOptions);
        var smtpEmailErrors = smtpEmailOptions.Validate();
        if (smtpEmailErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", smtpEmailErrors));
        }
        var doctorDeliverySettingsOptions = new DoctorDeliverySettingsOptions();
        configuration.GetSection(DoctorDeliverySettingsOptions.SectionName).Bind(doctorDeliverySettingsOptions);
        var deliverySettingsErrors = doctorDeliverySettingsOptions.Validate();
        if (deliverySettingsErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", deliverySettingsErrors));
        }

        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value);
        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<CloudinaryStorageOptions>>().Value);
        services.AddSingleton(contactVerificationOptions);
        services.AddSingleton(smtpEmailOptions);
        services.AddSingleton(doctorDeliverySettingsOptions);
        services.AddSingleton<IAuthTokenService>(_ => new AuthTokenService(
            tokenOptions.Issuer,
            tokenOptions.Audience,
            tokenOptions.SigningKey,
            tokenOptions.AccessTokenMinutes,
            tokenOptions.RefreshTokenDays));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEgyptBusinessClock, EgyptBusinessClock>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<FileUploadRequestValidator>();
        services.AddScoped<FileReviewRequestValidator>();
        services.AddScoped<IFileStorageProvider>(serviceProvider =>
        {
            var fileStorageOptions = serviceProvider.GetRequiredService<FileStorageOptions>();
            return fileStorageOptions.UploadsEnabled
                ? serviceProvider.GetRequiredService<CloudinaryFileStorageProvider>()
                : serviceProvider.GetRequiredService<DisabledFileStorageProvider>();
        });
        services.AddScoped<CloudinaryFileStorageProvider>();
        services.AddScoped<DisabledFileStorageProvider>();
        services.AddScoped<IFileWorkflowService, FileWorkflowService>();
        services.AddScoped<ICompanyDoctorSearchService, CompanyDoctorSearchService>();
        services.AddScoped<ICampaignWorkflowService, CampaignWorkflowService>();
        services.AddScoped<Validators.Campaigns.CompanyReportingDateRangeValidator>();
        services.AddScoped<Validators.Campaigns.CompanyReportingPaginationValidator>();
        services.AddScoped<Validators.Campaigns.CompanyReportingFilterValidator>();
        services.AddScoped<ICompanyReportingService, CompanyReportingService>();
        services.AddScoped<ICampaignDraftService, CampaignDraftService>();
        services.AddScoped<IAdminCampaignReviewService, AdminCampaignReviewService>();
        services.AddScoped<ICompanyWalletService, CompanyWalletService>();
        services.AddScoped<IAdminPricingService, AdminPricingService>();
        services.AddScoped<IAdminPlatformFeePolicyService, AdminPlatformFeePolicyService>();
        services.AddScoped<IAdminDeliveryJobService, AdminDeliveryJobService>();
        services.AddScoped<Validators.Admin.DoctorEnforcementActionRequestValidator>();
        services.AddScoped<Validators.Admin.RunDailyActivityScoreRequestDtoValidator>();
        services.AddScoped<Validators.Admin.RunWeeklyEnforcementRequestDtoValidator>();
        services.AddSingleton<ActivityScoreCalculator>();
        services.AddScoped<ActivityEnforcementJobRunTracker>();
        services.AddScoped<IActivityScoreService, ActivityScoreService>();
        services.AddSingleton<WeeklyEnforcementPolicy>();
        services.AddScoped<IWeeklyEnforcementService, WeeklyEnforcementService>();
        services.AddScoped<IAdminActivityEnforcementService, AdminActivityEnforcementService>();
        services.AddScoped<Validators.Admin.AdminToolsPaginationValidator>();
        services.AddScoped<Validators.Admin.AdminStatisticsDateRangeValidator>();
        services.AddScoped<IAdminWorkQueueService, AdminWorkQueueService>();
        services.AddScoped<IAdminStatisticsService, AdminStatisticsService>();
        services.AddScoped<IWithdrawalService, WithdrawalService>();
        services.AddScoped<DeliveryJobRunTracker>();
        services.AddScoped<IDeliveryJobRecoveryCoordinator, DeliveryJobRecoveryCoordinator>();
        services.AddScoped<IDeliveryExpiryService, DeliveryExpiryService>();
        services.AddScoped<Validators.Messaging.DoctorInteractionRequestValidator>();
        services.AddSingleton<DoctorInteractionIdempotency>();
        services.AddScoped<IDoctorMessageService, DoctorMessageService>();
        services.AddSingleton<DeliverySettlementSnapshotCalculator>();
        services.AddSingleton<DeliveryCandidateEligibilityPolicy>();
        services.AddScoped<IDailyDeliveryInjectorService>(serviceProvider => new DailyDeliveryInjectorService(
            serviceProvider.GetRequiredService<MediBridge.Core.Interfaces.IDomainUnitOfWork>(),
            serviceProvider.GetRequiredService<IEgyptBusinessClock>(),
            serviceProvider.GetRequiredService<DeliverySettlementSnapshotCalculator>(),
            serviceProvider.GetRequiredService<DeliveryCandidateEligibilityPolicy>(),
            configuration.GetValue<int?>("DeliveryJobs:BatchSize") ?? 100,
            serviceProvider.GetRequiredService<DeliveryJobRunTracker>(),
            serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DailyDeliveryInjectorService>>()));

        RegisterValidators(services, typeof(IdentityServiceCollectionExtensions).Assembly);

        return services;
    }

    private static void RegisterValidators(IServiceCollection services, Assembly assembly)
    {
        foreach (var implementationType in assembly.GetTypes()
                     .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IValidator).IsAssignableFrom(type)))
        {
            foreach (var validatorInterface in implementationType.GetInterfaces().Where(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IValidator<>)))
            {
                services.AddScoped(validatorInterface, implementationType);
            }
        }
    }
}
