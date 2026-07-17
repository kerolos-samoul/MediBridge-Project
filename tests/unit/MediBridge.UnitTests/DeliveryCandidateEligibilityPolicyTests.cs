using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class DeliveryCandidateEligibilityPolicyTests
{
    private readonly DeliveryCandidateEligibilityPolicy policy = new();

    [Theory]
    [InlineData(CampaignStatus.Approved)]
    [InlineData(CampaignStatus.Active)]
    public void Evaluate_AllEligibleStatesReturnEligible(CampaignStatus campaignStatus)
    {
        var result = policy.Evaluate(Doctor(), Campaign(campaignStatus), 0, 50m, 50m, false);

        Assert.Equal(DeliveryCandidateEligibilityDecision.Eligible, result.Decision);
    }

    [Fact]
    public void Evaluate_DeletedOrStructurallyInvalidOwnersCancelTerminally()
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(userDeleted: true), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(profileDeleted: true), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(role: UserRole.Company), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(), Campaign(companyDeleted: true), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(), Campaign(companyUserDeleted: true), 0, 50m, 50m, false).Decision);
    }

    [Theory]
    [InlineData(CampaignStatus.Rejected)]
    [InlineData(CampaignStatus.Completed)]
    [InlineData(CampaignStatus.Cancelled)]
    public void Evaluate_TerminalCampaignStatesCancel(CampaignStatus status)
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(), Campaign(status), 0, 50m, 50m, false).Decision);
    }

    [Fact]
    public void Evaluate_DeletedCampaignCancels()
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.CancelTerminal,
            policy.Evaluate(Doctor(), Campaign(campaignDeleted: true), 0, 50m, 50m, false).Decision);
    }

    [Fact]
    public void Evaluate_TemporaryStatesPreserveTheQueueRow()
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(marketplaceStatus: DoctorMarketplaceStatus.Suspended), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(accountStatus: AccountStatus.Pending), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(price: null), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(price: 0m), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(pricingIsActive: false), Campaign(), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(), Campaign(CampaignStatus.Paused), 0, 50m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedTemporary,
            policy.Evaluate(Doctor(), Campaign(companyStatus: AccountStatus.Suspended), 0, 50m, 50m, false).Decision);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    public void Evaluate_ZeroOrFilledCapacityReturnsNoCapacity(int currentCount, int limit)
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.NoCapacity,
            policy.Evaluate(Doctor(limit: limit), Campaign(), currentCount, 50m, 50m, false).Decision);
    }

    [Fact]
    public void Evaluate_InsufficientFundsAndUnresolvedExpiryHaveDistinctSkipResults()
    {
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedInsufficientFunds,
            policy.Evaluate(Doctor(), Campaign(), 0, 49.99m, 50m, false).Decision);
        Assert.Equal(DeliveryCandidateEligibilityDecision.KeepQueuedExpiryBlocked,
            policy.Evaluate(Doctor(), Campaign(), 0, 50m, 50m, true).Decision);
    }

    private static LockedDoctorDeliveryEligibilityReadModel Doctor(
        UserRole role = UserRole.Doctor,
        AccountStatus accountStatus = AccountStatus.Approved,
        bool userDeleted = false,
        bool profileDeleted = false,
        DoctorMarketplaceStatus marketplaceStatus = DoctorMarketplaceStatus.Active,
        bool pricingIsActive = true,
        decimal? price = 50m,
        int limit = 2) =>
        new("doctor", "doctor-user", role, accountStatus, userDeleted, profileDeleted, marketplaceStatus, pricingIsActive, price, limit);

    private static LockedCampaignCompanyEligibilityReadModel Campaign(
        CampaignStatus status = CampaignStatus.Approved,
        bool campaignDeleted = false,
        bool companyDeleted = false,
        UserRole companyRole = UserRole.Company,
        AccountStatus companyStatus = AccountStatus.Approved,
        bool companyUserDeleted = false) =>
        new("campaign", "company", status, campaignDeleted, companyDeleted, companyRole, companyStatus, companyUserDeleted);
}
