using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MediBridge.Core.Enums;

namespace MediBridge.Repository.Data.Identity;

public static class AdminIdentitySeed
{
    public const string DefaultAdminRole = nameof(UserRole.Admin);
    public const string DefaultAdminEmail = "admin@medibridge.local";
    private const string DevelopmentAdminPasswordKey = "Identity:DevelopmentAdminPassword";

    public static MediBridgeIdentityUser CreateDevelopmentAdmin(string email)
    {
        return new MediBridgeIdentityUser
        {
            UserName = email,
            Email = email,
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailConfirmed = true,
            EmailVerified = true,
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            LastStatusChangedAtUtc = DateTime.UtcNow
        };
    }

    public static async Task EnsureDevelopmentAdminAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MediBridgeIdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var developmentAdminPassword = configuration[DevelopmentAdminPasswordKey];

        if (string.IsNullOrWhiteSpace(developmentAdminPassword))
        {
            throw new InvalidOperationException($"{DevelopmentAdminPasswordKey} must be configured when development admin seeding is enabled.");
        }

        if (!await roleManager.RoleExistsAsync(DefaultAdminRole))
        {
            var roleCreationResult = await roleManager.CreateAsync(new IdentityRole(DefaultAdminRole));
            if (roleCreationResult.Succeeded is false)
            {
                throw new InvalidOperationException(string.Join(", ", roleCreationResult.Errors.Select(error => error.Description)));
            }
        }

        var existingAdmin = await userManager.FindByEmailAsync(DefaultAdminEmail);
        if (existingAdmin is null)
        {
            var adminUser = CreateDevelopmentAdmin(DefaultAdminEmail);
            var creationResult = await userManager.CreateAsync(adminUser, developmentAdminPassword);
            if (creationResult.Succeeded is false)
            {
                throw new InvalidOperationException(string.Join(", ", creationResult.Errors.Select(error => error.Description)));
            }

            existingAdmin = adminUser;
        }
        else
        {
            existingAdmin.Role = UserRole.Admin;
            existingAdmin.AccountStatus = AccountStatus.Approved;
            existingAdmin.EmailVerified = true;
            existingAdmin.PhoneVerified = true;
            existingAdmin.ApprovedAtUtc = existingAdmin.ApprovedAtUtc ?? DateTime.UtcNow;
            existingAdmin.LastStatusChangedAtUtc = DateTime.UtcNow;
            var updateResult = await userManager.UpdateAsync(existingAdmin);
            if (updateResult.Succeeded is false)
            {
                throw new InvalidOperationException(string.Join(", ", updateResult.Errors.Select(error => error.Description)));
            }

            if (await userManager.HasPasswordAsync(existingAdmin) is false)
            {
                var passwordResult = await userManager.AddPasswordAsync(existingAdmin, developmentAdminPassword);
                if (passwordResult.Succeeded is false)
                {
                    throw new InvalidOperationException(string.Join(", ", passwordResult.Errors.Select(error => error.Description)));
                }
            }
        }

        if (await userManager.IsInRoleAsync(existingAdmin, DefaultAdminRole) is false)
        {
            await userManager.AddToRoleAsync(existingAdmin, DefaultAdminRole);
        }
    }
}
