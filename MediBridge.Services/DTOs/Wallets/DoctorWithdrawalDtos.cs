using System.Text.Json.Serialization;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.DTOs.Wallets;

public sealed record CreateWithdrawalRequestDto(
    [property: JsonPropertyName("amount")] decimal? Amount);

public sealed record DoctorWithdrawalDto(
    [property: JsonPropertyName("withdrawalId")] string WithdrawalId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("status")] WithdrawalRequestStatus Status,
    [property: JsonPropertyName("requestedAtUtc")] DateTime RequestedAtUtc,
    [property: JsonPropertyName("reviewedAtUtc")] DateTime? ReviewedAtUtc,
    [property: JsonPropertyName("decisionReason")] string? DecisionReason,
    [property: JsonPropertyName("payoutReference")] string? PayoutReference,
    [property: JsonPropertyName("payoutStatusChangedAtUtc")] DateTime? PayoutStatusChangedAtUtc);

public sealed record DoctorWithdrawalPageDto(
    [property: JsonPropertyName("page")] PageMetadataDto Page,
    [property: JsonPropertyName("items")] IReadOnlyList<DoctorWithdrawalDto> Items);
