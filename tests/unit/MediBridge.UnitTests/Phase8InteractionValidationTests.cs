using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Messaging;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase8InteractionValidationTests
{
    private readonly DoctorInteractionRequestValidator validator = new();
    private readonly DoctorInteractionIdempotency idempotency = new();

    [Fact]
    public void Idempotency_ProducesStableHashesWithoutRawKeyOutput()
    {
        var first = idempotency.Create(
            Phase8InteractionTestData.DoctorId,
            Phase8InteractionTestData.DeliveryId,
            DeliveryInteractionOutcome.Accept,
            "feedback",
            Phase8InteractionTestData.IdempotencyKey);
        var second = idempotency.Create(
            Phase8InteractionTestData.DoctorId,
            Phase8InteractionTestData.DeliveryId,
            DeliveryInteractionOutcome.Accept,
            "feedback",
            Phase8InteractionTestData.IdempotencyKey);
        var different = idempotency.Create(
            Phase8InteractionTestData.DoctorId,
            Phase8InteractionTestData.DeliveryId,
            DeliveryInteractionOutcome.Reject,
            "feedback",
            Phase8InteractionTestData.IdempotencyKey);

        Assert.Equal(first, second);
        Assert.Equal(first.IdempotencyKeyHash, different.IdempotencyKeyHash);
        Assert.NotEqual(first.RequestFingerprint, different.RequestFingerprint);
        Assert.DoesNotContain(Phase8InteractionTestData.IdempotencyKey, first.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData(" plain punctuation, ok! ", "plain punctuation, ok!", true)]
    public void Validator_AllowsOmittedEmptyWhitespaceAndPlainText(string? feedback, string? expectedFeedback, bool expectedAccepted)
    {
        var result = validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Accept", feedback));

        Assert.Equal(DeliveryInteractionOutcome.Accept, result.Outcome);
        Assert.Equal(expectedFeedback, result.Feedback);
        Assert.Equal(expectedAccepted, result.FeedbackQualifiesForScore);
    }

    [Fact]
    public void Validator_AppliesFeedbackScoreAndLengthBoundaries()
    {
        var shortFeedback = validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Reject", "abcdefghijklmn"));
        var qualifyingFeedback = validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Reject", "abcdefghijklmno"));
        var maxFeedback = validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Accept", Phase8InteractionTestData.Repeat('a', 2000)));

        Assert.False(shortFeedback.FeedbackQualifiesForScore);
        Assert.True(qualifyingFeedback.FeedbackQualifiesForScore);
        Assert.Equal(2000, maxFeedback.Feedback!.Length);
        Assert.Throws<Phase7BadRequestException>(() =>
            validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Accept", Phase8InteractionTestData.Repeat('a', 2001))));
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("&lt;script&gt;")]
    [InlineData("&LT;script&GT;")]
    [InlineData("[label](https://example.test)")]
    [InlineData("![alt](https://example.test/a.png)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("DATA:text/plain,hello")]
    public void Validator_RejectsUnsafeFeedbackPatterns(string feedback)
    {
        Assert.Throws<Phase7BadRequestException>(() =>
            validator.NormalizeAndValidate(new DoctorInteractionRequestDto("Accept", feedback)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Maybe")]
    [InlineData("Accepted")]
    public void Validator_RejectsUnsupportedOutcomes(string outcome)
    {
        Assert.Throws<Phase7BadRequestException>(() =>
            validator.NormalizeAndValidate(new DoctorInteractionRequestDto(outcome, null)));
    }
}
