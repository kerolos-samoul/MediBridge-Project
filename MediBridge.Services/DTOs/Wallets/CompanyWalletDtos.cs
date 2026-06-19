using MediBridge.Core.Enums;

namespace MediBridge.Services.DTOs.Wallets;

public sealed class CompanyWalletQueryRequestDto
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class CompanyWalletDto
{
    public string WalletId { get; set; } = string.Empty;
    public decimal AvailableBalance { get; set; }
    public decimal ReservedBalance { get; set; }
    public string Currency { get; set; } = "EGP";
    public CompanyWalletTransactionPageDto Transactions { get; set; } = new();
}

public sealed class CompanyWalletTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public WalletTransactionType OperationType { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EGP";
    public DateTime CreatedAtUtc { get; set; }
    public string? Description { get; set; }
}

public sealed class CompanyWalletTransactionPageDto
{
    public CompanyWalletPageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<CompanyWalletTransactionDto> Items { get; set; } = Array.Empty<CompanyWalletTransactionDto>();
}

public sealed class CompanyWalletPageMetadataDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}

public sealed class TopUpCompanyWalletRequestDto
{
    public decimal Amount { get; set; }
    public string? Description { get; set; }
}

public sealed class TopUpCompanyWalletResultDto
{
    public string WalletId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public decimal AvailableBalance { get; set; }
    public decimal ReservedBalance { get; set; }
    public string Currency { get; set; } = "EGP";
    public string IdempotencyStatus { get; set; } = "Created";
}
