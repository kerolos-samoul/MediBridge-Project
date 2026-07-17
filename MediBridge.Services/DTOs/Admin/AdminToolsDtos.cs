using System.Text.Json.Serialization;
using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Admin;

public sealed record PageMetadataDto(
    [property: JsonPropertyName("pageNumber")] int PageNumber,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalCount")] int TotalCount,
    [property: JsonPropertyName("totalPages")] int TotalPages,
    [property: JsonPropertyName("hasPreviousPage")] bool HasPreviousPage,
    [property: JsonPropertyName("hasNextPage")] bool HasNextPage);

public sealed record AdminWorkQueueItemDto(
    [property: JsonPropertyName("itemId")] string ItemId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("urgencyRank")] int UrgencyRank,
    [property: JsonPropertyName("submittedAtUtc")] DateTime SubmittedAtUtc,
    [property: JsonPropertyName("ownerType")] string? OwnerType,
    [property: JsonPropertyName("ownerId")] string? OwnerId,
    [property: JsonPropertyName("ownerDisplay")] string? OwnerDisplay,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("nextActions")] IReadOnlyList<string> NextActions,
    [property: JsonPropertyName("sensitiveFlags")] IReadOnlyList<string> SensitiveFlags);

public sealed record AdminWorkQueuePageDto(
    [property: JsonPropertyName("page")] PageMetadataDto Page,
    [property: JsonPropertyName("categoryCounts")] IReadOnlyDictionary<string, int> CategoryCounts,
    [property: JsonPropertyName("items")] IReadOnlyList<AdminWorkQueueItemDto> Items);

public sealed record AdminWithdrawalDto(
    [property: JsonPropertyName("withdrawalId")] string WithdrawalId,
    [property: JsonPropertyName("doctorId")] string DoctorId,
    [property: JsonPropertyName("doctorPublicId")] string? DoctorPublicId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] WithdrawalRequestStatus Status,
    [property: JsonPropertyName("requestedAtUtc")] DateTime RequestedAtUtc,
    [property: JsonPropertyName("reviewedAtUtc")] DateTime? ReviewedAtUtc,
    [property: JsonPropertyName("decisionReason")] string? DecisionReason,
    [property: JsonPropertyName("payoutReference")] string? PayoutReference,
    [property: JsonPropertyName("payoutStatusChangedAtUtc")] DateTime? PayoutStatusChangedAtUtc);

public sealed record AdminWithdrawalPageDto(
    [property: JsonPropertyName("page")] PageMetadataDto Page,
    [property: JsonPropertyName("items")] IReadOnlyList<AdminWithdrawalDto> Items);

public sealed record AdminWithdrawalDecisionRequestDto(
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record MarkWithdrawalPaidRequestDto(
    [property: JsonPropertyName("payoutReference")] string? PayoutReference);

public sealed record MarkWithdrawalFailedRequestDto(
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record DoctorPricingDto(
    [property: JsonPropertyName("doctorId")] string DoctorId,
    [property: JsonPropertyName("pricePerMessage")] decimal? PricePerMessage,
    [property: JsonPropertyName("pricingIsActive")] bool PricingIsActive,
    [property: JsonPropertyName("updatedAtUtc")] DateTime? UpdatedAtUtc);

public sealed record AdminStatisticsDto(
    [property: JsonPropertyName("fromDateEgypt")] DateOnly FromDateEgypt,
    [property: JsonPropertyName("toDateEgypt")] DateOnly ToDateEgypt,
    [property: JsonPropertyName("accountCounts")] IReadOnlyDictionary<string, int> AccountCounts,
    [property: JsonPropertyName("reviewCounts")] IReadOnlyDictionary<string, int> ReviewCounts,
    [property: JsonPropertyName("campaignCounts")] IReadOnlyDictionary<string, int> CampaignCounts,
    [property: JsonPropertyName("deliveryOutcomeCounts")] IReadOnlyDictionary<string, int> DeliveryOutcomeCounts,
    [property: JsonPropertyName("interactionOutcomeCounts")] IReadOnlyDictionary<string, int> InteractionOutcomeCounts,
    [property: JsonPropertyName("withdrawalStatusCounts")] IReadOnlyDictionary<string, int> WithdrawalStatusCounts,
    [property: JsonPropertyName("walletMovementSummary")] IReadOnlyDictionary<string, decimal>? WalletMovementSummary,
    [property: JsonPropertyName("pricingPolicySummary")] IReadOnlyDictionary<string, object> PricingPolicySummary,
    [property: JsonPropertyName("enforcementActionCounts")] IReadOnlyDictionary<string, int> EnforcementActionCounts,
    [property: JsonPropertyName("withheldFinancialScopes")] IReadOnlyList<string> WithheldFinancialScopes);
