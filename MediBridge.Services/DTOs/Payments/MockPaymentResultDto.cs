using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Payments;

public sealed record MockPaymentResultDto(
    [property: JsonPropertyName("paymentId")] string PaymentId,
    [property: JsonPropertyName("companyId")] string CompanyId,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc,
    [property: JsonPropertyName("transactionReference")] string TransactionReference,
    [property: JsonPropertyName("walletBalanceBefore")] decimal WalletBalanceBefore,
    [property: JsonPropertyName("walletBalanceAfter")] decimal WalletBalanceAfter);
