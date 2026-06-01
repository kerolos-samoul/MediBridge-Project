using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Repository.Repositories.Identity;
using MediBridge.Repository.UnitOfWork;
using MediBridge.Core.Interfaces.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.Repository.Extensions;

public static class RepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeRepository(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<MediBridgeDbContext>(options =>
            options.UseSqlServer(connectionString));

        services
            .AddIdentityCore<MediBridgeIdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MediBridgeDbContext>();
        services.Configure<IdentityOptions>(options =>
        {
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
        });

        services.AddScoped<IApplicationUserRepository, ApplicationUserRepository>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IRefreshCredentialRepository, RefreshCredentialRepository>();
        services.AddScoped<IPasswordResetFlowRepository, PasswordResetFlowRepository>();
        services.AddScoped<IContactVerificationFlowRepository, ContactVerificationFlowRepository>();
        services.AddScoped<IAdminAccountDecisionRepository, AdminAccountDecisionRepository>();
        services.AddScoped<IAccountResubmissionRepository, AccountResubmissionRepository>();
        services.AddScoped<IAccountResubmissionTokenRepository, AccountResubmissionTokenRepository>();
        services.AddScoped<IAuthenticationAuditEventRepository, AuthenticationAuditEventRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        return services;
    }
}
