using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Wallets;

public static class CompanyWalletTopUpValidation
{
    public static string NormalizeIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new Phase5ValidationException("Validation failed.", ["Idempotency-Key is required."]);
        }

        var normalized = idempotencyKey.Trim();
        if (normalized.Length is < 8 or > 128)
        {
            throw new Phase5ValidationException("Validation failed.", ["Idempotency-Key length must be between 8 and 128 characters."]);
        }

        return normalized;
    }
}
