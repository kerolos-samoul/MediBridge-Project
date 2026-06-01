using System.Reflection;
using FluentValidation;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.Services.Extensions;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var issuer = jwtSection["Issuer"] ?? string.Empty;
        var audience = jwtSection["Audience"] ?? string.Empty;
        var signingKey = jwtSection["SigningKey"] ?? string.Empty;

        services.AddSingleton<IAuthTokenService>(_ => new AuthTokenService(
            issuer,
            audience,
            signingKey,
            jwtSection.GetValue<int>("AccessTokenMinutes"),
            jwtSection.GetValue<int>("RefreshTokenDays")));

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
