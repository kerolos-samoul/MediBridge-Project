using MediBridge.Core.Enums;

namespace MediBridge.Performance;

public sealed record Phase11AdminToolsPerformanceSeed(
    DateOnly FromDateEgypt,
    DateOnly ToDateEgypt,
    int AccountCount,
    int FileCount,
    int CampaignCount,
    int DeliveryCount,
    int InteractionCount,
    int WalletTransactionCount,
    int WithdrawalCount,
    int PolicyHistoryCount,
    int EnforcementActionCount);

public static class Phase11AdminToolsPerformanceSeedFactory
{
    public const int RequiredOperationalRecordCount = 10_000;
    public static readonly DateOnly FromDateEgypt = new(2026, 4, 15);
    public static readonly DateOnly ToDateEgypt = new(2026, 7, 13);

    public static Phase11AdminToolsPerformanceSeed CreateProfile()
        => new(
            FromDateEgypt,
            ToDateEgypt,
            AccountCount: 1_200,
            FileCount: 1_000,
            CampaignCount: 1_200,
            DeliveryCount: 3_200,
            InteractionCount: 1_600,
            WalletTransactionCount: 1_200,
            WithdrawalCount: 400,
            PolicyHistoryCount: 100,
            EnforcementActionCount: 100);

    public static IReadOnlyList<Phase11AccountPerformanceRow> CreateAccounts()
    {
        var accountStatuses = new[]
        {
            AccountStatus.Pending,
            AccountStatus.Approved,
            AccountStatus.Rejected
        };

        var marketplaceStatuses = new[]
        {
            DoctorMarketplaceStatus.Active,
            DoctorMarketplaceStatus.Warned,
            DoctorMarketplaceStatus.Suspended
        };

        return Enumerable.Range(0, 1_200)
            .Select(index => new Phase11AccountPerformanceRow(
                $"perf-user-{index:00000}",
                index % 3 == 0 ? "Company" : "Doctor",
                accountStatuses[index % accountStatuses.Length],
                index % 3 == 0 ? null : marketplaceStatuses[index % marketplaceStatuses.Length],
                FromDateEgypt.AddDays(index % 90)))
            .ToArray();
    }

    public static IReadOnlyList<Phase11FilePerformanceRow> CreateFiles()
    {
        var statuses = new[]
        {
            StoredFileReviewStatus.Pending,
            StoredFileReviewStatus.Approved,
            StoredFileReviewStatus.Rejected
        };

        return Enumerable.Range(0, 1_000)
            .Select(index => new Phase11FilePerformanceRow(
                $"perf-file-{index:00000}",
                statuses[index % statuses.Length],
                FromDateEgypt.AddDays(index % 90)))
            .ToArray();
    }

    public static IReadOnlyList<Phase11CampaignPerformanceRow> CreateCampaigns()
    {
        var statuses = new[]
        {
            CampaignStatus.Draft,
            CampaignStatus.PendingReview,
            CampaignStatus.Approved,
            CampaignStatus.Rejected,
            CampaignStatus.Active,
            CampaignStatus.Completed
        };

        return Enumerable.Range(0, 1_200)
            .Select(index => new Phase11CampaignPerformanceRow(
                $"perf-campaign-{index:00000}",
                $"perf-company-{index % 120:000}",
                statuses[index % statuses.Length],
                FromDateEgypt.AddDays(index % 90)))
            .ToArray();
    }

    public static IReadOnlyList<Phase11DeliveryPerformanceRow> CreateDeliveries()
    {
        var statuses = new[]
        {
            DeliveryStatus.Active,
            DeliveryStatus.Accepted,
            DeliveryStatus.Rejected,
            DeliveryStatus.Expired
        };

        return Enumerable.Range(0, 3_200)
            .Select(index => new Phase11DeliveryPerformanceRow(
                $"perf-delivery-{index:00000}",
                $"perf-campaign-{index % 1_200:00000}",
                $"perf-doctor-{index % 400:000}",
                statuses[index % statuses.Length],
                FromDateEgypt.AddDays(index % 90),
                100m,
                80m,
                20m))
            .ToArray();
    }

    public static IReadOnlyList<Phase11InteractionPerformanceRow> CreateInteractions()
    {
        var outcomes = new[]
        {
            DeliveryInteractionOutcome.Accept,
            DeliveryInteractionOutcome.Reject
        };

        return Enumerable.Range(0, 1_600)
            .Select(index => new Phase11InteractionPerformanceRow(
                $"perf-interaction-{index:00000}",
                $"perf-delivery-{index % 3_200:00000}",
                outcomes[index % outcomes.Length],
                FromDateEgypt.AddDays(index % 90)))
            .ToArray();
    }

