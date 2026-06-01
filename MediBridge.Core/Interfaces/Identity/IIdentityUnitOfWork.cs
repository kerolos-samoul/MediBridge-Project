namespace MediBridge.Core.Interfaces.Identity;

public interface IIdentityUnitOfWork
{
    IApplicationUserRepository Users { get; }
    IProfileRepository Profiles { get; }
    IRefreshCredentialRepository RefreshCredentials { get; }
    IPasswordResetFlowRepository PasswordResetFlows { get; }
    IContactVerificationFlowRepository ContactVerificationFlows { get; }
    IAdminAccountDecisionRepository AdminAccountDecisions { get; }
    IAccountResubmissionRepository AccountResubmissions { get; }
    IAccountResubmissionTokenRepository AccountResubmissionTokens { get; }
    IAuthenticationAuditEventRepository AuthenticationAuditEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
