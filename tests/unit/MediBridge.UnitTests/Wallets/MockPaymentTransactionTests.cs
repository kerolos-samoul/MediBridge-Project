using MediBridge.Core.Entities.Payments;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.UnitTests.Wallets;

public sealed class MockPaymentTransactionTests
{
    [Fact]
    public void Amount_RejectsNonPositiveAndOverPreciseValues()
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Amount = 0m);
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Amount = -1m);
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Amount = 10.001m);
    }

    [Fact]
    public void CurrencyAndStatus_AreRestrictedToEgpAndSucceeded()
    {
        var payment = new MockPaymentTransaction();

        Assert.Throws<ArgumentException>(() => payment.Currency = "USD");
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.Status = PaymentStatus.Failed);
        Assert.Equal("EGP", payment.Currency);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void PublicSurface_DoesNotExposeProviderCredentials()
    {
        var propertyNames = typeof(MockPaymentTransaction)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name => name.Contains("Card", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Provider", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}
