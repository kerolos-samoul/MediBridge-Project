using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Policies;

public sealed class PolicyHistoryRepository : IPolicyHistoryRepository
{
    private readonly MediBridgeDbContext context;

    public PolicyHistoryRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddDoctorPriceHistoryAsync(string historyId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default)
    {
        await AddDoctorPriceHistoryAsync(historyId, doctorId, previousPricePerMessage, newPricePerMessage, adminUserId, reason: null, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddDoctorPriceHistoryAsync(
        string historyId,
        string doctorId,
        decimal? previousPricePerMessage,
        decimal? newPricePerMessage,
        string adminUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        await AddDoctorPriceHistoryAsync(historyId, doctorId, previousPricePerMessage, newPricePerMessage, adminUserId, reason, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddDoctorPriceHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal? previousPricePerMessage, decimal? newPricePerMessage, string adminUserId, CancellationToken cancellationToken = default)
    {
        await AddDoctorPriceHistoryAsync(historyId, doctorId, previousPricePerMessage, newPricePerMessage, adminUserId, reason: null, correctsHistoryId, cancellationToken);
    }

    private async Task AddDoctorPriceHistoryAsync(
        string historyId,
        string doctorId,
        decimal? previousPricePerMessage,
        decimal? newPricePerMessage,
        string adminUserId,
        string? reason,
        string? correctsHistoryId,
        CancellationToken cancellationToken)
    {
        await context.DoctorPriceHistories.AddAsync(new DoctorPriceHistory
        {
            Id = historyId,
            DoctorId = doctorId,
            PreviousPricePerMessage = previousPricePerMessage is null ? null : MoneyRules.EnsureValid(previousPricePerMessage.Value, nameof(previousPricePerMessage)),
            NewPricePerMessage = newPricePerMessage is null ? null : MoneyRules.EnsureValid(newPricePerMessage.Value, nameof(newPricePerMessage)),
            ChangedByAdminUserId = adminUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CorrectsHistoryId = correctsHistoryId
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDoctorPriceHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return await context.DoctorPriceHistories
            .Where(history => history.DoctorId == doctorId)
            .OrderBy(history => history.CreatedAtUtc)
            .Select(history => history.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddPlatformFeePolicyHistoryAsync(string historyId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default)
    {
        await AddPlatformFeePolicyHistoryAsync(historyId, feePercent, effectiveFromUtc, adminUserId, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddPlatformFeePolicyHistoryCorrectionAsync(string historyId, string correctsHistoryId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default)
    {
        await AddPlatformFeePolicyHistoryAsync(historyId, feePercent, effectiveFromUtc, adminUserId, correctsHistoryId, cancellationToken);
    }

    private async Task AddPlatformFeePolicyHistoryAsync(string historyId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, string? correctsHistoryId, CancellationToken cancellationToken)
    {
        if (feePercent < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(feePercent), feePercent, "Platform fee percent cannot be negative.");
        }

        await context.PlatformFeePolicyHistories.AddAsync(new PlatformFeePolicyHistory
        {
            Id = historyId,
            FeePercent = feePercent,
            EffectiveFromUtc = effectiveFromUtc,
            ChangedByAdminUserId = adminUserId,
            CorrectsHistoryId = correctsHistoryId
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListPlatformFeePolicyHistoryIdsAsync(DateTime? effectiveAtUtc = null, CancellationToken cancellationToken = default)
    {
        var query = context.PlatformFeePolicyHistories.AsQueryable();
        if (effectiveAtUtc is not null)
        {
            query = query.Where(history => history.EffectiveFromUtc <= effectiveAtUtc && (history.EffectiveToUtc == null || history.EffectiveToUtc >= effectiveAtUtc));
        }

        return await query
            .OrderBy(history => history.EffectiveFromUtc)
            .Select(history => history.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task AddActivityScoreHistoryAsync(string historyId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default)
    {
        await AddActivityScoreHistoryAsync(historyId, doctorId, activityScore, windowStartDateEgypt, windowEndDateEgypt, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddActivityScoreHistoryCorrectionAsync(string historyId, string correctsHistoryId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, CancellationToken cancellationToken = default)
    {
        await AddActivityScoreHistoryAsync(historyId, doctorId, activityScore, windowStartDateEgypt, windowEndDateEgypt, correctsHistoryId, cancellationToken);
    }

    private async Task AddActivityScoreHistoryAsync(string historyId, string doctorId, decimal activityScore, DateOnly windowStartDateEgypt, DateOnly windowEndDateEgypt, string? correctsHistoryId, CancellationToken cancellationToken)
    {
        if (activityScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(activityScore), activityScore, "Activity score must be between 0 and 100.");
        }

        await context.ActivityScoreHistories.AddAsync(new ActivityScoreHistory
        {
            Id = historyId,
            DoctorId = doctorId,
            ActivityScore = activityScore,
            WindowStartDateEgypt = windowStartDateEgypt,
            WindowEndDateEgypt = windowEndDateEgypt,
            CorrectsHistoryId = correctsHistoryId
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListActivityScoreHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return await context.ActivityScoreHistories
            .Where(history => history.DoctorId == doctorId)
            .OrderBy(history => history.CreatedAtUtc)
            .Select(history => history.Id)
            .ToListAsync(cancellationToken);
    }
}
