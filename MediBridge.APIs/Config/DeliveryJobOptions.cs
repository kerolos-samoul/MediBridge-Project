using System.ComponentModel.DataAnnotations;

namespace MediBridge.APIs.Config;

public sealed class DeliveryJobOptions
{
    public const string SectionName = "DeliveryJobs";
    public const string DeliveryQueueName = "delivery";
    public const string CairoTimeZoneId = "Africa/Cairo";

    public bool Enabled { get; set; } = true;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    [Range(1, 10)]
    public int AutomaticRetryAttempts { get; set; } = 5;

    [Range(1, 64)]
    public int WorkerCount { get; set; } = 4;

    [Required]
    [RegularExpression("^" + DeliveryQueueName + "$")]
    public string QueueName { get; set; } = DeliveryQueueName;

    [Required]
    [RegularExpression("^" + CairoTimeZoneId + "$")]
    public string TimeZoneId { get; set; } = CairoTimeZoneId;
}
