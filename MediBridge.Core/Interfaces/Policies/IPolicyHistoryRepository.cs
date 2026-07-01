namespace MediBridge.Core.Interfaces.Policies;

public interface IPolicyHistoryRepository
{
    Task AddDoctorPriceHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default);
    Task AddDoctorPriceHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, string? reason, CancellationToken cancellationToken = default);
    Task AddDoctorPriceHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDoctorPriceHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default);
    Task AddPlatformFeePolicyHistoryAsync(string historyId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default);
    Task AddPlatformFeePolicyHistoryCorrectionAsync(string historyId, string correctsHistoryId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPlatformFeePolicyHistoryIdsAsync(DateTime? effectiveAtUtc = null, CancellationToken cancellationToken = default);
    Task AddActivityScoreHistoryAsync(string historyId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default);
    Task AddActivityScoreHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActivityScoreHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default);
}
