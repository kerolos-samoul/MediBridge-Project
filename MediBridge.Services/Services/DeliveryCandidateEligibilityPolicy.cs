using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Services.Services;

public enum DeliveryCandidateEligibilityDecision
{
    Eligible = 1,
    CancelTerminal = 2,
    KeepQueuedTemporary = 3,
    KeepQueuedInsufficientFunds = 4,
    KeepQueuedExpiryBlocked = 5,
    NoCapacity = 6,
    Failed = 7
}

public sealed record DeliveryCandidateEligibilityResult(DeliveryCandidateEligibilityDecision Decision);

public sealed class DeliveryCandidateEligibilityPolicy
{
    public DeliveryCandidateEligibilityResult Evaluate(
        LockedDoctorDeliveryEligibilityReadModel? doctor,
        LockedCampaignCompanyEligibilityReadModel? campaign,
        int currentDayDeliveryCount,
        decimal availableBalance,
        decimal requiredAmount,
        bool companyHasUnresolvedExpiry)
    {
        if (doctor is null || campaign is null || IsTerminalDoctor(doctor) || IsTerminalCampaignOrCompany(campaign))
        {
            return Result(DeliveryCandidateEligibilityDecision.CancelTerminal);
        }

        if (IsTemporarilyBlocked(doctor, campaign))
        {
            return Result(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary);
        }

        if (doctor.DailyMessageLimit <= 0 || currentDayDeliveryCount >= doctor.DailyMessageLimit)
        {
            return Result(DeliveryCandidateEligibilityDecision.NoCapacity);
        }

        if (companyHasUnresolvedExpiry)
        {
            return Result(DeliveryCandidateEligibilityDecision.KeepQueuedExpiryBlocked);
        }

        if (requiredAmount <= 0m || availableBalance < requiredAmount)
        {
            return Result(requiredAmount <= 0m
                ? DeliveryCandidateEligibilityDecision.Failed
                : DeliveryCandidateEligibilityDecision.KeepQueuedInsufficientFunds);
        }

        return Result(DeliveryCandidateEligibilityDecision.Eligible);
    }

    private static bool IsTerminalDoctor(LockedDoctorDeliveryEligibilityReadModel doctor) =>
        doctor.UserIsDeleted ||
        doctor.ProfileIsDeleted ||
        doctor.UserRole != UserRole.Doctor;

    private static bool IsTerminalCampaignOrCompany(LockedCampaignCompanyEligibilityReadModel campaign) =>
        campaign.CampaignIsDeleted ||
        campaign.CompanyProfileIsDeleted ||
        campaign.CompanyUserIsDeleted ||
        campaign.CompanyUserRole != UserRole.Company ||
        campaign.CompanyAccountStatus is AccountStatus.Rejected or AccountStatus.Inactive ||
        campaign.CampaignStatus is CampaignStatus.Rejected or CampaignStatus.Completed or CampaignStatus.Cancelled;

    private static bool IsTemporarilyBlocked(
        LockedDoctorDeliveryEligibilityReadModel doctor,
        LockedCampaignCompanyEligibilityReadModel campaign) =>
        doctor.AccountStatus != AccountStatus.Approved ||
        doctor.MarketplaceStatus != DoctorMarketplaceStatus.Active ||
        !doctor.PricingIsActive ||
        doctor.PricePerMessage is null or <= 0m ||
        campaign.CompanyAccountStatus != AccountStatus.Approved ||
        campaign.CampaignStatus is not (CampaignStatus.Approved or CampaignStatus.Active);

    private static DeliveryCandidateEligibilityResult Result(DeliveryCandidateEligibilityDecision decision) => new(decision);
}
