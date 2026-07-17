using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.DTOs.Admin;
using Xunit;

namespace MediBridge.UnitTests.Admin;

public sealed class AdminPricingDeactivationTests
{
    [Fact]
    public void PricingDeactivationHistory_UsesExplicitInactiveStateNotNumericMarker()
    {
        var history = new DoctorPriceHistory
        {
            DoctorId = "doctor-1",
            PreviousPricePerMessage = 25m,
            NewPricePerMessage = null,
            PricingIsActive = false,
            ChangedByAdminUserId = "admin-1",
            Reason = "Paused"
        };

        Assert.False(history.PricingIsActive);
        Assert.Null(history.NewPricePerMessage);
        Assert.NotEqual(0m, history.PreviousPricePerMessage);
    }

    [Fact]
    public void DoctorPricingMapper_HidesPriceWhenPricingIsInactive()
    {
        var dto = AdminToolsDtoMapper.ToDoctorPricing(new AdminDoctorPricingReadModel(
            "doctor-1",
            25m,
            PricingIsActive: false,
            UpdatedAtUtc: null));

        Assert.False(dto.PricingIsActive);
        Assert.Null(dto.PricePerMessage);
    }
}
