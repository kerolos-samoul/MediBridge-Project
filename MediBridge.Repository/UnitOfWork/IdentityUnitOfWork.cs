using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Data.SqlClient;
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
        return SaveChangesWithConflictMappingAsync(cancellationToken);
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await SaveChangesWithConflictMappingAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (IsIdentityUniquenessConflict(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityRecordConflictException("Duplicate email, phone, or license.", ex);
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
            await SaveChangesWithConflictMappingAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex) when (IsIdentityUniquenessConflict(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IdentityRecordConflictException("Duplicate email, phone, or license.", ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<int> SaveChangesWithConflictMappingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsIdentityUniquenessConflict(ex))
        {
            throw new IdentityRecordConflictException("Duplicate email, phone, or license.", ex);
        }
    }

    private static bool IsIdentityUniquenessConflict(Exception exception)
    {
        if (exception is IdentityRecordConflictException)
        {
            return false;
        }

        return exception is DbUpdateException dbUpdateException
            && IsSqlUniqueConstraintViolation(dbUpdateException)
            && dbUpdateException.Entries.Count != 0
            && dbUpdateException.Entries.All(entry =>
                entry.Entity is MediBridgeIdentityUser or DoctorProfile or CompanyProfile);
    }

    private static bool IsSqlUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException sqlException
            && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627);
    }
}
