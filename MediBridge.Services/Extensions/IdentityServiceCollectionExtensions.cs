using System.Reflection;
using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.Services.Extensions;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        var tokenOptions = new AuthTokenOptions();
        configuration.GetSection(AuthTokenOptions.SectionName).Bind(tokenOptions);
        tokenOptions.Validate();

        services.AddSingleton<IAuthTokenService>(_ => new AuthTokenService(
            tokenOptions.Issuer,
            tokenOptions.Audience,
            tokenOptions.SigningKey,
            tokenOptions.AccessTokenMinutes,
            tokenOptions.RefreshTokenDays));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();

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
