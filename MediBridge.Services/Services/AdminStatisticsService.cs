using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Admin;

namespace MediBridge.Services.Services;

public sealed class AdminStatisticsService : IAdminStatisticsService
{
    private readonly IAdminStatisticsRepository repository;
    private readonly AdminStatisticsDateRangeValidator dateRangeValidator;

    public AdminStatisticsService(
        IAdminStatisticsRepository repository,
        AdminStatisticsDateRangeValidator dateRangeValidator)
    {
        this.repository = repository;
        this.dateRangeValidator = dateRangeValidator;
    }

    public async Task<AdminStatisticsDto> GetAsync(string? fromDateEgypt, string? toDateEgypt, CancellationToken cancellationToken = default)
    {
        var dateRange = dateRangeValidator.Resolve(fromDateEgypt, toDateEgypt);
        var sourceCounts = await repository.GetSourceCountsAsync(dateRange, cancellationToken);
        var hasMismatch = await repository.HasFinancialEvidenceMismatchAsync(dateRange, cancellationToken);
        var walletSummary = hasMismatch
            ? null
            : await repository.GetWalletMovementSummaryAsync(dateRange, cancellationToken);
        var withheldScopes = hasMismatch ? new[] { "WalletMovementSummary" } : Array.Empty<string>();

        return AdminToolsDtoMapper.ToStatistics(new AdminStatisticsReadModel(
            dateRange.FromDateEgypt,
            dateRange.ToDateEgypt,
            sourceCounts.AccountCounts,
            sourceCounts.ReviewCounts,
            sourceCounts.CampaignCounts,
            sourceCounts.DeliveryOutcomeCounts,
            sourceCounts.InteractionOutcomeCounts,
            sourceCounts.WithdrawalStatusCounts,
            walletSummary,
            sourceCounts.PricingPolicyCounts.ToDictionary(item => item.Key, item => (object)item.Value),
            sourceCounts.EnforcementActionCounts,
            withheldScopes));
    }
}
