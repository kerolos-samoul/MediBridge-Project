using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.UnitTests.Pricing;

public sealed class DoctorTargetEligibilityTests
{
    [Theory]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, 50.0, true)]
    [InlineData(AccountStatus.Pending, DoctorMarketplaceStatus.Active, false, 50.0, false)]
    [InlineData(AccountStatus.Rejected, DoctorMarketplaceStatus.Active, false, 50.0, false)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Suspended, false, 50.0, false)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Active, true, 50.0, false)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, null, false)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, 0.0, false)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Active, false, 1.001, false)]
    public void DoctorTargetEligibility_FiltersByApprovalMarketplaceDeletionAndPrice(
        AccountStatus accountStatus,
        DoctorMarketplaceStatus marketplaceStatus,
        bool isDeleted,
        double? price,
        bool expected)
    {
        var user = new ApplicationUser
        {
            Role = UserRole.Doctor,
            AccountStatus = accountStatus,
            IsDeleted = isDeleted
        };
        var doctor = new DoctorProfile
        {
            Status = marketplaceStatus,
            IsDeleted = isDeleted,
            PricePerMessage = price is null ? null : (decimal)price.Value
        };

        var actual = MediBridge.Services.Services.CampaignTargetEligibility.IsEligible(doctor, user);

        Assert.Equal(expected, actual);
    }
}
