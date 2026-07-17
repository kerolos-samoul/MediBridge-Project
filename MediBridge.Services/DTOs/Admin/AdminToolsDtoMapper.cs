using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.DTOs.Wallets;

namespace MediBridge.Services.DTOs.Admin;

public static class AdminToolsDtoMapper
{
    public static PageMetadataDto ToPageMetadata<T>(AdminPageReadModel<T> page)
        => new(page.PageNumber, page.PageSize, page.TotalCount, page.TotalPages, page.HasPreviousPage, page.HasNextPage);

    public static AdminWorkQueuePageDto ToWorkQueuePage(AdminWorkQueuePageReadModel model)
        => new(
            ToPageMetadata(model.Page),
            model.CategoryCounts.ToDictionary(item => item.Key.ToString(), item => item.Value),
            model.Page.Items.Select(ToWorkQueueItem).ToArray());

    public static AdminWorkQueueItemDto ToWorkQueueItem(AdminWorkQueueItemReadModel model)
        => new(
            model.ItemId,
            model.Category.ToString(),
            model.Status,
            model.UrgencyRank,
            model.SubmittedAtUtc,
            model.OwnerType?.ToString(),
            model.OwnerId,
            model.OwnerDisplay,
            model.Summary,
            model.NextActions,
            model.SensitiveFlags);

    public static AdminWithdrawalDto ToAdminWithdrawal(AdminWithdrawalReadModel model)
        => new(
            model.WithdrawalId,
            model.DoctorId,
            model.DoctorPublicId,
            model.Amount,
            model.Currency,
            model.Status,
            model.RequestedAtUtc,
            model.ReviewedAtUtc,
            model.DecisionReason,
            model.PayoutReference,
            model.PayoutStatusChangedAtUtc);

    public static DoctorWithdrawalDto ToDoctorWithdrawal(AdminWithdrawalReadModel model)
        => new(
            model.WithdrawalId,
            model.Amount,
            model.Currency,
            model.Status,
            model.RequestedAtUtc,
            model.ReviewedAtUtc,
            model.DecisionReason,
            model.PayoutReference,
            model.PayoutStatusChangedAtUtc);

    public static DoctorPricingDto ToDoctorPricing(AdminDoctorPricingReadModel model)
        => new(model.DoctorId, model.PricingIsActive ? model.PricePerMessage : null, model.PricingIsActive, model.UpdatedAtUtc);

    public static AdminStatisticsDto ToStatistics(AdminStatisticsReadModel model)
        => new(
            model.FromDateEgypt,
            model.ToDateEgypt,
            model.AccountCounts,
            model.ReviewCounts,
            model.CampaignCounts,
            model.DeliveryOutcomeCounts,
            model.InteractionOutcomeCounts,
            model.WithdrawalStatusCounts,
            model.WalletMovementSummary,
            model.PricingPolicySummary,
            model.EnforcementActionCounts,
            model.WithheldFinancialScopes);
}
