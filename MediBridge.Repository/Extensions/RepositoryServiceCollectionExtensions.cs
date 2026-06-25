using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Repository.Auditing;
using MediBridge.Repository.Notifications;
using MediBridge.Repository.Repositories.Campaigns;
using MediBridge.Repository.Repositories.Files;
using MediBridge.Repository.Repositories.Identity;
using MediBridge.Repository.Repositories.Messaging;
using MediBridge.Repository.Repositories.Payments;
using MediBridge.Repository.Repositories.Policies;
using MediBridge.Repository.Repositories.Wallets;
using MediBridge.Repository.Storage;
using MediBridge.Repository.UnitOfWork;
using CloudinaryDotNet;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Notifications;
using MediBridge.Core.Interfaces.Payments;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MediBridge.Repository.Extensions;

public static class RepositoryServiceCollectionExtensions
{
    public static IServiceCollection AddMediBridgeRepository(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var cloudinarySection = configuration.GetSection(CloudinaryStorageOptions.SectionName);
        var cloudinaryOptions = new CloudinaryStorageOptions
        {
            CloudinaryUrl = cloudinarySection["CloudinaryUrl"] ?? string.Empty,
            FolderPrefix = cloudinarySection["FolderPrefix"] ?? "medibridge",
            UseSecureUrls = !bool.TryParse(cloudinarySection["UseSecureUrls"], out var useSecureUrls) || useSecureUrls,
            SignedUrlMinutes = int.TryParse(cloudinarySection["SignedUrlMinutes"], out var signedUrlMinutes)
                ? signedUrlMinutes
                : 5
        };
        services.AddSingleton(Options.Create(cloudinaryOptions));

        var smtpSection = configuration.GetSection(SmtpEmailOptions.SectionName);
        var contactVerificationSection = configuration.GetSection("ContactVerification");
        var smtpOptions = new SmtpEmailOptions
        {
            Host = smtpSection["Host"] ?? string.Empty,
            Port = int.TryParse(smtpSection["Port"], out var smtpPort) ? smtpPort : 587,
            Username = smtpSection["Username"] ?? string.Empty,
            Password = smtpSection["Password"] ?? string.Empty,
            FromEmail = smtpSection["FromEmail"] ?? string.Empty,
            FromName = smtpSection["FromName"] ?? smtpSection["FromDisplayName"] ?? "MediBridge",
            UseStartTls = !bool.TryParse(smtpSection["UseStartTls"] ?? smtpSection["EnableSsl"], out var useStartTls) || useStartTls,
            AllowOverrideRecipientEmail =
                bool.TryParse(smtpSection["AllowOverrideRecipientEmail"] ?? contactVerificationSection["AllowOverrideRecipientEmail"], out var allowOverride)
                    && allowOverride,
            OverrideRecipientEmail = smtpSection["OverrideRecipientEmail"] ?? contactVerificationSection["OverrideRecipientEmail"]
        };
        services.AddSingleton(Options.Create(smtpOptions));

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
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();
        services.AddScoped<IWalletLedgerEntryRepository, WalletLedgerEntryRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IStoredFileRepository, StoredFileRepository>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CloudinaryStorageOptions>>().Value;
            options.Validate();
            return new Cloudinary(options.CloudinaryUrl);
        });
        services.AddScoped<IFileStorageProvider, CloudinaryFileStorageProvider>();
        services.AddScoped<IEmailDelivery, MailKitEmailDelivery>();
        services.AddScoped<IAuditLogger, DatabaseAuditLogger>();
        services.AddScoped<IPolicyHistoryRepository, PolicyHistoryRepository>();
        services.AddScoped<IAuditEventRepository, AuditEventRepository>();
        services.AddScoped<IDomainUnitOfWork, DomainUnitOfWork>();

        return services;
    }
}
