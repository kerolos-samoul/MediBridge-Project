using FluentValidation;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Wallets;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase5CompanyWalletValidationTests
{
    private readonly IValidator<TopUpCompanyWalletRequestDto> validator = new TopUpCompanyWalletRequestValidator();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99.99)]
    public async Task Validator_RejectsAmountsBelowMinimum(decimal amount)
    {
        var result = await validator.ValidateAsync(new TopUpCompanyWalletRequestDto { Amount = amount });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("100", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(100.001)]
    [InlineData(123.456)]
    public async Task Validator_RejectsAmountsWithMoreThanTwoDecimalPlaces(decimal amount)
    {
        var result = await validator.ValidateAsync(new TopUpCompanyWalletRequestDto { Amount = amount });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("two decimal places", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(100.01)]
    [InlineData(5000.99)]
    public async Task Validator_AcceptsValidEgpTopUpAmounts(decimal amount)
    {
        var result = await validator.ValidateAsync(new TopUpCompanyWalletRequestDto { Amount = amount });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validator_RejectsUnsafeOversizedDescription()
    {
        var result = await validator.ValidateAsync(new TopUpCompanyWalletRequestDto
        {
            Amount = 100m,
            Description = new string('x', 501)
        });

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void IdempotencyKeyValidation_RejectsMissingOrTooShortKeys(string? key)
    {
        var exception = Assert.Throws<Phase5ValidationException>(() => CompanyWalletTopUpValidation.NormalizeIdempotencyKey(key));

        Assert.Contains(exception.Errors, error => error.Contains("Idempotency-Key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdempotencyKeyValidation_RejectsOverlyLongKeys()
    {
        var exception = Assert.Throws<Phase5ValidationException>(() => CompanyWalletTopUpValidation.NormalizeIdempotencyKey(new string('x', 129)));

        Assert.Contains(exception.Errors, error => error.Contains("128", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdempotencyKeyValidation_TrimsAndReturnsValidKey()
    {
        var normalized = CompanyWalletTopUpValidation.NormalizeIdempotencyKey("  idem-1234567890  ");

        Assert.Equal("idem-1234567890", normalized);
    }
}
