using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Repository.Repositories.Campaigns;
using MediBridge.Repository.Repositories.Files;
using MediBridge.Repository.Repositories.Identity;
using MediBridge.Repository.Repositories.Messaging;
using MediBridge.Repository.Repositories.Payments;
using MediBridge.Repository.Repositories.Policies;
using MediBridge.Repository.Repositories.Wallets;
using MediBridge.Repository.UnitOfWork;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;
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
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IMessageQueueRepository, MessageQueueRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IDeliveryInteractionOperationRepository, DeliveryInteractionOperationRepository>();
        services.AddScoped<IDeliveryJobRunRepository, DeliveryJobRunRepository>();
        services.AddScoped<IDeliveryRecoveryDispatchRepository, DeliveryRecoveryDispatchRepository>();
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();
        services.AddScoped<IWalletLedgerEntryRepository, WalletLedgerEntryRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IStoredFileRepository, StoredFileRepository>();
        services.AddScoped<IFileReviewRepository, FileReviewRepository>();
        services.AddScoped<IFileAccessGrantAuditRepository, FileAccessGrantAuditRepository>();
        services.AddScoped<IPolicyHistoryRepository, PolicyHistoryRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IDomainUnitOfWork, DomainUnitOfWork>();

        return services;
    }
}
