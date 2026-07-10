using MediBridge.Core.Enums;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Messaging;
using Xunit;

namespace MediBridge.UnitTests.InteractionPayments;

public sealed class Phase8InteractionValidationTests
{
    [Theory]
    [InlineData(" eight-08 ", "eight-08")]
    [InlineData("12345678", "12345678")]
    [InlineData(" 12345678901234567890 ", "12345678901234567890")]
    public void NormalizeIdempotencyKey_TrimsValidKeys(string key, string expected)
    {
        var normalized = InteractionPaymentValidation.NormalizeIdempotencyKey(key);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    public void NormalizeIdempotencyKey_RejectsMissingOrShortKeys(string? key)
    {
        Assert.Throws<Phase8BadRequestException>(() => InteractionPaymentValidation.NormalizeIdempotencyKey(key));
    }

    [Fact]
    public void NormalizeIdempotencyKey_Accepts128Characters_AndRejects129()
    {
        var valid = new string('k', 128);
        var invalid = new string('k', 129);

        Assert.Equal(valid, InteractionPaymentValidation.NormalizeIdempotencyKey(valid));
        Assert.Throws<Phase8BadRequestException>(() => InteractionPaymentValidation.NormalizeIdempotencyKey(invalid));
    }

    [Theory]
    [InlineData("Accept", DeliveryInteractionDecision.Accept)]
    [InlineData(" accept ", DeliveryInteractionDecision.Accept)]
    [InlineData("Reject", DeliveryInteractionDecision.Reject)]
    [InlineData(" reject ", DeliveryInteractionDecision.Reject)]
    public void ParseDecision_ParsesAcceptAndReject(string decision, DeliveryInteractionDecision expected)
    {
        Assert.Equal(expected, InteractionPaymentValidation.ParseDecision(decision));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void ParseDecision_RejectsInvalidDecision(string? decision)
    {
        Assert.Throws<Phase8BadRequestException>(() => InteractionPaymentValidation.ParseDecision(decision));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" useful detail ", "useful detail")]
    public void NormalizeFeedback_TrimsAndTreatsWhitespaceAsAbsent(string? feedback, string? expected)
    {
        Assert.Equal(expected, InteractionPaymentValidation.NormalizeFeedback(feedback));
    }

    [Fact]
    public void NormalizeFeedback_Accepts1000Characters_AndRejects1001AfterTrimming()
    {
        var valid = new string('f', 1000);
        var invalid = $" {new string('f', 1001)} ";

        Assert.Equal(valid, InteractionPaymentValidation.NormalizeFeedback(valid));
        Assert.Throws<Phase8BadRequestException>(() => InteractionPaymentValidation.NormalizeFeedback(invalid));
    }

    public static IEnumerable<object?[]> IdempotencyKeyCases()
    {
        yield return [" eight-08 ", "eight-08"];
        yield return [null, null];
        yield return ["short", null];
        yield return [new string('k', 129), null];
    }

    public static IEnumerable<object?[]> DecisionCases()
    {
        yield return ["Accept", "Accepted"];
        yield return ["Reject", "Rejected"];
        yield return ["unknown", null];
    }

    public static IEnumerable<object?[]> FeedbackCases()
    {
        yield return [null, null];
        yield return ["   ", null];
        yield return [" useful detail ", "useful detail"];
        yield return [new string('f', 1000), new string('f', 1000)];
        yield return [new string('f', 1001), null];
    }
}
