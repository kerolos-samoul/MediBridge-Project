using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Admin;

public sealed class AdminWorkQueueRepository : IAdminWorkQueueRepository
{
    private readonly MediBridgeDbContext context;

    public AdminWorkQueueRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task<IReadOnlyDictionary<AdminWorkQueueCategory, int>> CountByCategoryAsync(
        AdminWorkQueueFilters filters,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(filters.Status))
        {
            var filteredItems = await ListAllItemsForStatusFilterAsync(filters.Status, cancellationToken);
            return filteredItems
                .Where(item => filters.Category is null || item.Category == filters.Category)
                .GroupBy(item => item.Category)
                .ToDictionary(group => group.Key, group => group.Count());
        }

        var counts = new Dictionary<AdminWorkQueueCategory, int>();
        if (filters.Category is null or AdminWorkQueueCategory.Account)
        {
            counts[AdminWorkQueueCategory.Account] = await context.Users.AsNoTracking().CountAsync(
                user => !user.IsDeleted
                    && user.AccountStatus == AccountStatus.Pending
                    && (user.Role == UserRole.Doctor || user.Role == UserRole.Company),
                cancellationToken);
        }

        if (filters.Category is null or AdminWorkQueueCategory.File)
        {
            counts[AdminWorkQueueCategory.File] = await context.StoredFiles.AsNoTracking().CountAsync(
                file => file.DeletedAtUtc == null && file.ReviewStatus == StoredFileReviewStatus.Pending,
                cancellationToken);
        }

        if (filters.Category is null or AdminWorkQueueCategory.Campaign)
        {
            counts[AdminWorkQueueCategory.Campaign] = await context.Campaigns.AsNoTracking().CountAsync(
                campaign => !campaign.IsDeleted && campaign.Status == CampaignStatus.PendingReview,
                cancellationToken);
        }

        if (filters.Category is null or AdminWorkQueueCategory.Enforcement)
        {
            counts[AdminWorkQueueCategory.Enforcement] = await context.DoctorWeeklyViolations.AsNoTracking().CountAsync(
                violation => violation.RollingViolationCount > 0,
                cancellationToken);
        }

        if (filters.Category is null or AdminWorkQueueCategory.Withdrawal)
        {
            counts[AdminWorkQueueCategory.Withdrawal] = await context.WithdrawalRequests.AsNoTracking().CountAsync(
                request => request.Status == WithdrawalRequestStatus.Requested || request.Status == WithdrawalRequestStatus.Approved,
                cancellationToken);
        }

