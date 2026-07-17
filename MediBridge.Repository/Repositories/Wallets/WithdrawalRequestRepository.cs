using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Core.Interfaces.Wallets;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Wallets;

public sealed class WithdrawalRequestRepository : IWithdrawalRequestRepository
{
    private readonly MediBridgeDbContext context;

    public WithdrawalRequestRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public Task AddAsync(WithdrawalRequest request, CancellationToken cancellationToken = default)
    {
        request.Amount = MoneyRules.EnsurePositive(request.Amount, nameof(request.Amount));
        return context.WithdrawalRequests.AddAsync(request, cancellationToken).AsTask();
    }

    public Task<WithdrawalRequest?> FindByIdAsync(string withdrawalRequestId, CancellationToken cancellationToken = default)
    {
        return context.WithdrawalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(request => request.Id == withdrawalRequestId, cancellationToken);
    }

    public Task<WithdrawalRequest?> FindByIdForUpdateAsync(string withdrawalRequestId, CancellationToken cancellationToken = default)
    {
        return context.WithdrawalRequests
            .FromSqlInterpolated($"SELECT * FROM [WithdrawalRequests] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {withdrawalRequestId}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<WithdrawalRequest?> FindByIdWithConcurrencyTokenAsync(string withdrawalRequestId, byte[] concurrencyToken, CancellationToken cancellationToken = default)
    {
        var request = await context.WithdrawalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(request => request.Id == withdrawalRequestId, cancellationToken);
        return request is not null && request.ConcurrencyToken.SequenceEqual(concurrencyToken)
            ? request
            : null;
    }

    public Task<AdminPageReadModel<WithdrawalRequest>> ListDoctorOwnedAsync(
        string doctorId,
        WithdrawalRequestStatus? status,
        AdminPagination pagination,
        CancellationToken cancellationToken = default)
    {
        var filters = new AdminWithdrawalFilters(Status: status, DoctorId: doctorId, Pagination: pagination);
        return ListAsync(ApplyFilters(context.WithdrawalRequests.AsNoTracking(), filters), pagination, cancellationToken);
    }

    public Task<AdminPageReadModel<WithdrawalRequest>> ListAdminAsync(AdminWithdrawalFilters filters, CancellationToken cancellationToken = default)
    {
        var pagination = filters.Pagination ?? new AdminPagination();
        return ListAsync(ApplyFilters(context.WithdrawalRequests.AsNoTracking(), filters), pagination, cancellationToken);
    }

    public Task<int> CountAsync(AdminWithdrawalFilters filters, CancellationToken cancellationToken = default)
    {
        return ApplyFilters(context.WithdrawalRequests.AsNoTracking(), filters).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<WithdrawalRequestStatus, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
    {
        return await context.WithdrawalRequests
            .AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, cancellationToken);
    }

    private static IQueryable<WithdrawalRequest> ApplyFilters(IQueryable<WithdrawalRequest> query, AdminWithdrawalFilters filters)
    {
        if (filters.Status is not null)
        {
            query = query.Where(request => request.Status == filters.Status);
        }

        if (!string.IsNullOrWhiteSpace(filters.DoctorId))
        {
            var doctorId = filters.DoctorId.Trim();
            query = query.Where(request => request.DoctorId == doctorId);
        }

        if (filters.RequestedFromUtc is not null)
        {
            query = query.Where(request => request.RequestedAtUtc >= filters.RequestedFromUtc);
        }

        if (filters.RequestedToUtc is not null)
        {
            query = query.Where(request => request.RequestedAtUtc <= filters.RequestedToUtc);
        }

        if (filters.ReviewedFromUtc is not null)
        {
            query = query.Where(request => request.ReviewedAtUtc >= filters.ReviewedFromUtc);
        }

        if (filters.ReviewedToUtc is not null)
        {
            query = query.Where(request => request.ReviewedAtUtc <= filters.ReviewedToUtc);
        }

        if (filters.MinimumAmount is not null)
        {
            query = query.Where(request => request.Amount >= filters.MinimumAmount);
        }

        if (filters.MaximumAmount is not null)
        {
            query = query.Where(request => request.Amount <= filters.MaximumAmount);
        }

        if (!string.IsNullOrWhiteSpace(filters.PayoutReference))
        {
            var payoutReference = filters.PayoutReference.Trim();
            query = query.Where(request => request.PayoutReference == payoutReference);
        }

        return query;
    }

    private static async Task<AdminPageReadModel<WithdrawalRequest>> ListAsync(
        IQueryable<WithdrawalRequest> query,
        AdminPagination pagination,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(request => request.RequestedAtUtc)
            .ThenByDescending(request => request.Id)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);
        return new AdminPageReadModel<WithdrawalRequest>(pagination.PageNumber, pagination.PageSize, totalCount, items);
    }
}
