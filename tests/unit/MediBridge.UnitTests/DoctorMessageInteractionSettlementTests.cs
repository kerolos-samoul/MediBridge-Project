using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Messaging;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class DoctorMessageInteractionSettlementTests
{
    [Fact]
    public void StoredSnapshots_UsePersistedPercentAndAwayFromZeroRounding()
    {
        var delivery = CreateDelivery(price: 100m, percent: 12.345m, fee: 12.35m, earnings: 87.65m);

        var valid = DoctorInteractionSettlementGuard.TryValidateSnapshots(delivery, out var category);

        Assert.True(valid);
        Assert.Equal(string.Empty, category);
    }

    [Fact]
    public void StoredSnapshots_DoNotRecalculateFromCurrentPolicy()
    {
        const decimal currentPolicyPercent = 20m;
        var delivery = CreateDelivery(price: 100m, percent: 12.345m, fee: 12.35m, earnings: 87.65m);
        var currentPolicyFee = Math.Round(delivery.PricePerMessageSnapshot * currentPolicyPercent / 100m, 2, MidpointRounding.AwayFromZero);

        var valid = DoctorInteractionSettlementGuard.TryValidateSnapshots(delivery, out _);

        Assert.True(valid);
        Assert.NotEqual(currentPolicyFee, delivery.PlatformFeeAmount);
    }

    [Theory]
    [InlineData(0, 12.345, 12.35, 87.65, "snapshot-values-invalid")]
    [InlineData(100, 12.345, 12.34, 87.66, "snapshot-formula-invalid")]
    [InlineData(100, 99.999, 100.00, 0.00, "snapshot-values-invalid")]
    public void StoredSnapshots_ClassifyInvalidFinancialEvidence(
        decimal price,
        decimal percent,
        decimal fee,
        decimal earnings,
        string expectedCategory)
    {
        var delivery = CreateDelivery(price, percent, fee, earnings);

        var valid = DoctorInteractionSettlementGuard.TryValidateSnapshots(delivery, out var category);

        Assert.False(valid);
        Assert.Equal(expectedCategory, category);
    }

    [Theory]
    [InlineData(null, 100, "company-wallet-reservation-invalid")]
    [InlineData("USD", 100, "company-wallet-reservation-invalid")]
    [InlineData("EGP", 99.99, "company-wallet-reservation-invalid")]
    public void CompanyWalletValidation_RequiresActiveEgpReservedFunds(
        string? currency,
        decimal reservedBalance,
        string expectedCategory)
    {
        var wallet = currency is null
            ? null
            : new Wallet
            {
                Currency = currency,
                ReservedBalance = reservedBalance
            };

        var valid = DoctorInteractionSettlementGuard.TryValidateCompanyReservedWallet(wallet, 100m, out var category);

        Assert.False(valid);
        Assert.Equal(expectedCategory, category);
    }

    [Fact]
    public void DoctorWalletValidation_AllowsGetOrCreateRepairedEgpWallet()
    {
        var wallet = new Wallet
        {
            Currency = "EGP",
            AvailableBalance = 0m,
            ReservedBalance = 0m
        };

        var valid = DoctorInteractionSettlementGuard.TryValidateDoctorWallet(wallet, out var category);

        Assert.True(valid);
        Assert.Equal(string.Empty, category);
    }

    [Fact]
    public void DoctorWalletValidation_ClassifiesNonEgpWalletAsAnomaly()
    {
        var wallet = new Wallet
        {
            Currency = "USD"
        };

        var valid = DoctorInteractionSettlementGuard.TryValidateDoctorWallet(wallet, out var category);

        Assert.False(valid);
        Assert.Equal("doctor-wallet-invalid", category);
    }

    [Theory]
    [InlineData("Useful feedback", false)]
    [InlineData("Useful feedback for the clinical message", true)]
    public void FeedbackScoreEligibility_UsesNormalizedPlainTextLength(string feedback, bool expectedQualifies)
    {
        var normalized = new DoctorInteractionRequestValidator().NormalizeAndValidate(new DoctorInteractionRequestDto("Accept", feedback));

        Assert.Equal(expectedQualifies, normalized.FeedbackQualifiesForScore);
    }

    private static DoctorAdDelivery CreateDelivery(decimal price, decimal percent, decimal fee, decimal earnings)
    {
        return new DoctorAdDelivery
        {
            Id = "delivery-1",
            DoctorId = "doctor-1",
            CampaignId = "campaign-1",
            CompanyId = "company-1",
            DeliveryDateEgypt = new DateOnly(2026, 7, 11),
            DeliveredAtUtc = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc),
            Status = DeliveryStatus.Active,
            ReservationStatus = ReservationStatus.Reserved,
            PricePerMessageSnapshot = price,
            PlatformFeePercentSnapshot = percent,
            PlatformFeeAmount = fee,
            DoctorEarnings = earnings,
            ReservedAmount = price,
            CreatedAtUtc = new DateTime(2026, 7, 11, 10, 0, 0, DateTimeKind.Utc)
        };
    }
}
