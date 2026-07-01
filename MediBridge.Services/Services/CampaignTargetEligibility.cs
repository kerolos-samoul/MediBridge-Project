using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;

namespace MediBridge.Services.Services;

public static class CampaignTargetEligibility
{
    public static bool IsEligible(DoctorProfile doctor, ApplicationUser user)
    {
        return user.Role == UserRole.Doctor
            && user.AccountStatus == AccountStatus.Approved
            && !user.IsDeleted
            && doctor.Status == DoctorMarketplaceStatus.Active
            && !doctor.IsDeleted
            && doctor.PricePerMessage is > 0m
            && MoneyRules.HasTwoOrFewerDecimalPlaces(doctor.PricePerMessage.Value);
    }

    public static CampaignTarget CreateSnapshot(string campaignId, DoctorProfile doctor)
    {
        if (doctor.PricePerMessage is not > 0m
            || !MoneyRules.HasTwoOrFewerDecimalPlaces(doctor.PricePerMessage.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(doctor), "An eligible doctor must have a positive two-decimal price.");
        }

        return new CampaignTarget
        {
            CampaignId = campaignId,
            DoctorId = doctor.Id,
            SpecializationSnapshot = doctor.Specialization,
            ExperienceYearsSnapshot = doctor.ExperienceYears,
            LocationSnapshot = doctor.Location,
            ActivityScoreSnapshot = doctor.ActivityScore,
            PricePerMessageSnapshot = doctor.PricePerMessage.Value,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
