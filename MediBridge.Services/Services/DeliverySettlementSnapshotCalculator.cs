using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Services.Services;

public sealed record DeliverySettlementSnapshot(
    decimal PricePerMessage,
    decimal PlatformFeePercent,
    decimal PlatformFeeAmount,
    decimal DoctorEarnings,
    decimal ReservedAmount);

public sealed class DeliverySettlementSnapshotCalculator
{
    public DeliverySettlementSnapshot Calculate(
        decimal pricePerMessage,
        IReadOnlyCollection<EffectivePlatformFeePolicyReadModel> effectivePolicies)
    {
        ArgumentNullException.ThrowIfNull(effectivePolicies);
        if (effectivePolicies.Count != 1)
        {
            throw new InvalidOperationException("Exactly one effective platform fee policy is required.");
        }

        MoneyRules.EnsurePositive(pricePerMessage, nameof(pricePerMessage));
        var percent = effectivePolicies.Single().FeePercent;
        if (percent <= 0m || percent > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(effectivePolicies), percent, "The platform fee percent must be greater than zero and at most 100.");
        }

        var roundedFee = decimal.Round(pricePerMessage * percent / 100m, 2, MidpointRounding.AwayFromZero);
        var earnings = pricePerMessage - roundedFee;
        if (roundedFee <= 0m || roundedFee >= pricePerMessage)
        {
            throw new ArgumentOutOfRangeException(nameof(effectivePolicies), roundedFee, "The rounded platform fee must be greater than zero and less than the price.");
        }

        MoneyRules.EnsurePositive(roundedFee, nameof(roundedFee));
        MoneyRules.EnsurePositive(earnings, nameof(earnings));

        var invariantProbe = new DoctorAdDelivery();
        invariantProbe.ApplySettlementSnapshot(pricePerMessage, percent, roundedFee, earnings, pricePerMessage);

        return new DeliverySettlementSnapshot(pricePerMessage, percent, roundedFee, earnings, pricePerMessage);
    }
}
