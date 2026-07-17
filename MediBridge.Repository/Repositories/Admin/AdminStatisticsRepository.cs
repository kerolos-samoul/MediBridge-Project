using MediBridge.Core.Interfaces.Admin;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using MediBridge.Core.Enums;

namespace MediBridge.Repository.Repositories.Admin;

public sealed class AdminStatisticsRepository : IAdminStatisticsRepository
{
    private readonly MediBridgeDbContext context;

    public AdminStatisticsRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task<AdminStatisticsSourceCounts> GetSourceCountsAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        var (fromUtc, toExclusiveUtc) = ToUtcBounds(dateRange);
        var accountRows = await context.Users
            .AsNoTracking()
            .Where(user => user.CreatedAtUtc >= fromUtc && user.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(user => new { user.Role, user.AccountStatus })
            .Select(group => new { Key = group.Key.Role + ":" + group.Key.AccountStatus, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var accountCounts = accountRows.ToDictionary(item => item.Key, item => item.Count);

        var pendingAccounts = await context.Users.AsNoTracking().CountAsync(
            user => !user.IsDeleted
                && user.CreatedAtUtc >= fromUtc
                && user.CreatedAtUtc < toExclusiveUtc
                && user.AccountStatus == AccountStatus.Pending
                && (user.Role == UserRole.Doctor || user.Role == UserRole.Company),
            cancellationToken);
        var pendingFiles = await context.StoredFiles.AsNoTracking().CountAsync(
            file => file.DeletedAtUtc == null
                && file.CreatedAtUtc >= fromUtc
                && file.CreatedAtUtc < toExclusiveUtc
                && file.ReviewStatus == StoredFileReviewStatus.Pending,
            cancellationToken);
        var pendingCampaigns = await context.Campaigns.AsNoTracking().CountAsync(
            campaign => !campaign.IsDeleted
                && (campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc) >= fromUtc
                && (campaign.SubmittedAtUtc ?? campaign.CreatedAtUtc) < toExclusiveUtc
                && campaign.Status == CampaignStatus.PendingReview,
            cancellationToken);
        var pendingWithdrawals = await context.WithdrawalRequests.AsNoTracking().CountAsync(
            request => request.RequestedAtUtc >= fromUtc
                && request.RequestedAtUtc < toExclusiveUtc
                && request.Status == WithdrawalRequestStatus.Requested,
            cancellationToken);
        var reviewCounts = new Dictionary<string, int>
        {
            ["PendingAccounts"] = pendingAccounts,
            ["PendingFiles"] = pendingFiles,
            ["PendingCampaigns"] = pendingCampaigns,
            ["PendingWithdrawals"] = pendingWithdrawals
        };

        var campaignRows = await context.Campaigns
            .AsNoTracking()
            .Where(campaign => !campaign.IsDeleted
                && campaign.CreatedAtUtc >= fromUtc
                && campaign.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(campaign => campaign.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var campaignCounts = campaignRows.ToDictionary(item => item.Status.ToString(), item => item.Count);

        var deliveryRows = await context.DoctorAdDeliveries
            .AsNoTracking()
            .Where(delivery => delivery.DeliveryDateEgypt >= dateRange.FromDateEgypt && delivery.DeliveryDateEgypt <= dateRange.ToDateEgypt)
            .GroupBy(delivery => delivery.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var deliveryCounts = deliveryRows.ToDictionary(item => item.Status.ToString(), item => item.Count);

        var interactionRows = await context.DeliveryInteractions
            .AsNoTracking()
            .Where(interaction => interaction.CreatedAtUtc >= fromUtc && interaction.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(interaction => interaction.Outcome)
            .Select(group => new { Outcome = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var interactionCounts = interactionRows.ToDictionary(item => item.Outcome.ToString(), item => item.Count);

        var withdrawalRows = await context.WithdrawalRequests
            .AsNoTracking()
            .Where(request => request.RequestedAtUtc >= fromUtc && request.RequestedAtUtc < toExclusiveUtc)
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var withdrawalCounts = withdrawalRows.ToDictionary(item => item.Status.ToString(), item => item.Count);

        var pricingRows = await context.DoctorPriceHistories
            .AsNoTracking()
            .Where(history => history.CreatedAtUtc >= fromUtc && history.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(history => history.PricingIsActive)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var pricingCounts = pricingRows.ToDictionary(
            item => item.Status ? "ActivePriceChanges" : "PricingDeactivations",
            item => item.Count);
        pricingCounts["PlatformFeePolicyChanges"] = await context.PlatformFeePolicyHistories.AsNoTracking().CountAsync(
            history => history.CreatedAtUtc >= fromUtc && history.CreatedAtUtc < toExclusiveUtc,
            cancellationToken);

        var enforcementRows = await context.DoctorEnforcementActions
            .AsNoTracking()
            .Where(action => action.EffectiveAtUtc >= fromUtc && action.EffectiveAtUtc < toExclusiveUtc)
            .GroupBy(action => action.ActionType)
            .Select(group => new { ActionType = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var enforcementCounts = enforcementRows.ToDictionary(item => item.ActionType.ToString(), item => item.Count);

        return new AdminStatisticsSourceCounts(
            accountCounts,
            reviewCounts,
            campaignCounts,
            deliveryCounts,
            interactionCounts,
            withdrawalCounts,
            pricingCounts,
            enforcementCounts);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> GetWalletMovementSummaryAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        var (fromUtc, toExclusiveUtc) = ToUtcBounds(dateRange);
        var rows = await context.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.CreatedAtUtc >= fromUtc && transaction.CreatedAtUtc < toExclusiveUtc)
            .GroupBy(transaction => transaction.OperationType)
            .Select(group => new { Operation = group.Key, Amount = group.Sum(transaction => transaction.Amount) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(item => item.Operation.ToString(), item => item.Amount);
    }

    public async Task<bool> HasFinancialEvidenceMismatchAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default)
    {
        var (fromUtc, toExclusiveUtc) = ToUtcBounds(dateRange);
        var walletTransactions = await context.WalletTransactions
            .AsNoTracking()
            .Where(transaction => transaction.CreatedAtUtc >= fromUtc
                && transaction.CreatedAtUtc < toExclusiveUtc)
            .Select(transaction => new { transaction.Id, transaction.OperationType, transaction.Amount })
            .ToListAsync(cancellationToken);
        if (walletTransactions.Count == 0)
        {
            return false;
        }

        var transactionIds = walletTransactions.Select(transaction => transaction.Id).ToArray();
        var ledgerRows = await context.WalletLedgerEntries
            .AsNoTracking()
            .Where(entry => transactionIds.Contains(entry.WalletTransactionId))
            .GroupBy(entry => entry.WalletTransactionId)
            .Select(group => new
            {
                TransactionId = group.Key,
                Count = group.Count(),
                Amount = group.Sum(entry => entry.Amount)
            })
            .ToDictionaryAsync(item => item.TransactionId, item => new { item.Count, item.Amount }, cancellationToken);

        foreach (var transaction in walletTransactions)
        {
            if (!ledgerRows.TryGetValue(transaction.Id, out var ledger))
            {
                return true;
            }

            var expectedCount = ExpectsTwoLedgerEntries(transaction.OperationType) ? 2 : 1;
            var expectedAmount = transaction.Amount * expectedCount;
            if (ledger.Count != expectedCount || ledger.Amount != expectedAmount)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExpectsTwoLedgerEntries(WalletTransactionType operationType)
        => operationType is WalletTransactionType.Reserve
            or WalletTransactionType.Release
            or WalletTransactionType.WithdrawalHold
            or WalletTransactionType.WithdrawalRelease;

    private static (DateTime FromUtc, DateTime ToExclusiveUtc) ToUtcBounds(AdminStatisticsDateRange dateRange)
    {
        var egyptTimeZone = ResolveEgyptTimeZone();
        var fromEgypt = DateTime.SpecifyKind(dateRange.FromDateEgypt.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var toExclusiveEgypt = DateTime.SpecifyKind(dateRange.ToDateEgypt.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (
            TimeZoneInfo.ConvertTimeToUtc(fromEgypt, egyptTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(toExclusiveEgypt, egyptTimeZone));
    }

    private static TimeZoneInfo ResolveEgyptTimeZone()
    {
        foreach (var id in new[] { "Egypt Standard Time", "Africa/Cairo" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }
}
