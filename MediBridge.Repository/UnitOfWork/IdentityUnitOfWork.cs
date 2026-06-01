using MediBridge.Core.Interfaces.Identity;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.UnitOfWork;

public sealed class IdentityUnitOfWork : IIdentityUnitOfWork
{
    private readonly MediBridgeDbContext context;

    public IdentityUnitOfWork(
        IApplicationUserRepository users,
        IProfileRepository profiles,
        IRefreshCredentialRepository refreshCredentials,
        IPasswordResetFlowRepository passwordResetFlows,
        IContactVerificationFlowRepository contactVerificationFlows,
        IAdminAccountDecisionRepository adminAccountDecisions,
        IAccountResubmissionRepository accountResubmissions,
        IAccountResubmissionTokenRepository accountResubmissionTokens,
        IAuthenticationAuditEventRepository authenticationAuditEvents,
        MediBridgeDbContext context)
    {
        Users = users;
        Profiles = profiles;
        RefreshCredentials = refreshCredentials;
        PasswordResetFlows = passwordResetFlows;
        ContactVerificationFlows = contactVerificationFlows;
        AdminAccountDecisions = adminAccountDecisions;
        AccountResubmissions = accountResubmissions;
        AccountResubmissionTokens = accountResubmissionTokens;
        AuthenticationAuditEvents = authenticationAuditEvents;
        this.context = context;
    }

    public IApplicationUserRepository Users { get; }
    public IProfileRepository Profiles { get; }
    public IRefreshCredentialRepository RefreshCredentials { get; }
    public IPasswordResetFlowRepository PasswordResetFlows { get; }
    public IContactVerificationFlowRepository ContactVerificationFlows { get; }
    public IAdminAccountDecisionRepository AdminAccountDecisions { get; }
    public IAccountResubmissionRepository AccountResubmissions { get; }
    public IAccountResubmissionTokenRepository AccountResubmissionTokens { get; }
    public IAuthenticationAuditEventRepository AuthenticationAuditEvents { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return context.SaveChangesAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}