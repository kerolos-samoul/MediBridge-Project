using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Profiles;

public sealed class ActivityScoreHistory
{
    public const decimal MinimumScore = 0.0m;
    public const decimal MaximumScore = 100.0m;
    public const decimal DefaultNoDeliveriesScore = 95.0m;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public DateOnly ScoreDateEgypt { get; set; }
    public DateOnly WindowStartDateEgypt { get; set; }
    public DateOnly WindowEndDateEgypt { get; set; }
    public int DeliveredCount { get; set; }
    public int InteractedCount { get; set; }
    public int FeedbackQualifiedCount { get; set; }
    public decimal ResponseSpeedScore { get; set; }
    public decimal EngagementScore { get; set; }
    public decimal FeedbackScore { get; set; }
    public decimal FinalScore { get; set; }
    public ActivityScoreCalculationMode CalculationMode { get; set; }
    public bool DoctorWasSuspended { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
    public string? JobRunId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DoctorId))
        {
            throw new InvalidOperationException("Activity score snapshot requires a doctor id.");
        }

        if (WindowEndDateEgypt != ScoreDateEgypt.AddDays(-1))
        {
            throw new InvalidOperationException("Activity score window must end the day before the score date.");
        }

        if (WindowStartDateEgypt != ScoreDateEgypt.AddDays(-30))
        {
            throw new InvalidOperationException("Activity score window must cover 30 completed Cairo dates before the score date.");
        }

        if (new[] { DeliveredCount, InteractedCount, FeedbackQualifiedCount }.Any(value => value < 0))
        {
            throw new InvalidOperationException("Activity score counts cannot be negative.");
        }

        EnsureScore(ResponseSpeedScore, nameof(ResponseSpeedScore));
        EnsureScore(EngagementScore, nameof(EngagementScore));
        EnsureScore(FeedbackScore, nameof(FeedbackScore));
        EnsureScore(FinalScore, nameof(FinalScore));
        if (FinalScore != Math.Round(FinalScore, 1, MidpointRounding.AwayFromZero))
        {
            throw new InvalidOperationException("Final activity score must be rounded to one decimal place.");
        }

        if (CalculatedAtUtc.Kind != DateTimeKind.Utc || CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Activity score timestamps must be UTC.");
        }

        if (CalculationMode == ActivityScoreCalculationMode.DefaultNoDeliveries
            && (DeliveredCount != 0 || FinalScore != DefaultNoDeliveriesScore))
        {
            throw new InvalidOperationException("Default no-deliveries score snapshots require zero deliveries and score 95.0.");
        }

        if (CalculationMode == ActivityScoreCalculationMode.ZeroInteractions
            && (DeliveredCount <= 0 || InteractedCount != 0 || ResponseSpeedScore != 0m || EngagementScore != 0m || FeedbackScore != 0m || FinalScore != 0m))
        {
            throw new InvalidOperationException("Zero-interaction score snapshots require deliveries, zero interactions, and zero scores.");
        }
    }

    private static void EnsureScore(decimal value, string name)
    {
        if (value is < MinimumScore or > MaximumScore)
        {
            throw new InvalidOperationException($"{name} must be between 0.0 and 100.0.");
        }
    }
}
