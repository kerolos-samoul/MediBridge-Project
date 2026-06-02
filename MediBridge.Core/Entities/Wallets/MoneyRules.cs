namespace MediBridge.Core.Entities.Wallets;

public static class MoneyRules
{
    public static decimal EnsureValid(decimal amount, string parameterName, bool allowNegative = false)
    {
        if (!allowNegative && amount < 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Money amount cannot be negative.");
        }

        if (!HasTwoOrFewerDecimalPlaces(amount))
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Money amount cannot have more than two decimal places.");
        }

        return amount;
    }

    public static decimal EnsurePositive(decimal amount, string parameterName)
    {
        EnsureValid(amount, parameterName);
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "Money amount must be positive.");
        }

        return amount;
    }

    public static bool HasTwoOrFewerDecimalPlaces(decimal amount)
    {
        return decimal.Round(amount, 2, MidpointRounding.ToZero) == amount;
    }
}
