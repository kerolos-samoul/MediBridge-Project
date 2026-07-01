using System.Text.Json.Serialization;

namespace MediBridge.Services.DTOs.Wallets;

public sealed record CompanyWalletDto(
    [property: JsonPropertyName("walletId")]
    string WalletId,
    [property: JsonPropertyName("companyId")]
    string CompanyId,
    [property: JsonPropertyName("availableBalance")]
    decimal AvailableBalance,
    [property: JsonPropertyName("reservedBalance")]
    decimal ReservedBalance,
    [property: JsonPropertyName("currency")]
    string Currency);
