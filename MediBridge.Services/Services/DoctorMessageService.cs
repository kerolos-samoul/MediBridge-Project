using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Messaging;

namespace MediBridge.Services.Services;

public sealed class DoctorMessageService : IDoctorMessageService
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 100;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly IFileWorkflowService fileWorkflowService;
    private readonly DoctorInteractionRequestValidator interactionRequestValidator;
    private readonly DoctorInteractionIdempotency interactionIdempotency;

    public DoctorMessageService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IEgyptBusinessClock businessClock,
        IFileWorkflowService fileWorkflowService,
        DoctorInteractionRequestValidator interactionRequestValidator,
        DoctorInteractionIdempotency interactionIdempotency)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.businessClock = businessClock;
        this.fileWorkflowService = fileWorkflowService;
        this.interactionRequestValidator = interactionRequestValidator;
        this.interactionIdempotency = interactionIdempotency;
    }

    public async Task<TodayInboxDto> GetTodayInboxAsync(
        string actorUserId,
        int? pageSize = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var take = pageSize ?? DefaultPageSize;
        if (take is < 1 or > MaximumPageSize)
        {
            throw new Phase7BadRequestException("Page size must be between 1 and 100.");
        }

        var doctorId = await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        var snapshot = businessClock.Capture();
        var after = DecodeCursor(cursor, doctorId, snapshot.BusinessDateEgypt);
        var rows = await domainUnitOfWork.Deliveries.ListTodayPageAsync(
            doctorId,
            snapshot.BusinessDateEgypt,
            after,
            take + 1,
            cancellationToken);

        var page = rows.Take(take).ToArray();
        var assets = await domainUnitOfWork.Deliveries.ListApprovedAssetsAsync(
            page.Select(row => row.CampaignId).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        var assetsByCampaign = assets
            .GroupBy(asset => asset.CampaignId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ApprovedDeliveryAssetReadModel>)group
                .OrderBy(asset => asset.FileId, StringComparer.Ordinal)
                .ToArray(), StringComparer.Ordinal);

        var items = page.Select(row => new TodayMessageDto(
            row.DeliveryId,
            row.CampaignId,
            row.Status,
            row.DeliveryDateEgypt,
            AsUtc(row.DeliveredAtUtc),
            row.Title,
            row.Description,
            row.ClinicalResearchInfo,
            assetsByCampaign.TryGetValue(row.CampaignId, out var campaignAssets)
                ? campaignAssets.Select(asset => new DeliveryAssetDto(
                    asset.FileId,
                    asset.Purpose,
                    asset.OriginalFileName,
                    asset.ContentType,
                    asset.SizeBytes,
                    asset.ReviewStatus,
                    $"/api/doctor/messages/{Uri.EscapeDataString(row.DeliveryId)}/assets/{Uri.EscapeDataString(asset.FileId)}/access"))
                    .ToArray()
                : Array.Empty<DeliveryAssetDto>()))
            .ToArray();

        var nextCursor = rows.Count > take && page.Length > 0
            ? EncodeCursor(new TodayInboxCursorPayload(
                doctorId,
                snapshot.BusinessDateEgypt,
                AsUtc(page[^1].DeliveredAtUtc),
                page[^1].DeliveryId))
            : null;

        return new TodayInboxDto(snapshot.BusinessDateEgypt, items, nextCursor);
    }

    public async Task<DeliveryAssetAccessGrantDto> CreateDeliveryAssetAccessGrantAsync(
        string actorUserId,
        string deliveryId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId) || string.IsNullOrWhiteSpace(fileId))
        {
            throw new Phase7NotFoundException("Delivery asset was not found.");
        }

        await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        return await fileWorkflowService.CreateDeliveryAssetAccessGrantAsync(
            actorUserId,
            deliveryId,
            fileId,
            cancellationToken);
    }

    public async Task<ReadTrackingResultDto> MarkReadAsync(
        string actorUserId,
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            throw new Phase7NotFoundException("Delivery was not found.");
        }

        var doctorId = await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        var snapshot = businessClock.Capture();

        var result = await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var delivery = await domainUnitOfWork.Deliveries.FindOwnedCurrentDayForReadAsync(
                doctorId,
                deliveryId,
                snapshot.BusinessDateEgypt,
                transactionCancellationToken);
            if (delivery is null)
            {
                throw new Phase7NotFoundException("Delivery was not found.");
            }

            var read = await domainUnitOfWork.Deliveries.TryMarkReadAsync(
                delivery.Id,
                snapshot.UtcNow,
                transactionCancellationToken);

            return read ?? throw new Phase7NotFoundException("Delivery was not found.");
        }, cancellationToken);

        return new ReadTrackingResultDto(result.DeliveryId, result.Status, AsUtc(result.ReadAtUtc), result.AlreadyRead);
    }

    public async Task<DoctorInteractionResultDto> InteractAsync(
        string actorUserId,
        string deliveryId,
        string? idempotencyKey,
        DoctorInteractionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            throw new Phase7NotFoundException("Delivery was not found.");
        }

        var normalized = interactionRequestValidator.NormalizeAndValidate(request);
        var doctorId = await ResolveApprovedDoctorIdAsync(actorUserId, cancellationToken);
        var material = interactionIdempotency.Create(
            doctorId,
            deliveryId,
            normalized.Outcome,
            normalized.Feedback,
            idempotencyKey);
        var snapshot = businessClock.Capture();

        try
        {
            var result = await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
            {
                var existingForKey = await domainUnitOfWork.DeliveryInteractions.FindByIdempotencyHashAsync(
                    doctorId,
                    material.IdempotencyKeyHash,
                    transactionCancellationToken);
                if (existingForKey is not null)
                {
                    if (!string.Equals(existingForKey.RequestFingerprint, material.RequestFingerprint, StringComparison.Ordinal))
                    {
                        throw new InteractionConflictException("idempotency-fingerprint-conflict");
                    }

                    var keyReplay = await FindCompletedSettledInteractionReplayAsync(
                        doctorId,
                        existingForKey.DeliveryId,
                        transactionCancellationToken);
                    if (keyReplay is null)
                    {
                        throw new SettlementAnomalyException("missing-settled-replay");
                    }

                    return ToInteractionResult(keyReplay, replayed: true);
                }

                var settledReplay = await FindCompletedSettledInteractionReplayAsync(
                    doctorId,
                    deliveryId,
                    transactionCancellationToken);
                if (settledReplay is not null)
                {
                    if (string.Equals(settledReplay.RequestFingerprint, material.RequestFingerprint, StringComparison.Ordinal))
                    {
                        return ToInteractionResult(settledReplay, replayed: true);
                    }

                    throw new InteractionConflictException("settled-delivery-conflict");
                }

                var delivery = await domainUnitOfWork.Deliveries.FindOwnedActiveReservedForInteractionAsync(
                    doctorId,
                    deliveryId,
                    snapshot.BusinessDateEgypt,
                    transactionCancellationToken);
                if (delivery is null)
                {
                    settledReplay = await FindCompletedSettledInteractionReplayAsync(
                        doctorId,
                        deliveryId,
                        transactionCancellationToken);
                    if (settledReplay is not null)
                    {
                        if (string.Equals(settledReplay.RequestFingerprint, material.RequestFingerprint, StringComparison.Ordinal))
                        {
                            return ToInteractionResult(settledReplay, replayed: true);
                        }

                        throw new InteractionConflictException("settled-delivery-conflict");
                    }

                    throw new Phase7NotFoundException("Delivery was not found.");
                }

                ValidateSettlementSnapshots(delivery);

                var companyWallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
                    WalletOwnerType.Company,
                    delivery.CompanyId,
                    transactionCancellationToken);
                if (!DoctorInteractionSettlementGuard.TryValidateCompanyReservedWallet(
                        companyWallet,
                        delivery.ReservedAmount,
                        out var companyWalletAnomaly))
                {
                    throw new SettlementAnomalyException(companyWalletAnomaly);
                }
                var lockedCompanyWallet = companyWallet!;

                var doctorWallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
                    Guid.NewGuid().ToString("N"),
                    WalletOwnerType.Doctor,
                    doctorId,
                    actorUserId,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                if (!DoctorInteractionSettlementGuard.TryValidateDoctorWallet(doctorWallet, out var doctorWalletAnomaly))
                {
                    throw new SettlementAnomalyException(doctorWalletAnomaly);
                }

                var chargeKey = DeliveryFinancialOperationKeys.ForCharge(delivery.Id);
                var earnKey = DeliveryFinancialOperationKeys.ForEarn(delivery.Id);
                if (await domainUnitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(WalletTransactionType.Charge, chargeKey, transactionCancellationToken)
                    || await domainUnitOfWork.WalletTransactions.IdempotencyKeyExistsAsync(WalletTransactionType.Earn, earnKey, transactionCancellationToken))
                {
                    throw new SettlementAnomalyException("financial-replay-incomplete");
                }

                var chargeTransactionId = Guid.NewGuid().ToString("N");
                var earnTransactionId = Guid.NewGuid().ToString("N");
                var auditEventId = Guid.NewGuid().ToString("N");
                var feedbackQualityStatus = normalized.Feedback is null
                    ? (FeedbackQualityStatus?)null
                    : normalized.FeedbackQualifiesForScore ? FeedbackQualityStatus.Accepted : FeedbackQualityStatus.Pending;

                await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(
                    lockedCompanyWallet.Id,
                    -delivery.ReservedAmount,
                    snapshot.UtcNow,
                    transactionCancellationToken);
                await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(
                    doctorWallet.Id,
                    delivery.DoctorEarnings,
                    snapshot.UtcNow,
                    transactionCancellationToken);

                await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
                {
                    Id = chargeTransactionId,
                    WalletId = lockedCompanyWallet.Id,
                    OperationType = WalletTransactionType.Charge,
                    IdempotencyKey = chargeKey,
                    Amount = delivery.ReservedAmount,
                    RelatedDeliveryId = delivery.Id,
                    Description = "Doctor message interaction charge.",
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);
                await domainUnitOfWork.WalletTransactions.AddTransactionAsync(new WalletTransaction
                {
                    Id = earnTransactionId,
                    WalletId = doctorWallet.Id,
                    OperationType = WalletTransactionType.Earn,
                    IdempotencyKey = earnKey,
                    Amount = delivery.DoctorEarnings,
                    RelatedDeliveryId = delivery.Id,
                    Description = "Doctor message interaction earning.",
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);

                await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    WalletTransactionId = chargeTransactionId,
                    WalletId = lockedCompanyWallet.Id,
                    Direction = WalletLedgerEntryDirection.Debit,
                    BalanceType = WalletBalanceType.Reserved,
                    Amount = delivery.ReservedAmount,
                    Currency = "EGP",
                    CampaignId = delivery.CampaignId,
                    MessageDeliveryId = delivery.Id,
                    DoctorId = doctorId,
                    CompanyId = delivery.CompanyId,
                    IdempotencyKey = chargeKey,
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);
                await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(new WalletLedgerEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    WalletTransactionId = earnTransactionId,
                    WalletId = doctorWallet.Id,
                    Direction = WalletLedgerEntryDirection.Credit,
                    BalanceType = WalletBalanceType.Available,
                    Amount = delivery.DoctorEarnings,
                    Currency = "EGP",
                    CampaignId = delivery.CampaignId,
                    MessageDeliveryId = delivery.Id,
                    DoctorId = doctorId,
                    CompanyId = delivery.CompanyId,
                    IdempotencyKey = earnKey,
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);

                await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                    auditEventId,
                    "Phase8InteractionSettlementSucceeded",
                    actorUserId,
                    "Doctor",
                    AuditTargetType.Delivery,
                    delivery.Id,
                    AuditOutcome.Success,
                    normalized.Outcome.ToString(),
                    null,
                    JsonSerializer.Serialize(new
                    {
                        category = "settlement-succeeded",
                        outcome = normalized.Outcome.ToString(),
                        chargeAmount = delivery.ReservedAmount,
                        earnAmount = delivery.DoctorEarnings,
                        feeAmount = delivery.PlatformFeeAmount
                    }),
                    snapshot.UtcNow,
                    transactionCancellationToken);

                await domainUnitOfWork.Deliveries.TryMarkInteractedAndChargedAsync(
                    delivery.Id,
                    normalized.Outcome,
                    snapshot.UtcNow,
                    normalized.Feedback,
                    feedbackQualityStatus,
                    transactionCancellationToken);

                await domainUnitOfWork.DeliveryInteractions.AddInteractionAsync(new DeliveryInteraction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    DeliveryId = delivery.Id,
                    DoctorId = doctorId,
                    ActorUserId = actorUserId,
                    Outcome = normalized.Outcome,
                    IdempotencyKeyHash = material.IdempotencyKeyHash,
                    RequestFingerprint = material.RequestFingerprint,
                    FeedbackText = normalized.Feedback,
                    FeedbackQualifiesForScore = normalized.FeedbackQualifiesForScore,
                    ChargeTransactionId = chargeTransactionId,
                    EarnTransactionId = earnTransactionId,
                    AuditEventId = auditEventId,
                    CreatedAtUtc = snapshot.UtcNow
                }, transactionCancellationToken);

                return new DoctorInteractionResultDto(
                    delivery.Id,
                    normalized.Outcome == DeliveryInteractionOutcome.Accept ? DeliveryStatus.Accepted : DeliveryStatus.Rejected,
                    ReservationStatus.Charged,
                    snapshot.UtcNow,
                    delivery.ReadAtUtc,
                    normalized.Feedback is not null,
                    normalized.FeedbackQualifiesForScore,
                    delivery.ReservedAmount,
                    delivery.DoctorEarnings,
                    delivery.PlatformFeeAmount,
                    Replayed: false);
            }, cancellationToken);

            return result;
        }
        catch (InteractionConflictException exception)
        {
            await RecordInteractionAuditAsync(
                "Phase8InteractionIdempotencyConflict",
                AuditOutcome.Denied,
                actorUserId,
                doctorId,
                deliveryId,
                exception.Category,
                cancellationToken);
            throw new Phase7ConflictException("Interaction request conflicts with an existing settlement.");
        }
        catch (SettlementAnomalyException exception)
        {
            await RecordInteractionAuditAsync(
                "Phase8InteractionSettlementAnomaly",
                AuditOutcome.Denied,
                actorUserId,
                doctorId,
                deliveryId,
                exception.Category,
                cancellationToken);
            throw new Phase7StorageUnavailableException("Settlement is unavailable.", exception);
        }
        catch (Exception exception) when (IsUniqueOrConcurrencyFailure(exception))
        {
            var replay = await FindCompletedSettledInteractionReplayAsync(doctorId, deliveryId, cancellationToken);
            if (replay is not null && string.Equals(replay.RequestFingerprint, material.RequestFingerprint, StringComparison.Ordinal))
            {
                return ToInteractionResult(replay, replayed: true);
            }

            await RecordInteractionAuditAsync(
                "Phase8InteractionIdempotencyConflict",
                AuditOutcome.Denied,
                actorUserId,
                doctorId,
                deliveryId,
                "concurrent-settlement-conflict",
                cancellationToken);
            throw new Phase7ConflictException("Interaction request conflicts with an existing settlement.");
        }
    }

    private async Task<string> ResolveApprovedDoctorIdAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase7ForbiddenException("Doctor access is required.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindDoctorProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is null
            || profile is null
            || user.Role != UserRole.Doctor
            || user.AccountStatus != AccountStatus.Approved
            || user.IsDeleted
            || profile.IsDeleted
            || profile.Status != DoctorMarketplaceStatus.Active)
        {
            throw new Phase7ForbiddenException("Doctor access is required.");
        }

        return profile.Id;
    }

    private static TodayDeliveryCursor? DecodeCursor(string? cursor, string doctorId, DateOnly businessDateEgypt)
    {
        if (cursor is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cursor))
        {
            throw new Phase7BadRequestException("Cursor is invalid.");
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(cursor));
            var payload = JsonSerializer.Deserialize<TodayInboxCursorPayload>(json)
                ?? throw new JsonException("Cursor payload is missing.");
            if (!string.Equals(payload.DoctorId, doctorId, StringComparison.Ordinal)
                || payload.BusinessDateEgypt != businessDateEgypt
                || payload.DeliveredAtUtc.Kind != DateTimeKind.Utc
                || string.IsNullOrWhiteSpace(payload.DeliveryId))
            {
                throw new Phase7BadRequestException("Cursor is invalid.");
            }

            return new TodayDeliveryCursor(payload.DeliveredAtUtc, payload.DeliveryId);
        }
        catch (Phase7BadRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new Phase7BadRequestException("Cursor is invalid.");
        }
    }

    private static string EncodeCursor(TodayInboxCursorPayload payload)
    {
        return Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        return Convert.FromBase64String(base64);
    }

    private async Task RecordInteractionAuditAsync(
        string eventType,
        AuditOutcome outcome,
        string actorUserId,
        string doctorId,
        string deliveryId,
        string category,
        CancellationToken cancellationToken)
    {
        var snapshot = businessClock.Capture();
        await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                eventType,
                actorUserId,
                "Doctor",
                AuditTargetType.Delivery,
                deliveryId,
                outcome,
                category,
                null,
                JsonSerializer.Serialize(new
                {
                    category,
                    actor = doctorId
                }),
                snapshot.UtcNow,
                transactionCancellationToken);
            return true;
        }, cancellationToken);
    }

    private static void ValidateSettlementSnapshots(DoctorAdDelivery delivery)
    {
        if (!DoctorInteractionSettlementGuard.TryValidateSnapshots(delivery, out var category))
        {
            throw new SettlementAnomalyException(category);
        }
    }

    private static DoctorInteractionResultDto ToInteractionResult(InteractionReplayReadModel replay, bool replayed)
    {
        return new DoctorInteractionResultDto(
            replay.DeliveryId,
            replay.Status,
            replay.ReservationStatus,
            AsUtc(replay.InteractedAtUtc),
            replay.ReadAtUtc is null ? null : AsUtc(replay.ReadAtUtc.Value),
            replay.FeedbackText is not null,
            replay.FeedbackQualifiesForScore,
            replay.ChargeAmount,
            replay.DoctorEarnings,
            replay.PlatformFeeAmount,
            replayed);
    }

    private async Task<InteractionReplayReadModel?> FindCompletedSettledInteractionReplayAsync(
        string doctorId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var replay = await domainUnitOfWork.Deliveries.FindSettledInteractionReplayAsync(
            doctorId,
            deliveryId,
            cancellationToken);
        if (replay is null)
        {
            return null;
        }

        return await HasCompleteFinancialReplayAsync(replay, cancellationToken)
            ? replay
            : throw new SettlementAnomalyException("financial-replay-incomplete");
    }

    private async Task<bool> HasCompleteFinancialReplayAsync(
        InteractionReplayReadModel replay,
        CancellationToken cancellationToken)
    {
        var chargeKey = DeliveryFinancialOperationKeys.ForCharge(replay.DeliveryId);
        var earnKey = DeliveryFinancialOperationKeys.ForEarn(replay.DeliveryId);
        var charge = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
            WalletTransactionType.Charge,
            chargeKey,
            cancellationToken);
        var earn = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
            WalletTransactionType.Earn,
            earnKey,
            cancellationToken);

        if (charge is null
            || earn is null
            || charge.RelatedDeliveryId != replay.DeliveryId
            || earn.RelatedDeliveryId != replay.DeliveryId
            || charge.Amount != replay.ChargeAmount
            || earn.Amount != replay.DoctorEarnings)
        {
            return false;
        }

        var chargeEntries = await domainUnitOfWork.WalletLedgerEntries.ListLedgerEntriesByWalletTransactionAsync(
            charge.Id,
            cancellationToken);
        var earnEntries = await domainUnitOfWork.WalletLedgerEntries.ListLedgerEntriesByWalletTransactionAsync(
            earn.Id,
            cancellationToken);

        return chargeEntries.Any(entry => IsMatchingReplayLedgerEntry(
                entry,
                replay.DeliveryId,
                chargeKey,
                WalletBalanceType.Reserved,
                WalletLedgerEntryDirection.Debit,
                replay.ChargeAmount))
            && earnEntries.Any(entry => IsMatchingReplayLedgerEntry(
                entry,
                replay.DeliveryId,
                earnKey,
                WalletBalanceType.Available,
                WalletLedgerEntryDirection.Credit,
                replay.DoctorEarnings));
    }

    private static bool IsMatchingReplayLedgerEntry(
        WalletLedgerEntry entry,
        string deliveryId,
        string idempotencyKey,
        WalletBalanceType balanceType,
        WalletLedgerEntryDirection direction,
        decimal amount)
    {
        return entry.MessageDeliveryId == deliveryId
            && entry.IdempotencyKey == idempotencyKey
            && entry.BalanceType == balanceType
            && entry.Direction == direction
            && entry.Amount == amount
            && entry.Currency == "EGP";
    }

    private static bool IsUniqueOrConcurrencyFailure(Exception exception)
    {
        var typeName = exception.GetType().Name;
        return typeName.Contains("DbUpdate", StringComparison.Ordinal)
            || typeName.Contains("Concurrency", StringComparison.Ordinal);
    }

    private sealed class InteractionConflictException : Exception
    {
        public InteractionConflictException(string category) : base("Interaction conflict.")
        {
            Category = category;
        }

        public string Category { get; }
    }

    private sealed class SettlementAnomalyException : Exception
    {
        public SettlementAnomalyException(string category) : base("Interaction settlement anomaly.")
        {
            Category = category;
        }

        public string Category { get; }
    }
}
