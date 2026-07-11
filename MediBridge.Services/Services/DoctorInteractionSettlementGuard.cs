using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;

namespace MediBridge.Services.Services;

internal static class DoctorInteractionSettlementGuard
{
    public static bool TryValidateSnapshots(DoctorAdDelivery delivery, out string category)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (delivery.PricePerMessageSnapshot <= 0m
            || delivery.ReservedAmount <= 0m
            || delivery.PlatformFeePercentSnapshot <= 0m
            || delivery.PlatformFeePercentSnapshot >= 100m
            || delivery.PlatformFeeAmount <= 0m
            || delivery.DoctorEarnings <= 0m
            || delivery.ReservedAmount != delivery.PricePerMessageSnapshot
            || delivery.PlatformFeeAmount + delivery.DoctorEarnings != delivery.PricePerMessageSnapshot)
        {
            category = "snapshot-values-invalid";
            return false;
        }

        var expectedFee = Math.Round(
            delivery.PricePerMessageSnapshot * delivery.PlatformFeePercentSnapshot / 100m,
            2,
            MidpointRounding.AwayFromZero);
        if (expectedFee <= 0m
            || expectedFee >= delivery.PricePerMessageSnapshot
            || expectedFee != delivery.PlatformFeeAmount
            || delivery.PricePerMessageSnapshot - expectedFee != delivery.DoctorEarnings)
        {
            category = "snapshot-formula-invalid";
            return false;
        }

        category = string.Empty;
        return true;
    }

    public static bool TryValidateCompanyReservedWallet(Wallet? wallet, decimal reservedAmount, out string category)
    {
        if (wallet is null || wallet.Currency != "EGP" || wallet.ReservedBalance < reservedAmount)
        {
            category = "company-wallet-reservation-invalid";
            return false;
        }

        category = string.Empty;
        return true;
    }

    public static bool TryValidateDoctorWallet(Wallet wallet, out string category)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        if (wallet.Currency != "EGP")
        {
            category = "doctor-wallet-invalid";
            return false;
        }

        category = string.Empty;
        return true;
    }
}
