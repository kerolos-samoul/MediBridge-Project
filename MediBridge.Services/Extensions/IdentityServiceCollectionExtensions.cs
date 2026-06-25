using System.Reflection;
using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
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
        contactVerificationOptions.Validate(tokenOptions.SigningKey);

        var passwordResetOptions = new PasswordResetOptions();
        configuration.GetSection(PasswordResetOptions.SectionName).Bind(passwordResetOptions);
        passwordResetOptions.Validate();

        services.AddSingleton<IAuthTokenService>(_ => new AuthTokenService(
            tokenOptions.Issuer,
            tokenOptions.Audience,
            tokenOptions.SigningKey,
            contactVerificationOptions.OneTimeSecretHashingKey,
            tokenOptions.AccessTokenMinutes,
            tokenOptions.RefreshTokenDays));
        services.AddSingleton(Options.Create(contactVerificationOptions));
        services.AddSingleton(Options.Create(passwordResetOptions));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<ICompanyWalletService, CompanyWalletService>();
        services.AddScoped<ICampaignWorkflowService, CampaignWorkflowService>();
        services.AddScoped<IFileAccessService, FileAccessService>();
        services.AddScoped<IAdminPricingService, AdminPricingService>();
        services.AddScoped<IAdminCampaignReviewService, AdminCampaignReviewService>();

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
