using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Admin;

public enum AdminWorkQueueCategory
{
    Account = 1,
    File = 2,
    Campaign = 3,
    Enforcement = 4,
    Withdrawal = 5
}

public enum AdminWorkQueueOwnerType
{
    Doctor = 1,
    Company = 2,
    Campaign = 3,
    System = 4
}

public sealed record AdminPageReadModel<T>(
    int PageNumber,
    int PageSize,
    int TotalCount,
    IReadOnlyList<T> Items)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed record AdminWorkQueueItemReadModel(
    string ItemId,
    AdminWorkQueueCategory Category,
    string Status,
    int UrgencyRank,
    DateTime SubmittedAtUtc,
    AdminWorkQueueOwnerType? OwnerType,
    string? OwnerId,
    string? OwnerDisplay,
    string Summary,
    IReadOnlyList<string> NextActions,
    IReadOnlyList<string> SensitiveFlags);

public sealed record AdminWorkQueuePageReadModel(
    AdminPageReadModel<AdminWorkQueueItemReadModel> Page,
    IReadOnlyDictionary<AdminWorkQueueCategory, int> CategoryCounts);

public sealed record AdminWithdrawalReadModel(
    string WithdrawalId,
    string DoctorId,
    string? DoctorPublicId,
    decimal Amount,
    string Currency,
    WithdrawalRequestStatus Status,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? DecisionReason,
    string? PayoutReference,
    DateTime? PayoutStatusChangedAtUtc);

public sealed record AdminPayoutReadModel(
    string WithdrawalId,
    WithdrawalRequestStatus Status,
    string? PayoutReference,
    string? FailureReason,
    string? ChangedByAdminUserId,
    DateTime? ChangedAtUtc);

public sealed record AdminDoctorPricingReadModel(
    string DoctorId,
    decimal? PricePerMessage,
    bool PricingIsActive,
    DateTime? UpdatedAtUtc);

public sealed record AdminStatisticsReadModel(
    DateOnly FromDateEgypt,
    DateOnly ToDateEgypt,
    IReadOnlyDictionary<string, int> AccountCounts,
    IReadOnlyDictionary<string, int> ReviewCounts,
    IReadOnlyDictionary<string, int> CampaignCounts,
    IReadOnlyDictionary<string, int> DeliveryOutcomeCounts,
    IReadOnlyDictionary<string, int> InteractionOutcomeCounts,
    IReadOnlyDictionary<string, int> WithdrawalStatusCounts,
    IReadOnlyDictionary<string, decimal>? WalletMovementSummary,
    IReadOnlyDictionary<string, object> PricingPolicySummary,
    IReadOnlyDictionary<string, int> EnforcementActionCounts,
    IReadOnlyList<string> WithheldFinancialScopes);

public sealed record AdminStatisticsSourceCounts(
    IReadOnlyDictionary<string, int> AccountCounts,
    IReadOnlyDictionary<string, int> ReviewCounts,
    IReadOnlyDictionary<string, int> CampaignCounts,
    IReadOnlyDictionary<string, int> DeliveryOutcomeCounts,
    IReadOnlyDictionary<string, int> InteractionOutcomeCounts,
    IReadOnlyDictionary<string, int> WithdrawalStatusCounts,
    IReadOnlyDictionary<string, int> PricingPolicyCounts,
    IReadOnlyDictionary<string, int> EnforcementActionCounts);

public sealed record AdminWithdrawalTransitionResult(
    AdminWithdrawalReadModel Withdrawal,
    bool WalletEffectApplied);