    public static IReadOnlyList<Phase11WithdrawalPerformanceRow> CreateWithdrawals()
    {
        var statuses = new[]
        {
            WithdrawalRequestStatus.Requested,
            WithdrawalRequestStatus.Approved,
            WithdrawalRequestStatus.Rejected,
            WithdrawalRequestStatus.Paid,
            WithdrawalRequestStatus.Failed
        };

        return Enumerable.Range(0, 400)
            .Select(index => new Phase11WithdrawalPerformanceRow(
                $"perf-withdrawal-{index:00000}",
                $"perf-doctor-{index % 100:000}",
                statuses[index % statuses.Length],
                FromDateEgypt.AddDays(index % 90),
                100m + index))
            .ToArray();
    }

    public static IReadOnlyList<Phase11WalletTransactionPerformanceRow> CreateWalletTransactions()
    {
        var operations = new[]
        {
            WalletTransactionType.TopUp,
            WalletTransactionType.Reserve,
            WalletTransactionType.Charge,
            WalletTransactionType.Earn,
            WalletTransactionType.WithdrawalHold,
            WalletTransactionType.WithdrawalRelease,
            WalletTransactionType.WithdrawalFinalizePayout
        };

        return Enumerable.Range(0, 1_200)
            .Select(index => new Phase11WalletTransactionPerformanceRow(
                $"perf-wallet-tx-{index:00000}",
                operations[index % operations.Length],
                FromDateEgypt.AddDays(index % 90),
                50m + index % 250))
            .ToArray();
    }

    public static IReadOnlyList<Phase11PolicyHistoryPerformanceRow> CreatePolicyHistories()
        => Enumerable.Range(0, 100)
            .Select(index => new Phase11PolicyHistoryPerformanceRow(
                $"perf-policy-{index:00000}",
                FromDateEgypt.AddDays(index % 90),
                10m + index % 15))
            .ToArray();

    public static IReadOnlyList<Phase11EnforcementActionPerformanceRow> CreateEnforcementActions()
    {
        var actionTypes = new[]
        {
            DoctorEnforcementActionType.Warn,
            DoctorEnforcementActionType.ReduceDailyLimit,
            DoctorEnforcementActionType.Suspend,
            DoctorEnforcementActionType.Reactivate
        };

        return Enumerable.Range(0, 100)
            .Select(index => new Phase11EnforcementActionPerformanceRow(
                $"perf-enforcement-{index:00000}",
                $"perf-doctor-{index % 400:000}",
                actionTypes[index % actionTypes.Length],
                FromDateEgypt.AddDays(index % 90)))
            .ToArray();
    }
}

public sealed record Phase11AccountPerformanceRow(
    string UserId,
    string Role,
    AccountStatus AccountStatus,
    DoctorMarketplaceStatus? MarketplaceStatus,
    DateOnly CreatedDateEgypt);

public sealed record Phase11FilePerformanceRow(
    string FileId,
    StoredFileReviewStatus ReviewStatus,
    DateOnly CreatedDateEgypt);

public sealed record Phase11CampaignPerformanceRow(
    string CampaignId,
    string CompanyId,
    CampaignStatus Status,
    DateOnly CreatedDateEgypt);

public sealed record Phase11DeliveryPerformanceRow(
    string DeliveryId,
    string CampaignId,
    string DoctorId,
    DeliveryStatus Status,
    DateOnly DeliveryDateEgypt,
    decimal PriceSnapshot,
    decimal DoctorEarnings,
    decimal PlatformFee);

public sealed record Phase11InteractionPerformanceRow(
    string InteractionId,
    string DeliveryId,
    DeliveryInteractionOutcome Outcome,
    DateOnly InteractionDateEgypt);

public sealed record Phase11WithdrawalPerformanceRow(
    string WithdrawalId,
    string DoctorId,
    WithdrawalRequestStatus Status,
    DateOnly RequestedDateEgypt,
    decimal Amount);

public sealed record Phase11WalletTransactionPerformanceRow(
    string TransactionId,
    WalletTransactionType OperationType,
    DateOnly CreatedDateEgypt,
    decimal Amount);

public sealed record Phase11PolicyHistoryPerformanceRow(
    string PolicyHistoryId,
    DateOnly EffectiveDateEgypt,
    decimal PlatformFeePercent);

public sealed record Phase11EnforcementActionPerformanceRow(
    string EnforcementActionId,
    string DoctorId,
    DoctorEnforcementActionType ActionType,
    DateOnly EffectiveDateEgypt);
