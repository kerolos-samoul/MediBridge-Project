using System.ComponentModel.DataAnnotations;

namespace MediBridge.Services.Config;

public sealed class DoctorDeliverySettingsOptions
{
    public const string SectionName = "DoctorDeliverySettings";

    [Range(1, 1000)]
    public int MinimumDailyMessageLimit { get; set; } = 1;

    [Range(1, 1000)]
    public int MaximumDailyMessageLimit { get; set; } = 100;

    [Range(0, 7000)]
    public int MinimumWeeklyRequirementFloor { get; set; } = 0;

    [Range(0, 7000)]
    public int MaximumWeeklyRequirement { get; set; } = 700;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MinimumDailyMessageLimit < 1)
        {
            errors.Add("MinimumDailyMessageLimit must be at least 1.");
        }

        if (MaximumDailyMessageLimit < MinimumDailyMessageLimit)
        {
            errors.Add("MaximumDailyMessageLimit must be greater than or equal to MinimumDailyMessageLimit.");
        }

        if (MinimumWeeklyRequirementFloor < 0)
        {
            errors.Add("MinimumWeeklyRequirementFloor must be non-negative.");
        }

        if (MaximumWeeklyRequirement < MinimumWeeklyRequirementFloor)
        {
            errors.Add("MaximumWeeklyRequirement must be greater than or equal to MinimumWeeklyRequirementFloor.");
        }

        return errors;
    }
}
