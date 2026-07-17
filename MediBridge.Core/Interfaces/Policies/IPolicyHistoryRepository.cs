namespace MediBridge.Core.Interfaces.Policies;

using MediBridge.Core.Interfaces.Messaging;

public sealed class EffectivePolicyConflictException : InvalidOperationException
{
    public EffectivePolicyConflictException(string message)
        : base(message)
    {
    }
}

public interface IPolicyHistoryRepository
{
    Task AddDoctorPriceHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default);
    Task AddDoctorPriceHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, string? reason, CancellationToken cancellationToken = default);
    Task AddDoctorPricingDeactivationHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, string adminUserId, string reason, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDoctorPricingDeactivationHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default);
    Task AddDoctorPriceHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListDoctorPriceHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default);
    Task AddPlatformFeePolicyHistoryAsync(string historyId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default);
    Task AddPlatformFeePolicyHistoryAsync(string historyId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, string? reason, CancellationToken cancellationToken = default);
    Task AddPlatformFeePolicyHistoryCorrectionAsync(string historyId, string correctsHistoryId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default);
    Task<int> CloseEffectivePlatformFeePoliciesAsync(DateTime effectiveAtUtc, DateTime effectiveToUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListPlatformFeePolicyHistoryIdsAsync(DateTime? effectiveAtUtc = null, CancellationToken cancellationToken = default);
    Task AddActivityScoreHistoryAsync(string historyId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default);
    Task AddActivityScoreHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListActivityScoreHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default);
    Task<EffectivePlatformFeePolicyReadModel?> FindSingleEffectivePlatformFeePolicyAsync(DateTime effectiveAtUtc, CancellationToken cancellationToken = default);
}
