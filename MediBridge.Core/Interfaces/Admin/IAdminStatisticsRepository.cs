namespace MediBridge.Core.Interfaces.Admin;

public interface IAdminStatisticsRepository
{
    Task<AdminStatisticsSourceCounts> GetSourceCountsAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, decimal>> GetWalletMovementSummaryAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default);

    Task<bool> HasFinancialEvidenceMismatchAsync(
        AdminStatisticsDateRange dateRange,
        CancellationToken cancellationToken = default);
}
