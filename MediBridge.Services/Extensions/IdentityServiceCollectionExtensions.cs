using System.Reflection;
using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<FileStorageOptions>>().Value);
        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<CloudinaryStorageOptions>>().Value);
        services.AddSingleton(contactVerificationOptions);
        services.AddSingleton(smtpEmailOptions);
        services.AddSingleton<IAuthTokenService>(_ => new AuthTokenService(
            tokenOptions.Issuer,
            tokenOptions.Audience,
            tokenOptions.SigningKey,
            tokenOptions.AccessTokenMinutes,
            tokenOptions.RefreshTokenDays));

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
        services.AddScoped<ICampaignDraftService, CampaignDraftService>();
        services.AddScoped<ICompanyWalletService, CompanyWalletService>();

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
