using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Interfaces.Policies;
using MediBridge.Core.Interfaces.Messaging;
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

    public async Task AddDoctorPricingDeactivationHistoryAsync(
        string historyId,
        string doctorId,
        decimal? previousPricePerMessage,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await context.DoctorPriceHistories.AddAsync(new DoctorPriceHistory
        {
            Id = historyId,
            DoctorId = doctorId,
            PreviousPricePerMessage = previousPricePerMessage is null ? null : MoneyRules.EnsureValid(previousPricePerMessage.Value, nameof(previousPricePerMessage)),
            NewPricePerMessage = null,
            PricingIsActive = false,
            ChangedByAdminUserId = adminUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Reason is required.", nameof(reason)) : reason.Trim()
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDoctorPricingDeactivationHistoryIdsAsync(string doctorId, CancellationToken cancellationToken = default)
    {
        return await context.DoctorPriceHistories
            .Where(history => history.DoctorId == doctorId && !history.PricingIsActive)
            .OrderBy(history => history.CreatedAtUtc)
            .Select(history => history.Id)
            .ToListAsync(cancellationToken);
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
            PricingIsActive = true,
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
        await AddPlatformFeePolicyHistoryAsync(historyId, feePercent, effectiveFromUtc, adminUserId, reason: null, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddPlatformFeePolicyHistoryAsync(
        string historyId,
        decimal feePercent,
        DateTime effectiveFromUtc,
        string adminUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        await AddPlatformFeePolicyHistoryAsync(historyId, feePercent, effectiveFromUtc, adminUserId, reason, correctsHistoryId: null, cancellationToken);
    }

    public async Task AddPlatformFeePolicyHistoryCorrectionAsync(string historyId, string correctsHistoryId, decimal feePercent, DateTime effectiveFromUtc, string adminUserId, CancellationToken cancellationToken = default)
    {
        await AddPlatformFeePolicyHistoryAsync(historyId, feePercent, effectiveFromUtc, adminUserId, reason: null, correctsHistoryId, cancellationToken);
    }

    private async Task AddPlatformFeePolicyHistoryAsync(
        string historyId,
        decimal feePercent,
        DateTime effectiveFromUtc,
        string adminUserId,
        string? reason,
        string? correctsHistoryId,
        CancellationToken cancellationToken)
    {
        if (feePercent <= 0m || feePercent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(feePercent), feePercent, "Platform fee percent must be greater than zero and at most 100.");
        }

        await context.PlatformFeePolicyHistories.AddAsync(new PlatformFeePolicyHistory
        {
            Id = historyId,
            FeePercent = feePercent,
            EffectiveFromUtc = effectiveFromUtc,
            ChangedByAdminUserId = adminUserId,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CorrectsHistoryId = correctsHistoryId
        }, cancellationToken);
    }

    public Task<int> CloseEffectivePlatformFeePoliciesAsync(
        DateTime effectiveAtUtc,
        DateTime effectiveToUtc,
        CancellationToken cancellationToken = default)
    {
        if (effectiveAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The effective policy instant must be UTC.", nameof(effectiveAtUtc));
        }

        if (effectiveToUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The closing instant must be UTC.", nameof(effectiveToUtc));
        }

        return context.PlatformFeePolicyHistories
            .Where(history => history.EffectiveFromUtc <= effectiveAtUtc
                && (history.EffectiveToUtc == null || effectiveAtUtc < history.EffectiveToUtc))
            .ExecuteUpdateAsync(setters => setters.SetProperty(history => history.EffectiveToUtc, effectiveToUtc), cancellationToken);
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

    public async Task<EffectivePlatformFeePolicyReadModel?> FindSingleEffectivePlatformFeePolicyAsync(
        DateTime effectiveAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (effectiveAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The effective policy instant must be UTC.", nameof(effectiveAtUtc));
        }

        var matches = await context.PlatformFeePolicyHistories
            .AsNoTracking()
            .Where(history => history.EffectiveFromUtc <= effectiveAtUtc
                && (history.EffectiveToUtc == null || effectiveAtUtc < history.EffectiveToUtc))
            .OrderBy(history => history.EffectiveFromUtc)
            .ThenBy(history => history.Id)
            .Take(2)
            .Select(history => new EffectivePlatformFeePolicyReadModel(history.Id, history.FeePercent))
            .ToListAsync(cancellationToken);

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new EffectivePolicyConflictException("Multiple platform fee policies overlap at the requested instant.")
        };
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
