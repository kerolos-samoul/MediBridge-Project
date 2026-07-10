namespace MediBridge.Core.Entities.Wallets;

public static class DeliveryFinancialOperationKeys
{
    public static string ForReserve(string deliveryId) => $"delivery:reserve:{Normalize(deliveryId)}";

    public static string ForRelease(string deliveryId) => $"delivery:release:{Normalize(deliveryId)}";

    public static string ForCharge(string deliveryId) => $"delivery:charge:{Normalize(deliveryId)}";

    public static string ForEarn(string deliveryId) => $"delivery:earn:{Normalize(deliveryId)}";

    private static string Normalize(string deliveryId)
    {
        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            throw new ArgumentException("A delivery id is required.", nameof(deliveryId));
        }

        return deliveryId;
    }
}
