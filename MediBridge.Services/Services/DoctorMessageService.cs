using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Messaging;
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
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IEgyptBusinessClock businessClock;
    private readonly IFileWorkflowService fileWorkflowService;

    public DoctorMessageService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IEgyptBusinessClock businessClock,
        IFileWorkflowService fileWorkflowService)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.businessClock = businessClock;
        this.fileWorkflowService = fileWorkflowService;
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

    public async Task<MarkReadResultDto> MarkDeliveryReadAsync(
        string actorUserId,
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            throw new Phase8NotFoundException("Delivery was not found.");
        }

        var doctorId = await ResolveApprovedDoctorIdForPhase8Async(actorUserId, cancellationToken);
        var snapshot = businessClock.Capture();

        return await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var delivery = await domainUnitOfWork.Deliveries.FindCurrentOwnedForReadForUpdateAsync(
                doctorId,
                deliveryId,
                snapshot.BusinessDateEgypt,
                transactionCancellationToken);
            if (delivery is null)
            {
                throw new Phase8NotFoundException("Delivery was not found.");
            }

            var created = delivery.ReadAtUtc is null;
            delivery.MarkRead(snapshot.UtcNow);
            var readAtUtc = delivery.ReadAtUtc
                ?? throw new Phase8ConsistencyException("Read tracking could not be completed.");
            var outcome = created ? InteractionPaymentResultStatus.Created : InteractionPaymentResultStatus.Replayed;

            await AddReadAuditAsync(
                actorUserId,
                doctorId,
                delivery.Id,
                delivery.CampaignId,
                outcome,
                readAtUtc,
                snapshot.UtcNow,
                transactionCancellationToken);
            await domainUnitOfWork.SaveChangesAsync(transactionCancellationToken);

            return new MarkReadResultDto
            {
                DeliveryId = delivery.Id,
                ReadAtUtc = AsUtc(readAtUtc),
                ReadStatus = outcome
            };
        }, cancellationToken);
    }

    public async Task<InteractDeliveryResultDto> InteractWithDeliveryAsync(
        string actorUserId,
        string deliveryId,
        string? idempotencyKey,
        InteractDeliveryRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            throw new Phase8NotFoundException("Delivery was not found.");
        }

        if (request is null)
        {
            throw new Phase8BadRequestException("Interaction request body is required.");
        }

        var normalizedKey = InteractionPaymentValidation.NormalizeIdempotencyKey(idempotencyKey);
        var decision = InteractionPaymentValidation.ParseDecision(request.Decision);
        var feedbackText = InteractionPaymentValidation.NormalizeFeedback(request.FeedbackText);
        var finalStatus = ToFinalDeliveryStatus(decision);
        var doctorId = await ResolveApprovedDoctorIdForPhase8Async(actorUserId, cancellationToken);
        var snapshot = businessClock.Capture();

        var outcome = await domainUnitOfWork.ExecuteIsolatedInTransactionAsync(async transactionCancellationToken =>
        {
            var delivery = await domainUnitOfWork.Deliveries.FindActiveReservedForInteractionForUpdateAsync(
                doctorId,
                deliveryId,
                snapshot.BusinessDateEgypt,
                transactionCancellationToken);

            if (delivery is null)
            {
                return await ClassifyMissingActiveDeliveryAsync(
                    actorUserId,
                    doctorId,
                    deliveryId,
                    normalizedKey,
                    decision,
                    finalStatus,
                    feedbackText,
                    snapshot.BusinessDateEgypt,
                    snapshot.UtcNow,
                    transactionCancellationToken);
            }

            var existingOperation = await domainUnitOfWork.DeliveryInteractionOperations.FindForUpdateAsync(
                doctorId,
                delivery.Id,
                normalizedKey,
                transactionCancellationToken);
            if (existingOperation is not null)
            {
                if (!SamePayload(existingOperation, decision, feedbackText))
                {
                    await AddInteractionAuditAsync(
                        actorUserId,
                        doctorId,
                        delivery.Id,
                        delivery.CampaignId,
                        delivery.CompanyId,
                        decision,
                        "Phase8DeliveryInteractionConflict",
                        AuditOutcome.Denied,
                        "Interaction replay conflicted with the original payload.",
                        snapshot.UtcNow,
                        transactionCancellationToken);
                    return InteractionSettlementOutcome.Conflict();
                }

                if (existingOperation.Status == DeliveryInteractionOperationStatus.Succeeded)
                {
                    return InteractionSettlementOutcome.ConsistencyFailure();
                }
            }

            if (!HasValidMonetarySnapshots(delivery))
            {
                await AddConsistencyAuditAsync(actorUserId, doctorId, delivery, decision, "InvalidMonetarySnapshot", snapshot.UtcNow, transactionCancellationToken);
                return InteractionSettlementOutcome.ConsistencyFailure();
            }

            var companyWallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
                WalletOwnerType.Company,
                delivery.CompanyId,
                transactionCancellationToken);
            if (companyWallet is null || !string.Equals(companyWallet.Currency, "EGP", StringComparison.Ordinal))
            {
                await AddConsistencyAuditAsync(actorUserId, doctorId, delivery, decision, "MissingCompanyWallet", snapshot.UtcNow, transactionCancellationToken);
                return InteractionSettlementOutcome.ConsistencyFailure();
            }

            if (companyWallet.ReservedBalance < delivery.ReservedAmount)
            {
                await AddConsistencyAuditAsync(actorUserId, doctorId, delivery, decision, "CompanyReservedInsufficient", snapshot.UtcNow, transactionCancellationToken);
                return InteractionSettlementOutcome.ConsistencyFailure();
            }

            var doctorWallet = await domainUnitOfWork.Wallets.FindActiveWalletForUpdateByOwnerAsync(
                WalletOwnerType.Doctor,
                doctorId,
                transactionCancellationToken);
            if (doctorWallet is null || !string.Equals(doctorWallet.Currency, "EGP", StringComparison.Ordinal))
            {
                await AddConsistencyAuditAsync(actorUserId, doctorId, delivery, decision, "MissingDoctorWallet", snapshot.UtcNow, transactionCancellationToken);
                return InteractionSettlementOutcome.ConsistencyFailure();
            }

            var chargeKey = DeliveryFinancialOperationKeys.ForCharge(delivery.Id);
            var earnKey = DeliveryFinancialOperationKeys.ForEarn(delivery.Id);
            var existingCharge = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
                WalletTransactionType.Charge,
                chargeKey,
                transactionCancellationToken);
            var existingEarn = await domainUnitOfWork.WalletTransactions.FindTransactionByIdempotencyAsync(
                WalletTransactionType.Earn,
                earnKey,
                transactionCancellationToken);
            if (existingCharge is not null || existingEarn is not null)
            {
                await AddConsistencyAuditAsync(actorUserId, doctorId, delivery, decision, "UnexpectedExistingFinancialEvidence", snapshot.UtcNow, transactionCancellationToken);
                return InteractionSettlementOutcome.ConsistencyFailure();
            }

            var operation = existingOperation ?? DeliveryInteractionOperation.Create(
                doctorId,
                delivery.Id,
                normalizedKey,
                decision,
                feedbackText,
                snapshot.UtcNow);
            if (existingOperation is null)
            {
                await domainUnitOfWork.DeliveryInteractionOperations.AddAsync(operation, transactionCancellationToken);
            }

            delivery.MarkInteracted(finalStatus, snapshot.UtcNow, feedbackText);
            await domainUnitOfWork.Wallets.StageReservedBalanceChangeAsync(
                companyWallet.Id,
                -delivery.ReservedAmount,
                snapshot.UtcNow,
                transactionCancellationToken);
            await domainUnitOfWork.Wallets.StageAvailableBalanceChangeAsync(
                doctorWallet.Id,
                delivery.DoctorEarnings,
                snapshot.UtcNow,
                transactionCancellationToken);

            var chargeTransactionId = Guid.NewGuid().ToString("N");
            var earnTransactionId = Guid.NewGuid().ToString("N");
            await domainUnitOfWork.WalletTransactions.AddTransactionAsync(
                CreateInteractionTransaction(
                    chargeTransactionId,
                    companyWallet.Id,
                    WalletTransactionType.Charge,
                    chargeKey,
                    delivery.ReservedAmount,
                    delivery.Id,
                    "Delivery interaction charge settled.",
                    snapshot.UtcNow),
                transactionCancellationToken);
            await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(
                CreateInteractionLedgerEntry(
                    chargeTransactionId,
                    companyWallet.Id,
                    WalletLedgerEntryDirection.Debit,
                    WalletBalanceType.Reserved,
                    delivery.ReservedAmount,
                    delivery,
                    chargeKey,
                    snapshot.UtcNow),
                transactionCancellationToken);
            await domainUnitOfWork.WalletTransactions.AddTransactionAsync(
                CreateInteractionTransaction(
                    earnTransactionId,
                    doctorWallet.Id,
                    WalletTransactionType.Earn,
                    earnKey,
                    delivery.DoctorEarnings,
                    delivery.Id,
                    "Delivery interaction earning settled.",
                    snapshot.UtcNow),
                transactionCancellationToken);
            await domainUnitOfWork.WalletLedgerEntries.AddLedgerEntryAsync(
                CreateInteractionLedgerEntry(
                    earnTransactionId,
                    doctorWallet.Id,
                    WalletLedgerEntryDirection.Credit,
                    WalletBalanceType.Available,
                    delivery.DoctorEarnings,
                    delivery,
                    earnKey,
                    snapshot.UtcNow),
                transactionCancellationToken);

            operation.MarkSucceeded(chargeTransactionId, earnTransactionId, snapshot.UtcNow);
            await AddInteractionAuditAsync(
                actorUserId,
                doctorId,
                delivery.Id,
                delivery.CampaignId,
                delivery.CompanyId,
                decision,
                "Phase8DeliveryInteractionSettled",
                AuditOutcome.Success,
                "Delivery interaction settled.",
                snapshot.UtcNow,
                transactionCancellationToken);

            return InteractionSettlementOutcome.Success(new InteractDeliveryResultDto
            {
                DeliveryId = delivery.Id,
                Status = finalStatus.ToString(),
                InteractedAtUtc = AsUtc(snapshot.UtcNow),
                FeedbackText = feedbackText,
                IdempotencyStatus = InteractionPaymentResultStatus.Created
            });
        }, cancellationToken);

        if (outcome.Error is not null)
        {
            throw outcome.Error;
        }

        return outcome.Result ?? throw new Phase8ConsistencyException("Interaction settlement could not be completed.");
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

    private async Task<string> ResolveApprovedDoctorIdForPhase8Async(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase8ForbiddenException("Doctor access is required.");
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
            throw new Phase8ForbiddenException("Doctor access is required.");
        }

        return profile.Id;
    }

    private Task AddReadAuditAsync(
        string actorUserId,
        string doctorId,
        string deliveryId,
        string campaignId,
        string outcome,
        DateTime readAtUtc,
        DateTime auditCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            outcome == InteractionPaymentResultStatus.Created ? "Phase8DeliveryReadRecorded" : "Phase8DeliveryReadReplayed",
            actorUserId,
            UserRole.Doctor.ToString(),
            AuditTargetType.Delivery,
            deliveryId,
            AuditOutcome.Success,
            outcome == InteractionPaymentResultStatus.Created ? "Delivery read timestamp recorded." : "Existing delivery read timestamp replayed.",
            correlationId: null,
            JsonSerializer.Serialize(new
            {
                ActorUserId = actorUserId,
                DoctorId = doctorId,
                DeliveryId = deliveryId,
                CampaignId = campaignId,
                Outcome = outcome,
                ReadAtUtc = readAtUtc
            }, AuditJsonOptions),
            auditCreatedAtUtc,
            cancellationToken);
    }

    private async Task<InteractionSettlementOutcome> ClassifyMissingActiveDeliveryAsync(
        string actorUserId,
        string doctorId,
        string deliveryId,
        string normalizedKey,
        DeliveryInteractionDecision decision,
        DeliveryStatus requestedFinalStatus,
        string? feedbackText,
        DateOnly businessDateEgypt,
        DateTime auditCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        var existingOperation = await domainUnitOfWork.DeliveryInteractionOperations.FindForUpdateAsync(
            doctorId,
            deliveryId,
            normalizedKey,
            cancellationToken);
        var settled = await domainUnitOfWork.Deliveries.FindSettledOwnedInteractionAsync(
            doctorId,
            deliveryId,
            businessDateEgypt,
            cancellationToken);

        if (existingOperation is not null)
        {
            if (!SamePayload(existingOperation, decision, feedbackText))
            {
                await AddInteractionAuditAsync(
                    actorUserId,
                    doctorId,
                    deliveryId,
                    settled?.DeliveryId ?? deliveryId,
                    string.Empty,
                    decision,
                    "Phase8DeliveryInteractionConflict",
                    AuditOutcome.Denied,
                    "Interaction replay conflicted with the original payload.",
                    auditCreatedAtUtc,
                    cancellationToken);
                return InteractionSettlementOutcome.Conflict();
            }

            if (existingOperation.Status == DeliveryInteractionOperationStatus.Succeeded && settled is not null)
            {
                await AddInteractionAuditAsync(
                    actorUserId,
                    doctorId,
                    settled.DeliveryId,
                    settled.DeliveryId,
                    string.Empty,
                    decision,
                    "Phase8DeliveryInteractionReplayed",
                    AuditOutcome.Success,
                    "Existing delivery interaction settlement replayed.",
                    auditCreatedAtUtc,
                    cancellationToken);
                return InteractionSettlementOutcome.Success(ToReplayDto(settled));
            }

            return InteractionSettlementOutcome.ConsistencyFailure();
        }

        if (settled is null)
        {
            return InteractionSettlementOutcome.NotFound();
        }

        if (settled.Status != requestedFinalStatus)
        {
            await AddInteractionAuditAsync(
                actorUserId,
                doctorId,
                settled.DeliveryId,
                settled.DeliveryId,
                string.Empty,
                decision,
                "Phase8DeliveryInteractionConflict",
                AuditOutcome.Denied,
                "Interaction request conflicted with the settled delivery decision.",
                auditCreatedAtUtc,
                cancellationToken);
            return InteractionSettlementOutcome.Conflict();
        }

        await AddInteractionAuditAsync(
            actorUserId,
            doctorId,
            settled.DeliveryId,
            settled.DeliveryId,
            string.Empty,
            decision,
            "Phase8DeliveryInteractionReplayed",
            AuditOutcome.Success,
            "Existing delivery interaction settlement replayed.",
            auditCreatedAtUtc,
            cancellationToken);
        return InteractionSettlementOutcome.Success(ToReplayDto(settled));
    }

    private Task AddConsistencyAuditAsync(
        string actorUserId,
        string doctorId,
        DoctorAdDelivery delivery,
        DeliveryInteractionDecision decision,
        string category,
        DateTime auditCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        return AddInteractionAuditAsync(
            actorUserId,
            doctorId,
            delivery.Id,
            delivery.CampaignId,
            delivery.CompanyId,
            decision,
            "Phase8DeliveryInteractionConsistencyFailure",
            AuditOutcome.Denied,
            category,
            auditCreatedAtUtc,
            cancellationToken);
    }

    private Task AddInteractionAuditAsync(
        string actorUserId,
        string doctorId,
        string deliveryId,
        string campaignId,
        string companyId,
        DeliveryInteractionDecision decision,
        string eventType,
        AuditOutcome outcome,
        string reason,
        DateTime auditCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        return domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            Guid.NewGuid().ToString("N"),
            eventType,
            actorUserId,
            UserRole.Doctor.ToString(),
            AuditTargetType.Delivery,
            deliveryId,
            outcome,
            reason,
            correlationId: null,
            JsonSerializer.Serialize(new
            {
                ActorUserId = actorUserId,
                DoctorId = doctorId,
                DeliveryId = deliveryId,
                CampaignId = campaignId,
                CompanyId = companyId,
                Decision = decision.ToString(),
                Outcome = eventType,
                CreatedAtUtc = auditCreatedAtUtc
            }, AuditJsonOptions),
            auditCreatedAtUtc,
            cancellationToken);
    }

    private static InteractDeliveryResultDto ToReplayDto(DeliveryInteractionSettlementResultReadModel settled)
    {
        return new InteractDeliveryResultDto
        {
            DeliveryId = settled.DeliveryId,
            Status = settled.Status.ToString(),
            InteractedAtUtc = AsUtc(settled.InteractedAtUtc),
            FeedbackText = settled.FeedbackText,
            IdempotencyStatus = InteractionPaymentResultStatus.Replayed
        };
    }

    private static WalletTransaction CreateInteractionTransaction(
        string id,
        string walletId,
        WalletTransactionType operationType,
        string operationKey,
        decimal amount,
        string deliveryId,
        string description,
        DateTime createdAtUtc)
    {
        return new WalletTransaction
        {
            Id = id,
            WalletId = walletId,
            OperationType = operationType,
            IdempotencyKey = operationKey,
            Amount = amount,
            RelatedDeliveryId = deliveryId,
            Description = description,
            Metadata = JsonSerializer.Serialize(new
            {
                DeliveryId = deliveryId,
                Operation = operationType.ToString()
            }, AuditJsonOptions),
            CreatedAtUtc = createdAtUtc
        };
    }

    private static WalletLedgerEntry CreateInteractionLedgerEntry(
        string transactionId,
        string walletId,
        WalletLedgerEntryDirection direction,
        WalletBalanceType balanceType,
        decimal amount,
        DoctorAdDelivery delivery,
        string operationKey,
        DateTime createdAtUtc)
    {
        return new WalletLedgerEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletTransactionId = transactionId,
            WalletId = walletId,
            Direction = direction,
            BalanceType = balanceType,
            Amount = amount,
            Currency = "EGP",
            CampaignId = delivery.CampaignId,
            MessageDeliveryId = delivery.Id,
            DoctorId = delivery.DoctorId,
            CompanyId = delivery.CompanyId,
            IdempotencyKey = operationKey,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static bool HasValidMonetarySnapshots(DoctorAdDelivery delivery)
    {
        return IsPositiveTwoDecimalMoney(delivery.ReservedAmount)
            && IsPositiveTwoDecimalMoney(delivery.PricePerMessageSnapshot)
            && IsPositiveTwoDecimalMoney(delivery.PlatformFeeAmount)
            && IsPositiveTwoDecimalMoney(delivery.DoctorEarnings)
            && delivery.ReservedAmount == delivery.PricePerMessageSnapshot
            && delivery.PlatformFeeAmount + delivery.DoctorEarnings == delivery.PricePerMessageSnapshot;
    }

    private static bool IsPositiveTwoDecimalMoney(decimal value)
    {
        return value > 0m && decimal.Round(value, 2, MidpointRounding.AwayFromZero) == value;
    }

    private static bool SamePayload(DeliveryInteractionOperation operation, DeliveryInteractionDecision decision, string? feedbackText)
    {
        return operation.Decision == decision && string.Equals(operation.FeedbackText, feedbackText, StringComparison.Ordinal);
    }

    private static DeliveryStatus ToFinalDeliveryStatus(DeliveryInteractionDecision decision)
    {
        return decision switch
        {
            DeliveryInteractionDecision.Accept => DeliveryStatus.Accepted,
            DeliveryInteractionDecision.Reject => DeliveryStatus.Rejected,
            _ => throw new Phase8BadRequestException("Decision must be Accept or Reject.")
        };
    }

    private sealed record InteractionSettlementOutcome(
        InteractDeliveryResultDto? Result,
        Phase8InteractionException? Error)
    {
        public static InteractionSettlementOutcome Success(InteractDeliveryResultDto result) => new(result, null);

        public static InteractionSettlementOutcome Conflict() => new(null, new Phase8ConflictException("Interaction request conflicts with the existing settlement."));

        public static InteractionSettlementOutcome NotFound() => new(null, new Phase8NotFoundException("Delivery was not found."));

        public static InteractionSettlementOutcome ConsistencyFailure() => new(null, new Phase8ConsistencyException("Interaction settlement could not be completed."));
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
}