        return counts;
    }

    public async Task<AdminPageReadModel<AdminWorkQueueItemReadModel>> ListQueueItemsAsync(
        AdminWorkQueueFilters filters,
        CancellationToken cancellationToken = default)
    {
        var pagination = filters.Pagination ?? new AdminPagination();
        IReadOnlyList<AdminWorkQueueItemReadModel> filtered;
        int totalCount;

        if (string.IsNullOrWhiteSpace(filters.Status))
        {
            filtered = await ListPagedItemsAsync(filters.Category, pagination, cancellationToken);
            totalCount = await CountQueueItemsAsync(filters.Category, cancellationToken);
        }
        else
        {
            var statusItems = (await ListAllItemsForStatusFilterAsync(filters.Status, cancellationToken))
                .Where(item => filters.Category is null || item.Category == filters.Category)
                .OrderBy(item => item.UrgencyRank)
                .ThenBy(item => item.SubmittedAtUtc)
                .ThenBy(item => item.ItemId, StringComparer.Ordinal)
                .ToArray();
            totalCount = statusItems.Length;
            filtered = statusItems
                .Skip(pagination.Skip)
                .Take(pagination.PageSize)
                .ToArray();
        }

        return new AdminPageReadModel<AdminWorkQueueItemReadModel>(
            pagination.PageNumber,
            pagination.PageSize,
            totalCount,
            filtered);
    }

    private async Task<int> CountQueueItemsAsync(AdminWorkQueueCategory? category, CancellationToken cancellationToken)
    {
        if (category is not null)
        {
            var counts = await CountByCategoryAsync(new AdminWorkQueueFilters(category), cancellationToken);
            return counts.TryGetValue(category.Value, out var count) ? count : 0;
        }

        var countsByCategory = await CountByCategoryAsync(new AdminWorkQueueFilters(), cancellationToken);
        return countsByCategory.Values.Sum();
    }

    private async Task<IReadOnlyList<AdminWorkQueueItemReadModel>> ListPagedItemsAsync(
        AdminWorkQueueCategory? category,
        AdminPagination pagination,
        CancellationToken cancellationToken)
    {
        var query = BuildQueueProjectionQuery(category);
        var rows = await query
            .OrderBy(item => item.UrgencyRank)
            .ThenBy(item => item.SubmittedAtUtc)
            .ThenBy(item => item.ItemId)
            .Skip(pagination.Skip)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);
        return rows.Select(ToReadModel).ToArray();
    }

    private IQueryable<WorkQueueProjection> BuildQueueProjectionQuery(AdminWorkQueueCategory? category)
    {
        var accounts = context.Users.AsNoTracking()
            .Where(user => !user.IsDeleted
                && user.AccountStatus == AccountStatus.Pending
                && (user.Role == UserRole.Doctor || user.Role == UserRole.Company))
            .Select(user => new WorkQueueProjection
            {
                ItemId = user.Id,
                Category = AdminWorkQueueCategory.Account,
                Status = "Pending",
                UrgencyRank = 10,
                SubmittedAtUtc = user.CreatedAtUtc,
                OwnerType = user.Role == UserRole.Doctor ? AdminWorkQueueOwnerType.Doctor : AdminWorkQueueOwnerType.Company,
                OwnerId = user.Id,
                OwnerDisplay = user.Role == UserRole.Doctor ? "Doctor" : "Company",
                Summary = user.Role == UserRole.Doctor ? "Doctor account pending verification" : "Company account pending verification",
                Amount = null,
                ActionSet = 1
            });

        var files = context.StoredFiles.AsNoTracking()
            .Where(file => file.DeletedAtUtc == null && file.ReviewStatus == StoredFileReviewStatus.Pending)
            .Select(file => new WorkQueueProjection
            {
                ItemId = file.Id,
                Category = AdminWorkQueueCategory.File,
                Status = "Pending",
                UrgencyRank = 15,
                SubmittedAtUtc = file.CreatedAtUtc,
                OwnerType = file.OwnerType == StoredFileOwnerType.Doctor
                    ? AdminWorkQueueOwnerType.Doctor
                    : file.OwnerType == StoredFileOwnerType.Company
                        ? AdminWorkQueueOwnerType.Company
                        : file.OwnerType == StoredFileOwnerType.Campaign
                            ? AdminWorkQueueOwnerType.Campaign
                            : AdminWorkQueueOwnerType.System,
                OwnerId = file.OwnerId,
                OwnerDisplay = "Protected file",
                Summary = "Protected file pending review",
                Amount = null,
                ActionSet = 1
            });

        var campaigns = context.Campaigns.AsNoTracking()
            .Where(campaign => !campaign.IsDeleted && campaign.Status == CampaignStatus.PendingReview)
            .Select(campaign => new WorkQueueProjection
            {
                ItemId = campaign.Id,
                Category = AdminWorkQueueCategory.Campaign,
                Status = "PendingReview",
                UrgencyRank = 20,
                SubmittedAtUtc = campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc,
                OwnerType = AdminWorkQueueOwnerType.Company,
                OwnerId = campaign.CompanyId,
                OwnerDisplay = "Campaign",
                Summary = "Campaign pending review",
                Amount = null,
                ActionSet = 2
            });

        var enforcement = context.DoctorWeeklyViolations.AsNoTracking()
            .Where(violation => violation.RollingViolationCount > 0)
            .Select(violation => new WorkQueueProjection
            {
                ItemId = violation.Id,
                Category = AdminWorkQueueCategory.Enforcement,
                Status = "Violation",
                UrgencyRank = 25,
                SubmittedAtUtc = violation.CreatedAtUtc,
                OwnerType = AdminWorkQueueOwnerType.Doctor,
                OwnerId = violation.DoctorId,
                OwnerDisplay = "Doctor enforcement",
                Summary = "Doctor has rolling weekly violation(s)",
                Amount = null,
                ActionSet = 3
            });

        var withdrawals = context.WithdrawalRequests.AsNoTracking()
            .Where(request => request.Status == WithdrawalRequestStatus.Requested || request.Status == WithdrawalRequestStatus.Approved)
            .Select(request => new WorkQueueProjection
            {
                ItemId = request.Id,
                Category = AdminWorkQueueCategory.Withdrawal,
                Status = request.Status == WithdrawalRequestStatus.Requested ? "Requested" : "Approved",
                UrgencyRank = request.Status == WithdrawalRequestStatus.Requested ? 20 : 30,
                SubmittedAtUtc = request.RequestedAtUtc,
                OwnerType = AdminWorkQueueOwnerType.Doctor,
                OwnerId = request.DoctorId,
                OwnerDisplay = request.DoctorId,
                Summary = "Withdrawal request pending admin action",
                Amount = request.Amount,
                ActionSet = request.Status == WithdrawalRequestStatus.Requested ? 4 : 5
            });

        var query = accounts.Concat(files).Concat(campaigns).Concat(enforcement).Concat(withdrawals);
        return category is null ? query : query.Where(item => item.Category == category);
    }

    private async Task<IReadOnlyList<AdminWorkQueueItemReadModel>> ListAllItemsForStatusFilterAsync(
        string? statusFilter,
        CancellationToken cancellationToken)
    {
        var items = await BuildQueueProjectionQuery(null)
            .ToListAsync(cancellationToken);
        var readModels = items.Select(ToReadModel).ToList();

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            readModels = readModels
                .Where(item => string.Equals(item.Status, statusFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return readModels;
    }

    private static AdminWorkQueueItemReadModel ToReadModel(WorkQueueProjection projection)
    {
        return new AdminWorkQueueItemReadModel(
            projection.ItemId,
            projection.Category,
            projection.Status,
            projection.UrgencyRank,
            projection.SubmittedAtUtc,
            projection.OwnerType,
            projection.OwnerId,
            projection.OwnerDisplay,
            BuildSummary(projection),
            ResolveActions(projection.ActionSet),
            Array.Empty<string>());
    }

    private static string BuildSummary(WorkQueueProjection projection)
    {
        if (projection.Category == AdminWorkQueueCategory.Withdrawal && projection.Amount is decimal amount)
        {
            return $"Withdrawal request for {amount:0.00} EGP";
        }

        return projection.Summary;
    }

    private static IReadOnlyList<string> ResolveActions(int actionSet)
        => actionSet switch
        {
            1 => ["Approve", "Reject", "RequestCorrection"],
            2 => ["Approve", "Reject", "RequestRevision"],
            3 => ["Review", "Warn", "Suspend", "ReduceDailyLimit"],
            4 => ["Approve", "Reject"],
            5 => ["MarkPaid", "MarkFailed"],
            _ => []
        };

    private sealed class WorkQueueProjection
    {
        public string ItemId { get; set; } = string.Empty;
        public AdminWorkQueueCategory Category { get; set; }
        public string Status { get; set; } = string.Empty;
        public int UrgencyRank { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public AdminWorkQueueOwnerType? OwnerType { get; set; }
        public string? OwnerId { get; set; }
        public string? OwnerDisplay { get; set; }
        public string Summary { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public int ActionSet { get; set; }
    }
}
