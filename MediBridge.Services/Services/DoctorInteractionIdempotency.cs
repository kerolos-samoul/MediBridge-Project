using System.Security.Cryptography;
using System.Text;
using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class DoctorInteractionIdempotency
{
    public const int MinimumRawKeyLength = 8;
    public const int MaximumRawKeyLength = 128;

    public DoctorInteractionIdempotencyMaterial Create(
        string doctorId,
        string deliveryId,
        DeliveryInteractionOutcome outcome,
        string? normalizedFeedback,
        string? rawClientKey)
    {
        if (string.IsNullOrWhiteSpace(rawClientKey))
        {
            throw new Phase7BadRequestException("Idempotency-Key is required.");
        }

        var normalizedKey = rawClientKey.Trim();
        if (normalizedKey.Length is < MinimumRawKeyLength or > MaximumRawKeyLength)
        {
            throw new Phase7BadRequestException("Idempotency-Key is invalid.");
        }

        var keyHash = HashJoin("phase8-key-v1", doctorId, normalizedKey);
        var requestFingerprint = HashJoin(
            "phase8-request-v1",
            doctorId,
            deliveryId,
            ((int)outcome).ToString(System.Globalization.CultureInfo.InvariantCulture),
            normalizedFeedback ?? string.Empty);

        return new DoctorInteractionIdempotencyMaterial(keyHash, requestFingerprint);
    }

    private static string HashJoin(params string[] parts)
    {
        var input = string.Join('\u001f', parts);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record DoctorInteractionIdempotencyMaterial(
    string IdempotencyKeyHash,
    string RequestFingerprint);
