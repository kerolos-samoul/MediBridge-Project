using System.Text.RegularExpressions;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Messaging;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Messaging;

public sealed partial class DoctorInteractionRequestValidator
{
    public NormalizedDoctorInteractionRequest NormalizeAndValidate(DoctorInteractionRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcome = request.Outcome?.Trim() switch
        {
            "Accept" => DeliveryInteractionOutcome.Accept,
            "Reject" => DeliveryInteractionOutcome.Reject,
            _ => throw new Phase7BadRequestException("Interaction outcome must be Accept or Reject.")
        };

        var feedback = request.Feedback?.Trim();
        if (string.IsNullOrEmpty(feedback))
        {
            return new NormalizedDoctorInteractionRequest(outcome, null, false);
        }

        if (feedback.Length > DeliveryInteraction.MaxFeedbackLength
            || feedback.Contains('<', StringComparison.Ordinal)
            || feedback.Contains('>', StringComparison.Ordinal)
            || feedback.Contains("&lt;", StringComparison.OrdinalIgnoreCase)
            || feedback.Contains("&gt;", StringComparison.OrdinalIgnoreCase)
            || MarkdownLinkPattern().IsMatch(feedback)
            || feedback.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || feedback.Contains("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new Phase7BadRequestException("Feedback contains unsupported content.");
        }

        var nonWhitespaceLength = feedback.Count(character => !char.IsWhiteSpace(character));
        return new NormalizedDoctorInteractionRequest(outcome, feedback, nonWhitespaceLength >= 15);
    }

    [GeneratedRegex(@"!?\[[^\]\r\n]+\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkPattern();
}

public sealed record NormalizedDoctorInteractionRequest(
    DeliveryInteractionOutcome Outcome,
    string? Feedback,
    bool FeedbackQualifiesForScore);
